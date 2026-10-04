using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// S-98 Rules R-104-A / R-111-A (docs/design/s98-interoperability.md §3.3):
/// the S-104 water-level colour band sits on
/// <see cref="S98DisplayPlane.OnDemandSurface"/> — above the ENC's area fills
/// (<see cref="S98DisplayPlane.BaseChartUnder"/>) and below its line work,
/// points, and text (<see cref="S98DisplayPlane.BaseChartOver"/>) — and the
/// S-111 arrows sit above the ENC line work on
/// <see cref="S98DisplayPlane.DynamicArrows"/>. The stack is built from the
/// real processors' sub-layers, so an ENC that lands as a single layer (the
/// S-57 regression where DepthArea fills painted over the S-104 band) fails.
/// </summary>
public class S98OnDemandSurfaceOrderingTests
{
    private const string S57Cell = "tests/datasets/S57/US5MA1BO/US5MA1BO.000";
    private const string S101Cell = "tests/datasets/S101/S-101/DATASET_FILES/101AA00DS0019.000";
    private const string S104Grid = "tests/datasets/S104/104US004SC1BO_20251217T12Z.h5";
    private const string S111Grid = "tests/datasets/S111/111US00_DBOFS_20260320T18Z_US4DE1BB.h5";

    private readonly InteroperabilityAuthority _authority = new();

    [Fact]
    public async Task S57_cell_splits_fills_onto_BaseChartUnder_and_linework_onto_BaseChartOver()
    {
        var result = await PortrayS57Async();

        var areas = Assert.Single(result.SubLayers, s => s.Plane == S98DisplayPlane.BaseChartUnder);
        var linework = Assert.Single(result.SubLayers, s => s.Plane == S98DisplayPlane.BaseChartOver);
        Assert.Equal(2, result.SubLayers.Count);

        Assert.Equal("s57.areas", areas.LayerKey);
        Assert.Equal("area", areas.SourceFeatureType);
        Assert.NotEmpty(areas.Instructions);
        Assert.All(areas.Instructions, i => Assert.IsType<AreaInstruction>(i));

        Assert.Equal("s57.linework", linework.LayerKey);
        Assert.NotEmpty(linework.Instructions);
        Assert.DoesNotContain(linework.Instructions, i => i is AreaInstruction);

        Assert.Equal(new[] { "s57.areas", "s57.linework" }, result.LayerNames);
    }

    [Fact]
    public async Task S104_band_paints_between_S57_fills_and_S57_linework_in_any_load_order()
    {
        var s57 = VectorItems(await PortrayS57Async(), "s57");
        var s104 = CoverageItems(await PortrayS104Async(), "s104");

        AssertOrderInEveryLoadOrder(
            [s57, s104],
            ["s57/s57.areas", "s104/s104.color-band", "s57/s57.linework"]);
    }

    [Fact]
    public async Task S104_band_is_partly_transparent_so_ENC_fills_show_through()
    {
        // Main §9.2.1: on-demand data must not obscure official colour fills.
        var band = Assert.IsType<GridCoverageSubLayer>(Assert.Single((await PortrayS104Async()).SubLayers));

        Assert.Equal(S104DatasetProcessor.ColorBandOpacity, band.Opacity);
        Assert.InRange(band.Opacity, 0.5, 0.95);
    }

    [Fact]
    public async Task S104_band_paints_between_S101_fills_and_S101_linework_in_any_load_order()
    {
        var s101 = VectorItems(await PortrayS101Async(), "s101");
        var s104 = CoverageItems(await PortrayS104Async(), "s104");

        AssertOrderInEveryLoadOrder(
            [s101, s104],
            ["s101/s101.areas", "s104/s104.color-band", "s101/s101.linework"]);
    }

    [Fact]
    public async Task S111_arrows_paint_above_ENC_linework_and_S104_band_in_any_load_order()
    {
        var s57 = VectorItems(await PortrayS57Async(), "s57");
        var s101 = VectorItems(await PortrayS101Async(), "s101");
        var s104 = CoverageItems(await PortrayS104Async(), "s104");
        var s111 = CoverageItems(await PortrayS111Async(), "s111");

        foreach (var permutation in Permutations([s57, s101, s104, s111]))
        {
            var keys = Keys(LayerStackBuilder.Build(_authority, permutation));

            // Planes: fills (0) < S-104 band (20) < line work (30) < arrows (60).
            Assert.True(keys.IndexOf("s57/s57.areas") < keys.IndexOf("s104/s104.color-band"));
            Assert.True(keys.IndexOf("s101/s101.areas") < keys.IndexOf("s104/s104.color-band"));
            Assert.True(keys.IndexOf("s104/s104.color-band") < keys.IndexOf("s57/s57.linework"));
            Assert.True(keys.IndexOf("s104/s104.color-band") < keys.IndexOf("s101/s101.linework"));
            Assert.Equal("s111/s111.arrows", keys[^1]);
        }
    }

