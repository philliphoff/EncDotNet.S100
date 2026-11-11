using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>Check passage: check mode and state, the passage cursor, departure, following the passage, and the findings list.</summary>
internal sealed class PassageCheckMcpToolGroup : IViewerMcpToolGroup
{
    private readonly RoutesService _routes;
    private readonly EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService _passageCheck;
    private readonly EncDotNet.S100.Viewer.Services.PassageCheck.RouteFindingsService _routeFindings;
    private readonly EncDotNet.S100.Viewer.Services.Depth.DepthBasisService _depthBasis;
    private readonly EncDotNet.S100.Viewer.Services.PassageCheck.FollowPassageService _followPassage;
    private readonly IViewerTimelineController _timeline;

    public PassageCheckMcpToolGroup(
        RoutesService routes,
        EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService passageCheck,
        EncDotNet.S100.Viewer.Services.PassageCheck.RouteFindingsService routeFindings,
        EncDotNet.S100.Viewer.Services.Depth.DepthBasisService depthBasis,
        EncDotNet.S100.Viewer.Services.PassageCheck.FollowPassageService followPassage,
        IViewerTimelineController timeline)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(passageCheck);
        ArgumentNullException.ThrowIfNull(routeFindings);
        ArgumentNullException.ThrowIfNull(depthBasis);
        ArgumentNullException.ThrowIfNull(followPassage);
        ArgumentNullException.ThrowIfNull(timeline);

        _routes = routes;
        _passageCheck = passageCheck;
        _routeFindings = routeFindings;
        _depthBasis = depthBasis;
        _followPassage = followPassage;
        _timeline = timeline;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var routes = _routes;
        var check = _passageCheck;
        var findings = _routeFindings;
        var invoker = new DispatcherRouteEditInvoker();
        yield return PassageCheckMcpAdapters.Create(new SetCheckModeTool(routes, invoker, check, findings) { DepthBasis = _depthBasis });
        yield return PassageCheckMcpAdapters.Create(new GetCheckStateTool(routes, invoker, check, findings) { DepthBasis = _depthBasis });
        yield return PassageCheckMcpAdapters.Create(new SetPassageCursorTool(routes, invoker, check, findings) { DepthBasis = _depthBasis });
        yield return PassageCheckMcpAdapters.Create(new SetDepartureTool(routes, invoker, check, findings) { DepthBasis = _depthBasis });
        yield return PassageCheckMcpAdapters.Create(new SetFollowPassageTool(_followPassage, check, invoker, _timeline));
        yield return RouteFindingsMcpAdapters.Create(new GetFindingsTool(routes, invoker, check, findings));
        yield return RouteFindingsMcpAdapters.Create(new SelectFindingTool(routes, invoker, check, findings));
        yield return RouteFindingsMcpAdapters.Create(new SetFindingsViewTool(routes, invoker, check, findings));
    }
}
