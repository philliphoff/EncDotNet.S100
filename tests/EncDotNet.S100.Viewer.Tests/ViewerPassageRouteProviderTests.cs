using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The viewer's routes as seen by <c>get_passage_result</c>: the same
/// effective passage values Check passage uses, and the route's departure,
/// falling back to the view time.
/// </summary>
public class ViewerPassageRouteProviderTests
{
    private static readonly DateTimeOffset ViewTime = new(2021, 4, 1, 7, 10, 0, TimeSpan.Zero);

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class FixedViewTime(DateTimeOffset now) : IRouteViewTime
    {
        public DateTimeOffset Current => now;

        public event EventHandler? Changed { add { } remove { } }
    }

    private static RoutesService Routes()
    {
        var routes = new RoutesService(new FixedViewTime(ViewTime));
        var route = routes.Routes.CreateRoute("Nab Tower to Southampton", "r1");
        route.AppendWaypoint(new GeoPosition(50.64, -0.935));
        route.AppendWaypoint(new GeoPosition(50.672, -0.978));
        route.AppendWaypoint(new GeoPosition(50.725, -1.04));
        route.SetPassageValue(RoutePassageField.Draft, 11.5);
        route.SetPassageValue(RoutePassageField.Speed, 12);
        route.SetPassageValue(RoutePassageField.Squat, 0.3);
        route.SetPassageValue(RoutePassageField.RequiredUkc, 1.0);
        route.SetLegOverride(1, RoutePassageField.Speed, 8);
        route.Legs[1].GeometryType = RouteLegGeometryType.Geodesic;
        return routes;
    }

    [Fact]
    public async Task The_active_route_maps_to_its_effective_passage_values_and_the_view_time()
    {
        var provider = new ViewerPassageRouteProvider(Routes(), new InlineInvoker());

        var result = await provider.GetRouteAsync(null, TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var route));
        Assert.Equal("r1", route!.RouteId);
        Assert.Equal("Nab Tower to Southampton", route.Name);
        Assert.Equal(ViewTime, route.DefaultDeparture);
        Assert.Equal(2, route.Legs.Count);

        var first = route.Legs[0];
        Assert.Equal([new GeoPosition(50.64, -0.935), new GeoPosition(50.672, -0.978)], first.Points);
        Assert.Equal(LegGeometry.Rhumb, first.Geometry);
        Assert.Equal(11.5, first.DraftMeters);
        Assert.Equal(12, first.SpeedKnots);
        Assert.Equal(0.3, first.SquatMeters);
        Assert.Equal(1.0, first.RequiredUkcMeters);
        Assert.Equal("Leg 1 → 2", first.Name);

        var second = route.Legs[1];
        Assert.Equal(LegGeometry.GreatCircle, second.Geometry);
        Assert.Equal(11.5, second.DraftMeters);
        Assert.Equal(8, second.SpeedKnots);
    }

    [Fact]
    public async Task A_route_departure_wins_over_the_view_time()
    {
        var routes = Routes();
        var departure = ViewTime.AddHours(-1);
        routes.Routes.FindById("r1")!.SetDeparture(departure);
        var provider = new ViewerPassageRouteProvider(routes, new InlineInvoker());

        var result = await provider.GetRouteAsync("r1", TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var route));
        Assert.Equal(departure, route!.DefaultDeparture);
    }

    [Fact]
    public async Task An_unknown_route_is_route_not_found()
    {
        var provider = new ViewerPassageRouteProvider(Routes(), new InlineInvoker());

        var result = await provider.GetRouteAsync("missing", TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.IsType<RouteNotFound>(error);
    }
}
