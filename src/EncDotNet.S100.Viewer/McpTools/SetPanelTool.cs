using System;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>Request payload for <see cref="SetPanelTool"/>.</summary>
internal sealed record SetPanelRequest(string Panel, bool? Visible);

/// <summary>Result payload for <see cref="SetPanelTool"/>.</summary>
internal sealed record SetPanelResult(
    string Panel,
    string Title,
    string Dock,
    bool Available,
    bool Selected,
    bool DockOpen,
    bool Showing,
    bool PreviousShowing,
    bool Changed);

/// <summary>
/// MCP tool that shows or hides one of the live viewer's activity panels
/// (a tab in the left / right / bottom dock). Showing selects the panel's
/// tab and opens its dock; hiding closes the panel's dock when the panel is
/// the one currently shown there. Idempotent — a panel already in the
/// requested state is left untouched. Lets scripted runs drive non-render
/// UX (e.g. the Datasets, Layers, Pick Report, or Timeline panels) from
/// outside the GUI so a code / run / verify loop can assert panel state.
/// </summary>
internal sealed class SetPanelTool
{
    /// <summary>The MCP tool name as exposed to clients.</summary>
    public const string Name = "set_panel";

    private readonly IViewerUiControllerAccessor _accessor;

    public SetPanelTool(IViewerUiControllerAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    /// <summary>
    /// Shows (default) or hides the requested panel. Returns the resulting
    /// panel state plus whether it changed. Rejects an unknown panel id
    /// (<see cref="PanelNotFound"/>) and an attempt to show a panel that is
    /// not currently available (<see cref="PanelUnavailable"/>).
    /// </summary>
    public async Task<ToolResult<SetPanelResult>> InvokeAsync(
        SetPanelRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Panel))
        {
            return ToolResult<SetPanelResult>.Err(new InvalidArgument(
                "panel", "value is required; call list_panels for the valid ids"));
        }

        var visible = request.Visible ?? true;
        var panelId = request.Panel.Trim();

        var controller = _accessor.Current;
        if (controller is null)
        {
            return ToolResult<SetPanelResult>.Err(new MapNotReady(
                "the viewer's UI controller has not been initialised yet"));
        }

        var outcome = await controller.SetPanelVisibilityAsync(panelId, visible, ct).ConfigureAwait(false);

        if (!outcome.Found)
        {
            return ToolResult<SetPanelResult>.Err(new PanelNotFound(panelId));
        }

        // A well-formed request to show a conditionally-registered panel that
        // is not currently available cannot be honoured; hiding one is a
        // harmless no-op, so only guard the show path.
        if (visible && !outcome.Available)
        {
            return ToolResult<SetPanelResult>.Err(new PanelUnavailable(panelId));
        }

        var state = outcome.State!.Value;
        return ToolResult<SetPanelResult>.Ok(new SetPanelResult(
            Panel: state.Id,
            Title: state.Title,
            Dock: state.Dock,
            Available: state.Available,
            Selected: state.Selected,
            DockOpen: state.DockOpen,
            Showing: state.Showing,
            PreviousShowing: outcome.PreviousShowing,
            Changed: state.Showing != outcome.PreviousShowing));
    }
}
