using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The viewer's routes as seen by <c>get_passage_result</c>: leg geometry,
/// the per-leg values the route carries, and the view time as departure.
/// </summary>
public class ViewerPassageRouteProviderTests
{
    private static readonly DateTime ViewTime = new(2021, 4, 1, 7, 10, 0, DateTimeKind.Utc);

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeTimeline(DateTime? viewTime) : IViewerTimelineController
    {
        public Task<ViewerTimelineState> GetStateAsync(CancellationToken ct = default) =>
            Task.FromResult(new ViewerTimelineState(
                true, ViewTime.AddHours(-3), viewTime, viewTime is null, null, null, 0, [], [], false, false, "", "", []));

        public Task<ViewTimeOutcome> SetViewTimeAsync(DateTime? time, bool snapToNearestSample, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ViewTimeOutcome> StepAsync(TimelineStepKind? kind, int direction, int count, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ViewTimeOutcome> SetViewAsync(TimelineViewChange change, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private static RoutesService Routes()
    {
        var routes = new RoutesService();
        var route = routes.Routes.CreateRoute("Nab Tower to Southampton", "r1");
        route.AppendWaypoint(new GeoPosition(50.64, -0.935));
        route.AppendWaypoint(new GeoPosition(50.672, -0.978));
        route.AppendWaypoint(new GeoPosition(50.725, -1.04));
        route.Legs[0].DraftMeters = 11.5;
        route.Legs[0].SpeedOverGroundMaxKnots = 12;
        route.Legs[0].StaticUnderKeelClearanceMeters = 1.0;
        route.Legs[1].GeometryType = RouteLegGeometryType.Geodesic;
        route.Legs[1].SpeedOverGroundMinKnots = 8;
        return routes;
    }

    [Fact]
    public async Task The_active_route_maps_to_legs_with_its_values_and_the_view_time()
    {
        var provider = new ViewerPassageRouteProvider(Routes(), new InlineInvoker(), new FakeTimeline(ViewTime));

        var result = await provider.GetRouteAsync(null, TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var route));
        Assert.Equal("r1", route!.RouteId);
        Assert.Equal("Nab Tower to Southampton", route.Name);
        Assert.Equal(new DateTimeOffset(ViewTime), route.DefaultDeparture);
        Assert.Equal(2, route.Legs.Count);

        var first = route.Legs[0];
        Assert.Equal([new GeoPosition(50.64, -0.935), new GeoPosition(50.672, -0.978)], first.Points);
        Assert.Equal(LegGeometry.Rhumb, first.Geometry);
        Assert.Equal(11.5, first.DraftMeters);
        Assert.Equal(12, first.SpeedKnots);
        Assert.Equal(1.0, first.RequiredUkcMeters);
        Assert.Null(first.SquatMeters);
        Assert.Equal("Leg 1 → 2", first.Name);

        var second = route.Legs[1];
        Assert.Equal(LegGeometry.GreatCircle, second.Geometry);
        Assert.Null(second.DraftMeters);
        Assert.Equal(8, second.SpeedKnots);
    }

    [Fact]
    public async Task Without_a_timeline_the_clock_supplies_the_departure()
    {
        var now = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var provider = new ViewerPassageRouteProvider(Routes(), new InlineInvoker(), timeline: null, new FixedClock(now));

        var result = await provider.GetRouteAsync("r1", TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var route));
        Assert.Equal(now, route!.DefaultDeparture);
    }

    [Fact]
    public async Task An_unknown_route_is_route_not_found()
    {
        var provider = new ViewerPassageRouteProvider(Routes(), new InlineInvoker(), new FakeTimeline(ViewTime));

        var result = await provider.GetRouteAsync("missing", TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<RouteNotFound>(error);
    }
}
