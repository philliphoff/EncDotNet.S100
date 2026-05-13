using System.Diagnostics;
using System.Globalization;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S131.Diagnostics;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S131;

/// <summary>
/// Executes the S-131 Lua portrayal stage as defined in S-100 Part 9A.
/// Creates a single Lua context, registers the Host API via
/// <see cref="S131LuaDataProvider"/>, loads the S-131 <c>main.lua</c>,
/// calls <c>PortrayalMain()</c>, and parses the emitted instruction strings
/// via <see cref="DrawingInstructionParser"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the GML+Lua bridge: S-131 features are parsed from GML by
/// <see cref="S131DatasetReader"/>, then the Lua portrayal engine processes
/// them identically to how it processes S-101 ISO 8211 features. The data
/// provider translates between the GML feature model and the Lua host API
/// contract.
/// </para>
/// <para>
/// Unlike <see cref="S101LuaRuleExecutor"/>, this executor does not apply
/// the SAFCON contour-label merger (an S-101-specific post-processing step).
/// </para>
/// </remarks>
public sealed class S131LuaRuleExecutor : ILuaRuleExecutor
{
    private readonly ILuaEngine _luaEngine;
    private readonly S131Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly FeatureCatalogue _featureCatalogue;

    /// <summary>
    /// Lua shim that wraps HostFeatureGetSpatialAssociations to convert
    /// the raw host data into proper SpatialAssociation objects via
    /// CreateSpatialAssociation() (from PortrayalAPI.lua).
    /// </summary>
    private const string SpatialAssociationShim = """
        local _rawHostGetSpatial = HostFeatureGetSpatialAssociations
        HostFeatureGetSpatialAssociations = function(featureID)
            local raw = _rawHostGetSpatial(featureID)
            if raw == nil then return nil end
            local result = {}
            for i, sa in ipairs(raw) do
                result[i] = CreateSpatialAssociation(sa.SpatialType, sa.SpatialID, sa.Orientation)
            end
            if #result == 0 then return nil end
            result.Type = 'array:SpatialAssociation'
            return result
        end
        """;

    /// <summary>
    /// Lua shim that implements HostGetSpatial by calling the C# HostGetSpatialData
    /// to get raw data, then constructing proper Spatial Lua objects.
    /// </summary>
    /// <remarks>
    /// For S-131 GML features with inline geometry, the spatial data is
    /// synthesized by <see cref="S131LuaDataProvider.HostGetSpatialData"/>.
    /// Synthetic start/end point IDs (<c>synth:start:*</c>, <c>synth:end:*</c>)
    /// are resolved inline since they don't exist as separate spatial records.
    /// </remarks>
    private const string HostGetSpatialShim = """
        local _rawHostGetSpatialData = HostGetSpatialData
        
        -- Cache for synthetic start/end points from curve data
        local _syntheticPoints = {}
        
        function HostGetSpatial(spatialID)
            -- Check synthetic point cache first (for curve start/end points)
            if _syntheticPoints[spatialID] then
                local sp = _syntheticPoints[spatialID]
                return CreatePoint(sp.X, sp.Y)
            end
        
            local data = _rawHostGetSpatialData(spatialID)
            if data == nil then return nil end

            if data.RecordType == 'Point' then
                return CreatePoint(data.X, data.Y)
            elseif data.RecordType == 'MultiPoint' then
                local points = {}
                for _, pt in ipairs(data.Points) do
                    points[#points + 1] = CreatePoint(pt.X, pt.Y, pt.Z)
                end
                points.Type = 'array:Spatial'
                return CreateMultiPoint(points)
            elseif data.RecordType == 'Curve' then
                -- Cache start/end point data for HostGetSpatial lookups
                if data.StartPointID and data._startX then
                    _syntheticPoints[data.StartPointID] = { X = data._startX, Y = data._startY }
                end
                if data.EndPointID and data._endX then
                    _syntheticPoints[data.EndPointID] = { X = data._endX, Y = data._endY }
                end
                local startSA = CreateSpatialAssociation('Point', data.StartPointID, 'Forward')
                local endSA = CreateSpatialAssociation('Point', data.EndPointID, 'Forward')
                local controlPoints = {}
                if data.ControlPoints then
                    for _, cp in ipairs(data.ControlPoints) do
                        controlPoints[#controlPoints + 1] = CreatePoint(cp.X, cp.Y)
                    end
                end
                local segment = CreateCurveSegment(controlPoints)
                return CreateCurve(startSA, endSA, { segment })
            elseif data.RecordType == 'CompositeCurve' then
                local curveAssocs = {}
                if data.CurveAssociations then
                    for _, ca in ipairs(data.CurveAssociations) do
                        curveAssocs[#curveAssocs + 1] = CreateSpatialAssociation(ca.SpatialType, ca.SpatialID, ca.Orientation)
                    end
                end
                curveAssocs.Type = 'array:SpatialAssociation'
                return CreateCompositeCurve(curveAssocs)
            elseif data.RecordType == 'Surface' then
                local exteriorRing = nil
                if data.ExteriorRing then
                    exteriorRing = CreateSpatialAssociation(data.ExteriorRing.SpatialType, data.ExteriorRing.SpatialID, data.ExteriorRing.Orientation)
                end
                local interiorRings = {}
                if data.InteriorRings then
                    for _, ir in ipairs(data.InteriorRings) do
                        interiorRings[#interiorRings + 1] = CreateSpatialAssociation(ir.SpatialType, ir.SpatialID, ir.Orientation)
                    end
                end
                interiorRings.Type = 'array:SpatialAssociation'
                return CreateSurface(exteriorRing, interiorRings)
            end
            return nil
        end
        """;

