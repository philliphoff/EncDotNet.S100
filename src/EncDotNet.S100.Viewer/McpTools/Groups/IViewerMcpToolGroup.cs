using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>
/// A set of the viewer's own MCP tools over the services they drive.
/// <see cref="Services.McpServerHost"/> asks every group for fresh tool
/// instances each time it starts the server.
/// </summary>
internal interface IViewerMcpToolGroup
{
    /// <summary>
    /// The MCP toolset (<see cref="EncDotNet.S100.Mcp.McpToolsets"/>) this
    /// group's tools belong to, unless a tool declares another itself.
    /// </summary>
    string Toolset { get; }

    /// <summary>Creates this group's tools, each declaring its toolset.</summary>
    IEnumerable<McpServerTool> CreateTools();
}
