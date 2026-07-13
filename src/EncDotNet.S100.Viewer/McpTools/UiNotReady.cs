using System.ComponentModel;
using EncDotNet.S100.Mcp.Tools;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The viewer's UI controller has not been initialised yet (or has been
/// torn down), so panel / activity-bar state cannot be read or changed.
/// Returned by <see cref="ListPanelsTool"/> and <see cref="SetPanelTool"/>.
/// Distinguished from <see cref="MapNotReady"/> (map control) and
/// <see cref="WindowNotReady"/> (window snapshot) in that it is the
/// viewer's non-render UI surface that is unavailable; the caller's
/// request is well-formed, the host environment is simply not ready.
/// </summary>
/// <param name="Reason">Single-sentence description of which aspect of the UI is unavailable.</param>
[Description("Raised when the viewer's UI controller has not been initialised yet (or has been torn down) and panel state cannot be read or changed.")]
internal sealed record UiNotReady(
    [property: Description("Single-sentence description of which aspect of the UI is unavailable.")] string Reason)
    : ToolError("ui_not_ready", $"The viewer's UI is not ready: {Reason}.");
