using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

public class OwnShipRendererTests
{
    private static DynamicFeature MakeFeature(
        double lat = 50.8, double lon = -1.3,
        double? headingDeg = 0.0, double sogKn = 10.0,
        DynamicVesselGeometry? geometry = null) => new()
    {
        Id = "ownship",
        Kind = "ownship",
        GeometryType = GeometryType.Point,
        Coordinates = new[] { (lat, lon) },
        Motion = headingDeg is null && sogKn == 0
            ? null
            : new DynamicMotion { HeadingDeg = headingDeg, SpeedOverGroundKn = sogKn },
        VesselGeometry = geometry,
        LastUpdated = DateTimeOffset.UtcNow,
    };

    private static DynamicVesselGeometry DefaultGeom() => new()
    {
        LengthMetres = 50,
        BeamMetres = 10,
        BowOffsetMetres = 25,
        PortOffsetMetres = 5,
    };

    [Fact]
    public void CanRender_OnlyAcceptsPoint()
    {
        var r = new OwnShipRenderer();
        Assert.True(r.CanRender(MakeFeature()));
        Assert.False(r.CanRender(new DynamicFeature
        {
            Id = "x", GeometryType = GeometryType.Curve,
            Coordinates = new[] { (0.0, 0.0), (1.0, 1.0) },
            LastUpdated = DateTimeOffset.UtcNow,
        }));
    }

    [Fact]
    public void NoGeometry_EmitsHeadingLine_Arrow_AndDiscOnly()
    {
        var features = new OwnShipRenderer().Render(MakeFeature()).ToArray();

        // heading line + arrow + disc fallback = 3
        Assert.Equal(3, features.Length);
        Assert.IsType<LineString>(((GeometryFeature)features[0]).Geometry);
        Assert.IsType<Point>(((GeometryFeature)features[1]).Geometry);
        Assert.IsType<Point>(((GeometryFeature)features[2]).Geometry);

        // Disc has no resolution gates.
        var discStyle = (SymbolStyle)((GeometryFeature)features[2]).Styles.First();
        Assert.Equal(0.0, discStyle.MinVisible);
        Assert.Equal(double.MaxValue, discStyle.MaxVisible);
    }

    [Fact]
    public void NoMotion_NoGeometry_EmitsDiscOnly()
    {
        var feat = new DynamicFeature
        {
            Id = "ownship", GeometryType = GeometryType.Point,
            Coordinates = new[] { (0.0, 0.0) },
            LastUpdated = DateTimeOffset.UtcNow,
        };
        var features = new OwnShipRenderer().Render(feat).ToArray();
        Assert.Single(features);
        Assert.IsType<Point>(((GeometryFeature)features[0]).Geometry);
    }

    [Fact]
    public void WithGeometry_EmitsHeadingArrowHullCcrpAndDisc()
    {
        var features = new OwnShipRenderer()
            .Render(MakeFeature(geometry: DefaultGeom())).ToArray();

        // line + arrow + hull + ccrp lateral + ccrp longitudinal + disc = 6
        Assert.Equal(6, features.Length);
        Assert.IsType<LineString>(((GeometryFeature)features[0]).Geometry);
        Assert.IsType<Point>(((GeometryFeature)features[1]).Geometry);      // arrow
        Assert.IsType<Polygon>(((GeometryFeature)features[2]).Geometry);    // hull
        Assert.IsType<LineString>(((GeometryFeature)features[3]).Geometry); // ccrp lateral arm
        Assert.IsType<LineString>(((GeometryFeature)features[4]).Geometry); // ccrp longitudinal arm
        Assert.IsType<Point>(((GeometryFeature)features[5]).Geometry);      // disc
    }

    [Fact]
    public void WithGeometry_OutlineAndPictogramHaveComplementaryResolutionGates()
    {
        const double lat = 50.8;
        var features = new OwnShipRenderer()
            .Render(MakeFeature(lat: lat, geometry: DefaultGeom())).ToArray();

        var hullStyle = (VectorStyle)((GeometryFeature)features[2]).Styles.First();
        var crossLateralStyle = (VectorStyle)((GeometryFeature)features[3]).Styles.First();
        var crossLongitudinalStyle = (VectorStyle)((GeometryFeature)features[4]).Styles.First();
        var discStyle = (SymbolStyle)((GeometryFeature)features[5]).Styles.First();

        var expectedSwitch =
            DefaultGeom().LengthMetres * Math.Cos(lat * Math.PI / 180.0)
            / OwnShipRenderer.MinVesselPixels;

        Assert.Equal(expectedSwitch, hullStyle.MaxVisible, 6);
        Assert.Equal(expectedSwitch, crossLateralStyle.MaxVisible, 6);
        Assert.Equal(expectedSwitch, crossLongitudinalStyle.MaxVisible, 6);
        Assert.Equal(expectedSwitch, discStyle.MinVisible, 6);
    }

    [Fact]
    public void HullPolygon_Has5VerticesPlusClosingPoint()
    {
        var features = new OwnShipRenderer()
            .Render(MakeFeature(geometry: DefaultGeom())).ToArray();
        var hull = (Polygon)((GeometryFeature)features[2]).Geometry!;
        Assert.Equal(6, hull.ExteriorRing!.Coordinates.Length); // 5 + closing
        Assert.Equal(hull.ExteriorRing.Coordinates[0],
            hull.ExteriorRing.Coordinates[^1]);
    }

    [Fact]
    public void HeadingArrow_RotationMatchesHeading()
    {
        var features = new OwnShipRenderer()
            .Render(MakeFeature(headingDeg: 45.0)).ToArray();
        var arrowStyle = (SymbolStyle)((GeometryFeature)features[1]).Styles.First();
        Assert.Equal(45.0, arrowStyle.SymbolRotation);
        Assert.Equal(SymbolType.Triangle, arrowStyle.SymbolType);
    }

    [Fact]
    public void NoMotion_WithGeometry_StillEmitsHullCcrpDisc_NoLine()
    {
        var feat = new DynamicFeature
        {
            Id = "ownship", GeometryType = GeometryType.Point,
            Coordinates = new[] { (0.0, 0.0) },
            VesselGeometry = DefaultGeom(),
            LastUpdated = DateTimeOffset.UtcNow,
        };
        var features = new OwnShipRenderer().Render(feat).ToArray();
        // hull + ccrp lateral + ccrp longitudinal + disc = 4, no line/arrow.
        Assert.Equal(4, features.Length);
        Assert.IsType<Polygon>(((GeometryFeature)features[0]).Geometry);
        Assert.IsType<LineString>(((GeometryFeature)features[1]).Geometry);
        Assert.IsType<LineString>(((GeometryFeature)features[2]).Geometry);
        Assert.IsType<Point>(((GeometryFeature)features[3]).Geometry);
    }
}
