using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests.Reference;

/// <summary>
/// <see cref="LuaRuleExecutor.OpenTraceSessionAsync"/> on an S-101 lateral
/// beacon: US5MA1BO's wreck made a red stake
/// (<see cref="SyntheticS57Cell.WriteWithLateralBeacon"/>) and translated to
/// S-101. Its trace names lines the way the chart reference's rule reader
/// does, so each symbol use's line and conditions can be found in it
/// (issue #1718).
/// </summary>
public sealed class LuaTraceSessionTests
{
    private static readonly Lazy<Task<Beacon>> Fixture = new(OpenAsync);

    private sealed record Beacon(
        S101LuaRuleExecutor Executor, string FeatureId, IReadOnlyDictionary<string, string> RuleSources);

    [Theory]
    [InlineData(true, "BCNLAT21")]
    [InlineData(false, "BCNSTK02")]
    public async Task Trace_includes_the_lines_the_rule_reader_recorded_for_the_drawn_symbol(bool simplified, string symbol)
    {
        var beacon = await Fixture.Value;
        var index = await ChartReferenceFixture.GetAsync("S-101");
        var mariner = MarinerSettings.Default with { SimplifiedSymbols = simplified };

        // Tracing runs on LuaCSharp whichever engine the tests render with.
        using var session = await beacon.Executor.OpenTraceSessionAsync(
            TestLuaEngine.Create(LuaEngineKind.LuaCSharp), mariner, TestContext.Current.CancellationToken);
        var trace = Assert.Single(session.Trace([beacon.FeatureId]));

        Assert.Equal(beacon.FeatureId, trace.FeatureId);
        Assert.Contains(trace.Instructions, i => i is PointInstruction p && p.SymbolReference == symbol);

        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, "LateralBeacon"), out var entry));
        var feature = Assert.IsType<FeatureTypeEntry>(entry);
        var uses = feature.Uses.Where(u => u.ItemId == symbol && u.RuleFile == "LateralBeacon").ToList();
        Assert.NotEmpty(uses);

        // The symbol is drawn by one of the branches recorded for it: the one
        // whose AddInstructions line ran, and whose conditions' lines ran.
        // Lua runs no instruction for an 'else', so the hook never reports
        // the line the reader gives an else branch's conditions; the line
        // the branch draws on shows it was taken.
        var source = beacon.RuleSources["LateralBeacon.lua"].Split('\n');
        var ran = trace.Lines.ToHashSet();
        var drawn = Assert.Single(uses, u => ran.Contains(new LuaTraceLine("LateralBeacon", u.Line)));
        Assert.Contains($"PointInstruction:{symbol}", source[drawn.Line - 1], StringComparison.Ordinal);
        Assert.NotEmpty(drawn.Conditions);
        foreach (var condition in drawn.Conditions)
        {
            if (source[condition.Line - 1].Trim() == "else")
            {
                Assert.DoesNotContain(new LuaTraceLine("LateralBeacon", condition.Line), ran);
            }
            else
            {
                Assert.Contains(new LuaTraceLine("LateralBeacon", condition.Line), ran);
            }
        }

        // Shared rules the beacon's rule requires are traced under their own names.
        Assert.All(trace.Lines, l => Assert.True(beacon.RuleSources.ContainsKey(l.File + ".lua"), l.File));
        Assert.Contains(trace.Lines, l => l.File == "main");
    }

    [Fact]
    public async Task Trace_session_on_an_engine_without_a_line_hook_is_not_supported()
    {
        var beacon = await Fixture.Value;

        await Assert.ThrowsAsync<NotSupportedException>(() => beacon.Executor.OpenTraceSessionAsync(
            TestLuaEngine.Create(LuaEngineKind.MoonSharp), MarinerSettings.Default, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Trace_returns_one_trace_per_feature_in_order()
    {
        var beacon = await Fixture.Value;
        using var session = await beacon.Executor.OpenTraceSessionAsync(
            TestLuaEngine.Create(LuaEngineKind.LuaCSharp), MarinerSettings.Default, TestContext.Current.CancellationToken);

        var traces = session.Trace([beacon.FeatureId, beacon.FeatureId]);

        // The first portrayal also loads the beacon's rule file, so its
        // trace starts with the file's top-level lines; after those, both
        // run the same lines of the rule.
        Assert.Equal(2, traces.Count);
        Assert.All(traces, t => Assert.Equal(beacon.FeatureId, t.FeatureId));
        var first = traces[0].Lines.Where(l => l.File == "LateralBeacon").ToList();
        var second = traces[1].Lines.Where(l => l.File == "LateralBeacon").ToList();
        Assert.NotEmpty(second);
        Assert.Equal(second, first[^second.Count..]);
        Assert.Equal(
            traces[0].Instructions.OfType<PointInstruction>().Select(p => p.SymbolReference),
            traces[1].Instructions.OfType<PointInstruction>().Select(p => p.SymbolReference));
    }

    private static async Task<Beacon> OpenAsync()
    {
        var directory = Directory.CreateTempSubdirectory("lua-trace-").FullName;
        var path = SyntheticS57Cell.WriteWithLateralBeacon(directory, "US5MA1BO.000");
        var dataset = S101Dataset.FromDocument(new S57ToS101Translator().Translate(S57Dataset.Open(path)));
        var featureId = new S101VectorSource(dataset).GetFeatures()
            .Single(f => f.FeatureType == "LateralBeacon").Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var fcStream = Specification.TryOpenFeatureCatalogue("S-101")
            ?? throw new InvalidOperationException("S-101 feature catalogue not bundled.");
        var featureCatalogue = FeatureCatalogueReader.Read(fcStream);
        var provider = await PortrayalCatalogueProvider.OpenAsync(Specification.CreatePortrayalCatalogueSource("S-101"));
        var engine = TestLuaEngine.Create();
        var catalogue = new S101PortrayalCatalogue(provider, engine);

        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in await catalogue.GetLuaSourceNamesAsync())
        {
            if (await catalogue.GetLuaSourceAsync(name) is { } source) sources[name] = source;
        }

        return new Beacon(new S101LuaRuleExecutor(engine, dataset, catalogue, featureCatalogue), featureId, sources);
    }
}
