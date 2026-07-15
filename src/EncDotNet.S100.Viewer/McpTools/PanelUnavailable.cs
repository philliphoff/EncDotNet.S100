using System.ComponentModel;
using EncDotNet.S100.Mcp.Tools;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The requested panel exists but is not currently available to be shown.
/// A few panels are conditionally registered in the activity bar — e.g.
/// <c>Vessels</c> only while the AIS overlay is enabled and <c>Helm</c>
/// only while own-ship tracking is enabled — so they cannot be shown until
/// their enabling condition is met. Distinguished from
/// <see cref="InvalidArgument"/> in that the caller's request is
/// well-formed; the viewer's current state simply forbids it.
/// </summary>
/// <param name="PanelId">The panel identifier that could not be shown.</param>
[Description("Raised when a well-formed request asks to show a panel that is not currently available (e.g. the Vessels panel while the AIS overlay is disabled, or the Helm panel while own-ship tracking is off).")]
internal sealed record PanelUnavailable(
    [property: Description("The panel identifier that could not be shown.")] string PanelId)
    : ToolError("panel_unavailable", $"Panel '{PanelId}' is not currently available; enable its owning feature first.");
