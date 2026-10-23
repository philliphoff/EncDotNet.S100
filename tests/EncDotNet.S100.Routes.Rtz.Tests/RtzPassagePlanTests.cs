using System.Text;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Routes.Rtz.Tests;

/// <summary>The RTZ route → <see cref="PassagePlan"/> conversion (#1034).</summary>
public class RtzPassagePlanTests
{
    [Fact]
    public void Each_leg_reads_the_leg_of_the_waypoint_it_leads_to_with_defaults()
    {
        var route = RtzReader.Read(RtzRoundTripTests.FixturePath("rtz-1.0-solent.rtz"));

        var plan = route.ToPassagePlan();

        Assert.Equal(3, plan.Legs.Count);
        var first = plan.Legs[0];
        Assert.Equal([route.Waypoints[0].Position, route.Waypoints[1].Position], first.Points);
        Assert.Equal("Leg 1 → 2", first.Name);
        // Waypoint 2's own leg: XTDs in nautical miles, draught and UKC in metres.
        Assert.Equal(0.15 * 1852, first.PortLimit?.TotalMetres);
        Assert.Equal(0.2 * 1852, first.StarboardLimit?.TotalMetres);
        Assert.Equal(8.5, first.Draft?.TotalMetres);
        Assert.Equal(1.5, first.RequiredUkc?.TotalMetres);
        Assert.Equal(0, first.Squat.TotalMetres);
        Assert.Equal(LegGeometry.Rhumb, first.Geometry);

        // Waypoint 3's leg sets no XTDs, so the default waypoint's 0.1 NM apply.
        Assert.Equal(0.1 * 1852, plan.Legs[1].PortLimit?.TotalMetres);
        Assert.Equal(0.1 * 1852, plan.Legs[1].StarboardLimit?.TotalMetres);
    }

    [Fact]
    public void Speeds_and_departure_come_from_the_schedule_element_of_the_waypoint_led_to()
    {
        var plan = RtzReader.Read(RtzRoundTripTests.FixturePath("rtz-1.0-solent.rtz")).ToPassagePlan();

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), plan.Departure);
        Assert.Equal([12.0, 12.0, 8.0], plan.Legs.Select(l => Math.Round(l.Speed!.Value.TotalKnots, 9)));
    }

    [Fact]
    public void The_draft_is_the_deeper_draught_and_orthodromes_are_great_circles()
    {
        var plan = Read("""
            <waypoint id="1"><position lat="50.0" lon="-1.0"/></waypoint>
            <waypoint id="2"><position lat="50.5" lon="-1.0"/><leg draughtForward="6.2" draughtAft="7.1" geometryType="Orthodrome"/></waypoint>
            """, schedule: null).ToPassagePlan();

        var leg = Assert.Single(plan.Legs);
        Assert.Equal(7.1, leg.Draft?.TotalMetres);
        Assert.Equal(LegGeometry.GreatCircle, leg.Geometry);
        Assert.Null(leg.Speed);
        Assert.Null(plan.Departure);
    }

    [Fact]
    public void A_missing_speed_is_implied_from_the_previous_departure_and_the_eta()
    {
        // 0.5° of latitude is 30 NM; three hours gives 10 kn.
        var plan = Read("""
            <waypoint id="1"><position lat="50.0" lon="-1.0"/></waypoint>
            <waypoint id="2"><position lat="50.5" lon="-1.0"/></waypoint>
            <waypoint id="3"><position lat="51.0" lon="-1.0"/></waypoint>
            """, schedule: """
            <scheduleElement waypointId="1" etd="2026-10-20T08:00:00Z"/>
            <scheduleElement waypointId="2" eta="2026-10-20T11:00:00Z"/>
            """).ToPassagePlan();

        Assert.Equal(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), plan.Departure);
        var implied = plan.Legs[0].Speed!.Value.TotalKnots;
        Assert.Equal(Math.Round(implied, 2), implied, 9);
        Assert.InRange(implied, 9.9, 10.1);
        // Waypoint 3 has no element, so the leg to it has no speed.
        Assert.Null(plan.Legs[1].Speed);
    }

    [Fact]
    public void Fewer_than_two_waypoints_give_no_legs()
    {
        var plan = Read("""<waypoint id="1"><position lat="50.0" lon="-1.0"/></waypoint>""", schedule: null).ToPassagePlan();

        Assert.Empty(plan.Legs);
    }

    private static RtzRoute Read(string waypoints, string? schedule)
    {
        var schedules = schedule is null
            ? ""
            : $"""<schedules><schedule id="1"><calculated>{schedule}</calculated></schedule></schedules>""";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <route xmlns="http://www.cirm.org/RTZ/1/1" version="1.1">
              <routeInfo routeName="Test"/>
              <waypoints>{waypoints}</waypoints>
              {schedules}
            </route>
            """;
        return RtzReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
    }
}
