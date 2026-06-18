using System.Collections.Immutable;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class QueryFeaturesToolS101Tests
{
    private static FakeDatasetCatalog CatalogWithSolentCell(string id = "enc")
    {
        // Two point features inside the default (-1,-1,1,1) dataset box.
        var featureTypes = new Dictionary<ushort, string>
        {
            [75] = "LIGHTS",
            [17] = "BOYLAT",
        }.ToImmutableDictionary();

        var ds = S101Synth.DatasetWithPointFeatures(
            id,
            new (uint, ushort, double, double)[]
            {
                (100u, 75, 0.50, 0.50), // LIGHTS
                (200u, 17, 0.80, -0.40), // BOYLAT
            },
            featureTypes);

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101(id, ds));
        return catalog;
    }

    [Fact]
    public async Task Box_query_returns_S101_features_with_resolved_bounds()
    {
        var catalog = CatalogWithSolentCell();
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(-1, -1, 1, 1))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);

        var light = Assert.Single(value.Features, f => f.FeatureId == "100");
        Assert.Equal("S-101", light.Spec.Name);
        Assert.Equal("LIGHTS", light.FeatureType);
        Assert.NotNull(light.Bounds);
        Assert.Equal(0.50, light.Bounds!.SouthLatitude, 6);
        Assert.Equal(0.50, light.Bounds.NorthLatitude, 6);
        Assert.Equal(0.50, light.Bounds.WestLongitude, 6);
        Assert.Equal(0.50, light.Bounds.EastLongitude, 6);
    }

    [Fact]
    public async Task Point_query_matches_only_the_feature_at_that_point()
    {
        var catalog = CatalogWithSolentCell();
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Point(new GeoPoint(0.50, 0.50))));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("100", match.FeatureId);
    }

    [Fact]
    public async Task FeatureType_filter_matches_S101_acronym()
    {
        var catalog = CatalogWithSolentCell();
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(-1, -1, 1, 1)),
            FeatureType: "BOYLAT"));

        Assert.True(result.TryGetValue(out var value));
        var match = Assert.Single(value.Features);
        Assert.Equal("200", match.FeatureId);
        Assert.Equal("BOYLAT", match.FeatureType);
    }

    [Fact]
    public async Task Spec_filter_for_S101_selects_only_S101_datasets()
    {
        var catalog = CatalogWithSolentCell();
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(-1, -1, 1, 1)),
            Spec: new Core.SpecRef("S-101", default)));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);
    }

    [Fact]
    public async Task Geometryless_S101_feature_is_excluded()
    {
        // A feature whose spatial point record is absent resolves to no
        // coordinates and must not appear in results.
        var featureTypes = new Dictionary<ushort, string> { [75] = "LIGHTS" }.ToImmutableDictionary();
        var orphan = S101Synth.Feature(rcid: 999, featureTypeCode: 75, spatialRcnm: 110);
        var ds = S101Synth.Dataset("enc", ImmutableArray.Create(orphan), featureTypes);

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101("enc", ds));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(-1, -1, 1, 1))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
    }
}
