using System.Runtime.CompilerServices;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Portrays every S-101 and S-57 cell of the test corpora once in one Lua
/// state and again split across 4 and 8 states (map render design, slice 2),
/// and checks the rules emit exactly the same output: the same features, in
/// the same order, with the same instruction strings and observed context
/// parameters. A difference fails and names the first differing emit.
/// </summary>
/// <remarks>
/// <para>
/// Shards here are much smaller than the default
/// (<see cref="LuaShardOptions.MinFeaturesPerShard"/> of 16), so the small
/// fixtures split too and features that must share a state are likely to
/// straddle a shard boundary if the grouping misses them. The test also
/// counts the Lua states the executor opens, so a run that silently fell back
/// to a full portrayal fails.
/// </para>
/// <para>
/// Real data runs when <c>ENCDOTNET_LUA_SHARD_CORPUS</c> names a folder:
/// every <c>.000</c> cell under it. Set <c>S100_LUA_ENGINE=moonsharp</c> to
/// run the comparison on MoonSharp.
/// </para>
/// </remarks>
public sealed class LuaShardParityTests
{
    private const string CorpusVariable = "ENCDOTNET_LUA_SHARD_CORPUS";
    private const int MinFeaturesPerShard = 16;

    public static TheoryData<string, int> CorpusCells
    {
        get
        {
            var root = DatasetsRoot();
            var cells = new TheoryData<string, int>();
            foreach (var dir in new[] { "S101", "S57" })
            {
                var path = Path.Combine(root, dir);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(path, "*.000", SearchOption.AllDirectories).Order())
                {
                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    cells.Add(relative, 4);
                    cells.Add(relative, 8);
                }
            }

            return cells;
        }
    }

    public static TheoryData<string> RealCells
    {
        get
        {
            var cells = new TheoryData<string>();
            var corpus = Environment.GetEnvironmentVariable(CorpusVariable);
            if (!string.IsNullOrEmpty(corpus) && Directory.Exists(corpus))
            {
                foreach (var file in Directory.EnumerateFiles(corpus, "*.000", SearchOption.AllDirectories).Order())
                {
                    cells.Add(file);
                }
            }
            else
            {
                cells.Add("");
            }

            return cells;
        }
    }

    [Theory]
    [MemberData(nameof(CorpusCells))]
    public async Task CorpusCell_ShardedOutputEqualsFullRun(string relativePath, int shards)
    {
        await AssertShardedEqualsFullAsync(
            Path.Combine(DatasetsRoot(), relativePath), shards, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InlandCell_ShardedOutputEqualsFullRun()
    {
        // S-401 through the inland ENC catalogue: the committed NOAA fixture
        // with its DSID product specification set to inland ENC.
        var dir = Directory.CreateTempSubdirectory("lua-shard-inland-").FullName;
        try
        {
            var path = SyntheticS57Cell.Write(
                dir, "U37TEST.000", S57ProductSpecification.InlandElectronicNavigationalChart);
            await AssertShardedEqualsFullAsync(path, 4, TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(RealCells))]
    public async Task RealCell_ShardedOutputEqualsFullRun(string path)
    {
        Assert.SkipUnless(path.Length > 0 && File.Exists(path), $"{CorpusVariable} is not set to a folder of cells.");
        await AssertShardedEqualsFullAsync(path, 8, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SmallCell_GetsOneFullRun()
    {
        var path = Directory.EnumerateFiles(Path.Combine(DatasetsRoot(), "S101"), "*.000", SearchOption.AllDirectories)
            .Order().First();
        var cancellationToken = TestContext.Current.CancellationToken;

        // A cell below twice the minimum shard size isn't worth a second state.
        var sharded = await PortrayAsync(
            path, new LuaShardOptions { MaxShards = 8, MinFeaturesPerShard = 1_000_000 }, cancellationToken);
        Assert.Equal(1, sharded.Contexts);
    }

    [Fact]
    public void Options_ForProduct_HonoursTheOffSwitch()
    {
        var options = new LuaShardOptions
        {
            MaxShards = 4,
            DisabledProducts = new HashSet<string>(["S-401"], StringComparer.OrdinalIgnoreCase),
        };

        Assert.Same(options, options.ForProduct("S-101"));
        Assert.False(options.ForProduct("s-401").IsEnabled);
        Assert.False(LuaShardOptions.Disabled.ForProduct("S-101").IsEnabled);
    }

    private static async Task AssertShardedEqualsFullAsync(string path, int shards, CancellationToken cancellationToken)
    {
        var cell = Path.GetFileName(path);
        var full = await PortrayAsync(path, sharding: null, cancellationToken);
        Assert.SkipWhen(full.Error is not null, $"{cell}: the rules fail on a full run ({full.Error?.Message}).");
        Assert.Equal(1, full.Contexts);

        var sharded = await PortrayAsync(
            path, new LuaShardOptions { MaxShards = shards, MinFeaturesPerShard = MinFeaturesPerShard }, cancellationToken);
        Assert.Null(sharded.Error);

        var expectedStates = Math.Clamp(full.Emitted.Count / MinFeaturesPerShard, 1, shards);
        Assert.True(
            sharded.Contexts > 1 || expectedStates == 1,
            $"{cell}: expected the run to split, but it opened {sharded.Contexts} Lua state(s).");
        Assert.True(
            sharded.Contexts <= shards,
            $"{cell}: the run opened {sharded.Contexts} Lua states for {shards} shards, so it fell back to a full run.");

        var count = Math.Min(full.Emitted.Count, sharded.Emitted.Count);
        for (var i = 0; i < count; i++)
        {
            var a = full.Emitted[i];
            var b = sharded.Emitted[i];
            if (a.FeatureRef != b.FeatureRef || a.InstructionString != b.InstructionString ||
                a.ObservedParameters != b.ObservedParameters)
            {
                Assert.Fail(
                    $"{cell} ({sharded.Contexts} shards): emit {i} differs.\n" +
                    $"  full:    feature {a.FeatureRef}: {a.InstructionString} ‖ {a.ObservedParameters}\n" +
                    $"  sharded: feature {b.FeatureRef}: {b.InstructionString} ‖ {b.ObservedParameters}");
            }
        }

        Assert.True(
            full.Emitted.Count == sharded.Emitted.Count,
            $"{cell} ({sharded.Contexts} shards): full run emitted {full.Emitted.Count}, sharded {sharded.Emitted.Count}.");
    }

    private static async Task<(IReadOnlyList<EmittedInstruction> Emitted, int Contexts, Exception? Error)> PortrayAsync(
        string path, LuaShardOptions? sharding, CancellationToken cancellationToken)
    {
        var (dataset, spec) = Open(path);
        var engine = new CountingLuaEngine(TestLuaEngine.Create());
        var catalogue = new S101PortrayalCatalogue(Catalogues.Value.GetProvider(spec), engine);
        await catalogue.SwitchPaletteAsync(PaletteType.Day, cancellationToken);
        var fc = FeatureCatalogues.Value.GetCatalogue(spec)
            ?? throw new InvalidOperationException($"No {spec} feature catalogue is bundled.");

        var executor = new S101LuaRuleExecutor(engine, dataset, catalogue, fc, depthShift: null, sharding);
        var before = engine.Contexts;
        try
        {
            var emitted = await executor.ExecuteRawAsync(MarinerSettings.Default, cancellationToken);
            return (emitted, engine.Contexts - before, null);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Lua portrayal failed", StringComparison.Ordinal))
        {
            return ([], engine.Contexts - before, ex);
        }
    }

    private static (S101Dataset Dataset, string Spec) Open(string path)
    {
        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        if (spec == "S-57")
        {
            var s57 = S57Dataset.Open(path);
            var target = Catalogues.Value.HasCatalogue(s57.TranslationTarget.Spec)
                ? s57.TranslationTarget
                : S57TranslationTarget.S101;
            return (S101Dataset.FromDocument(S57ToS101Translator.ForTarget(target).Translate(s57)), target.Spec);
        }

        return (S101Dataset.Open(path), spec ?? "S-101");
    }

    private static readonly Lazy<PortrayalCatalogueManager> Catalogues = new(() =>
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
            {
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
            }
        }

        return manager;
    });

    private static readonly Lazy<FeatureCatalogueManager> FeatureCatalogues =
        new(() => new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

    private static string DatasetsRoot([CallerFilePath] string callerFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets"));

    /// <summary>Counts the Lua states an engine opens.</summary>
    private sealed class CountingLuaEngine(ILuaEngine inner) : ILuaEngine
    {
        private int _contexts;

        public int Contexts => Volatile.Read(ref _contexts);

        public ILuaContext CreateContext()
        {
            Interlocked.Increment(ref _contexts);
            return inner.CreateContext();
        }
    }
}
