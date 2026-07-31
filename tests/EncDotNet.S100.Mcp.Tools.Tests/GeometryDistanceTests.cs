using System.Collections.ObjectModel;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class GeometryDistanceTests
{
    private const double MetersPerDegree = 111_320.0;

    private static S124Feature PointFeature(params GeoPosition[] points)
        => new()
        {
            Id = "p",
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.Point,
            Points = points.ToArray(),
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };

    private static S124Feature CurveFeature(params GeoPosition[] vertices)
        => new()
        {
            Id = "c",
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.Curve,
            Curves = [vertices.ToArray()],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };

    private static S124Feature SurfaceFeature(
        IReadOnlyList<GeoPosition> exterior,
        IReadOnlyList<IReadOnlyList<GeoPosition>>? holes = null)
        => new()
        {
            Id = "s",
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.Surface,
            ExteriorRing = exterior,
            InteriorRings = holes ?? [],
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };

    [Fact]
    public void Point_distance_is_equirectangular()
    {
        var feature = PointFeature(new GeoPosition(1.0, 0.0));
        var measured = GeometryDistance.Measure(feature, new GeoPoint(0, 0));

        Assert.NotNull(measured);
        Assert.Equal(S100GeometryType.Point, measured!.Value.Primitive);
        Assert.False(measured.Value.Inside);
        Assert.Equal(MetersPerDegree, measured.Value.DistanceMeters, 0);
    }

    [Fact]
    public void Curve_distance_is_perpendicular_not_vertex()
    {
        // Horizontal segment at lat=1 from lon=-1 to lon=1; nearest point to
        // the origin is (1, 0), one degree of latitude away — closer than
        // either vertex (which are ~1.41 degrees away).
        var feature = CurveFeature(new GeoPosition(1.0, -1.0), new GeoPosition(1.0, 1.0));
        var measured = GeometryDistance.Measure(feature, new GeoPoint(0, 0));

        Assert.NotNull(measured);
        Assert.Equal(S100GeometryType.Curve, measured!.Value.Primitive);
        Assert.InRange(measured.Value.DistanceMeters, MetersPerDegree * 0.99, MetersPerDegree * 1.01);
        Assert.Equal(1.0, measured.Value.NearestLatitude, 6);
        Assert.Equal(0.0, measured.Value.NearestLongitude, 6);
    }

    [Fact]
    public void Surface_containment_reports_zero_distance_inside()
    {
        IReadOnlyList<GeoPosition> ring = [
            new GeoPosition(-1, -1), new GeoPosition(-1, 1), new GeoPosition(1, 1), new GeoPosition(1, -1), new GeoPosition(-1, -1)];
        var feature = SurfaceFeature(ring);

        var measured = GeometryDistance.Measure(feature, new GeoPoint(0, 0));

        Assert.NotNull(measured);
        Assert.True(measured!.Value.Inside);
        Assert.Equal(0.0, measured.Value.DistanceMeters);
        Assert.Equal(S100GeometryType.Surface, measured.Value.Primitive);
    }

    [Fact]
    public void Surface_outside_measures_distance_to_nearest_edge()
    {
        IReadOnlyList<GeoPosition> ring = [
            new GeoPosition(-1, -1), new GeoPosition(-1, 1), new GeoPosition(1, 1), new GeoPosition(1, -1), new GeoPosition(-1, -1)];
        var feature = SurfaceFeature(ring);

        // Point two degrees east of centre; nearest edge is lon=1 at lat 0.
        var measured = GeometryDistance.Measure(feature, new GeoPoint(0, 2));

        Assert.NotNull(measured);
        Assert.False(measured!.Value.Inside);
        Assert.InRange(measured.Value.DistanceMeters, MetersPerDegree * 0.99, MetersPerDegree * 1.01);
    }

    [Fact]
    public void Surface_point_inside_hole_is_not_contained()
    {
        IReadOnlyList<GeoPosition> exterior = [
            new GeoPosition(-2, -2), new GeoPosition(-2, 2), new GeoPosition(2, 2), new GeoPosition(2, -2), new GeoPosition(-2, -2)];
        IReadOnlyList<GeoPosition> hole = [
            new GeoPosition(-1, -1), new GeoPosition(-1, 1), new GeoPosition(1, 1), new GeoPosition(1, -1), new GeoPosition(-1, -1)];
        var feature = SurfaceFeature(exterior, [hole]);

        var measured = GeometryDistance.Measure(feature, new GeoPoint(0, 0));

        Assert.NotNull(measured);
        Assert.False(measured!.Value.Inside);
        // Nearest hole edge is one degree away.
        Assert.InRange(measured.Value.DistanceMeters, MetersPerDegree * 0.99, MetersPerDegree * 1.01);
    }

    [Fact]
    public void Measure_returns_null_when_feature_has_no_geometry()
    {
        var feature = new S124Feature
        {
            Id = "empty",
            FeatureType = "MarineProtectedArea",
            GeometryType = S100GeometryType.None,
            Attributes = ReadOnlyDictionary<string, string>.Empty,
            ComplexAttributes = [],
        };

        Assert.Null(GeometryDistance.Measure(feature, new GeoPoint(0, 0)));
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0)]    // due north
    [InlineData(0.0, 1.0, 90.0)]   // due east
    [InlineData(-1.0, 0.0, 180.0)] // due south
    [InlineData(0.0, -1.0, 270.0)] // due west
    public void Bearing_points_in_the_expected_cardinal_direction(double toLat, double toLon, double expected)
    {
        var bearing = GeometryDistance.Bearing(new GeoPoint(0, 0), toLat, toLon);
        Assert.Equal(expected, bearing, 1);
    }
}
