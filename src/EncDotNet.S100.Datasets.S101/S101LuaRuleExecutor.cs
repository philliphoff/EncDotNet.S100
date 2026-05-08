using System.Globalization;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Executes the S-101 Lua portrayal stage as defined in S-100 Part 9A.
/// Creates a single Lua context, registers the Host API, loads the main.lua
/// entry point (which requires the S-100 scripting framework), initialises
/// context parameters, calls <c>PortrayalMain()</c>, parses the emitted
/// instruction strings via <see cref="DrawingInstructionParser"/>, and applies
/// S-101-specific post-processing (e.g. SAFCON contour-label merging) before
/// returning typed drawing instructions to the unified vector pipeline.
/// </summary>
public sealed class S101LuaRuleExecutor : ILuaRuleExecutor
{
    private readonly ILuaEngine _luaEngine;
    private readonly S101Dataset _dataset;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly FeatureCatalogue _featureCatalogue;

    /// <summary>
    /// Lua shim that wraps HostFeatureGetSpatialAssociations to convert
    /// the raw host data (List of {SpatialID, SpatialType, Orientation})
    /// into proper SpatialAssociation objects via CreateSpatialAssociation().
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
    /// to get raw data, then constructing proper Spatial Lua objects via the
    /// Create* functions from PortrayalAPI.lua.
    /// </summary>
    private const string HostGetSpatialShim = """
        local _rawHostGetSpatialData = HostGetSpatialData
        function HostGetSpatial(spatialID)
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
                for _, ca in ipairs(data.CurveAssociations) do
                    curveAssocs[#curveAssocs + 1] = CreateSpatialAssociation(ca.SpatialType, ca.SpatialID, ca.Orientation)
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

    public S101LuaRuleExecutor(
        ILuaEngine luaEngine,
        S101Dataset dataset,
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
    /// Runs the S-101 Lua portrayal stage and returns typed drawing instructions
    /// ready for the renderer. Equivalent to <see cref="ExecuteRaw"/> followed by
    /// <see cref="DrawingInstructionParser.Parse"/> and
    /// <see cref="S101SafconLabelMerger.Merge"/>.
    /// </summary>
    public IReadOnlyList<DrawingInstruction> Execute(MarinerSettings mariner)
    {
        var emitted = ExecuteRaw(mariner);

        var parsed = new List<DrawingInstruction>();
        foreach (var e in emitted)
        {
            parsed.AddRange(DrawingInstructionParser.Parse(e.FeatureRef, e.InstructionString));
        }

        return S101SafconLabelMerger.Merge(parsed);
    }

    /// <summary>
    /// Runs the S-101 Lua portrayal pipeline for the bound dataset and returns
    /// the raw emitted drawing-instruction strings keyed by feature reference.
    /// Intended for diagnostics; production callers should prefer
    /// <see cref="Execute(MarinerSettings)"/>, which returns typed instructions.
    /// </summary>
    public IReadOnlyList<EmittedInstruction> ExecuteRaw(MarinerSettings mariner)
    {
        ArgumentNullException.ThrowIfNull(mariner);
        var dataset = _dataset;

        var dataProvider = new S101LuaDataProvider(dataset, _featureCatalogue);

        using var lua = _luaEngine.CreateContext();

        // 1. Configure require() to resolve modules from the Rules/ subdirectory
        lua.SetModuleLoader(moduleName =>
        {
            // MoonSharp may pass the bare name (e.g. "S100Scripting") or with
            // a .lua extension (e.g. "S100Scripting.lua") depending on ModulePaths.
            var fileName = moduleName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                ? moduleName
                : $"{moduleName}.lua";
            try
            {
                using var stream = _provider.FetchRuleAsync(fileName)
                    .GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        });

        // 2. Register all Host* functions on the Lua context
        dataProvider.RegisterHostFunctions(lua);

        // 3. Load and execute main.lua (which will require S100Scripting, PortrayalModel, etc.)
        string mainSource = LoadRuleSource("main.lua");
        lua.Execute(mainSource);

        // 3a. Wrap HostFeatureGetSpatialAssociations so the raw data from the host
        //     is converted into proper SpatialAssociation objects via CreateSpatialAssociation().
        //     CreateSpatialAssociation (from PortrayalAPI.lua) handles string→SpatialType
        //     lookup, sets metatables, etc.
        lua.Execute(SpatialAssociationShim);

        // 3b. Implement HostGetSpatial by wrapping HostGetSpatialData (C#) and
        //     constructing proper Spatial Lua objects via Create* functions.
        lua.Execute(HostGetSpatialShim);

        // 3c. Patch 'contains' to handle nil/void gracefully. MoonSharp may
        //     return DynValue.Void (rather than nil) from __index when an
        //     attribute is missing, causing type() to error.
        lua.Execute("""
            local _orig_contains = contains
            function contains(value, array)
                if value == nil then return false end
                return _orig_contains(value, array)
            end
            """);

        // 4. Build context parameter array using the Lua-side factory function
        //    PortrayalCreateContextParameter(name, type, default) → ContextParameter table
        //    Then pass the array to PortrayalInitializeContextParameters()
        var cpSource = BuildContextParameterInitScript();
        lua.Execute(cpSource);

        // 5. Set context parameter overrides from MarinerSettings
        SetContextParameters(lua, mariner);

        // 6. Call PortrayalMain(featureIDs) — iterates features, calls per-feature
        //    rule functions, emits drawing instructions via HostPortrayalEmit.
        try
        {
            lua.Call("PortrayalMain");
        }
        catch (Exception ex)
        {
            // Extract Lua source location if available (MoonSharp)
            var decorated = ex.GetType().GetProperty("DecoratedMessage")?.GetValue(ex) as string;
            var detail = decorated ?? ex.Message;
            throw new InvalidOperationException(
                $"S-101 Lua portrayal failed: {detail}", ex);
        }

        // 7. Collect results.
        //    The S-101 Lua PortrayalModel.lua AddFeature() stores items in the same
        //    table using both sequential array append (self[#self+1]) and feature-ID
        //    indexing (self[feature.ID]). Because feature IDs are numeric and overlap
        //    with array positions, ipairs() can visit some items twice, causing
        //    HostPortrayalEmit to be called with identical instructions for the same
        //    feature. Deduplicate by (FeatureRef, InstructionString) to eliminate
        //    these spurious repeats.
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

    /// <summary>
    /// Builds a Lua script that creates context parameters via
    /// <c>PortrayalCreateContextParameter(name, type, default)</c> and passes them
    /// to <c>PortrayalInitializeContextParameters()</c>.
    /// </summary>
    private string BuildContextParameterInitScript()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("local _cp = {}");

        foreach (var cp in _provider.Catalogue.ContextParameters)
        {
            // Escape the default value for Lua string literal
            var escapedId = EscapeLuaString(cp.Id);
            var escapedType = EscapeLuaString(cp.Type);
            var escapedDefault = EscapeLuaString(cp.Default);
            sb.AppendLine(
                $"_cp[#_cp + 1] = PortrayalCreateContextParameter('{escapedId}', '{escapedType}', '{escapedDefault}')");
        }

        sb.AppendLine("PortrayalInitializeContextParameters(_cp)");
        return sb.ToString();
    }

    private void SetContextParameters(ILuaContext lua, MarinerSettings mariner)
    {
        // Known S-101 context parameters mapped from MarinerSettings.
        // PortrayalSetContextParameter expects both arguments as strings.
        // The Lua side parses booleans from the lower-case literals
        // "true"/"false" (per the bundled S-101 PC parameter declarations).
        var parameters = new Dictionary<string, string>
        {
            ["SafetyContour"] = mariner.SafetyContour.ToString(CultureInfo.InvariantCulture),
            ["SafetyDepth"] = mariner.SafetyDepth.ToString(CultureInfo.InvariantCulture),
            ["ShallowContour"] = mariner.ShallowContour.ToString(CultureInfo.InvariantCulture),
            ["DeepContour"] = mariner.DeepContour.ToString(CultureInfo.InvariantCulture),
            ["FourShades"] = mariner.FourShades ? "true" : "false",
            ["ShallowWaterDangers"] = mariner.ShallowWaterDangers ? "true" : "false",
            ["PlainBoundaries"] = mariner.PlainBoundaries ? "true" : "false",
            ["SimplifiedSymbols"] = mariner.SimplifiedSymbols ? "true" : "false",
            ["FullLightLines"] = mariner.FullLightLines ? "true" : "false",
            ["RadarOverlay"] = mariner.RadarOverlay ? "true" : "false",
            ["IgnoreScaleMinimum"] = mariner.IgnoreScaleMinimum ? "true" : "false",
        };

        // NationalLanguage is only sent when the user has explicitly chosen
        // one — empty means "use the catalogue's declared default" (eng).
        if (!string.IsNullOrWhiteSpace(mariner.NationalLanguage))
        {
            parameters["NationalLanguage"] = mariner.NationalLanguage;
        }

        foreach (var (name, value) in parameters)
        {
            try
            {
                lua.Call("PortrayalSetContextParameter", name, value);
            }
            catch
            {
                // Skip parameters not recognized by this catalogue version.
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
/// A single emitted drawing instruction from the Lua portrayal pipeline.
/// </summary>
public sealed class EmittedInstruction
{
    /// <summary>Feature reference string (typically the feature record ID).</summary>
    public required string FeatureRef { get; init; }

    /// <summary>
    /// Semicolon-separated key:value drawing instruction string, e.g.
    /// "ViewingGroup:36050;DrawingPriority:6;DisplayPlane:UnderRadar;PointInstruction:BOYLAT01".
    /// </summary>
    public required string InstructionString { get; init; }

    /// <summary>Observed context parameter names used during rule evaluation.</summary>
    public required string ObservedParameters { get; init; }
}
