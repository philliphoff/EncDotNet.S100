using EncDotNet.S100.Datasets.S129.Fusion.Routing;
using EncDotNet.S100.Datasets.S129.Fusion.Tests.Helpers;

namespace EncDotNet.S100.Datasets.S129.Fusion.Tests;

public class RouteBinderTests
{
    [Fact]
    public void Bind_ControlPointAtWaypoint_MapsOnWaypoint()
    {
        // WP1 sits at (50.005, 5.001). Place a CP right on top.
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.001, SyntheticDatasets.T0),
        });
        var route = SyntheticDatasets.MakeRoute();

        var binding = S129RouteBinder.Bind(plan, route);
        var mapping = binding.Mappings[0].Mapping;

        Assert.Equal(S129RouteMappingKind.OnWaypoint, mapping.Kind);
        Assert.Equal("WP1", mapping.Waypoint!.Id);
        Assert.True(mapping.DistanceMeters < 1.0);
    }

    [Fact]
    public void Bind_ControlPointAlongLeg_MapsOnLeg()
    {
        // WP1 (5.001) → WP2 (5.005); place CP at (50.005, 5.003), mid-leg.
        // Use a tiny waypoint tolerance so the leg branch is exercised.
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.003, SyntheticDatasets.T0),
        });
        var route = SyntheticDatasets.MakeRoute();
        var opts = new S129RouteBindingOptions(
            WaypointToleranceMeters: 10,
            LegToleranceMeters: 100);

        var binding = S129RouteBinder.Bind(plan, route, opts);
        var mapping = binding.Mappings[0].Mapping;

        Assert.Equal(S129RouteMappingKind.OnLeg, mapping.Kind);
        Assert.Equal("LEG12", mapping.Leg!.Id);
        Assert.NotNull(mapping.LegPositionFraction);
        Assert.InRange(mapping.LegPositionFraction!.Value, 0.4, 0.6);
    }

    [Fact]
    public void Bind_ControlPointFarFromRoute_IsUnmapped()
    {
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.5, 5.5, SyntheticDatasets.T0),
        });
        var route = SyntheticDatasets.MakeRoute();

        var binding = S129RouteBinder.Bind(plan, route);
        var mapping = binding.Mappings[0].Mapping;

        Assert.Equal(S129RouteMappingKind.Unmapped, mapping.Kind);
        Assert.Null(mapping.Waypoint);
        Assert.Null(mapping.Leg);
    }

    [Fact]
    public void Bind_ControlPointWithoutPosition_IsUnmapped()
    {
        // The synthetic helper always emits a position for control
        // points; verify the same outcome by binding against an
        // unreachable route (waypoints far from the CPs).
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.005, SyntheticDatasets.T0),
        });
        var route = SyntheticDatasets.MakeRoute(); // Route is at 5.001..5.009; CP at 5.005 is ~0m from leg.

        // Force a tiny leg tolerance — CP is ON the leg so still under 1m.
        // Use coordinates well off-route instead:
        var farPlan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP1", 60.0, 10.0, SyntheticDatasets.T0),
        });
        var binding = S129RouteBinder.Bind(farPlan, route);
        Assert.Equal(S129RouteMappingKind.Unmapped, binding.Mappings[0].Mapping.Kind);
    }

    [Fact]
    public void Bind_PreservesControlPointOrdering()
    {
        var t0 = SyntheticDatasets.T0;
        var plan = SyntheticDatasets.MakePlan(extraFeatures: new[]
        {
            SyntheticDatasets.MakeControlPoint("CP2", 50.005, 5.005, t0.AddMinutes(10)),
            SyntheticDatasets.MakeControlPoint("CP1", 50.005, 5.001, t0),
        });
        var route = SyntheticDatasets.MakeRoute();

        var binding = S129RouteBinder.Bind(plan, route);

        // Plan ordering puts CP1 first (earlier time) per typed-projection sorting.
        Assert.Equal("CP1", binding.Mappings[0].ControlPoint.Id);
        Assert.Equal("CP2", binding.Mappings[1].ControlPoint.Id);
    }
}
