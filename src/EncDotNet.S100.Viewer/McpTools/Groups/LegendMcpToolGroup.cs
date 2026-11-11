using EncDotNet.S100.Viewer.Services.Legends;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The legend tools: <c>get_legend</c> (#1403).</summary>
internal sealed class LegendMcpToolGroup : IViewerMcpToolGroup
{
    private readonly LegendService _legend;

    public LegendMcpToolGroup(LegendService legend)
    {
        ArgumentNullException.ThrowIfNull(legend);
        _legend = legend;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        yield return LegendMcpAdapter.Create(new GetLegendTool(_legend, new DispatcherRouteEditInvoker()));
    }
}
