using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Datasets.S421.Validation;
using EncDotNet.S100.Viewer.Routing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Exporting a viewer route as S-421 and importing it back (#390).</summary>
public sealed class RouteS421MappingTests
{
    private static Route SampleRoute()
    {
        var route = new Route("ZZ00-ROUTE-7", "Synthetic route (fictitious)");
        route.Info.Author = "Test";
        route.Info.Vessel = new RouteVesselInfo { Name = "MV SYNTHETIC (fictitious)", LengthMeters = 90 };
        for (var i = 0; i < 4; i++)
        {
            var waypoint = route.AppendWaypoint(new GeoPosition(-41 + 0.01 * i, -135 + 0.02 * i));
            waypoint.Name = $"WP{i + 1}";
            waypoint.TurnRadiusNm = 0.2;
        }

        route.SetPassageValue(RoutePassageField.Draft, 7.5);
        route.SetPassageValue(RoutePassageField.Speed, 10);
        route.SetPassageValue(RoutePassageField.RequiredUkc, 1.2);
        route.SetPassageValue(RoutePassageField.PortCrossTrack, 150);
        route.SetPassageValue(RoutePassageField.StarboardCrossTrack, 150);
        route.SetLegOverride(1, RoutePassageField.Speed, 6);
        route.SetLegOverride(2, RoutePassageField.Draft, 7.0);
        route.SetDeparture(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero));
        route.Legs[0].GeometryType = RouteLegGeometryType.Geodesic;
        route.Legs[1].Note = "Pilot boards (fictitious)";
        route.Legs[2].SafetyContourMeters = 10;
        return route;
    }

    [Fact]
    public void Export_writes_each_legs_effective_values()
    {
        var s421 = RouteS421Mapping.ToS421(SampleRoute());

        Assert.Equal("ZZ00-ROUTE-7", s421.RouteId);
        Assert.Equal([7.5, 7.5, 7.0], s421.Legs.Select(l => l.Draft!.Value));
        Assert.All(s421.Legs, l => Assert.Equal(1.2, l.StaticUnderKeelClearance));
        Assert.Equal([2, 1, 1], s421.Legs.Select(l => l.GeometryTypeCode!.Value));
        var elements = s421.Schedules.Single().Variants.Single().Elements;
        Assert.Equal([10.0, 6.0, 10.0, null], elements.Select(e => e.PlannedSpeedOverGround));
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 6, 0, 0, TimeSpan.Zero), elements[0].Etd);
    }

    [Fact]
    public void A_route_survives_export_to_GML_and_import()
    {
        var original = SampleRoute();

        var bytes = S421DatasetWriter.ToBytes(RouteS421Mapping.ToS421(original));
        var plan = S421RoutePlan.From(S421Dataset.Open(new MemoryStream(bytes)), out var diagnostics);
        var imported = RouteS421Mapping.FromS421(plan.Route);

        Assert.Empty(diagnostics);
        Assert.Empty(S421RoutePlanRules.Validate(plan).Findings);
        Assert.Equal(original.Id, imported.Id);
        Assert.Equal(original.Name, imported.Name);
        Assert.Equal(original.Info.Author, imported.Info.Author);
        Assert.Equal(original.Info.Vessel!.Name, imported.Info.Vessel!.Name);
        Assert.Equal(original.Waypoints.Select(w => (w.Position, w.Name, w.TurnRadiusNm)), imported.Waypoints.Select(w => (w.Position, w.Name, w.TurnRadiusNm)));
        Assert.Equal(original.Passage.Departure, imported.Passage.Departure);
        foreach (var field in new[] { RoutePassageField.Draft, RoutePassageField.Speed, RoutePassageField.RequiredUkc, RoutePassageField.PortCrossTrack, RoutePassageField.StarboardCrossTrack })
        {
            Assert.Equal(
                Enumerable.Range(0, original.Legs.Count).Select(i => original.GetEffectiveLegValue(i, field)),
                Enumerable.Range(0, imported.Legs.Count).Select(i => imported.GetEffectiveLegValue(i, field)));
        }

        // The value most legs share comes back as the passage value; the rest as overrides.
        Assert.Equal(1.2, imported.Passage.RequiredUkcMeters);
        Assert.Equal(10, imported.Passage.SpeedKnots);
        Assert.True(imported.IsLegOverridden(1, RoutePassageField.Speed));
        Assert.Equal(original.Legs.Select(l => (l.GeometryType, l.Note, l.SafetyContourMeters)), imported.Legs.Select(l => (l.GeometryType, l.Note, l.SafetyContourMeters)));
    }
}
