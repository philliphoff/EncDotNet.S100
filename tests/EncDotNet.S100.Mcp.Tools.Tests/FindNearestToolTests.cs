using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class FindNearestToolTests
{
    private static S124Feature PointFeature(
        string id,
        double lat,
        double lon,
        string featureType = "NavwarnPart",
        IDictionary<string, string>? attributes = null)
    {
        return new S124Feature
        {
            Id = id,
            FeatureType = featureType,
            GeometryType = GmlGeometryType.Point,
            Points = ImmutableArray.Create((lat, lon)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = (attributes ?? new Dictionary<string, string>()).ToImmutableDictionary(),
            ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
        };
    }

    private static FakeDatasetCatalog CatalogWith(params S124Feature[] features)
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124(
            "a",
            S124Synth.Dataset(features),
            bounds: LoadedDatasetFactory.Box(-90, -180, 90, 180)));
        return catalog;
    }

    [Fact]
    public async Task Empty_catalog_returns_no_features()
    {
        var tool = new FindNearestTool(new FakeDatasetCatalog());

        var result = await tool.InvokeAsync(new FindNearestRequest(0, 0));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
        Assert.Equal(0, value.TotalConsidered);
    }

    [Fact]
    public async Task Results_are_ordered_closest_first()
    {
        var near = PointFeature("near", 0.10, 0.10);
        var mid = PointFeature("mid", 0.50, 0.50);
        var far = PointFeature("far", 1.00, 1.00);
        var tool = new FindNearestTool(CatalogWith(far, near, mid));

        var result = await tool.InvokeAsync(new FindNearestRequest(0, 0));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(new[] { "near", "mid", "far" }, value.Features.Select(f => f.FeatureId).ToArray());
        Assert.Equal(3, value.TotalConsidered);
        // Distances are strictly increasing along the ranking.
        Assert.True(value.Features[0].DistanceMeters < value.Features[1].DistanceMeters);
        Assert.True(value.Features[1].DistanceMeters < value.Features[2].DistanceMeters);
    }

    [Fact]
    public async Task Point_inside_feature_bounds_reports_zero_distance()
    {
        var catalog = new FakeDatasetCatalog();
        var area = new S124Feature
        {
            Id = "area",
            FeatureType = "NavigationalMeteorologicalArea",
            GeometryType = GmlGeometryType.Surface,
            Points = default,
            Curves = default,
            ExteriorRing = ImmutableArray.Create((0.0, 0.0), (0.0, 10.0), (10.0, 10.0), (10.0, 0.0), (0.0, 0.0)),
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
        };
        catalog.Add(LoadedDatasetFactory.S124(
            "a", S124Synth.Dataset(area), bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        var tool = new FindNearestTool(catalog);

        var result = await tool.InvokeAsync(new FindNearestRequest(5, 5));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal(0.0, value.Features[0].DistanceMeters);
    }

    [Fact]
    public async Task MaxResults_truncates_but_total_considered_reflects_all_matches()
    {
        var features = Enumerable.Range(1, 5)
            .Select(i => PointFeature($"f{i}", 0.1 * i, 0.1 * i))
            .ToArray();
        var tool = new FindNearestTool(CatalogWith(features));

        var result = await tool.InvokeAsync(new FindNearestRequest(0, 0, MaxResults: 2));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.Features.Length);
        Assert.Equal(5, value.TotalConsidered);
        Assert.Equal(new[] { "f1", "f2" }, value.Features.Select(f => f.FeatureId).ToArray());
    }

    [Fact]
    public async Task MaxDistance_excludes_features_beyond_the_cap()
    {
        var near = PointFeature("near", 0.001, 0.001); // ~157 m from origin
        var far = PointFeature("far", 1.0, 1.0);       // ~157 km from origin
        var tool = new FindNearestTool(CatalogWith(near, far));

        var result = await tool.InvokeAsync(new FindNearestRequest(0, 0, MaxDistanceMeters: 1_000));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("near", value.Features[0].FeatureId);
        Assert.Equal(1, value.TotalConsidered);
    }

    [Fact]
    public async Task FeatureType_filter_is_applied()
    {
        var warn = PointFeature("warn", 0.1, 0.1, featureType: "NavwarnPart");
        var other = PointFeature("other", 0.05, 0.05, featureType: "NavwarnPreamble");
        var tool = new FindNearestTool(CatalogWith(warn, other));

        var result = await tool.InvokeAsync(new FindNearestRequest(0, 0, FeatureType: "NavwarnPart"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("warn", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Attribute_filter_narrows_results()
    {
        var match = PointFeature("match", 0.5, 0.5,
            attributes: new Dictionary<string, string> { ["status"] = "1" });
        var nonMatch = PointFeature("nonMatch", 0.1, 0.1,
            attributes: new Dictionary<string, string> { ["status"] = "2" });
        var tool = new FindNearestTool(CatalogWith(match, nonMatch));

        var filter = new AttributeFilter(ImmutableArray.Create(new AttributePredicate("status", "1")));
        var result = await tool.InvokeAsync(new FindNearestRequest(0, 0, Attributes: filter));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("match", value.Features[0].FeatureId);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public async Task Out_of_range_coordinates_are_rejected(double lat, double lon)
    {
        var tool = new FindNearestTool(new FakeDatasetCatalog());

        var result = await tool.InvokeAsync(new FindNearestRequest(lat, lon));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.Equal("invalid_argument", err!.Code);
    }

    [Fact]
    public async Task MaxResults_is_clamped_to_upper_bound()
    {
        var features = Enumerable.Range(1, 3)
            .Select(i => PointFeature($"f{i}", 0.1 * i, 0.1 * i))
            .ToArray();
        var tool = new FindNearestTool(CatalogWith(features));

        // A wildly large maxResults must not throw; it clamps and returns
        // every available match.
        var result = await tool.InvokeAsync(new FindNearestRequest(0, 0, MaxResults: 10_000));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.Features.Length);
    }

    [Fact]
    public async Task Coverage_specs_contribute_no_features()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("bathymetry"));
        var tool = new FindNearestTool(catalog);

        var result = await tool.InvokeAsync(new FindNearestRequest(0.02, 0.02));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
    }
}