    /// <summary>
    /// Initialises a new <see cref="S131LuaRuleExecutor"/> for the given
    /// dataset and portrayal catalogue.
    /// </summary>
    public S131LuaRuleExecutor(
        ILuaEngine luaEngine,
        S131Dataset dataset,
        PortrayalCatalogueProvider provider,
        FeatureCatalogue featureCatalogue)
    {
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(featureCatalogue);

        _luaEngine = luaEngine;
        _dataset = dataset;
        _provider = provider;
        _featureCatalogue = featureCatalogue;
    }

    /// <summary>
    /// Runs the S-131 Lua portrayal stage and returns typed drawing instructions
    /// ready for the renderer.
    /// </summary>
    public IReadOnlyList<DrawingInstruction> Execute(MarinerSettings mariner)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("s100.lua.execute");
        activity?.SetTag(TelemetryTags.Product, "S-131");
        var start = Stopwatch.GetTimestamp();

        try
        {
            var emitted = ExecuteRaw(mariner);

            activity?.SetTag("s100.lua.features.count", emitted.Count);

            var parsed = new List<DrawingInstruction>();
            foreach (var e in emitted)
            {
                parsed.AddRange(DrawingInstructionParser.Parse(e.FeatureRef, e.InstructionString));
            }

            // No S-101-specific post-processing (SAFCON merger) needed for S-131.
            activity?.SetTag("s100.lua.instructions.emitted.count", parsed.Count);
            return parsed;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            var elapsed = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            activity?.SetTag("s100.lua.duration.ms", elapsed);
        }
    }

    /// <summary>
    /// Runs the S-131 Lua portrayal pipeline and returns the raw emitted
    /// drawing-instruction strings keyed by feature reference.
    /// </summary>
    public IReadOnlyList<EmittedInstruction> ExecuteRaw(MarinerSettings mariner)
    {
        ArgumentNullException.ThrowIfNull(mariner);

        var dataProvider = new S131LuaDataProvider(_dataset, _featureCatalogue);

        using var lua = _luaEngine.CreateContext();

        // 1. Configure require() module loader
        var moduleCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        lua.SetModuleLoader(moduleName =>
        {
            var fileName = moduleName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                ? moduleName
                : $"{moduleName}.lua";

            if (moduleCache.TryGetValue(fileName, out var cached))
                return cached;

            try
            {
                using var stream = _provider.FetchRuleAsync(fileName)
                    .GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                var source = reader.ReadToEnd();
                moduleCache[fileName] = source;
                return source;
            }
            catch
            {
                moduleCache[fileName] = null;
                return null;
            }
        });

        // 2. Register all Host* functions
        dataProvider.RegisterHostFunctions(lua);

        // 3. Load and execute main.lua
        string mainSource = LoadRuleSource("main.lua");
        lua.Execute(mainSource);

        // 3a. Spatial association shim
        lua.Execute(SpatialAssociationShim);

        // 3b. HostGetSpatial shim
        lua.Execute(HostGetSpatialShim);

        // 3c. Patch 'contains' for nil/void safety (MoonSharp quirk)
        lua.Execute("""
            local _orig_contains = contains
            function contains(value, array)
                if value == nil then return false end
                return _orig_contains(value, array)
            end
            """);

        // 4. Build and initialise context parameters
        var cpSource = BuildContextParameterInitScript();
        lua.Execute(cpSource);

        // 5. Set context parameter overrides from MarinerSettings
        SetContextParameters(lua, mariner);

        // 6. Call PortrayalMain()
        try
        {
            lua.Call("PortrayalMain");
        }
        catch (Exception ex)
        {
            var decorated = ex.GetType().GetProperty("DecoratedMessage")?.GetValue(ex) as string;
            var detail = decorated ?? ex.Message;
            throw new InvalidOperationException(
                $"S-131 Lua portrayal failed: {detail}", ex);
        }

        // 7. Collect and deduplicate results
        var seen = new HashSet<(string, string)>();
        var results = new List<EmittedInstruction>();
        foreach (var e in dataProvider.EmittedInstructions)
        {
            if (seen.Add((e.FeatureRef, e.Instructions)))
            {
                results.Add(new EmittedInstruction
                {
                    FeatureRef = e.FeatureRef,
                    InstructionString = e.Instructions,
                    ObservedParameters = e.ObservedParams,
                });
            }
        }

        return results;
    }

    private string BuildContextParameterInitScript()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("local _cp = {}");

        foreach (var cp in _provider.Catalogue.ContextParameters)
        {
            var escapedId = EscapeLuaString(cp.Id);
            var escapedType = EscapeLuaString(cp.Type);
            var escapedDefault = EscapeLuaString(cp.Default);
            sb.AppendLine(
                $"_cp[#_cp + 1] = PortrayalCreateContextParameter('{escapedId}', '{escapedType}', '{escapedDefault}')");
        }

        sb.AppendLine("PortrayalInitializeContextParameters(_cp)");
        return sb.ToString();
    }

    private static void SetContextParameters(ILuaContext lua, MarinerSettings mariner)
    {
        // S-131 context parameters — pass through any that the PC declares.
        // S-131 PC 2.0.0 declares fewer context parameters than S-101.
        var parameters = new Dictionary<string, string>
        {
            ["SafetyContour"] = mariner.SafetyContour.ToString(CultureInfo.InvariantCulture),
            ["SafetyDepth"] = mariner.SafetyDepth.ToString(CultureInfo.InvariantCulture),
        };

        foreach (var (name, value) in parameters)
        {
            try
            {
                lua.Call("PortrayalSetContextParameter", name, value);
            }
            catch
            {
                // Skip parameters not recognised by this catalogue version.
            }
        }
    }

    private static string EscapeLuaString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("'", "\\'");
    }

    private string LoadRuleSource(string fileName)
    {
        using var stream = _provider.FetchRuleAsync(fileName)
            .GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// A single emitted drawing instruction from the S-131 Lua portrayal pipeline.
/// </summary>
public sealed class EmittedInstruction
{
    /// <summary>Feature reference string (the feature's numeric ID).</summary>
    public required string FeatureRef { get; init; }

    /// <summary>
    /// Semicolon-separated key:value drawing instruction string.
    /// </summary>
    public required string InstructionString { get; init; }

    /// <summary>Observed context parameter names used during rule evaluation.</summary>
    public required string ObservedParameters { get; init; }
}
