using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class QueryFeaturesToolTests
{
    private static S124Feature PointFeature(
        string id,
        double lat,
        double lon,
        string featureType = "NavwarnPart")
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
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
        };
    }

    private static S122Feature S122PointFeature(string id, double lat, double lon)
    {
        return new S122Feature
        {
            Id = id,
            FeatureType = "MarineProtectedArea",
            GeometryType = GmlGeometryType.Point,
            Points = ImmutableArray.Create((lat, lon)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S122ComplexAttribute>.Empty,
        };
    }

    private static S131Feature S131GeometrylessFeature(string id, string featureType)
    {
        return new S131Feature
        {
            Id = id,
            FeatureType = featureType,
            GeometryType = GmlGeometryType.None,
            Points = default,
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S131ComplexAttribute>.Empty,
            References = ImmutableArray<S131Reference>.Empty,
        };
    }

    [Fact]
    public async Task Empty_catalog_returns_no_features()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Point(new GeoPoint(0, 0))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
        Assert.Equal(0, value.TotalCount);
        Assert.False(value.HasMore);
    }

    [Fact]
    public async Task Point_query_matches_features_whose_geometry_contains_the_point()
    {
        var inside = PointFeature("inside", 5, 5);
        var outside = PointFeature("outside", 9, 9);
        var dataset = LoadedDatasetFactory.S124(
            "a",
            S124Synth.Dataset(inside, outside),
            bounds: LoadedDatasetFactory.Box(0, 0, 10, 10));
        var catalog = new FakeDatasetCatalog();
        catalog.Add(dataset);
        var tool = new QueryFeaturesTool(catalog);

        // Point queries match feature bbox; a feature with a single
        // point has a degenerate bbox at that point. The 'inside'
        // feature at (5,5) is exactly the query point and matches; the
        // 'outside' feature at (9,9) does not.
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Point(new GeoPoint(5, 5))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("inside", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Bbox_query_returns_matches_across_multiple_specs()
    {
        var s124Inside = PointFeature("s124-inside", 5, 5);
        var s124Outside = PointFeature("s124-outside", 50, 50);
        var s122Inside = S122PointFeature("s122-inside", 6, 6);

        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124(
            "warnings",
            S124Synth.Dataset(s124Inside, s124Outside),
            bounds: LoadedDatasetFactory.Box(0, 0, 100, 100)));

        var s122 = new S122Dataset
        {
            Features = ImmutableArray.Create(s122Inside),
            InformationTypes = ImmutableArray<S122InformationType>.Empty,
        };
        catalog.Add(new LoadedDataset(
            new DatasetId("mpa"),
            LoadedDatasetFactory.S122Spec,
            LoadedDatasetFactory.Box(0, 0, 10, 10),
            null,
            new EncDotNet.S100.Mcp.Tools.Catalog.S122DatasetData(s122)));

        var tool = new QueryFeaturesTool(catalog);
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.TotalCount);
        Assert.Equal(
            new[] { "s124-inside", "s122-inside" }.OrderBy(x => x),
            value.Features.Select(f => f.FeatureId).OrderBy(x => x));
    }

    [Fact]
    public async Task Geometryless_features_are_excluded_even_when_dataset_bounds_intersect()
    {
        var authority = S131GeometrylessFeature("auth-1", "Authority");
        var s131 = new S131Dataset
        {
            Features = ImmutableArray.Create(authority),
            InformationTypes = ImmutableArray<S131InformationType>.Empty,
        };
        var catalog = new FakeDatasetCatalog();
        catalog.Add(new LoadedDataset(
            new DatasetId("harbour"),
            LoadedDatasetFactory.S131Spec,
            LoadedDatasetFactory.Box(0, 0, 10, 10),
            null,
            new EncDotNet.S100.Mcp.Tools.Catalog.S131DatasetData(s131)));

        var tool = new QueryFeaturesTool(catalog);
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
    }

    [Fact]
    public async Task Spec_filter_restricts_to_matching_spec_name()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124(
            "warnings",
            S124Synth.Dataset(PointFeature("w1", 5, 5)),
            bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));

        var s122 = new S122Dataset
        {
            Features = ImmutableArray.Create(S122PointFeature("m1", 6, 6)),
            InformationTypes = ImmutableArray<S122InformationType>.Empty,
        };
        catalog.Add(new LoadedDataset(
            new DatasetId("mpa"),
            LoadedDatasetFactory.S122Spec,
            LoadedDatasetFactory.Box(0, 0, 10, 10),
            null,
            new EncDotNet.S100.Mcp.Tools.Catalog.S122DatasetData(s122)));

        var tool = new QueryFeaturesTool(catalog);

        // Spec filter with default (any) edition matches every dataset
        // of that spec name.
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10)),
            Spec: new SpecRef("S-122", default)));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("m1", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task FeatureType_filter_uses_case_sensitive_exact_match()
    {
        var matching = PointFeature("a", 5, 5, featureType: "NavwarnPart");
        var other = PointFeature("b", 5, 5, featureType: "NavwarnAreaAffected");
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124(
            "x",
            S124Synth.Dataset(matching, other),
            bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));

        var tool = new QueryFeaturesTool(catalog);
        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10)),
            FeatureType: "NavwarnPart"));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Features);
        Assert.Equal("a", value.Features[0].FeatureId);
    }

    [Fact]
    public async Task Pagination_returns_consecutive_pages_with_HasMore_flag()
    {
        var features = Enumerable.Range(0, 5)
            .Select(i => PointFeature($"f{i}", 5, 5))
            .ToArray();
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124(
            "x",
            S124Synth.Dataset(features),
            bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));

        var tool = new QueryFeaturesTool(catalog);

        var page0 = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10)),
            Page: 0,
            PageSize: 2));
        Assert.True(page0.TryGetValue(out var p0));
        Assert.Equal(5, p0.TotalCount);
        Assert.Equal(2, p0.Features.Length);
        Assert.True(p0.HasMore);

        var page2 = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(0, 0, 10, 10)),
            Page: 2,
            PageSize: 2));
        Assert.True(page2.TryGetValue(out var p2));
        Assert.Single(p2.Features);
        Assert.False(p2.HasMore);
    }

    [Fact]
    public async Task Invalid_geometry_returns_geometry_invalid()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new QueryFeaturesTool(catalog);

        // Polygon must close on itself; an open ring is invalid.
        var open = new GeoPolygon(ImmutableArray.Create(
            new GeoPoint(0, 0),
            new GeoPoint(0, 1),
            new GeoPoint(1, 1)));

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Polygon(open)));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<GeometryInvalid>(err);
    }

    [Fact]
    public async Task Coverage_specs_contribute_no_features()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102("bathy"));
        var tool = new QueryFeaturesTool(catalog);

        var result = await tool.InvokeAsync(new QueryFeaturesRequest(
            new GeoQuery.Box(new GeoBoundingBox(-1, -1, 1, 1))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Features);
    }
}
