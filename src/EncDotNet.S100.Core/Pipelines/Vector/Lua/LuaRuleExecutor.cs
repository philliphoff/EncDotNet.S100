using System.Diagnostics;
using System.Text;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Product-agnostic executor for the S-100 Part 9A Lua portrayal stage. Drives
/// the full pass — create context, load <c>main.lua</c>, run provider shims,
/// initialise and override context parameters, call <c>PortrayalMain</c>,
/// dedup and parse the emitted instructions, and apply post-parse transforms —
/// entirely through injected seams, so a single implementation serves every
/// Lua-portrayed product (S-101, S-131, S-57-as-S-101).
/// </summary>
/// <remarks>
/// All product variation is supplied at construction:
/// <list type="bullet">
///   <item><see cref="ILuaRuleSource"/> — module source + declared context parameters.</item>
///   <item><see cref="ILuaDataProviderFactory"/> — render-scoped host-API bridge.</item>
///   <item><see cref="LuaContextParameterBinding"/> list — mariner→parameter mapping policy.</item>
///   <item><see cref="IFeatureAnchorProvider"/> — optional anchor lookup during parse.</item>
///   <item><see cref="IDrawingInstructionTransform"/> list — optional post-parse transforms.</item>
/// </list>
/// </remarks>
public sealed class LuaRuleExecutor : ILuaVectorRuleExecutor
{
    private readonly ILuaEngine _luaEngine;
    private readonly ILuaRuleSource _source;
    private readonly ILuaDataProviderFactory _providerFactory;
    private readonly string _productTag;
    private readonly IReadOnlyList<LuaContextParameterBinding> _contextParameterBindings;
    private readonly IFeatureAnchorProvider? _anchorProvider;
    private readonly IReadOnlyList<IDrawingInstructionTransform> _transforms;

    /// <summary>Initialises a new generic Lua rule executor.</summary>
    /// <param name="luaEngine">The sandboxed Lua engine (S-100 Part 9A).</param>
    /// <param name="source">Supplies Lua module source and declared context parameters.</param>
    /// <param name="providerFactory">Creates a render-scoped host-API data provider.</param>
    /// <param name="productTag">
    /// Telemetry product tag (e.g. <c>"S-101"</c>, <c>"S-131"</c>); S-57 reuses
    /// the S-101 Lua pipeline and therefore tags as <c>"S-101"</c>.
    /// </param>
    /// <param name="contextParameterBindings">
    /// Per-product mapping from <see cref="MarinerSettings"/> to declared
    /// context parameters, including any name aliases or inversions.
    /// </param>
    /// <param name="anchorProvider">
    /// Optional feature-anchor lookup for augmented line tessellation during
    /// parse; <see langword="null"/> for products without augmented geometry.
    /// </param>
    /// <param name="transforms">
    /// Optional ordered post-parse transforms (e.g. the S-101 SAFCON label
    /// merger); <see langword="null"/> or empty when none are required.
    /// </param>
    public LuaRuleExecutor(
        ILuaEngine luaEngine,
        ILuaRuleSource source,
        ILuaDataProviderFactory providerFactory,
        string productTag,
        IReadOnlyList<LuaContextParameterBinding> contextParameterBindings,
        IFeatureAnchorProvider? anchorProvider = null,
        IReadOnlyList<IDrawingInstructionTransform>? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(providerFactory);
        ArgumentException.ThrowIfNullOrEmpty(productTag);
        ArgumentNullException.ThrowIfNull(contextParameterBindings);

        _luaEngine = luaEngine;
        _source = source;
        _providerFactory = providerFactory;
        _productTag = productTag;
        _contextParameterBindings = contextParameterBindings;
        _anchorProvider = anchorProvider;
        _transforms = transforms ?? [];
    }

    /// <inheritdoc/>
    public IReadOnlyList<DrawingInstruction> Execute(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mariner);

        // The MoonSharp interpreter is not interruptible mid-script, so the
        // token is honoured at the coarse boundary before invocation.
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = Telemetry.ActivitySource.StartActivity("s100.lua.execute");
        activity?.SetTag(TelemetryTags.Product, _productTag);
        var start = Stopwatch.GetTimestamp();

        try
        {
            var (emitted, provider) = ExecuteRawCore(mariner);

            Telemetry.LuaFeaturesCount.Add(
                emitted.Count,
                new KeyValuePair<string, object?>(TelemetryTags.Product, _productTag));
            activity?.SetTag("s100.lua.features.count", emitted.Count);

            RecordPerFeatureTypeCardinality(emitted, provider);

            cancellationToken.ThrowIfCancellationRequested();

            var parsed = new List<DrawingInstruction>();
            foreach (var e in emitted)
            {
                var anchor = _anchorProvider?.GetAnchor(e.FeatureRef);
                parsed.AddRange(DrawingInstructionParser.Parse(
                    e.FeatureRef, e.InstructionString, anchor));
            }

            IReadOnlyList<DrawingInstruction> result = parsed;
            foreach (var transform in _transforms)
            {
                result = transform.Transform(result);
            }

            Telemetry.LuaInstructionsEmittedCount.Record(
                result.Count,
                new KeyValuePair<string, object?>(TelemetryTags.Product, _productTag));
            activity?.SetTag("s100.lua.instructions.emitted.count", result.Count);

            return result;
        }
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            Telemetry.LuaExecuteDuration.Record(
                (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
                new KeyValuePair<string, object?>(TelemetryTags.Product, _productTag));
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<EmittedInstruction> ExecuteRaw(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mariner);
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteRawCore(mariner).Emitted;
    }

