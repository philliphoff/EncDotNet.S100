using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Hands viewer routes to the built-in <c>get_passage_result</c> tool: the
/// leg geometry plus the values the route already carries, and the view
/// time as the default departure.
/// </summary>
/// <remarks>
/// Until the passage model (#897) lands, the values come from the existing
/// per-leg S-421 attributes: draft from <see cref="RouteLeg.DraftMeters"/>,
/// speed from <see cref="RouteLeg.SpeedOverGroundMaxKnots"/> (else the
/// minimum), and required UKC from
/// <see cref="RouteLeg.StaticUnderKeelClearanceMeters"/>. There is no squat
/// field yet; the request supplies it.
/// </remarks>
internal sealed class ViewerPassageRouteProvider : IPassageRouteProvider
{
    private readonly RoutesService _routes;
    private readonly IRouteEditInvoker _invoker;
    private readonly IViewerTimelineController? _timeline;
    private readonly TimeProvider _clock;

    public ViewerPassageRouteProvider(
        RoutesService routes,
        IRouteEditInvoker invoker,
        IViewerTimelineController? timeline,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(invoker);
        _routes = routes;
        _invoker = invoker;
        _timeline = timeline;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ToolResult<PassageRouteSnapshot>> GetRouteAsync(string? routeId, CancellationToken cancellationToken)
    {
        var snapshot = await _invoker.InvokeAsync(() => Snapshot(routeId)).ConfigureAwait(false);
        if (snapshot.TryGetError(out _) || _timeline is null)
        {
            return snapshot;
        }

        var state = await _timeline.GetStateAsync(cancellationToken).ConfigureAwait(false);
        snapshot.TryGetValue(out var route);
        var viewTime = state.ViewTime ?? state.Now;
        return ToolResult<PassageRouteSnapshot>.Ok(route! with
        {
            DefaultDeparture = new DateTimeOffset(DateTime.SpecifyKind(viewTime, DateTimeKind.Utc)),
        });
    }

    private ToolResult<PassageRouteSnapshot> Snapshot(string? routeId)
    {
        var route = string.IsNullOrWhiteSpace(routeId)
            ? _routes.Routes.ActiveRoute
            : _routes.Routes.FindById(routeId);
        if (route is null)
        {
            return ToolResult<PassageRouteSnapshot>.Err(new RouteNotFound(string.IsNullOrWhiteSpace(routeId) ? "(active)" : routeId));
        }

        var legs = new List<PassageRouteLeg>(route.Legs.Count);
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            var from = route.Waypoints[i].Position;
            var to = route.Waypoints[i + 1].Position;
            legs.Add(new PassageRouteLeg(
                [new GeoPosition(from.Latitude, from.Longitude), new GeoPosition(to.Latitude, to.Longitude)],
                leg.GeometryType == RouteLegGeometryType.Geodesic ? LegGeometry.GreatCircle : LegGeometry.Rhumb,
                leg.DraftMeters,
                leg.SpeedOverGroundMaxKnots ?? leg.SpeedOverGroundMinKnots,
                SquatMeters: null,
                leg.StaticUnderKeelClearanceMeters,
                $"Leg {i + 1} → {i + 2}"));
        }

        return ToolResult<PassageRouteSnapshot>.Ok(new PassageRouteSnapshot(route.Id, route.Name, legs, _clock.GetUtcNow()));
    }
}
