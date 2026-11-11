using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The legend tools: <c>get_legend</c> (#1403) and <c>set_legend</c> (#1408, hover to find #1412).</summary>
internal sealed class LegendMcpToolGroup : IViewerMcpToolGroup
{
    private readonly LegendService _legend;
    private readonly PositionLegendService _position;
    private readonly LegendViewModel _tab;
    private readonly IViewerUiControllerAccessor _ui;
    private readonly LegendFindService _find;

    public LegendMcpToolGroup(LegendService legend, PositionLegendService position, LegendViewModel tab, IViewerUiControllerAccessor ui, LegendFindService find)
    {
        ArgumentNullException.ThrowIfNull(find);
        _find = find;
        ArgumentNullException.ThrowIfNull(legend);
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(ui);
        _legend = legend;
        _position = position;
        _tab = tab;
        _ui = ui;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        var invoker = new DispatcherRouteEditInvoker();
        yield return LegendMcpAdapter.Create(new GetLegendTool(_legend, _position, invoker));
        yield return LegendMcpAdapter.Create(new SetLegendTool(_legend, _position, _tab, _ui, invoker, _find));
    }
}
