using EncDotNet.S100.Mcp;
using EncDotNet.S100.Viewer.Services.ChartReference;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>
/// The chart reference tools (#1635): <c>search_chart_reference</c>,
/// <c>get_chart_reference_entry</c> and, for the Reference tab (#1650),
/// <c>open_chart_reference</c>.
/// </summary>
internal sealed class ChartReferenceMcpToolGroup : IViewerMcpToolGroup
{
    private readonly ChartReferenceService _service;
    private readonly ReferenceNavigator _navigator;

    public ChartReferenceMcpToolGroup(ChartReferenceService service, ReferenceNavigator navigator)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(navigator);
        _service = service;
        _navigator = navigator;
    }

    /// <inheritdoc />
    public string Toolset => McpToolsets.Display;

    /// <inheritdoc />
    public IEnumerable<McpServerTool> CreateTools() => McpToolsets.AssignDefault(Toolset, CreateGroupTools());

    private IEnumerable<McpServerTool> CreateGroupTools()
    {
        yield return ChartReferenceMcpAdapter.Create(new SearchChartReferenceTool(_service, new DispatcherRouteEditInvoker()));
        yield return ChartReferenceMcpAdapter.Create(new GetChartReferenceEntryTool(_service));
        yield return new OpenChartReferenceTool(_service, _navigator, new DispatcherRouteEditInvoker()).CreateTool();
    }
}
