using System.Collections.Immutable;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Geometry;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class GeometryIntersectionTests
{
    private static S124Feature Square(string id, double half, params ImmutableArray<(double Lat, double Lon)>[] holes)
    {
        var ring = ImmutableArray.Create<(double, double)>(
            (-half, -half), (-half, half), (half, half), (half, -half), (-half, -half));
        return new S124Feature
        {
            Id = id,
            FeatureType = "RestrictedArea",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = ring,
            InteriorRings = holes.ToImmutableArray(),
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
        };
    }

    private static S124Feature Curve(string id, params (double Lat, double Lon)[] vertices) => new()
    {
        Id = id,
        FeatureType = "Fairway",
        GeometryType = S100GeometryType.Curve,
        Curves = ImmutableArray.Create(vertices.ToImmutableArray()),
        Attributes = ImmutableDictionary<string, string>.Empty,
        ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
    };

    private static S124Feature Point(string id, double lat, double lon) => new()
    {
        Id = id,
        FeatureType = "Light",
        GeometryType = S100GeometryType.Point,
        Points = ImmutableArray.Create((lat, lon)),
        Attributes = ImmutableDictionary<string, string>.Empty,
        ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
    };

    private static GeoQuery.Polyline Leg(double? corridor, params (double Lat, double Lon)[] vertices)
        => new(new GeoPolyline(
            vertices.Select(v => new GeoPoint(v.Lat, v.Lon)).ToImmutableArray(),
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
            ExteriorRing = ImmutableArray.Create<(double, double)>(
                (0, 0), (2, 0), (0, 2), (0, 0)),
            InteriorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty,
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
        };

        // (1.5, 1.5) is in the bbox [0,2]x[0,2] but outside the lower-left
        // triangle.
        Assert.False(GeometryIntersection.Intersects(triangle, new GeoQuery.Point(new GeoPoint(1.5, 1.5))));
    }

    [Fact]
    public void Point_inside_hole_does_not_intersect()
    {
        var hole = ImmutableArray.Create<(double, double)>(
            (-0.2, -0.2), (-0.2, 0.2), (0.2, 0.2), (0.2, -0.2), (-0.2, -0.2));
        var donut = Square("d", 1.0, hole);
        Assert.False(GeometryIntersection.Intersects(donut, new GeoQuery.Point(new GeoPoint(0, 0))));
    }

    [Fact]
    public void Route_leg_crossing_area_boundary_intersects()
    {
        var area = Square("a", 1.0);
        // A leg from far west to far east passes straight through the area.
        var leg = Leg(null, (0.0, -5.0), (0.0, 5.0));
        Assert.True(GeometryIntersection.Intersects(area, leg));
    }

    [Fact]
    public void Route_leg_missing_area_does_not_intersect()
    {
        var area = Square("a", 1.0);
        // A leg well to the north of the area.
        var leg = Leg(null, (5.0, -5.0), (5.0, 5.0));
        Assert.False(GeometryIntersection.Intersects(area, leg));
    }

    [Fact]
    public void Route_leg_crossing_curve_intersects()
    {
        var curve = Curve("c", (-1.0, 0.0), (1.0, 0.0));
        var leg = Leg(null, (0.0, -1.0), (0.0, 1.0));
        Assert.True(GeometryIntersection.Intersects(curve, leg));
    }

    [Fact]
    public void Route_leg_near_point_matches_only_within_corridor()
    {
        var point = Point("p", 0.0, 0.0);
        // A leg ~111 m north of the point.
        var near = Leg(null, (0.001, -1.0), (0.001, 1.0));

        Assert.False(GeometryIntersection.Intersects(point, near));
        Assert.True(GeometryIntersection.Intersects(
            point,
            Leg(500.0, (0.001, -1.0), (0.001, 1.0))));
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
            Attributes = ImmutableDictionary<string, string>.Empty,
            ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
        };

        Assert.False(GeometryIntersection.Intersects(empty, new GeoQuery.Point(new GeoPoint(0, 0))));
    }
}
