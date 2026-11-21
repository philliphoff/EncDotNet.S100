using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>The route planner (#1560) over fake masks and depths.</summary>
public sealed class RoutePlannerTests
{
    // Open water 10 m deep, with a land wall along 0.01° east from 0.004° north
    // to the top, and a gap below it: the way from west to east goes south of it.
    private static readonly RouteCheckSources Wall = Sources(
        p => Math.Abs(p.Longitude - 0.01) < 0.0011 && p.Latitude > 0.004 ? WaterMaskClass.Land : WaterMaskClass.Water,
        (_, _) => 10);

    [Fact]
    public void Open_water_is_one_straight_leg()
    {
        var plan = Plan(Sources(_ => WaterMaskClass.Water, (_, _) => 10), new(0.01, 0.0), new(0.01, 0.02));

        Assert.Equal(2, plan.Waypoints.Count);
        Assert.InRange(plan.DistanceMeters, 2_200, 2_250);
        Assert.All(plan.Stops, s => Assert.False(s.Moved));
    }

    [Fact]
    public void A_wall_is_rounded_with_few_waypoints()
    {
        var plan = Plan(Wall, new(0.012, 0.004), new(0.012, 0.016));

        Assert.InRange(plan.Waypoints.Count, 3, 5);
        Assert.All(plan.Waypoints.Skip(1).SkipLast(1), w => Assert.True(w.Latitude < 0.004));
        AssertNoLand(plan.Waypoints, Wall.WaterMask!);
        Assert.True(plan.DistanceMeters > 1_400);
    }

    [Fact]
    public void A_stop_on_land_moves_to_water_and_says_so()
    {
        var plan = Plan(Wall, new(0.012, 0.0101), new(0.012, 0.016));

        var from = plan.Stops[0];
        Assert.True(from.Moved);
        Assert.Equal("land", from.Reason);
        Assert.Equal(WaterMaskClass.Water, Wall.WaterMask!.Classify(from.Position));
        Assert.Equal(from.Position, plan.Waypoints[0]);
    }

    [Fact]
    public void Via_points_make_one_route_with_a_leg_each()
    {
        var plan = Plan(Wall, new(0.012, 0.004), new(0.012, 0.016), via: [new(0.002, 0.01)]);

        Assert.Equal(2, plan.Legs.Count);
        Assert.Equal((0, 1), (plan.Legs[0].From, plan.Legs[0].To));
        Assert.Equal(plan.Legs[0].Points[^1], plan.Legs[1].Points[0]);
        Assert.Equal(plan.Legs.Sum(l => l.Points.Count) - 1, plan.Waypoints.Count);
        Assert.Equal(plan.Legs.Sum(l => l.DistanceMeters), plan.DistanceMeters, 3);
    }

    [Fact]
    public void Shoal_water_is_avoided_away_from_the_stops_but_allowed_at_them()
    {
        // 1 m east of 0.015°: too shallow for 2 m, deep enough for a 0.8 m berth.
        var sources = Sources(_ => WaterMaskClass.Water, (_, lon) => lon > 0.015 ? 1 : 10);
        var into = new RoutePlanRequest([new(0.01, 0.0), new(0.01, 0.0165)], 2) { BerthDepthMeters = 0.8, BerthRadiusMeters = 300, CellMeters = 20 };

        var result = RoutePlanner.Plan(sources, into, TestContext.Current.CancellationToken);

        Assert.NotNull(result.Plan);
        Assert.False(result.Plan!.Stops[1].Moved);

        var across = RoutePlanner.Plan(sources, into with { Stops = [new(0.01, 0.0), new(0.01, 0.03)] }, TestContext.Current.CancellationToken);
        Assert.Null(across.Plan);
        Assert.Equal("no_navigable_way", across.Failure!.Reason);
        Assert.Equal(0, across.Failure.LegIndex);
    }

    [Fact]
    public void The_route_keeps_its_clearance_from_land()
    {
        // A 1.1 km wide channel between land north of 0.015° and south of 0.005°.
        var sources = Sources(p => p.Latitude > 0.015 || p.Latitude < 0.005 ? WaterMaskClass.Land : WaterMaskClass.Water, (_, _) => 10);

        var plan = Plan(sources, new(0.01, 0.0), new(0.01, 0.03), clearance: 300);

        Assert.All(plan.Legs, l => Assert.True(l.LeastClearanceMeters >= 300));
    }

    [Fact]
    public void A_preferred_area_draws_the_route_into_it()
    {
        // Open water; a fairway along the south side.
        var sources = Sources(_ => WaterMaskClass.Water, (_, _) => 10);
        var fairway = new SurfacePart(
            [new(0.0, 0.0), new(0.0, 0.03), new(0.003, 0.03), new(0.003, 0.0), new(0.0, 0.0)],
            []);
        var request = new RoutePlanRequest([new(0.006, 0.002), new(0.006, 0.028)], 2) { CellMeters = 20, PreferredAreas = [fairway], PreferredCostFactor = 0.3 };

        var plan = RoutePlanner.Plan(sources, request, TestContext.Current.CancellationToken).Plan!;

        Assert.Contains(plan.Waypoints, w => w.Latitude < 0.003);
    }

    [Fact]
    public void Fewer_than_two_stops_are_refused() =>
        Assert.Throws<ArgumentException>(() => RoutePlanner.Plan(Wall, new RoutePlanRequest([new(0, 0)], 2), TestContext.Current.CancellationToken));

    private static RoutePlan Plan(RouteCheckSources sources, GeoPosition from, GeoPosition to, GeoPosition[]? via = null, double clearance = 0)
    {
        var stops = new List<GeoPosition> { from };
        stops.AddRange(via ?? []);
        stops.Add(to);
        var result = RoutePlanner.Plan(sources, new RoutePlanRequest(stops, 2) { CellMeters = 20, ClearanceMeters = clearance }, TestContext.Current.CancellationToken);
        Assert.True(result.Plan is not null, result.Failure?.Message);
        return result.Plan!;
    }

    // Every leg, walked at 10 m, stays in water.
    private static void AssertNoLand(IReadOnlyList<GeoPosition> points, IWaterMask mask)
    {
        for (var k = 1; k < points.Count; k++)
        {
            var length = PassageGeodesy.RhumbDistanceMeters(points[k - 1], points[k]);
            var n = (int)Math.Ceiling(length / 10);
            for (var i = 0; i <= n; i++)
            {
                var p = PassageGeodesy.RhumbInterpolate(points[k - 1], points[k], (double)i / n);
                Assert.Equal(WaterMaskClass.Water, mask.Classify(p));
            }
        }
    }

    private static RouteCheckSources Sources(Func<GeoPosition, WaterMaskClass> mask, Func<double, double, double?> depth) => new()
    {
        WaterMask = new FakeMask(mask),
        Depth = new FakeDepth((lat, lon) => mask(new GeoPosition(lat, lon)) == WaterMaskClass.Land ? null : depth(lat, lon) is { } d ? FakeDepth.DepthArea(d) : null),
    };

    private sealed class FakeMask(Func<GeoPosition, WaterMaskClass> classify) : IWaterMask
    {
        public WaterMaskClass Classify(GeoPosition position) => classify(position);
    }
}
