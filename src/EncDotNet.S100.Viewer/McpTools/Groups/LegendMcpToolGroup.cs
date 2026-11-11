using EncDotNet.S100.Viewer.Services.Legends;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The legend tools: <c>get_legend</c> (#1403).</summary>
internal sealed class LegendMcpToolGroup : IViewerMcpToolGroup
{
    private readonly LegendService _legend;
    private readonly PositionLegendService _position;

    public LegendMcpToolGroup(LegendService legend, PositionLegendService position)
    {
        ArgumentNullException.ThrowIfNull(legend);
        ArgumentNullException.ThrowIfNull(position);
        _legend = legend;
        _position = position;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        yield return LegendMcpAdapter.Create(new GetLegendTool(_legend, _position, new DispatcherRouteEditInvoker()));
    }
}
