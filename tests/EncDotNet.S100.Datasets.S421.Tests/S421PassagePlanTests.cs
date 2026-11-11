using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S421.DataModel;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Datasets.S421.Tests;

/// <summary>The S-421 route → <see cref="PassagePlan"/> conversion (#1034).</summary>
public class S421PassagePlanTests
{
    private static readonly Dictionary<string, string> Empty = [];
    private static readonly GeoPosition P1 = new(50.0, -1.0);
    private static readonly GeoPosition P2 = new(50.1, -1.1);
    private static readonly GeoPosition P3 = new(50.2, -1.2);

    [Fact]
    public void Each_leg_reads_its_start_waypoints_outgoing_leg()
    {
        var first = Leg("L1", P1, P2, draft: 7.5, ukc: 1.2, port: 150, starboard: 90, geometry: 2);
        var second = Leg("L2", P2, P3, draft: 6.0, ukc: null, port: null, starboard: null, geometry: 1);
        var route = Route([Waypoint(1, P1, first), Waypoint(2, P2, second), Waypoint(3, P3, null)]);

        var plan = route.ToPassagePlan();

        Assert.Equal(2, plan.Legs.Count);
        var a = plan.Legs[0];
        Assert.Equal([P1, P2], a.Points);
        Assert.Equal(LegGeometry.GreatCircle, a.Geometry);
        Assert.Equal(7.5, a.Draft?.TotalMetres);
        Assert.Equal(1.2, a.RequiredUkc?.TotalMetres);
        Assert.Equal(150, a.PortLimit?.TotalMetres);
        Assert.Equal(90, a.StarboardLimit?.TotalMetres);
        Assert.Equal(0, a.Squat.TotalMetres);
        Assert.Equal("Leg 1 → 2", a.Name);

        var b = plan.Legs[1];
        Assert.Equal([P2, P3], b.Points);
        Assert.Equal(LegGeometry.Rhumb, b.Geometry);
        Assert.Equal(6.0, b.Draft?.TotalMetres);
        Assert.Null(b.RequiredUkc);
        Assert.Null(b.PortLimit);
        Assert.Null(b.StarboardLimit);
        Assert.Equal("Leg 2 → 3", b.Name);
    }

    [Fact]
    public void A_waypoint_without_an_outgoing_leg_gives_a_leg_with_no_values()
    {
        var route = Route([Waypoint(1, P1, null), Waypoint(2, P2, null)]);

        var leg = Assert.Single(route.ToPassagePlan().Legs);

        Assert.Equal([P1, P2], leg.Points);
        Assert.Equal(LegGeometry.Rhumb, leg.Geometry);
        Assert.Null(leg.Draft);
        Assert.Null(leg.Speed);
        Assert.Null(leg.PortLimit);
    }

    [Fact]
    public void Speeds_and_departure_come_from_the_planning_schedule()
    {
        var departure = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
        var manual = Variant(S421ScheduleVariantKind.Manual, Element(1, 5, departure.AddHours(-1)));
        var calculated = Variant(S421ScheduleVariantKind.Calculated, Element(1, 12, departure), Element(2, 0, null));
        var route = Route(
            [Waypoint(1, P1, null), Waypoint(2, P2, null), Waypoint(3, P3, null)],
            new S421Schedule { Id = "S1", Variants = [manual, calculated], ExtraAttributes = Empty });

        var plan = route.ToPassagePlan();

        Assert.Equal(departure, plan.Departure);
        Assert.Equal(12, plan.Legs[0].Speed!.Value.TotalKnots, 9);
        // A zero planned SOG is no speed, as is a waypoint without an element.
        Assert.Null(plan.Legs[1].Speed);
    }

    [Fact]
    public void A_route_without_schedules_has_no_departure_or_speeds()
    {
        var plan = Route([Waypoint(1, P1, null), Waypoint(2, P2, null)]).ToPassagePlan();

        Assert.Null(plan.Departure);
        Assert.All(plan.Legs, l => Assert.Null(l.Speed));
    }

    [Fact]
    public void The_IEC_sample_converts_with_its_leg_values()
    {
        var route = S421RoutePlan.From(S421Dataset.Open(Path.Combine("TestData", "RTE-TEST-GFULL.s421.gml")), out _).Route;

        var plan = route.ToPassagePlan();

        Assert.Equal(route.Waypoints.Count - 1, plan.Legs.Count);
        Assert.Equal(new DateTimeOffset(2019, 10, 18, 15, 0, 0, TimeSpan.Zero), plan.Departure);
        Assert.Equal(20, plan.Legs[0].Speed!.Value.TotalKnots, 9);
        for (var i = 0; i < plan.Legs.Count; i++)
        {
            Assert.Equal(route.Waypoints[i].Position, plan.Legs[i].Points[0]);
            Assert.Equal(route.Waypoints[i + 1].Position, plan.Legs[i].Points[^1]);
            Assert.Equal(route.Waypoints[i].OutgoingLeg?.Draft, plan.Legs[i].Draft?.TotalMetres);
        }
    }

    [Fact]
    public void Fewer_than_two_waypoints_give_no_legs()
    {
        Assert.Empty(Route([Waypoint(1, P1, null)]).ToPassagePlan().Legs);
    }

    private static S421Route Route(IReadOnlyList<S421Waypoint> waypoints, params S421Schedule[] schedules) => new()
    {
        Id = "RTE",
        Waypoints = waypoints,
        Legs = [.. waypoints.Select(w => w.OutgoingLeg).OfType<S421Leg>()],
        ActionPoints = [],
        Schedules = schedules,
        ExtraAttributes = Empty,
    };

    private static S421Waypoint Waypoint(int number, GeoPosition position, S421Leg? outgoing) => new()
    {
        Id = $"WPT.{number}",
        WaypointNumber = number,
        Position = position,
        OutgoingLeg = outgoing,
        ExtraAttributes = Empty,
    };

    private static S421Leg Leg(string id, GeoPosition from, GeoPosition to, double? draft, double? ukc, double? port, double? starboard, int? geometry) => new()
    {
        Id = id,
        Coordinates = [from, to],
        Draft = draft,
        StaticUnderKeelClearance = ukc,
        PortCrossTrackDistanceLimit = port,
        StarboardCrossTrackDistanceLimit = starboard,
        GeometryTypeCode = geometry,
        ExtraAttributes = Empty,
    };

    private static S421ScheduleVariant Variant(S421ScheduleVariantKind kind, params S421ScheduleElement[] elements) => new()
    {
        Id = kind.ToString(),
        Kind = kind,
        Elements = elements,
    };

    private static S421ScheduleElement Element(int waypoint, double sog, DateTimeOffset? etd) => new()
    {
        Id = $"E.{waypoint}",
        WaypointNumber = waypoint,
        PlannedSpeedOverGround = sog,
        Etd = etd,
        ExtraAttributes = Empty,
    };
}
