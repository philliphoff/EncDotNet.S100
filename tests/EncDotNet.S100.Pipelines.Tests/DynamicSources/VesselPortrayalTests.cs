using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Renderers.Skia.Scene;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

/// <summary>
/// <see cref="VesselPortrayal"/>: own-ship and AIS targets as S-100 Part 9
/// drawing instructions, which render headless as well as on the map.
/// </summary>
public class VesselPortrayalTests
{
    private static DynamicFeature Vessel(
        string kind = "ownship",
        double? headingDegrees = 90.0,
        double knots = 10.0,
        DynamicVesselGeometry? geometry = null) => new()
        {
            Id = "v1",
            Kind = kind,
            GeometryType = GeometryType.Point,
            Coordinates = [new GeoPosition(50.8, -1.3)],
            Motion = new DynamicMotion
            {
                Heading = headingDegrees is { } h ? Angle.FromDegrees(h) : null,
                SpeedOverGround = Speed.FromKnots(knots),
            },
            VesselGeometry = geometry,
            LastUpdated = DateTimeOffset.UtcNow,
        };

    private static DynamicVesselGeometry Hull() => new()
    {
        LengthMetres = 50,
        BeamMetres = 10,
        BowOffsetMetres = 25,
        PortOffsetMetres = 5,
    };

    [Fact]
    public void Ais_portrays_only_ais_targets()
    {
        Assert.True(VesselPortrayal.Ais.CanPortray(Vessel("vessel.ais.tanker")));
        Assert.False(VesselPortrayal.Ais.CanPortray(Vessel("ownship")));
        Assert.True(VesselPortrayal.OwnShip.CanPortray(Vessel("ownship")));
    }

    [Theory]
    [InlineData("vessel.ais.tanker", "TNK")]
    [InlineData("vessel.ais.passenger", "PAS")]
    [InlineData("vessel.ais.highspeedcraft", "HSC")]
    [InlineData("vessel.ais.cargo", "DEF")]
    [InlineData("vessel.ais.something", "UNK")]
    public void Ais_targets_are_coloured_by_class(string kind, string cls)
    {
        Assert.Equal(cls, VesselPortrayal.AisClassOf(Vessel(kind)));
        var disc = VesselPortrayal.Ais.Portray(Vessel(kind, knots: 0)).OfType<PointInstruction>().Single();
        Assert.Equal("VSLDISC_" + cls, disc.SymbolReference);
        Assert.NotNull(VesselPortrayal.Ais.SymbolProvider(disc.SymbolReference!));
    }

    [Fact]
    public void A_moving_vessel_has_a_six_minute_course_vector_with_an_arrowhead_at_its_end()
    {
        var instructions = VesselPortrayal.OwnShip.Portray(Vessel(headingDegrees: 90, knots: 10));

        var vector = instructions.OfType<LineInstruction>().Single();
        Assert.Equal("v1", vector.FeatureReference);
        var (start, end) = (vector.CoordinatesOverride![0], vector.CoordinatesOverride![1]);
        Assert.Equal(start.Latitude, end.Latitude, 3);
        // Ten knots for six minutes is one nautical mile, due east.
        var eastMetres = (end.Longitude - start.Longitude) * 111_320 * Math.Cos(start.Latitude * Math.PI / 180);
        // (The flat-earth check here is good to a few metres.)
        Assert.InRange(eastMetres, 1842, 1862);

        var arrow = instructions.OfType<PointInstruction>().Single(p => p.SymbolReference == "VSLARRW_OWN");
        Assert.Equal(end, arrow.CoordinateOverride);
        Assert.Equal(90, arrow.Rotation);
        Assert.Equal(SymbolRotationCrs.Geographic, arrow.RotationCrs);
    }

    [Fact]
    public void A_stationary_vessel_has_no_course_vector()
    {
        var instructions = VesselPortrayal.OwnShip.Portray(Vessel(knots: 0));

        Assert.Empty(instructions.OfType<LineInstruction>());
        Assert.Equal("VSLDISC_OWN", Assert.Single(instructions.OfType<PointInstruction>()).SymbolReference);
    }

    [Fact]
    public void With_geometry_the_outline_and_disc_switch_where_the_hull_is_22_pixels_long()
    {
        var instructions = VesselPortrayal.OwnShip.Portray(Vessel(knots: 0, geometry: Hull()));

        // 50 m over 22 px of 0.28 mm.
        var switchScale = 50 / 22.0 / 0.00028;
        var outline = instructions.OfType<LineInstruction>().Single();
        Assert.Equal(6, outline.CoordinatesOverride!.Count);
        Assert.Equal(outline.CoordinatesOverride[0], outline.CoordinatesOverride[^1]);
        Assert.Equal(switchScale, outline.ScaleMinimum!.Value, 6);
        var ccrp = instructions.OfType<PointInstruction>().Single(p => p.SymbolReference == "VSLCCRP_OWN");
        Assert.Equal(switchScale, ccrp.ScaleMinimum!.Value, 6);
        var disc = instructions.OfType<PointInstruction>().Single(p => p.SymbolReference == "VSLDISC_OWN");
        Assert.Equal(switchScale, disc.ScaleMaximum!.Value, 6);
        Assert.Null(disc.ScaleMinimum);
    }

    [Fact]
    public void The_outline_is_turned_to_the_heading()
    {
        var north = VesselPortrayal.OwnShip.Portray(Vessel(headingDegrees: 0, knots: 0, geometry: Hull()))
            .OfType<LineInstruction>().Single().CoordinatesOverride!;
        var east = VesselPortrayal.OwnShip.Portray(Vessel(headingDegrees: 90, knots: 0, geometry: Hull()))
            .OfType<LineInstruction>().Single().CoordinatesOverride!;

        // The bow (first vertex) is north of the antenna heading north, east of it heading east.
        Assert.True(north[0].Latitude > 50.8);
        Assert.True(east[0].Longitude > -1.3);
        Assert.Equal(50.8, east[0].Latitude, 4);
    }

    [Fact]
    public void The_portrayal_builds_a_headless_scene()
    {
        var feature = Vessel("vessel.ais.passenger", geometry: Hull());
        var portrayal = VesselPortrayal.Ais;

        var scene = HeadlessVectorRenderer.BuildScene(
            portrayal.Portray(feature),
            new DynamicFeatureGeometryProvider([feature]),
            portrayal.Palette.GetColorPalette(PaletteType.Night),
            portrayal.SymbolProvider,
            lineStyleProvider: null,
            symbolScale: 1.0,
            textScale: 1.0);

        Assert.NotEmpty(scene.Ops);
    }
}
