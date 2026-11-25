using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests.Reference;

/// <summary>
/// <see cref="IRuleTraceSource.TraceFeatureAsync"/> on an S-57 cell drawn with
/// S-101's catalogue: US5MA1BO's wreck made a red stake
/// (<see cref="SyntheticS57Cell.WriteWithLateralBeacon"/>) (issue #1719).
/// </summary>
public sealed class RuleTraceSourceTests
{
    [Theory]
    [InlineData(true, "BCNLAT21")]
    [InlineData(false, "BCNSTK02")]
    public async Task A_feature_is_traced_without_its_rule_files_load_time_lines(bool simplified, string symbol)
    {
        var directory = Directory.CreateTempSubdirectory("rule-trace-").FullName;
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S57DatasetProcessor(
            SyntheticS57Cell.WriteWithLateralBeacon(directory, "US5MA1BO.000"),
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
        var beacon = processor.EnumerateFeatures().Single(f => f.FeatureType == "LateralBeacon").FeatureRef;

        var trace = await ((IRuleTraceSource)processor).TraceFeatureAsync(
            beacon,
            TestLuaEngine.Create(LuaEngineKind.LuaCSharp),
            MarinerSettings.Default with { SimplifiedSymbols = simplified },
            TestContext.Current.CancellationToken);

        Assert.NotNull(trace);
        Assert.Equal(beacon, trace.FeatureId);
        Assert.Contains(trace.Instructions, i => i is PointInstruction p && p.SymbolReference == symbol);

        // The use that drew the symbol ran.
        var index = await ChartReferenceFixture.GetAsync("S-101");
        Assert.True(index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, "LateralBeacon"), out var entry));
        var uses = ((FeatureTypeEntry)entry).Uses.Where(u => u.ItemId == symbol && u.RuleFile == "LateralBeacon");
        Assert.Contains(uses, u => trace.Lines.Contains(new LuaTraceLine("LateralBeacon", u.Line)));

        // The rule file's top level (its requires and function definition)
        // ran when it was loaded, which the warm-up portrayal did.
        var source = (await LateralBeaconSourceAsync()).Split('\n');
        var topLevel = Enumerable.Range(1, source.Length)
            .Where(line => source[line - 1].StartsWith("require", StringComparison.Ordinal)
                || source[line - 1].StartsWith("function", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(topLevel);
        Assert.DoesNotContain(trace.Lines, l => l.File == "LateralBeacon" && topLevel.Contains(l.Line));
    }

    [Fact]
    public async Task Tracing_a_cancelled_feature_throws()
    {
        var directory = Directory.CreateTempSubdirectory("rule-trace-").FullName;
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S57DatasetProcessor(
            SyntheticS57Cell.WriteWithLateralBeacon(directory, "US5MA1BO.000"),
            catalogues,
            TestLuaEngine.Create(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ((IRuleTraceSource)processor).TraceFeatureAsync(
            "1", TestLuaEngine.Create(LuaEngineKind.LuaCSharp), MarinerSettings.Default, new CancellationToken(canceled: true)));
    }

    private static async Task<string> LateralBeaconSourceAsync()
    {
        var provider = await PortrayalCatalogueProvider.OpenAsync(Specification.CreatePortrayalCatalogueSource("S-101"));
        var catalogue = new S101PortrayalCatalogue(provider, TestLuaEngine.Create());
        return await catalogue.GetLuaSourceAsync("LateralBeacon.lua") ?? throw new InvalidOperationException("No LateralBeacon.lua.");
    }
}
