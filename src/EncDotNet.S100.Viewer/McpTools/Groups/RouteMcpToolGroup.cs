using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>Routes: editing (with undo and redo), received routes and route exchange,
/// the passage card and passage, and the Clearance tab.</summary>
internal sealed class RouteMcpToolGroup : IViewerMcpToolGroup
{
    private readonly RoutesService _routes;
    private readonly RouteExchangeService _routeExchange;
    private readonly EncDotNet.S100.Viewer.ViewModels.ClearanceViewModel _clearance;

    public RouteMcpToolGroup(
        RoutesService routes,
        RouteExchangeService routeExchange,
        EncDotNet.S100.Viewer.ViewModels.ClearanceViewModel clearance)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(routeExchange);
        ArgumentNullException.ThrowIfNull(clearance);

        _routes = routes;
        _routeExchange = routeExchange;
        _clearance = clearance;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var routes = _routes;
        var invoker = new DispatcherRouteEditInvoker();
        yield return CreateRouteMcpAdapter.Create(new CreateRouteTool(routes, invoker));
        yield return ListRoutesMcpAdapter.Create(new ListRoutesTool(routes, invoker));
        yield return GetRouteMcpAdapter.Create(new GetRouteTool(routes, invoker));
        yield return DeleteRouteMcpAdapter.Create(new DeleteRouteTool(routes, invoker));
        yield return AppendWaypointMcpAdapter.Create(new AppendWaypointTool(routes, invoker));
        yield return InsertWaypointMcpAdapter.Create(new InsertWaypointTool(routes, invoker));
        yield return MoveWaypointMcpAdapter.Create(new MoveWaypointTool(routes, invoker));
        yield return DeleteWaypointMcpAdapter.Create(new DeleteWaypointTool(routes, invoker));
        yield return SetLegAttributesMcpAdapter.Create(new SetLegAttributesTool(routes, invoker));
        yield return SetRouteInfoMcpAdapter.Create(new SetRouteInfoTool(routes, invoker));
        var clearanceTools = new ClearanceTools(_clearance, invoker);
        yield return clearanceTools.CreateGet();
        yield return clearanceTools.CreateSet();
        yield return UndoRouteEditMcpAdapter.Create(new UndoRouteEditTool(routes, invoker));
        yield return RedoRouteEditMcpAdapter.Create(new RedoRouteEditTool(routes, invoker));
        yield return SetRouteEditModeMcpAdapter.Create(new SetRouteEditModeTool(routes, invoker));
        yield return ReceivedRouteMcpAdapters.Create(new SetRouteDisplayModeTool(routes, invoker));
        yield return ReceivedRouteMcpAdapters.Create(new ImportRouteTool(routes, invoker, _routeExchange, Path.Combine(Path.GetTempPath(), "soundcharts-route-imports")));
        yield return ReceivedRouteMcpAdapters.Create(new CopyRouteTool(routes, invoker, _routeExchange));
        yield return ReceivedRouteMcpAdapters.Create(new ExportRouteTool(routes, invoker, _routeExchange));
        yield return SelectRouteItemMcpAdapter.Create(new SelectRouteItemTool(routes, invoker));
        yield return SetPassageCardMcpAdapter.Create(new SetPassageCardTool(routes, invoker));
        yield return GetPassageMcpAdapter.Create(new GetPassageTool(routes, invoker));
        yield return SetPassageMcpAdapter.Create(new SetPassageTool(routes, invoker));
    }
}
