using System.Collections.Immutable;
using EncDotNet.S100.Mcp.Tools.Geometry;

namespace EncDotNet.S100.Mcp.Tools.Tests.Geometry;

public class GeoQueryTests
{
    [Fact]
    public void Point_bounding_box_is_a_zero_area_rectangle()
    {
        var q = new GeoQuery.Point(new GeoPoint(47.6, -122.3));
        var b = q.GetBoundingBox();

        Assert.Equal(47.6, b.SouthLatitude);
        Assert.Equal(47.6, b.NorthLatitude);
        Assert.Equal(-122.3, b.WestLongitude);
        Assert.Equal(-122.3, b.EastLongitude);
    }

    [Fact]
    public void Box_bounding_box_is_passthrough()
    {
        var box = new GeoBoundingBox(0, -10, 5, 10);
        var q = new GeoQuery.Box(box);

        Assert.Equal(box, q.GetBoundingBox());
    }

    [Fact]
    public void Polygon_bounding_box_covers_every_vertex()
    {
        var ring = ImmutableArray.Create(
            new GeoPoint(0, 0),
            new GeoPoint(0, 10),
            new GeoPoint(5, 10),
            new GeoPoint(5, 0),
            new GeoPoint(0, 0));
        var q = new GeoQuery.Polygon(new GeoPolygon(ring));

        var b = q.GetBoundingBox();

        Assert.Equal(0, b.SouthLatitude);
        Assert.Equal(5, b.NorthLatitude);
        Assert.Equal(0, b.WestLongitude);
        Assert.Equal(10, b.EastLongitude);
    }

    [Fact]
    public void Polyline_with_corridor_inflates_bounding_box()
    {
        var line = new GeoPolyline(
            ImmutableArray.Create(new GeoPoint(0, 0), new GeoPoint(0, 1)),
            CorridorWidthMeters: 111_320.0); // ~1°
        var q = new GeoQuery.Polyline(line);

        var b = q.GetBoundingBox();

        // Lat pad is exactly 1° (we used a 1° half-width in metres). The
        // longitude pad is 1°/cos(0°) = 1° at the equator.
        Assert.InRange(b.SouthLatitude, -1.001, -0.999);
        Assert.InRange(b.NorthLatitude, 0.999, 1.001);
        Assert.InRange(b.WestLongitude, -1.001, -0.999);
        Assert.InRange(b.EastLongitude, 1.999, 2.001);
    }

    [Fact]
    public void Polyline_without_corridor_returns_tight_bbox()
    {
        var line = new GeoPolyline(
            ImmutableArray.Create(new GeoPoint(0, 0), new GeoPoint(0, 1)));
        var q = new GeoQuery.Polyline(line);

        var b = q.GetBoundingBox();

        Assert.Equal(0, b.SouthLatitude);
        Assert.Equal(0, b.NorthLatitude);
        Assert.Equal(0, b.WestLongitude);
        Assert.Equal(1, b.EastLongitude);
    }
}
