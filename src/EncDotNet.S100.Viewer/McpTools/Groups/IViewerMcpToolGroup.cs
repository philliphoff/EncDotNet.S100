using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>
/// A set of the viewer's own MCP tools over the services they drive.
/// <see cref="Services.McpServerHost"/> asks every group for fresh tool
/// instances each time it starts the server.
/// </summary>
internal interface IViewerMcpToolGroup
{
    /// <summary>Creates this group's tools.</summary>
    IEnumerable<McpServerTool> CreateTools();
}
