using EncDotNet.S100.Mcp;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The catalogue tools (design note <c>catalogue-panels.md</c> CR1): <c>list_catalogues</c>.</summary>
internal sealed class CataloguesMcpToolGroup : IViewerMcpToolGroup
{
    private readonly CatalogueRegistry _registry;
    private readonly CatalogueRedrawCoordinator _redraw;

    public CataloguesMcpToolGroup(CatalogueRegistry registry, CatalogueRedrawCoordinator redraw)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(redraw);
        _registry = registry;
        _redraw = redraw;
    }

    /// <inheritdoc />
    public string Toolset => McpToolsets.Display;

    /// <inheritdoc />
    public IEnumerable<McpServerTool> CreateTools() => McpToolsets.AssignDefault(Toolset, CreateGroupTools());

    private IEnumerable<McpServerTool> CreateGroupTools()
    {
        yield return CatalogueMcpAdapter.Create(
            new ListCataloguesTool(_registry, spec => _redraw.AffectedBy(spec).Count, new DispatcherRouteEditInvoker()));
    }
}
