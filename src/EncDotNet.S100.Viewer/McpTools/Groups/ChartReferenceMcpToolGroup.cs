using EncDotNet.S100.Mcp;
using EncDotNet.S100.Viewer.Services.ChartReference;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The chart reference tools (#1635): <c>search_chart_reference</c> and <c>get_chart_reference_entry</c>.</summary>
internal sealed class ChartReferenceMcpToolGroup : IViewerMcpToolGroup
{
    private readonly ChartReferenceService _service;

    public ChartReferenceMcpToolGroup(ChartReferenceService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    /// <inheritdoc />
    public string Toolset => McpToolsets.Display;

    /// <inheritdoc />
    public IEnumerable<McpServerTool> CreateTools() => McpToolsets.AssignDefault(Toolset, CreateGroupTools());

    private IEnumerable<McpServerTool> CreateGroupTools()
    {
        yield return ChartReferenceMcpAdapter.Create(new SearchChartReferenceTool(_service, new DispatcherRouteEditInvoker()));
        yield return ChartReferenceMcpAdapter.Create(new GetChartReferenceEntryTool(_service));
    }
}
