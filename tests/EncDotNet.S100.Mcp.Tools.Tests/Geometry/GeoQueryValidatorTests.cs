using System.Collections.Immutable;
using EncDotNet.S100.Mcp.Tools.Geometry;

namespace EncDotNet.S100.Mcp.Tools.Tests.Geometry;

public class GeoQueryValidatorTests
{
    [Fact]
    public void Valid_point_returns_null()
    {
        var q = new GeoQuery.Point(new GeoPoint(0, 0));
        Assert.Null(GeoQueryValidator.Validate(q));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    public void Out_of_range_point_returns_invalid_argument(double lat, double lon)
    {
        var q = new GeoQuery.Point(new GeoPoint(lat, lon));
        var err = GeoQueryValidator.Validate(q);

        var ia = Assert.IsType<InvalidArgument>(err);
        Assert.StartsWith("query.", ia.Parameter);
    }

    [Fact]
    public void Box_with_inverted_latitude_returns_geometry_invalid()
    {
        var q = new GeoQuery.Box(new GeoBoundingBox(10, 0, 5, 10));
        var err = GeoQueryValidator.Validate(q);
        Assert.IsType<GeometryInvalid>(err);
    }

    [Fact]
    public void Antimeridian_crossing_box_returns_geometry_invalid()
    {
        var q = new GeoQuery.Box(new GeoBoundingBox(0, 170, 5, -170));
        var err = GeoQueryValidator.Validate(q);
        Assert.IsType<GeometryInvalid>(err);
    }

    [Fact]
    public void Open_polygon_returns_geometry_invalid()
    {
        var ring = ImmutableArray.Create(
            new GeoPoint(0, 0),
            new GeoPoint(0, 1),
            new GeoPoint(1, 1),
            new GeoPoint(1, 0));
        var q = new GeoQuery.Polygon(new GeoPolygon(ring));

        var err = GeoQueryValidator.Validate(q);
        Assert.IsType<GeometryInvalid>(err);
    }

    [Fact]
    public void Polygon_with_too_few_points_returns_geometry_invalid()
    {
        var ring = ImmutableArray.Create(
            new GeoPoint(0, 0),
            new GeoPoint(0, 1),
            new GeoPoint(0, 0));
        var q = new GeoQuery.Polygon(new GeoPolygon(ring));

        var err = GeoQueryValidator.Validate(q);
        Assert.IsType<GeometryInvalid>(err);
    }

    [Fact]
    public void Closed_quad_polygon_validates()
    {
        var ring = ImmutableArray.Create(
            new GeoPoint(0, 0),
            new GeoPoint(0, 1),
            new GeoPoint(1, 1),
            new GeoPoint(1, 0),
            new GeoPoint(0, 0));
        var q = new GeoQuery.Polygon(new GeoPolygon(ring));

        Assert.Null(GeoQueryValidator.Validate(q));
    }

    [Fact]
    public void Polyline_with_single_vertex_returns_geometry_invalid()
    {
        var line = new GeoPolyline(ImmutableArray.Create(new GeoPoint(0, 0)));
        var q = new GeoQuery.Polyline(line);
        Assert.IsType<GeometryInvalid>(GeoQueryValidator.Validate(q));
    }

    [Fact]
    public void Polyline_with_negative_corridor_returns_invalid_argument()
    {
        var line = new GeoPolyline(
            ImmutableArray.Create(new GeoPoint(0, 0), new GeoPoint(0, 1)),
            CorridorWidthMeters: -1);
        var q = new GeoQuery.Polyline(line);

        var err = GeoQueryValidator.Validate(q);
        var ia = Assert.IsType<InvalidArgument>(err);
        Assert.Contains("corridorWidthMeters", ia.Parameter);
    }
}
