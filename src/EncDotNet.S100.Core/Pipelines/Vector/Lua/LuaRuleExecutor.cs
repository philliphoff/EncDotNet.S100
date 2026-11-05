using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
///   <item><see cref="ISpatialReferenceResolver"/> — optional curve lookup for <c>SpatialReference</c>.</item>
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
    private readonly ISpatialReferenceResolver? _spatialReferences;
    private readonly IReadOnlyList<IDrawingInstructionTransform> _transforms;
    private readonly LuaShardOptions _sharding;

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
    /// <param name="spatialReferences">
    /// Optional resolver for <c>SpatialReference</c> commands, so lines drawn
    /// on particular edges (such as the safety contour) follow those edges
    /// only; <see langword="null"/> for products whose rules emit none.
    /// </param>
    /// <param name="sharding">
    /// Whether to split a run across several Lua states
    /// (<see cref="LuaShardOptions"/>); <see langword="null"/> for one full
    /// run, as before.
    /// </param>
    public LuaRuleExecutor(
        ILuaEngine luaEngine,
        ILuaRuleSource source,
        ILuaDataProviderFactory providerFactory,
        string productTag,
        IReadOnlyList<LuaContextParameterBinding> contextParameterBindings,
        IFeatureAnchorProvider? anchorProvider = null,
        IReadOnlyList<IDrawingInstructionTransform>? transforms = null,
        ISpatialReferenceResolver? spatialReferences = null,
        LuaShardOptions? sharding = null)
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
        _spatialReferences = spatialReferences;
        _transforms = transforms ?? [];
        _sharding = sharding ?? LuaShardOptions.Disabled;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DrawingInstruction>> ExecuteAsync(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mariner);

        // The token is checked before the run, passed to the engine (which
        // Lua-CSharp checks between instructions) and checked at every
        // feature the rules emit, which is the only check MoonSharp gets (see
        // RunPortrayalMain).
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = Telemetry.ActivitySource.StartActivity("s100.lua.execute");
        activity?.SetTag(TelemetryTags.Product, _productTag);
        var start = Stopwatch.GetTimestamp();

        try
        {
            // Async pre-warm — load every Lua module declared by the catalogue
            // so the synchronous require() loader can resolve modules
            // without blocking. The snapshot dictionary is captured by the
            // sync loader callback inside ExecuteRawCoreAsync.
            var luaSources = await LoadLuaSourcesAsync(cancellationToken).ConfigureAwait(false);

            var (emitted, provider, shards) = await ExecuteRawCoreAsync(mariner, luaSources, cancellationToken)
                .ConfigureAwait(false);
            activity?.SetTag("s100.lua.shards", shards);

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
                    e.FeatureRef, e.InstructionString, anchor, _spatialReferences));
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
    public async Task<IReadOnlyList<EmittedInstruction>> ExecuteRawAsync(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mariner);
        cancellationToken.ThrowIfCancellationRequested();
        var luaSources = await LoadLuaSourcesAsync(cancellationToken).ConfigureAwait(false);
        return (await ExecuteRawCoreAsync(mariner, luaSources, cancellationToken).ConfigureAwait(false)).Emitted;
    }

    /// <summary>
    /// Awaits every Lua rule file declared in the portrayal catalogue
    /// manifest into a local snapshot dictionary so the synchronous
    /// <c>require()</c> module loader (registered inside
    /// <c>ExecuteRawCoreAsync</c>) can perform its lookups against an
    /// in-memory map. This is the sync↔async bridge for Lua portrayal:
    /// catalogues serve content asynchronously, the executor freezes the set
    /// before driving the synchronous engine.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> LoadLuaSourcesAsync(CancellationToken cancellationToken)
    {
        var names = await _source.GetLuaSourceNamesAsync(cancellationToken).ConfigureAwait(false);
        var dict = new Dictionary<string, string>(names.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var src = await _source.GetLuaSourceAsync(name, cancellationToken).ConfigureAwait(false);
            if (src is not null)
            {
                dict[name] = src;
            }
        }

        // Remove the framework's empty type-check and profiling calls (#1275).
        if (LuaNoOpCallStripper.CanStrip(dict.Values))
        {
            foreach (var name in dict.Keys.ToList())
            {
                dict[name] = LuaNoOpCallStripper.Strip(dict[name]);
            }
        }

        return dict;
    }

    /// <summary>
    /// Opens a Lua portrayal session: one Lua state, set up with the
    /// catalogue's rules and <paramref name="mariner"/>'s context parameters,
    /// that portrays chosen features on demand
    /// (<see cref="LuaPortrayalSession.Portray"/>). Setting up the state is
    /// most of the cost of portraying a few features, so a caller that
    /// portrays features in batches, such as a route check leg by leg, keeps
    /// one session.
    /// </summary>
    /// <param name="mariner">The mariner settings, for the context parameters.</param>
    /// <param name="cancellationToken">Cancels loading the rules.</param>
    /// <returns>The session; the caller disposes it.</returns>
    public async Task<LuaPortrayalSession> OpenSessionAsync(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mariner);
        cancellationToken.ThrowIfCancellationRequested();
        var luaSources = await LoadLuaSourcesAsync(cancellationToken).ConfigureAwait(false);
        var provider = _providerFactory.Create(mariner);
        var lua = _luaEngine.CreateContext();
        try
        {
            Prepare(lua, provider, mariner, luaSources);
        }
        catch
        {
            lua.Dispose();
            throw;
        }

        return new LuaPortrayalSession(lua, provider, _productTag, _anchorProvider, _spatialReferences);
    }

    private async Task<(IReadOnlyList<EmittedInstruction> Emitted, ILuaDataProvider Provider, int Shards)> ExecuteRawCoreAsync(
        MarinerSettings mariner, IReadOnlyDictionary<string, string> luaSources, CancellationToken cancellationToken)
    {
        var provider = _providerFactory.Create(mariner);
        ILuaContext? lua = _luaEngine.CreateContext();
        try
        {
            Prepare(lua, provider, mariner, luaSources);

            if (_sharding.IsEnabled && provider is ILuaShardGrouping grouping)
            {
                var visits = ReadVisits(lua);
                var shardCount = visits is null
                    ? 1
                    : LuaShardPlan.ShardCount(visits.Value.Refs.Count, _sharding.MaxShards, _sharding.MinFeaturesPerShard);
                if (shardCount > 1)
                {
                    var (refs, codes) = visits!.Value;
                    var positions = LuaShardPlan.Assign(grouping.GetShardGroups(refs), shardCount);
                    var first = lua;
                    lua = null; // RunShardsAsync disposes it.
                    var emitted = await RunShardsAsync(
                        first, provider, positions, mariner, luaSources, cancellationToken).ConfigureAwait(false);

                    var merged = LuaShardPlan.Merge(refs, codes, positions, emitted);
                    if (merged is not null)
                    {
                        return (Deduplicate(merged, 0), provider, shardCount);
                    }

                    // The catalogue's main loop isn't the one the merge
                    // assumes: portray the cell again in one state.
                    provider = _providerFactory.Create(mariner);
                    lua = _luaEngine.CreateContext();
                    Prepare(lua, provider, mariner, luaSources);
                }
            }

            RunPortrayalMain(lua!, cancellationToken);
            return (Deduplicate(provider.EmittedInstructions, 0), provider, 1);
        }
        finally
        {
            lua?.Dispose();
        }
    }

    private void RunPortrayalMain(ILuaContext lua, CancellationToken cancellationToken)
    {
        if (cancellationToken.CanBeCanceled)
            InstallCancellationCheck(lua, cancellationToken);

        // 7. Run portrayal.
        try
        {
            lua.CallCancellable("PortrayalMain", cancellationToken);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{_productTag} Lua portrayal failed: {lua.DescribeError(ex)}", ex);
        }
    }

    /// <summary>
    /// Reads the features a full <c>PortrayalMain</c> run would visit, in
    /// order, from a prepared state: the S-100 Part 9a main loop walks
    /// <c>portrayalContext.FeaturePortrayalItems</c> with <c>ipairs</c>.
    /// Reading the walk itself, rather than the host's feature ids, keeps
    /// whatever that walk does (the array's numeric feature-id keys can
    /// overwrite its entries, so some features are visited twice and others
    /// not at all). Returns <see langword="null"/> when the catalogue has no
    /// such list, so the cell gets a full run.
    /// </summary>
    private static (List<string> Refs, List<string> Codes)? ReadVisits(ILuaContext lua)
    {
        var refs = new List<string>();
        var codes = new List<string>();
        lua.SetGlobal(VisitGlobal, (Action<string, string>)((id, code) =>
        {
            refs.Add(id);
            codes.Add(code);
        }));
        try
        {
            lua.Execute(
                "if portrayalContext == nil or portrayalContext.FeaturePortrayalItems == nil then return end\n" +
                "for _, item in ipairs(portrayalContext.FeaturePortrayalItems) do\n" +
                "  " + VisitGlobal + "(item.Feature.ID, item.Feature.Code)\n" +
                "end\n" +
                VisitGlobal + " = nil\n");
        }
        catch (Exception)
        {
            return null;
        }

        return refs.Count == 0 ? null : (refs, codes);
    }

    /// <summary>
    /// Runs one shard per entry of <paramref name="positions"/>: shard 0 in
    /// <paramref name="first"/>, the others each in a new state prepared the
    /// same way, at most <see cref="LuaShardOptions.MaxDegreeOfParallelism"/>
    /// at once on the thread pool. Disposes every state, including
    /// <paramref name="first"/>.
    /// </summary>
    /// <returns>Each shard's emits, in its emission order.</returns>
    private async Task<IReadOnlyList<EmittedInstruction>[]> RunShardsAsync(
        ILuaContext first,
        ILuaDataProvider firstProvider,
        List<int>[] positions,
        MarinerSettings mariner,
        IReadOnlyDictionary<string, string> luaSources,
        CancellationToken cancellationToken)
    {
        var emitted = new IReadOnlyList<EmittedInstruction>[positions.Length];
        var firstTaken = 0;
        Exception? failure = null;
        try
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(_sharding.MaxDegreeOfParallelism, 1, positions.Length),
                CancellationToken = cancellationToken,
            };
            await Parallel.ForEachAsync(Enumerable.Range(0, positions.Length), options, (shard, ct) =>
            {
                ILuaContext lua;
                ILuaDataProvider provider;
                if (shard == 0)
                {
                    Interlocked.Exchange(ref firstTaken, 1);
                    (lua, provider) = (first, firstProvider);
                }
                else
                {
                    provider = _providerFactory.Create(mariner);
                    lua = _luaEngine.CreateContext();
                }

                using (lua)
                {
                    try
                    {
                        if (shard != 0)
                        {
                            Prepare(lua, provider, mariner, luaSources);
                        }

                        SelectShard(lua, positions[shard]);
                        RunPortrayalMain(lua, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A failing shard cancels the others; remember the
                        // failure so it, not their cancellation, surfaces.
                        Interlocked.CompareExchange(ref failure, ex, null);
                        throw;
                    }
                }

                emitted[shard] = provider.EmittedInstructions;
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (Exception) when (Volatile.Read(ref failure) is { } shardFailure && !cancellationToken.IsCancellationRequested)
        {
            ExceptionDispatchInfo.Throw(shardFailure);
        }
        finally
        {
            if (Volatile.Read(ref firstTaken) == 0)
            {
                first.Dispose();
            }
        }

        return emitted;
    }

    /// <summary>
    /// Narrows a prepared state's <c>FeaturePortrayalItems</c> to one
    /// shard's visits, in visit order, so <c>PortrayalMain</c>'s own loop
    /// portrays just those (and emits their <c>TextPlacement</c> features
    /// last, as a full run does).
    /// </summary>
    /// <remarks>
    /// Rules may still look across the whole cell through
    /// <c>portrayalContext:GetFeatures(code)</c> (the S-401 <c>MARSYS01</c>
    /// rule looks for the enclosing <c>NavigationalSystemOfMarks</c>), so
    /// that keeps answering from every feature.
    /// </remarks>
    private static void SelectShard(ILuaContext lua, List<int> positions)
    {
        var luaPositions = new List<object>(positions.Count);
        foreach (var p in positions)
        {
            luaPositions.Add((double)(p + 1));
        }

        lua.SetGlobal(ShardPositionsGlobal, (Func<List<object>>)(() => luaPositions));
        lua.Execute(
            "local all = portrayalContext.FeaturePortrayalItems\n" +
            "local visits = {}\n" +
            "for i, item in ipairs(all) do visits[i] = item end\n" +
            "local shard = { Type = all.Type, AddFeature = all.AddFeature }\n" +
            "for _, p in ipairs(" + ShardPositionsGlobal + "()) do shard[#shard + 1] = visits[p] end\n" +
            "portrayalContext.FeaturePortrayalItems = shard\n" +
            "local getFeatures = portrayalContext.GetFeatures\n" +
            "if getFeatures ~= nil then\n" +
            "  portrayalContext.GetFeatures = function(self, ...)\n" +
            "    self.FeaturePortrayalItems = all\n" +
            "    local ok, result = pcall(getFeatures, self, ...)\n" +
            "    self.FeaturePortrayalItems = shard\n" +
            "    if not ok then error(result, 0) end\n" +
            "    return result\n" +
            "  end\n" +
            "end\n" +
            ShardPositionsGlobal + " = nil\n");
    }

    private const string VisitGlobal = "__EncDotNetVisit";
    private const string ShardPositionsGlobal = "__EncDotNetShardPositions";

    /// <summary>
    /// Makes a running <c>PortrayalMain</c> stop soon after
    /// <paramref name="cancellationToken"/> is cancelled. The S-100 Part 9a
    /// rules hand each feature's drawing instructions to the global
    /// <c>HostPortrayalEmit</c>, so wrapping that global with a token check
    /// gives one check per feature without touching the interpreter. A cell's
    /// rules can run for several seconds, so without this a cancelled load kept
    /// a core busy until they finished.
    /// </summary>
    /// <remarks>
    /// Lua-CSharp also checks the token passed to <c>PortrayalMain</c> between
    /// instructions, so on that engine this is a second, coarser check.
    /// MoonSharp has no interrupt, so there it's the only one. Keep it until
    /// the work scheduler shows whether it needs a finer yield point (map
    /// render design, slice 5).
    /// </remarks>
    private static void InstallCancellationCheck(ILuaContext lua, CancellationToken cancellationToken)
    {
        lua.SetGlobal(CancellationCheckGlobal, (Action)cancellationToken.ThrowIfCancellationRequested);
        lua.Execute(
            "local emit = HostPortrayalEmit\n" +
            "if emit ~= nil then\n" +
            "  HostPortrayalEmit = function(...) " + CancellationCheckGlobal + "() return emit(...) end\n" +
            "end\n");
    }

    private const string CancellationCheckGlobal = "__EncDotNetCheckCancelled";

    // 8. Collect + dedup. PortrayalModel.lua AddFeature() stores items under
    //    both array append and feature-ID index, so ipairs() may visit some
    //    twice; dedup by (FeatureRef, InstructionString).
    internal static List<EmittedInstruction> Deduplicate(IReadOnlyList<EmittedInstruction> emitted, int from)
    {
        var seen = new HashSet<(string, string)>();
        var results = new List<EmittedInstruction>();
        for (var i = from; i < emitted.Count; i++)
        {
            var e = emitted[i];
            if (seen.Add((e.FeatureRef, e.InstructionString)))
            {
                results.Add(e);
            }
        }

        return results;
    }

    // Steps 1–6: a Lua state ready for PortrayalMain.
    private void Prepare(ILuaContext lua, ILuaDataProvider provider, MarinerSettings mariner, IReadOnlyDictionary<string, string> luaSources)
    {
        // 1. Resolve require() modules from the pre-loaded snapshot.
        //    Normalise the module name here (an engine may pass it bare or
        //    with a .lua extension) so each catalogue need not repeat the logic.
        lua.SetModuleLoader(moduleName =>
        {
            var fileName = moduleName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                ? moduleName
                : $"{moduleName}.lua";
            return luaSources.TryGetValue(fileName, out var src) ? src : null;
        });

        // 2. Register the product's Host* functions.
        provider.RegisterHostFunctions(lua);

        // 3. Load main.lua (which require()s the S-100 scripting framework).
        if (!luaSources.TryGetValue("main.lua", out var mainSource))
        {
            throw new InvalidOperationException(
                $"{_productTag} portrayal catalogue is missing required rule file 'main.lua'.");
        }
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
    }

    private void RecordPerFeatureTypeCardinality(
        IReadOnlyList<EmittedInstruction> emitted, ILuaDataProvider provider)
    {
        if (emitted.Count == 0) return;

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var e in emitted)
        {
            var code = provider.GetFeatureTypeCode(e.FeatureRef) ?? "(unknown)";
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
                $"{_productTag} Lua portrayal failed loading {what}: {lua.DescribeError(ex)}", ex);
        }
    }
}
