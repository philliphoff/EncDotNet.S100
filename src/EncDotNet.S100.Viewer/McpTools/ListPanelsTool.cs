using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Per-panel entry in a <see cref="ListPanelsResult"/>.</summary>
internal sealed record ListPanelsPanel(
    string Id,
    string Title,
    string Dock,
    bool Available,
    bool Selected,
    bool DockOpen,
    bool Showing);

/// <summary>Result payload for <see cref="ListPanelsTool"/>.</summary>
internal sealed record ListPanelsResult(IReadOnlyList<ListPanelsPanel> Panels);

/// <summary>
/// MCP tool that lists the live viewer's activity panels (the tabs in the
/// left / right / bottom docks) and their current visibility. Read-only:
/// it snapshots the activity bar without changing it. Companion of
/// <see cref="SetPanelTool"/> — call this first to discover the valid
/// panel ids and again afterwards to verify a show / hide took effect.
/// </summary>
internal sealed class ListPanelsTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "list_panels";

    private readonly IViewerUiControllerAccessor _accessor;

    public ListPanelsTool(IViewerUiControllerAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>Snapshots every registered activity panel.</summary>
    public async Task<ToolResult<ListPanelsResult>> InvokeAsync(CancellationToken ct = default)
    {
        var controller = _accessor.Current;
        if (controller is null)
        {
            return ToolResult<ListPanelsResult>.Err(new UiNotReady(
                "the viewer's UI controller has not been initialised yet"));
        }

        var panels = await controller.GetPanelsAsync(ct).ConfigureAwait(false);
        var mapped = panels
            .Select(p => new ListPanelsPanel(
                p.Id, p.Title, p.Dock, p.Available, p.Selected, p.DockOpen, p.Showing))
            .ToList();
        return ToolResult<ListPanelsResult>.Ok(new ListPanelsResult(mapped));
    }
}
