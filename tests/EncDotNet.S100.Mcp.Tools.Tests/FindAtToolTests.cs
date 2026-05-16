using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class FindAtToolTests
{
    [Fact]
    public async Task Returns_empty_for_empty_catalog()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(0, 0));

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
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(Latitude: 5, Longitude: 5));

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
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(Latitude: 5.5, Longitude: 5.5));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.Datasets.Length);
        Assert.Equal(new[] { "a", "b", "c" }, value.Datasets.Select(d => d.Id.Value).ToArray());
    }

    [Fact]
    public async Task Spec_filter_narrows_result_set()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("warn", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        catalog.Add(LoadedDatasetFactory.S122("mpa", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(
            Latitude: 5,
            Longitude: 5,
            Spec: LoadedDatasetFactory.S124Spec));

        Assert.True(result.TryGetValue(out var value));
        var single = Assert.Single(value.Datasets);
        Assert.Equal("S-124", single.Spec.Name);
    }

    [Fact]
    public async Task Spec_filter_with_default_edition_matches_any_edition()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        var tool = new FindAtTool(catalog);

        var filter = new SpecRef("S-124", default);
        var result = await tool.InvokeAsync(new FindAtRequest(
            Latitude: 5, Longitude: 5, Spec: filter));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Datasets);
    }

    [Fact]
    public async Task Point_on_bbox_edge_is_inside()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("edge", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        var tool = new FindAtTool(catalog);

        var southWest = await tool.InvokeAsync(new FindAtRequest(0, 0));
        var northEast = await tool.InvokeAsync(new FindAtRequest(10, 10));

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
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(lat, 0));

        Assert.True(result.TryGetError(out var err));
        var invalid = Assert.IsType<InvalidArgument>(err);
        Assert.Equal("latitude", invalid.Parameter);
    }

    [Theory]
    [InlineData(180.01)]
    [InlineData(-181.0)]
    [InlineData(double.NaN)]
    public async Task Longitude_out_of_range_returns_invalid_argument(double lon)
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(0, lon));

        Assert.True(result.TryGetError(out var err));
        var invalid = Assert.IsType<InvalidArgument>(err);
        Assert.Equal("longitude", invalid.Parameter);
    }

    [Fact]
    public async Task Paging_returns_requested_page_with_HasMore_flag()
    {
        var catalog = new FakeDatasetCatalog();
        for (var i = 0; i < 7; i++)
        {
            catalog.Add(LoadedDatasetFactory.S124($"ds{i}", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        }
        var tool = new FindAtTool(catalog);

        var page0 = await tool.InvokeAsync(new FindAtRequest(5, 5, Page: 0, PageSize: 3));
        Assert.True(page0.TryGetValue(out var v0));
        Assert.Equal(3, v0.Datasets.Length);
        Assert.True(v0.HasMore);
        Assert.Equal(7, v0.TotalCount);

        var page2 = await tool.InvokeAsync(new FindAtRequest(5, 5, Page: 2, PageSize: 3));
        Assert.True(page2.TryGetValue(out var v2));
        Assert.Single(v2.Datasets);
        Assert.False(v2.HasMore);

        var page5 = await tool.InvokeAsync(new FindAtRequest(5, 5, Page: 5, PageSize: 3));
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
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(50, 50));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Datasets);
        Assert.Equal(0, value.TotalCount);
    }

    // S-100 Part 10b §6.2 allows bounding boxes that cross the antimeridian
    // (stored with WestLongitude > EastLongitude). The current containment
    // check assumes West ≤ East and matches ListDatasetsTool.Intersects;
    // wrap-around handling would need a coordinated change across all three
    // bbox helpers (FindAtTool.Contains, ListDatasetsTool.Intersects,
    // SampleCoverageTool.Contains). TODO: enable once those are unified.
    [Fact(Skip = "Antimeridian-crossing bbox support is a codebase-wide change; see S-100 Part 10b §6.2.")]
    public async Task Antimeridian_crossing_bbox_contains_point()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("dateline",
            bounds: new EncDotNet.S100.Pipelines.BoundingBox(0, 170, 10, -170)));
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(5, 175));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Datasets);
    }
}
