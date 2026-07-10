using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui.Nts;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

public class DefaultDynamicFeatureRendererTests
{
    private static DynamicFeature Make(GeometryType kind, params GeoPosition[] coords) => new()
    {
        Id = "f",
        GeometryType = kind,
        Coordinates = coords,
        LastUpdated = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void CanRender_AcceptsPointCurveSurface()
    {
        var r = new DefaultDynamicFeatureRenderer();
        Assert.True(r.CanRender(Make(GeometryType.Point, new GeoPosition(0, 0))));
        Assert.True(r.CanRender(Make(GeometryType.Curve, new GeoPosition(0, 0), new GeoPosition(1, 1))));
        Assert.True(r.CanRender(Make(GeometryType.Surface, new GeoPosition(0, 0), new GeoPosition(1, 0), new GeoPosition(1, 1))));
    }

    [Fact]
    public void Point_NoMotion_EmitsSingleDisc()
    {
        var r = new DefaultDynamicFeatureRenderer();
        var features = r.Render(Make(GeometryType.Point, new GeoPosition(47.6, -122.3))).ToArray();
        Assert.Single(features);
        var disc = Assert.IsType<GeometryFeature>(features[0]);
        Assert.IsType<Point>(disc.Geometry);
    }

    [Fact]
    public void Point_WithHeadingAndSpeed_EmitsHeadingLineAndDisc()
    {
        var feature = new DynamicFeature
        {
            Id = "ownship",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { new GeoPosition(47.6, -122.3) },
            Motion = new DynamicMotion { Heading = Angle.FromDegrees(0), SpeedOverGround = Speed.FromKnots(10) },
            LastUpdated = DateTimeOffset.UtcNow,
        };

        var features = new DefaultDynamicFeatureRenderer().Render(feature).ToArray();

        Assert.Equal(2, features.Length);
        Assert.IsType<LineString>(((GeometryFeature)features[0]).Geometry);
        Assert.IsType<Point>(((GeometryFeature)features[1]).Geometry);
    }

    [Fact]
    public void Point_WithHeadingButZeroSpeed_OmitsHeadingLine()
    {
        var feature = new DynamicFeature
        {
            Id = "stopped",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { new GeoPosition(47.6, -122.3) },
            Motion = new DynamicMotion { Heading = Angle.FromDegrees(90), SpeedOverGround = Speed.FromKnots(0) },
            LastUpdated = DateTimeOffset.UtcNow,
        };

        var features = new DefaultDynamicFeatureRenderer().Render(feature).ToArray();
        Assert.Single(features);
    }

    [Fact]
    public void Curve_TwoOrMore_EmitsLineString()
    {
        var features = new DefaultDynamicFeatureRenderer()
            .Render(Make(GeometryType.Curve, new GeoPosition(0, 0), new GeoPosition(1, 1), new GeoPosition(2, 2)))
            .ToArray();

        var ls = Assert.IsType<LineString>(((GeometryFeature)Assert.Single(features)).Geometry);
        Assert.Equal(3, ls.NumPoints);
    }

    [Fact]
    public void Curve_SingleCoord_EmitsNothing()
    {
        var features = new DefaultDynamicFeatureRenderer()
            .Render(Make(GeometryType.Curve, new GeoPosition(0, 0)))
            .ToArray();
        Assert.Empty(features);
    }

    [Fact]
    public void Surface_OpenRing_AutoClosesPolygon()
    {
        var features = new DefaultDynamicFeatureRenderer()
            .Render(Make(GeometryType.Surface, new GeoPosition(0, 0), new GeoPosition(1, 0), new GeoPosition(1, 1)))
            .ToArray();

        var poly = Assert.IsType<Polygon>(((GeometryFeature)Assert.Single(features)).Geometry);
        Assert.Equal(poly.ExteriorRing.Coordinates[0], poly.ExteriorRing.Coordinates[^1]);
        Assert.Equal(4, poly.ExteriorRing.NumPoints);
    }

    [Fact]
    public void Surface_ClosedRing_KeepsRingAsGiven()
    {
        var features = new DefaultDynamicFeatureRenderer()
            .Render(Make(GeometryType.Surface, new GeoPosition(0, 0), new GeoPosition(1, 0), new GeoPosition(1, 1), new GeoPosition(0, 0)))
            .ToArray();

        var poly = Assert.IsType<Polygon>(((GeometryFeature)Assert.Single(features)).Geometry);
        Assert.Equal(4, poly.ExteriorRing.NumPoints);
    }

    [Fact]
    public void Surface_TooFewCoords_EmitsNothing()
    {
        var features = new DefaultDynamicFeatureRenderer()
            .Render(Make(GeometryType.Surface, new GeoPosition(0, 0), new GeoPosition(1, 1)))
            .ToArray();
        Assert.Empty(features);
    }

    [Fact]
    public void EmptyCoordinates_EmitsNothing()
    {
        var feature = new DynamicFeature
        {
            Id = "empty",
            GeometryType = GeometryType.Point,
            Coordinates = Array.Empty<GeoPosition>(),
            LastUpdated = DateTimeOffset.UtcNow,
        };
        Assert.Empty(new DefaultDynamicFeatureRenderer().Render(feature));
    }
}
