using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Hands viewer routes to the built-in <c>check_route</c> tool with the same
/// effective values Check route uses (#897 passage model: route values, leg
/// overrides, squat and cross-track limits), and the route's departure,
/// falling back to the view time, so the tool's numbers match what the panel
/// shows.
/// </summary>
internal sealed class ViewerPassageRouteProvider : IPassageRouteProvider
{
    private readonly RoutesService _routes;
    private readonly IRouteEditInvoker _invoker;

    public ViewerPassageRouteProvider(RoutesService routes, IRouteEditInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(invoker);
        _routes = routes;
        _invoker = invoker;
    }

    public Task<ToolResult<PassageRouteSnapshot>> GetRouteAsync(string? routeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _invoker.InvokeAsync(() => Snapshot(routeId));
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

        var effective = RouteEtas.ToPassageLegs(route);
        var legs = new List<PassageRouteLeg>(route.Legs.Count);
        for (var i = 0; i < route.Legs.Count; i++)
        {
            legs.Add(new PassageRouteLeg(
                effective[i].Points,
                effective[i].Geometry,
                route.GetEffectiveLegValue(i, RoutePassageField.Draft),
                effective[i].Speed?.TotalKnots,
                route.Passage.SquatMeters,
                effective[i].RequiredUkc?.TotalMetres,
                $"Leg {i + 1} → {i + 2}",
                route.GetEffectiveLegValue(i, RoutePassageField.PortCrossTrack),
                route.GetEffectiveLegValue(i, RoutePassageField.StarboardCrossTrack)));
        }

        return ToolResult<PassageRouteSnapshot>.Ok(new PassageRouteSnapshot(
            route.Id, route.Name, legs, route.Passage.Departure ?? _routes.ViewTime.Current));
    }
}
