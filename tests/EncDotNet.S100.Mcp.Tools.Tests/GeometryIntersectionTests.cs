using EncDotNet.S100.DataModel;
using System.Collections.ObjectModel;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Geometry;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class GeometryIntersectionTests
{
    private static S124Feature Square(string id, double half, params IReadOnlyList<GeoPosition>[] holes)
    {
        IReadOnlyList<GeoPosition> ring = [
            new GeoPosition(-half, -half), new GeoPosition(-half, half), new GeoPosition(half, half), new GeoPosition(half, -half), new GeoPosition(-half, -half)];
        return new S124Feature
        {
            Id = id,
            FeatureType = "RestrictedArea",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = ring,
            InteriorRings = holes.ToArray(),
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };
    }

    private static S124Feature Curve(string id, params GeoPosition[] vertices) => new()
    {
        Id = id,
        FeatureType = "Fairway",
        GeometryType = S100GeometryType.Curve,
        Curves = [vertices.ToArray()],
        Attributes = ReadOnlyDictionary<string, string>.Empty,
        ComplexAttributes = [],
    };

    private static S124Feature Point(string id, double lat, double lon) => new()
    {
        Id = id,
        FeatureType = "Light",
        GeometryType = S100GeometryType.Point,
        Points = [new GeoPosition(lat, lon)],
        Attributes = ReadOnlyDictionary<string, string>.Empty,
        ComplexAttributes = [],
    };

    private static GeoQuery.Polyline Leg(double? corridor, params GeoPosition[] vertices)
        => new(new GeoPolyline(
            vertices.Select(v => new GeoPoint(v.Latitude, v.Longitude)).ToArray(),
            corridor));

    [Fact]
    public void Point_inside_area_intersects()
    {
        var area = Square("a", 1.0);
        Assert.True(GeometryIntersection.Intersects(area, new GeoQuery.Point(new GeoPoint(0, 0))));
    }

    [Fact]
    public void Point_in_bbox_but_outside_polygon_does_not_intersect()
    {
        // A diagonal triangle whose bbox covers the origin but whose body
        // does not.
        var triangle = new S124Feature
        {
            Id = "tri",
            FeatureType = "RestrictedArea",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = [
                new GeoPosition(0, 0), new GeoPosition(2, 0), new GeoPosition(0, 2), new GeoPosition(0, 0)],
            InteriorRings = [],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };

        // (1.5, 1.5) is in the bbox [0,2]x[0,2] but outside the lower-left
        // triangle.
        Assert.False(GeometryIntersection.Intersects(triangle, new GeoQuery.Point(new GeoPoint(1.5, 1.5))));
    }

    [Fact]
    public void Point_inside_hole_does_not_intersect()
    {
        IReadOnlyList<GeoPosition> hole = [
            new GeoPosition(-0.2, -0.2), new GeoPosition(-0.2, 0.2), new GeoPosition(0.2, 0.2), new GeoPosition(0.2, -0.2), new GeoPosition(-0.2, -0.2)];
        var donut = Square("d", 1.0, hole);
        Assert.False(GeometryIntersection.Intersects(donut, new GeoQuery.Point(new GeoPoint(0, 0))));
    }

    [Fact]
    public void Route_leg_crossing_area_boundary_intersects()
    {
        var area = Square("a", 1.0);
        // A leg from far west to far east passes straight through the area.
        var leg = Leg(null, new GeoPosition(0.0, -5.0), new GeoPosition(0.0, 5.0));
        Assert.True(GeometryIntersection.Intersects(area, leg));
    }

    [Fact]
    public void Route_leg_missing_area_does_not_intersect()
    {
        var area = Square("a", 1.0);
        // A leg well to the north of the area.
        var leg = Leg(null, new GeoPosition(5.0, -5.0), new GeoPosition(5.0, 5.0));
        Assert.False(GeometryIntersection.Intersects(area, leg));
    }

    [Fact]
    public void Route_leg_crossing_curve_intersects()
    {
        var curve = Curve("c", new GeoPosition(-1.0, 0.0), new GeoPosition(1.0, 0.0));
        var leg = Leg(null, new GeoPosition(0.0, -1.0), new GeoPosition(0.0, 1.0));
        Assert.True(GeometryIntersection.Intersects(curve, leg));
    }

    [Fact]
    public void Route_leg_near_point_matches_only_within_corridor()
    {
        var point = Point("p", 0.0, 0.0);
        // A leg ~111 m north of the point.
        var near = Leg(null, new GeoPosition(0.001, -1.0), new GeoPosition(0.001, 1.0));

        Assert.False(GeometryIntersection.Intersects(point, near));
        Assert.True(GeometryIntersection.Intersects(
            point,
            Leg(500.0, new GeoPosition(0.001, -1.0), new GeoPosition(0.001, 1.0))));
    }

    [Fact]
    public void Box_overlapping_area_intersects()
    {
        var area = Square("a", 1.0);
        var box = new GeoQuery.Box(new GeoBoundingBox(0.5, 0.5, 5.0, 5.0));
        Assert.True(GeometryIntersection.Intersects(area, box));
    }

    [Fact]
    public void Feature_without_geometry_never_intersects()
    {
        var empty = new S124Feature
        {
            Id = "empty",
            FeatureType = "RestrictedArea",
            GeometryType = S100GeometryType.None,
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };

        Assert.False(GeometryIntersection.Intersects(empty, new GeoQuery.Point(new GeoPoint(0, 0))));
    }
}
