using System.ComponentModel;
using EncDotNet.S100.Mcp.Tools;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// The viewer's map control has not been initialised yet (or the
/// snapshot path failed). Returned by <see cref="RenderToImageTool"/>
/// when no PNG can be produced. Distinguished from
/// <see cref="InvalidArgument"/> in that the caller's request is
/// well-formed; the host environment is simply not ready.
/// </summary>
/// <param name="Reason">Single-sentence description of which aspect of the host is unavailable.</param>
[Description("Raised when the viewer's map control has not been initialised yet (or has been torn down) and a render snapshot cannot be produced.")]
internal sealed record MapNotReady(
    [property: Description("Single-sentence description of which aspect of the host is unavailable.")] string Reason)
    : ToolError("map_not_ready", $"The viewer's map is not ready: {Reason}.");
