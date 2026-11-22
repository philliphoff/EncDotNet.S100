using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// list_datasets' spatial filter, which took over find_at (#1609): a point
/// keeps the datasets whose bounding box contains it, any other shape those
/// whose bounding box intersects it.
/// </summary>
public class ListDatasetsQueryTests
{
    private static GeoQuery At(double latitude, double longitude) => new GeoQuery.Point(new GeoPoint(latitude, longitude));

    [Fact]
    public async Task Point_query_over_empty_catalog_returns_empty()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(0, 0)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Datasets);
        Assert.Equal(0, value.TotalCount);
        Assert.False(value.HasMore);
    }

    [Fact]
    public async Task Returns_only_datasets_containing_point()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("inside", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        catalog.Add(LoadedDatasetFactory.S124("outside", bounds: LoadedDatasetFactory.Box(20, 20, 30, 30)));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(5, 5)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var single = Assert.Single(value.Datasets);
        Assert.Equal(new DatasetId("inside"), single.Id);
        Assert.Equal(1, value.TotalCount);
    }

    [Fact]
    public async Task Returns_all_overlapping_datasets_in_insertion_order()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        catalog.Add(LoadedDatasetFactory.S122("b", bounds: LoadedDatasetFactory.Box(4, 4, 8, 8)));
        catalog.Add(LoadedDatasetFactory.S102("c", bounds: LoadedDatasetFactory.Box(5, 5, 6, 6)));
        catalog.Add(LoadedDatasetFactory.S124("disjoint", bounds: LoadedDatasetFactory.Box(50, 50, 51, 51)));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(5.5, 5.5)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.Datasets.Count);
        Assert.Equal(new[] { "a", "b", "c" }, value.Datasets.Select(d => d.Id.Value).ToArray());
    }

    [Fact]
    public async Task Spec_filter_narrows_result_set()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        catalog.Add(LoadedDatasetFactory.S122("mpa", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(5, 5),
            Spec: LoadedDatasetFactory.S124Spec), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        var single = Assert.Single(value.Datasets);
        Assert.Equal("S-124", single.Spec.Name);
    }

    [Fact]
    public async Task Spec_filter_with_default_edition_matches_any_edition()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        var tool = new ListDatasetsTool(catalog);

        var filter = new SpecRef("S-124", default);
        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(5, 5), Spec: filter), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Datasets);
    }

    [Fact]
    public async Task Point_on_bbox_edge_is_inside()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("edge", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        var tool = new ListDatasetsTool(catalog);

        var southWest = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(0, 0)), TestContext.Current.CancellationToken);
        var northEast = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(10, 10)), TestContext.Current.CancellationToken);

        Assert.True(southWest.TryGetValue(out var sw));
        Assert.Single(sw.Datasets);
        Assert.True(northEast.TryGetValue(out var ne));
        Assert.Single(ne.Datasets);
    }

    [Theory]
    [InlineData(91.0)]
    [InlineData(-90.01)]
    [InlineData(double.NaN)]
    public async Task Latitude_out_of_range_returns_invalid_argument(double lat)
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(lat, 0)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var err));
        var invalid = Assert.IsType<InvalidArgument>(err);
        Assert.Equal("query.latitude", invalid.Parameter);
    }

    [Theory]
    [InlineData(180.01)]
    [InlineData(-181.0)]
    [InlineData(double.NaN)]
    public async Task Longitude_out_of_range_returns_invalid_argument(double lon)
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(0, lon)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var err));
        var invalid = Assert.IsType<InvalidArgument>(err);
        Assert.Equal("query.longitude", invalid.Parameter);
    }

    [Fact]
    public async Task Paging_returns_requested_page_with_HasMore_flag()
    {
        var catalog = new FakeDatasetCatalog();
        for (var i = 0; i < 7; i++)
        {
            catalog.Add(LoadedDatasetFactory.S124($"ds{i}", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        }
        var tool = new ListDatasetsTool(catalog);

        var page0 = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(5, 5), Page: 0, PageSize: 3), TestContext.Current.CancellationToken);
        Assert.True(page0.TryGetValue(out var v0));
        Assert.Equal(3, v0.Datasets.Count);
        Assert.True(v0.HasMore);
        Assert.Equal(7, v0.TotalCount);

        var page2 = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(5, 5), Page: 2, PageSize: 3), TestContext.Current.CancellationToken);
        Assert.True(page2.TryGetValue(out var v2));
        Assert.Single(v2.Datasets);
        Assert.False(v2.HasMore);

        var page5 = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(5, 5), Page: 5, PageSize: 3), TestContext.Current.CancellationToken);
        Assert.True(page5.TryGetValue(out var v5));
        Assert.Empty(v5.Datasets);
        Assert.False(v5.HasMore);
        Assert.Equal(7, v5.TotalCount);
    }

    [Fact]
    public async Task Point_outside_all_bboxes_returns_empty_success()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a", bounds: LoadedDatasetFactory.Box(0, 0, 1, 1)));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(50, 50)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Datasets);
        Assert.Equal(0, value.TotalCount);
    }

    // S-100 Part 10b §6.2 allows bounding boxes that cross the antimeridian
    // (stored with WestLongitude > EastLongitude). The containment check
    // assumes West ≤ East; wrap-around handling would need a coordinated
    // change across the bbox helpers (SpatialPredicates, SampleTool.Contains).
    // TODO: enable once those are unified.
    [Fact(Skip = "Antimeridian-crossing bbox support is a codebase-wide change; see S-100 Part 10b §6.2.")]
    public async Task Antimeridian_crossing_bbox_contains_point()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("dateline",
            bounds: new EncDotNet.S100.Pipelines.BoundingBox(0, 170, 10, -170)));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: At(5, 175)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Datasets);
    }

    [Fact]
    public async Task Box_query_returns_every_dataset_with_overlapping_bounds()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        catalog.Add(LoadedDatasetFactory.S122("b", bounds: LoadedDatasetFactory.Box(5, 5, 15, 15)));
        catalog.Add(LoadedDatasetFactory.S124("c", bounds: LoadedDatasetFactory.Box(50, 50, 60, 60)));
        var tool = new ListDatasetsTool(catalog);

        var result = await tool.InvokeAsync(new ListDatasetsRequest(
            Query: new GeoQuery.Box(new GeoBoundingBox(3, 3, 12, 12))), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(new[] { "a", "b" }, value.Datasets.Select(d => d.Id.Value).ToArray());
    }

    [Fact]
    public async Task Polyline_query_with_corridor_inflates_match_radius()
    {
        var catalog = new FakeDatasetCatalog();
        // Dataset is a small bbox just north of the equator at longitude 0.
        catalog.Add(LoadedDatasetFactory.S124("near", bounds: LoadedDatasetFactory.Box(0.5, -0.1, 0.6, 0.1)));
        var tool = new ListDatasetsTool(catalog);

        // A polyline along the equator with a 111 km corridor (≈1°) picks up
        // the dataset 0.5° to the north; without a corridor it doesn't.
        var line = new GeoPolyline(
            [new GeoPoint(0, -1), new GeoPoint(0, 1)],
            CorridorWidthMeters: 111_320.0);

        var withCorridor = await tool.InvokeAsync(new ListDatasetsRequest(Query: new GeoQuery.Polyline(line)), TestContext.Current.CancellationToken);
        Assert.True(withCorridor.TryGetValue(out var withVal));
        Assert.Single(withVal.Datasets);

        var withoutCorridor = await tool.InvokeAsync(new ListDatasetsRequest(
            Query: new GeoQuery.Polyline(line with { CorridorWidthMeters = null })), TestContext.Current.CancellationToken);
        Assert.True(withoutCorridor.TryGetValue(out var withoutVal));
        Assert.Empty(withoutVal.Datasets);
    }

    [Fact]
    public async Task Invalid_geometry_returns_geometry_invalid()
    {
        var tool = new ListDatasetsTool(new FakeDatasetCatalog());

        var open = new GeoPolygon([
            new GeoPoint(0, 0),
            new GeoPoint(0, 1),
            new GeoPoint(1, 1),
            new GeoPoint(1, 0)]); // unclosed
        var result = await tool.InvokeAsync(new ListDatasetsRequest(Query: new GeoQuery.Polygon(open)), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<GeometryInvalid>(err);
    }
}
