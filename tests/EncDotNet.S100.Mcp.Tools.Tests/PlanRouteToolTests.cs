using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>plan_route</c> (#1560) over a synthetic S-101 cell: 10 m of water west
/// of 0.025°, 1 m east of it.
/// </summary>
public sealed class PlanRouteToolTests
{
    private static readonly VesselArguments Vessel = new(DraftMeters: 1.1, SafetyContourMeters: 2, SafetyDepthMeters: 2, SpeedKnots: 6);

    [Fact]
    public async Task A_route_across_deep_water_is_planned_and_checked()
    {
        var result = Ok(await Tool().InvokeAsync(new PlanRouteRequest(new GeoPoint(0.005, 0.005), new GeoPoint(0.045, 0.02), Vessel: Vessel), TestContext.Current.CancellationToken));

        Assert.Equal(2, result.MinimumDepthMeters);
        Assert.Equal(1.1, result.BerthDepthMeters);
        Assert.InRange(result.DistanceNm, 2.3, 2.6);
        Assert.Equal("from", result.Waypoints[0].Name);
        Assert.Equal("to", result.Waypoints[^1].Name);
        Assert.StartsWith("{\"vertices\":[[0.005,0.005]", result.Polyline, StringComparison.Ordinal);
        var leg = Assert.Single(result.Legs);
        Assert.Equal(result.Waypoints.Count - 1, leg.LastWaypoint);
        Assert.InRange(leg.RunningMinutes!.Value, 23, 26);
        Assert.Equal(10, leg.LeastDepthMeters);
        Assert.NotNull(result.Check);
        Assert.Equal(0, result.Check!.MustReviewAwayFromStops);
        Assert.Null(result.RouteId);
        Assert.Contains("Not for navigation", result.Caveat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stop_on_the_shoal_moves_west_and_via_points_add_legs()
    {
        var request = new PlanRouteRequest(
            new GeoPoint(0.005, 0.005),
            new GeoPoint(0.045, 0.04),
            Via: [new GeoPoint(0.025, 0.01)],
            Vessel: Vessel,
            Check: false);

        var result = Ok(await Tool().InvokeAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(2, result.Legs.Count);
        Assert.Equal("via 1", result.Waypoints[result.Legs[0].LastWaypoint].Name);
        var to = result.Stops[^1];
        Assert.Equal("to", to.Role);
        Assert.True(to.Moved);
        Assert.Equal("shoal", to.Reason);
        Assert.True(to.Longitude < 0.025);
        Assert.InRange(to.BearingDegrees!.Value, 260, 280);
        Assert.Null(result.Check);
    }

    [Fact]
    public async Task A_stop_with_no_water_near_is_reported()
    {
        var request = new PlanRouteRequest(new GeoPoint(0.005, 0.005), new GeoPoint(0.025, 0.045), Vessel: Vessel, Limits: new PlanRouteLimits(SnapRadiusMeters: 500));

        var result = await Tool().InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        var none = Assert.IsType<NoRoutePlanned>(error);
        Assert.Equal("no_route_planned", none.Code);
        Assert.Equal("stop_not_navigable", none.Reason);
        Assert.Equal(1, none.StopIndex);
    }

    [Fact]
    public async Task RouteName_creates_the_route_where_the_host_has_routes()
    {
        var writer = new FakeWriter();
        var request = new PlanRouteRequest(new GeoPoint(0.005, 0.005), new GeoPoint(0.045, 0.02), Vessel: Vessel, Check: false, RouteName: "Day 1");

        var result = Ok(await Tool(writer).InvokeAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("route-1", result.RouteId);
        Assert.Equal("Day 1", writer.Name);
        Assert.Equal(result.Waypoints, writer.Waypoints);

        var refused = await Tool().InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(refused.TryGetError(out var error));
        Assert.Equal("routeName", Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Theory]
    [InlineData("vessel")]
    [InlineData("prefer")]
    [InlineData("limits.snapRadiusMeters")]
    [InlineData("vessel.speedKnots")]
    public async Task Bad_arguments_are_refused(string parameter)
    {
        var request = new PlanRouteRequest(new GeoPoint(0.005, 0.005), new GeoPoint(0.045, 0.02), Vessel: Vessel);
        request = parameter switch
        {
            "vessel" => request with { Vessel = new VesselArguments(SpeedKnots: 6) },
            "prefer" => request with { Prefer = ["anchorages"] },
            "limits.snapRadiusMeters" => request with { Limits = new PlanRouteLimits(SnapRadiusMeters: 50_000) },
            _ => request with { Vessel = Vessel with { SpeedKnots = -1 } },
        };

        var result = await Tool().InvokeAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(parameter, Assert.IsType<InvalidArgument>(error).Parameter);
    }

    private static PlanRouteResult Ok(ToolResult<PlanRouteResult> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    private static PlanRouteTool Tool(IPassageRouteWriter? writer = null)
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S101(
            "101PLAN",
            S101AreaSynth.Cell(
            [
                S101AreaSynth.DataCoverage(22_000, S101AreaSynth.Box(0, 0, 0.05, 0.05)),
                S101AreaSynth.DepthArea(10, S101AreaSynth.Box(0, 0, 0.05, 0.025)),
                S101AreaSynth.DepthArea(1, S101AreaSynth.Box(0, 0.025, 0.05, 0.05)),
            ]),
            LoadedDatasetFactory.Box(0, 0, 0.05, 0.05)));
        var sources = new RouteCheckSourceCache(new ProjNetCrsTransformFactory());
        return new PlanRouteTool(catalog, sources, new CheckRouteTool(catalog, null, sources, TimeProvider.System), writer);
    }

    private sealed class FakeWriter : IPassageRouteWriter
    {
        public string? Name { get; private set; }

        public IReadOnlyList<PlanRouteWaypoint>? Waypoints { get; private set; }

        public Task<ToolResult<string>> CreateRouteAsync(string name, IReadOnlyList<PlanRouteWaypoint> waypoints, CancellationToken cancellationToken)
        {
            Name = name;
            Waypoints = waypoints;
            return Task.FromResult(ToolResult<string>.Ok("route-1"));
        }
    }
}
