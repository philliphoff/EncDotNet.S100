using System.ComponentModel;
using EncDotNet.S100.Mcp.Tools;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The supplied panel identifier does not match any activity panel
/// registered in the viewer. Distinguished from
/// <see cref="InvalidArgument"/> in that the value is well-formed; it
/// simply does not resolve to a known panel.
/// </summary>
/// <param name="PanelId">The panel identifier that could not be resolved.</param>
[Description("Raised when the requested panel id does not match any activity panel registered in the viewer (call list_panels for the valid ids).")]
internal sealed record PanelNotFound(
    [property: Description("The panel identifier that could not be resolved.")] string PanelId)
    : ToolError("panel_not_found", $"No activity panel with id '{PanelId}' is registered in the viewer.");