    private (IReadOnlyList<EmittedInstruction> Emitted, ILuaDataProvider Provider) ExecuteRawCore(
        MarinerSettings mariner)
    {
        var provider = _providerFactory.Create(mariner);

        using var lua = _luaEngine.CreateContext();

        // 1. Resolve require() modules from the catalogue's cached source.
        //    Normalise the module name here (MoonSharp may pass it bare or with
        //    a .lua extension) so each catalogue need not repeat the logic.
        lua.SetModuleLoader(moduleName =>
        {
            var fileName = moduleName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                ? moduleName
                : $"{moduleName}.lua";
            return _source.GetLuaSource(fileName);
        });

        // 2. Register the product's Host* functions.
        provider.RegisterHostFunctions(lua);

        // 3. Load main.lua (which require()s the S-100 scripting framework).
        var mainSource = _source.GetLuaSource("main.lua")
            ?? throw new InvalidOperationException(
                $"{_productTag} portrayal catalogue is missing required rule file 'main.lua'.");
        ExecuteScript(lua, mainSource, "main.lua");

        // 4. Run the provider's post-load shims/patches, in order.
        var shimIndex = 0;
        foreach (var script in provider.PostLoadScripts)
        {
            ExecuteScript(lua, script, $"post-load script #{shimIndex++}");
        }

        // 5. Declare the catalogue's context parameters on the Lua side.
        ExecuteScript(lua, BuildContextParameterInitScript(), "context-parameter init");

        // 6. Override context parameters from MarinerSettings via the product's
        //    bindings, restricted to parameters the catalogue actually declares.
        ApplyContextParameters(lua, mariner);

        // 7. Run portrayal.
        try
        {
            lua.Call("PortrayalMain");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{_productTag} Lua portrayal failed: {DescribeLuaError(ex)}", ex);
        }

        // 8. Collect + dedup. PortrayalModel.lua AddFeature() stores items under
        //    both array append and feature-ID index, so ipairs() may visit some
        //    twice; dedup by (FeatureRef, InstructionString).
        var seen = new HashSet<(string, string)>();
        var results = new List<EmittedInstruction>();
        foreach (var e in provider.EmittedInstructions)
        {
            if (seen.Add((e.FeatureRef, e.InstructionString)))
            {
                results.Add(e);
            }
        }

        return (results, provider);
    }

    private void RecordPerFeatureTypeCardinality(
        IReadOnlyList<EmittedInstruction> emitted, ILuaDataProvider provider)
    {
        if (emitted.Count == 0) return;

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in emitted)
        {
            var code = provider.TryGetFeatureTypeCode(e.FeatureRef) ?? "(unknown)";
            counts.TryGetValue(code, out var existing);
            counts[code] = existing + 1;
        }

        foreach (var (code, count) in counts)
        {
            Telemetry.LuaFeatureInstructionsCount.Record(
                count,
                new KeyValuePair<string, object?>(TelemetryTags.Product, _productTag),
                new KeyValuePair<string, object?>(TelemetryTags.FeatureType, code));
        }
    }

    private string BuildContextParameterInitScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("local _cp = {}");

        foreach (var cp in _source.ContextParameters)
        {
            sb.AppendLine(
                $"_cp[#_cp + 1] = PortrayalCreateContextParameter('{LuaSource.EscapeLiteral(cp.Id)}', " +
                $"'{LuaSource.EscapeLiteral(cp.Type)}', '{LuaSource.EscapeLiteral(cp.Default)}')");
        }

        sb.AppendLine("PortrayalInitializeContextParameters(_cp)");
        return sb.ToString();
    }

    private void ApplyContextParameters(ILuaContext lua, MarinerSettings mariner)
    {
        var declared = new HashSet<string>(
            _source.ContextParameters.Select(cp => cp.Id), StringComparer.Ordinal);

        foreach (var binding in _contextParameterBindings)
        {
            if (!declared.Contains(binding.DeclaredId)) continue;

            var value = binding.ValueFactory(mariner);
            if (value is null) continue;

            lua.Call("PortrayalSetContextParameter", binding.DeclaredId, binding.Serialize(value));
        }
    }

    private void ExecuteScript(ILuaContext lua, string source, string what)
    {
        try
        {
            lua.Execute(source);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{_productTag} Lua portrayal failed loading {what}: {DescribeLuaError(ex)}", ex);
        }
    }

    private static string DescribeLuaError(Exception ex)
    {
        // MoonSharp exceptions carry a DecoratedMessage with the Lua source
        // location; surface it when present.
        var decorated = ex.GetType().GetProperty("DecoratedMessage")?.GetValue(ex) as string;
        return decorated ?? ex.Message;
    }
}
