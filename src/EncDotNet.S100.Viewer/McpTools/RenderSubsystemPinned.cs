using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The render subsystem is pinned by the <c>S100_RENDER_SUBSYSTEM</c>
/// environment variable, so it cannot be switched at runtime. Returned
/// by <see cref="SetRenderSubsystemTool"/>. Distinguished from
/// <see cref="InvalidArgument"/> in that the caller's request is
/// well-formed; the host configuration forbids the change.
/// </summary>
/// <param name="Reason">Single-sentence description of why the switch was refused.</param>
[Description("Raised when the render subsystem is pinned by the S100_RENDER_SUBSYSTEM environment variable and cannot be switched at runtime.")]
internal sealed record RenderSubsystemPinned(
    [property: Description("Single-sentence description of why the switch was refused.")] string Reason)
    : ToolError("render_subsystem_pinned", $"The render subsystem cannot be switched: {Reason}.");
