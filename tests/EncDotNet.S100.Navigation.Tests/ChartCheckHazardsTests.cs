using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The navigational-hazards check (#1127): aids to navigation, structures and
/// overhead features whose portrayal raises <c>NavHazard</c>, grouped by
/// their class and where they lie from the track.
/// </summary>
public sealed class ChartCheckHazardsTests
{
    // Metres per degree on the WGS-84 ellipsoid at the equator: east (along
    // the track) and north (across it).
    private static readonly double E = Wgs84Geodesy.MetresPerDegree(0).East;
    private static readonly double N = Wgs84Geodesy.MetresPerDegree(0).North;

    // East along the equator; 50 m limits either side.
    private static readonly PassageLeg East = Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 0.01)]);

    private static readonly FakeCell Harbour = new("101HARBOUR", 22_000);

    private static RouteCheckSettings Settings(Length? trackClearance = null, Length? nearby = null) => new()
    {
        At = Fixture.Departure,
        TrackClearance = trackClearance,
        NearbyDistance = nearby,
    };

    private static IReadOnlyList<RouteFinding> Check(RouteCheckSettings settings, params RouteChartFeature[] features) =>
        Checks.Chart(new FakeCharts([Harbour], features), new PassagePlan([East]), settings).Findings;

    private static IReadOnlyList<RouteFinding> Check(params RouteChartFeature[] features) => Check(Settings(), features);

    // A point feature metres north (positive, to port) of the track that raises NavHazard.
    private static RouteChartFeature Hazard(string type, string id, double northMeters, bool alert = true) =>
        new(RouteChartFeatureKind.Alerted, Harbour, id, type, [], [])
        {
            Points = [new GeoPosition(northMeters / N, 0.005)],
            Alerts = alert ? [new RouteChartAlert(RouteFindingAlert.NavHazard)] : [new RouteChartAlert(RouteFindingAlert.SafetyContour)],
        };

    [Theory]
    // Track clearance 30 m, port limit 50 m, then 185,2 m near.
    [InlineData("LateralBuoy", 10.0, "route_check.aid_to_navigation", RouteFindingGroup.Check, "Lateral buoy on the track")]
    [InlineData("LateralBuoy", 40.0, "route_check.aid_to_navigation", RouteFindingGroup.Check, "Lateral buoy in the corridor")]
    [InlineData("LateralBuoy", 120.0, "route_check.aid_to_navigation", RouteFindingGroup.Info, "Lateral buoy near the route")]
    [InlineData("Pile", 10.0, "route_check.structure_on_track", RouteFindingGroup.MustReview, "Pile on the track")]
    [InlineData("Pile", -40.0, "route_check.structure_in_corridor", RouteFindingGroup.Check, "Pile in the corridor")]
    [InlineData("Pile", -120.0, "route_check.structure_in_corridor", RouteFindingGroup.Info, "Pile near the route")]
    [InlineData("CableOverhead", 0.0, "route_check.overhead_on_route", RouteFindingGroup.Check, "Cable overhead over the track")]
    [InlineData("CableOverhead", 120.0, "route_check.overhead_on_route", RouteFindingGroup.Info, "Cable overhead near the route")]
    [InlineData("VirtualAISAidToNavigation", 40.0, "route_check.aid_to_navigation", RouteFindingGroup.Check, "Virtual AIS aid to navigation in the corridor")]
    public void Hazards_are_grouped_by_class_and_position(string type, double northMeters, string code, RouteFindingGroup group, string title)
    {
        var finding = Assert.Single(Check(Hazard(type, "1", northMeters)));

        Assert.Equal(code, finding.Code.Value);
        Assert.Equal(group, finding.Group);
        Assert.Equal(title, finding.Title);
        var details = Assert.IsType<HazardDetails>(finding.Details);
        Assert.Equal(type, details.FeatureType);
        Assert.Equal(Math.Abs(northMeters), details.Offset.TotalMetres, 1);
    }

    [Fact]
    public void The_meta_line_names_the_type_and_side()
    {
        var finding = Assert.Single(Check(Hazard("Dolphin", "1", -40)));

        Assert.Equal("Leg 1 → 2 · Dolphin · 40 m to starboard", finding.Meta);
        Assert.Equal(RouteHazardClass.Structure, ((HazardDetails)finding.Details!).HazardClass);
        Assert.Equal(RouteFindingPosition.InCorridor, ((HazardDetails)finding.Details!).Position);
    }

    [Fact]
    public void Features_beyond_the_near_band_or_without_NavHazard_are_left_out()
    {
        Assert.Empty(Check(Hazard("LateralBuoy", "1", 240), Hazard("Pontoon", "2", 0, alert: false)));
    }

    [Fact]
    public void The_track_clearance_and_near_band_are_settings()
    {
        var wide = Assert.Single(Check(Settings(trackClearance: Length.FromMetres(45)), Hazard("Pile", "1", 40)));
        Assert.Equal(RouteFindingCodes.StructureOnTrack, wide.Code);

        Assert.Empty(Check(Settings(nearby: Length.FromMetres(50)), Hazard("LateralBuoy", "1", 120)));
    }

    [Fact]
    public void Hazards_run_without_the_depth_settings()
    {
        // Aids and structures don't depend on the safety contour.
        Assert.Single(Check(Hazard("LateralBuoy", "1", 40)));
    }

    [Theory]
    [InlineData("LateralBuoy", RouteHazardClass.AidToNavigation)]
    [InlineData("Bridge", RouteHazardClass.Overhead)]
    [InlineData("Wreck", RouteHazardClass.Danger)]
    [InlineData("OffshorePlatform", RouteHazardClass.Structure)]
    [InlineData("SomethingNew", RouteHazardClass.Structure)]
    public void Hazard_classes(string type, RouteHazardClass hazardClass) =>
        Assert.Equal(hazardClass, RouteHazardClasses.Of(type));
}
