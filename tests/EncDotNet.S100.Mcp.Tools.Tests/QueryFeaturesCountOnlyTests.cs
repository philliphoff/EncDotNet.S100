using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>query_features with countOnly, which took over count_features (#1609).</summary>
public class QueryFeaturesCountOnlyTests
{
    private static readonly IReadOnlyDictionary<ushort, string> SolentTypes =
        new Dictionary<ushort, string> { [75] = "LIGHTS", [17] = "BOYLAT" }.ToDictionary();

    private static LoadedDataset SolentCell(string id) =>
        LoadedDatasetFactory.S101(id, S101Synth.DatasetWithPointFeatures(
            id,
            new (uint, ushort, double, double)[]
            {
                (100u, 75, 0.50, 0.50),  // LIGHTS
                (101u, 75, 0.55, 0.55),  // LIGHTS
                (102u, 75, 0.60, 0.60),  // LIGHTS
                (200u, 17, 0.80, -0.40), // BOYLAT
            },
            SolentTypes));

    [Fact]
    public async Task Counts_types_per_dataset_ordered_by_count_descending()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(SolentCell("enc"));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(null, CountOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(4, value.TotalCount);
        Assert.Equal(2, value.Counts!.Count);
        Assert.Empty(value.Features);
        Assert.False(value.HasMore);
        Assert.Equal(1, value.DatasetCount);

        Assert.Equal("LIGHTS", value.Counts![0].FeatureType);
        Assert.Equal(3, value.Counts![0].Count);
        Assert.Equal(3, value.Counts![0].WithGeometry);
        Assert.Equal("BOYLAT", value.Counts![1].FeatureType);
        Assert.Equal(1, value.Counts![1].Count);
    }

    [Fact]
    public async Task Dataset_filter_restricts_to_one_dataset()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(SolentCell("a"));
        catalog.Add(SolentCell("b"));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(null, Dataset: new DatasetId("b"), CountOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1, value.DatasetCount);
        Assert.All(value.Counts!, t => Assert.Equal("b", t.DatasetId.Value));
    }

    [Fact]
    public async Task Spec_filter_restricts_to_matching_spec()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(SolentCell("enc"));
        catalog.Add(LoadedDatasetFactory.S124("warn", S124Synth.Dataset(S124Synth.Feature("w1"))));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(null, Spec: new SpecRef("S-101", default), CountOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(1, value.DatasetCount);
        Assert.All(value.Counts!, t => Assert.Equal("S-101", t.Spec.Name));
    }

    [Fact]
    public async Task Spatial_query_filters_features_and_excludes_geometryless()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(SolentCell("enc"));
        var tool = new QueryFeaturesTool(catalog);

        // Box covers the three LIGHTS (lon 0.5..0.6) but not the BOYLAT (lon -0.4).
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 1, 1)), CountOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var tally = Assert.Single(value.Counts!);
        Assert.Equal("LIGHTS", tally.FeatureType);
        Assert.Equal(3, tally.Count);
        Assert.Equal(3, tally.WithGeometry);
        Assert.Equal(3, value.TotalCount);
    }

    [Fact]
    public async Task Coverage_products_contribute_no_tallies()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("depth"));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(null, CountOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Counts!);
        Assert.Equal(0, value.TotalCount);
        Assert.Equal(0, value.DatasetCount);
    }

    [Fact]
    public async Task Geometryless_features_counted_but_not_with_geometry()
    {
        // GML container-style features expose a geometry primitive label but
        // carry no resolved coordinates, so they are counted yet contribute
        // nothing to WithGeometry. (S-101 features without resolvable geometry
        // are dropped by the vector source and never reach the accessor.)
        var model = S124Synth.Dataset(
            S124Synth.Feature("w1"),
            S124Synth.Feature("w2"));

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", model));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(null, CountOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var tally = Assert.Single(value.Counts!);
        Assert.Equal("NavwarnPart", tally.FeatureType);
        Assert.Equal(2, tally.Count);
        Assert.Equal(0, tally.WithGeometry);
    }

    [Fact]
    public async Task Feature_type_and_attribute_filters_apply_to_counts()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(SolentCell("enc"));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(null, FeatureType: "BOYLAT", CountOnly: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var tally = Assert.Single(value.Counts!);
        Assert.Equal("BOYLAT", tally.FeatureType);
        Assert.Equal(1, value.TotalCount);
        Assert.Equal("BOYLAT", Assert.Single(value.TypeBreakdown).FeatureType);
    }

    [Fact]
    public async Task Without_countOnly_a_query_is_required()
    {
        var tool = new QueryFeaturesTool(new FakeDatasetCatalog());

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(null), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("query", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task Listing_leaves_counts_out()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(SolentCell("enc"));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(new GeoQuery.Box(new GeoBoundingBox(-1, -1, 1, 1))), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(4, value.Features.Count);
        Assert.Null(value.Counts);
        Assert.Null(value.DatasetCount);
    }
}
