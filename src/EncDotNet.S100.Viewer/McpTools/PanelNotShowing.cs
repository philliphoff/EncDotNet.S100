using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The panel exists but is not on screen, so it cannot be captured on its own
/// (<c>capture_app_screenshot</c> <c>panel</c>).
/// </summary>
/// <param name="PanelId">The panel identifier that is not showing.</param>
[Description("Raised when capture_app_screenshot is asked to crop to a panel that is not on screen (show it with set_panel first).")]
internal sealed record PanelNotShowing(
    [property: Description("The panel identifier that is not showing.")] string PanelId)
    : ToolError("panel_not_showing", $"Panel '{PanelId}' is not showing; show it with set_panel first.");
