using System.Collections.Immutable;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class FindAtToolQueryTests
{
    [Fact]
    public async Task Query_point_matches_legacy_lat_lon_path()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("inside", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        catalog.Add(LoadedDatasetFactory.S124("outside", bounds: LoadedDatasetFactory.Box(20, 20, 30, 30)));
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(
            Latitude: 0,
            Longitude: 0,
            Query: new GeoQuery.Point(new GeoPoint(5, 5))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Datasets);
        Assert.Equal(new DatasetId("inside"), value.Datasets[0].Id);
    }

    [Fact]
    public async Task Query_bbox_returns_every_dataset_with_overlapping_bounds()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("a", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)));
        catalog.Add(LoadedDatasetFactory.S122("b", bounds: LoadedDatasetFactory.Box(5, 5, 15, 15)));
        catalog.Add(LoadedDatasetFactory.S124("c", bounds: LoadedDatasetFactory.Box(50, 50, 60, 60)));
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(
            Latitude: 0,
            Longitude: 0,
            Query: new GeoQuery.Box(new GeoBoundingBox(3, 3, 12, 12))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(2, value.Datasets.Length);
        Assert.Equal(new[] { "a", "b" }, value.Datasets.Select(d => d.Id.Value).ToArray());
    }

    [Fact]
    public async Task Query_polyline_with_corridor_inflates_match_radius()
    {
        var catalog = new FakeDatasetCatalog();
        // Dataset is a small bbox just north of the equator at longitude 0.
        catalog.Add(LoadedDatasetFactory.S124("near", bounds: LoadedDatasetFactory.Box(0.5, -0.1, 0.6, 0.1)));
        var tool = new FindAtTool(catalog);

        // A polyline running along the equator with a 111 km corridor
        // (≈1°) should pick up the dataset 0.5° to the north; without a
        // corridor it should not.
        var line = new GeoPolyline(
            ImmutableArray.Create(new GeoPoint(0, -1), new GeoPoint(0, 1)),
            CorridorWidthMeters: 111_320.0);

        var withCorridor = await tool.InvokeAsync(new FindAtRequest(
            0, 0, Query: new GeoQuery.Polyline(line)));
        Assert.True(withCorridor.TryGetValue(out var withVal));
        Assert.Single(withVal.Datasets);

        var withoutCorridor = await tool.InvokeAsync(new FindAtRequest(
            0, 0, Query: new GeoQuery.Polyline(line with { CorridorWidthMeters = null })));
        Assert.True(withoutCorridor.TryGetValue(out var withoutVal));
        Assert.Empty(withoutVal.Datasets);
    }

    [Fact]
    public async Task Query_with_invalid_geometry_returns_geometry_invalid()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new FindAtTool(catalog);

        var open = new GeoPolygon(ImmutableArray.Create(
            new GeoPoint(0, 0),
            new GeoPoint(0, 1),
            new GeoPoint(1, 1),
            new GeoPoint(1, 0))); // unclosed
        var result = await tool.InvokeAsync(new FindAtRequest(
            0, 0, Query: new GeoQuery.Polygon(open)));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<GeometryInvalid>(err);
    }

    [Fact]
    public async Task Query_with_out_of_range_point_returns_invalid_argument()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new FindAtTool(catalog);

        var result = await tool.InvokeAsync(new FindAtRequest(
            0, 0, Query: new GeoQuery.Point(new GeoPoint(91, 0))));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Query_takes_precedence_over_legacy_lat_lon()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S124("northern", bounds: LoadedDatasetFactory.Box(50, 50, 60, 60)));
        var tool = new FindAtTool(catalog);

        // Lat/Lon are invalid here but should be ignored because Query
        // is supplied and validates cleanly.
        var result = await tool.InvokeAsync(new FindAtRequest(
            Latitude: 1000,
            Longitude: 1000,
            Query: new GeoQuery.Point(new GeoPoint(55, 55))));

        Assert.True(result.TryGetValue(out var value));
        Assert.Single(value.Datasets);
    }
}
