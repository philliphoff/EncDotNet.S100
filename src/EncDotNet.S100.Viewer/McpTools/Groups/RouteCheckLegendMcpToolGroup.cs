using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The Check route map legend: <c>get_map_legend</c> and <c>set_map_legend</c>.</summary>
internal sealed class RouteCheckLegendMcpToolGroup : IViewerMcpToolGroup
{
    private readonly EncDotNet.S100.Viewer.Services.PassageCheck.RouteCheckLegendState _legend;
    private readonly RoutesService _routes;
    private readonly EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService _passageCheck;

    public RouteCheckLegendMcpToolGroup(
        EncDotNet.S100.Viewer.Services.PassageCheck.RouteCheckLegendState legend,
        RoutesService routes,
        EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService passageCheck)
    {
        ArgumentNullException.ThrowIfNull(legend);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(passageCheck);

        _legend = legend;
        _routes = routes;
        _passageCheck = passageCheck;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var invoker = new DispatcherRouteEditInvoker();
        yield return RouteCheckMapMcpAdapters.Create(new GetMapLegendTool(_legend, _routes, _passageCheck, invoker));
        yield return RouteCheckMapMcpAdapters.Create(new SetMapLegendTool(_legend, _routes, _passageCheck, invoker));
    }
}