    [Fact]
    public async Task S111_emits_no_colour_band_that_could_cover_the_ENC()
    {
        // The bundled S-111 Ed 2.0.0 portrayal catalogue defines arrows only;
        // if a colour band is ever reintroduced it must go on OnDemandSurface
        // (R-111-A), never above the ENC line work.
        var result = await PortrayS111Async();

        Assert.All(result.SubLayers, s => Assert.True(
            s.Plane is S98DisplayPlane.OnDemandSurface or S98DisplayPlane.DynamicArrows,
            $"{s.LayerKey} landed on {s.Plane}"));
        Assert.Contains(result.SubLayers, s => s.Plane == S98DisplayPlane.DynamicArrows);
    }

    private void AssertOrderInEveryLoadOrder(
        IReadOnlyList<IReadOnlyList<SubLayerStackItem>> datasets,
        IReadOnlyList<string> expectedBottomFirst)
    {
        foreach (var permutation in Permutations(datasets))
        {
            Assert.Equal(expectedBottomFirst, Keys(LayerStackBuilder.Build(_authority, permutation)));
        }
    }

    private static List<string> Keys(IReadOnlyList<SubLayerStackItem> sorted)
        => sorted.Select(item => $"{item.SourceDatasetId}/{LayerKey(item.Payload)}").ToList();

    private static string LayerKey(StackPayload payload) => payload switch
    {
        VectorStackPayload vector => vector.SubLayer.LayerKey,
        CoverageStackPayload coverage => coverage.SubLayer.LayerKey,
        _ => throw new InvalidOperationException($"Unexpected payload {payload.GetType().Name}."),
    };

    private static IReadOnlyList<SubLayerStackItem> VectorItems(VectorPortrayalResult result, string datasetId)
        => result.SubLayers
            .Select(sub => new SubLayerStackItem(
                new VectorStackPayload(result, sub), sub.Plane, sub.WithinPlanePriority, datasetId, sub.SourceFeatureType))
            .ToArray();

    private static IReadOnlyList<SubLayerStackItem> CoverageItems(CoveragePortrayalResult result, string datasetId)
        => result.SubLayers
            .Select(sub => new SubLayerStackItem(
                new CoverageStackPayload(result, sub), sub.Plane, sub.WithinPlanePriority, datasetId, sub.SourceFeatureType))
            .ToArray();

    private static IEnumerable<IReadOnlyList<T>> Permutations<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, index) => index != i).ToArray();
            foreach (var tail in Permutations(rest))
                yield return [items[i], .. tail];
        }
    }

    private static Task<VectorPortrayalResult> PortrayS57Async()
    {
        var path = RequireFixture(S57Cell);
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S57DatasetProcessor(
            path, catalogues, new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));
        return processor.BuildVectorPortrayalAsync(new S101RenderContext());
    }

    private static Task<VectorPortrayalResult> PortrayS101Async()
    {
        var path = RequireFixture(S101Cell);
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var processor = new S101DatasetProcessor(
            path, catalogues, new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
        return processor.BuildVectorPortrayalAsync(new S101RenderContext());
    }

    private static Task<CoveragePortrayalResult> PortrayS104Async()
    {
        var processor = new S104DatasetProcessor(RequireFixture(S104Grid), new ProjNetCrsTransformFactory());
        Assert.True(processor.IsGriddedSurface);
        return processor.BuildCoveragePortrayalAsync(new S104RenderContext());
    }

    private static async Task<CoveragePortrayalResult> PortrayS111Async()
    {
        using var catalogues = S111TestCatalogues.Create();
        using var processor = new S111DatasetProcessor(
            RequireFixture(S111Grid), catalogues, new ProjNetCrsTransformFactory());
        return await processor.BuildCoveragePortrayalAsync(new S111RenderContext());
    }

    private static string RequireFixture(string repoRelativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, repoRelativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        Assert.SkipWhen(true, $"Fixture not found: {repoRelativePath}");
        throw new InvalidOperationException("Unreachable.");
    }
}
