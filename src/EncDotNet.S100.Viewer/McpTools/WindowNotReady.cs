using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The viewer's application window has not been attached yet (or has no
/// on-screen size), so a whole-window PNG snapshot cannot be produced.
/// Returned by <see cref="CaptureAppScreenshotTool"/>. Distinguished from
/// <see cref="MapNotReady"/> in that it is the surrounding window chrome —
/// not the map control — that is unavailable.
/// </summary>
/// <param name="Reason">Single-sentence description of why no window snapshot could be produced.</param>
[Description("Raised when the viewer's application window is not attached yet (or has no on-screen size) and a whole-window screenshot cannot be produced.")]
internal sealed record WindowNotReady(
    [property: Description("Single-sentence description of why no window snapshot could be produced.")] string Reason)
    : ToolError("window_not_ready", $"The viewer's application window is not ready: {Reason}.");
