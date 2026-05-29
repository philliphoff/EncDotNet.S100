using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

public class AisVesselRendererTests
{
    private static DynamicFeature MakeFeature(
        string kind = "vessel.ais.cargo",
        double lat = 50.8, double lon = -1.3,
        double? headingDeg = 0.0, double sogKn = 10.0,
        DynamicVesselGeometry? geometry = null) => new()
    {
        Id = "ais:123456789",
        Kind = kind,
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
    public void CanRender_RequiresPointAndAisKindPrefix()
    {
        var r = new AisVesselRenderer();

        Assert.True(r.CanRender(MakeFeature()));
        Assert.False(r.CanRender(MakeFeature(kind: "ownship")));
        Assert.False(r.CanRender(new DynamicFeature
        {
            Id = "ais:1", Kind = "vessel.ais.cargo",
            GeometryType = GeometryType.Curve,
            Coordinates = new[] { (0.0, 0.0), (1.0, 1.0) },
            LastUpdated = DateTimeOffset.UtcNow,
        }));
    }

    [Fact]
    public void NoGeometry_EmitsHeadingLine_Arrow_AndDiscOnly()
    {
        var features = new AisVesselRenderer().Render(MakeFeature()).ToArray();

        Assert.Equal(3, features.Length);
        Assert.IsType<LineString>(((GeometryFeature)features[0]).Geometry);
        Assert.IsType<Point>(((GeometryFeature)features[1]).Geometry);
        Assert.IsType<Point>(((GeometryFeature)features[2]).Geometry);
    }

    [Fact]
    public void Stationary_NoGeometry_EmitsOnlyDisc()
    {
        var features = new AisVesselRenderer()
            .Render(MakeFeature(headingDeg: null, sogKn: 0.0))
            .ToArray();

        Assert.Single(features);
        Assert.IsType<Point>(((GeometryFeature)features[0]).Geometry);
    }

    [Fact]
    public void WithGeometry_EmitsHullAndCcrpCrossAndPictogram()
    {
        var features = new AisVesselRenderer()
            .Render(MakeFeature(geometry: DefaultGeom()))
            .ToArray();

        // heading line + arrow + hull + cross + disc = 5
        Assert.Equal(5, features.Length);
        Assert.IsType<Polygon>(((GeometryFeature)features[2]).Geometry);
        Assert.IsType<Point>(((GeometryFeature)features[3]).Geometry);
        // CCRP cross has an ImageStyle
        Assert.Contains(((GeometryFeature)features[3]).Styles, s => s is ImageStyle);
    }

    [Fact]
    public void TankerKind_UsesRedishPalette()
    {
        var features = new AisVesselRenderer()
            .Render(MakeFeature(kind: "vessel.ais.tanker", geometry: DefaultGeom()))
            .ToArray();

        var hull = (GeometryFeature)features[2];
        var style = (VectorStyle)hull.Styles.First(s => s is VectorStyle);
        Assert.NotNull(style.Outline);
        // Tanker stroke is (0xCC, 0x33, 0x33).
        Assert.Equal(0xCC, style.Outline!.Color!.R);
        Assert.Equal(0x33, style.Outline.Color.G);
        Assert.Equal(0x33, style.Outline.Color.B);
    }

    [Fact]
    public void CargoKind_UsesGreenDefaultPalette()
    {
        var features = new AisVesselRenderer()
            .Render(MakeFeature(kind: "vessel.ais.cargo", geometry: DefaultGeom()))
            .ToArray();

        var hull = (GeometryFeature)features[2];
        var style = (VectorStyle)hull.Styles.First(s => s is VectorStyle);
        // Default stroke is (0x00, 0xA0, 0x40).
        Assert.Equal(0x00, style.Outline!.Color!.R);
        Assert.Equal(0xA0, style.Outline.Color.G);
        Assert.Equal(0x40, style.Outline.Color.B);
    }

    [Fact]
    public void UnknownClassToken_FallsBackToGreyPalette()
    {
        var features = new AisVesselRenderer()
            .Render(MakeFeature(kind: "vessel.ais.somethingelse", geometry: DefaultGeom()))
            .ToArray();

        var hull = (GeometryFeature)features[2];
        var style = (VectorStyle)hull.Styles.First(s => s is VectorStyle);
        Assert.Equal(0x66, style.Outline!.Color!.R);
        Assert.Equal(0x66, style.Outline.Color.G);
        Assert.Equal(0x66, style.Outline.Color.B);
    }

    [Fact]
    public void EmptyCoordinates_EmitsNothing()
    {
        var feature = new DynamicFeature
        {
            Id = "ais:1", Kind = "vessel.ais.cargo",
            GeometryType = GeometryType.Point,
            Coordinates = Array.Empty<(double, double)>(),
            LastUpdated = DateTimeOffset.UtcNow,
        };
        Assert.Empty(new AisVesselRenderer().Render(feature));
    }
}
