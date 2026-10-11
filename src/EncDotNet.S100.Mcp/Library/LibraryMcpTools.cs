using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Mcp.Tools.Library;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp.Library;

/// <summary>The Library tool set a host registers (#792): the same tools, in the same order, in every host.</summary>
public static class LibraryMcpTools
{
    /// <summary>Creates the Library tools a host supports.</summary>
    /// <param name="reader">Reads the Library; the read tools are left out when null.</param>
    /// <param name="editor">Changes the Library; the edit tools are left out when null.</param>
    /// <param name="secomRegistry">Finds SECOM services; <c>list_secom_services</c> is left out when null (or without a reader).</param>
    /// <param name="secomTrust">The SECOM trust whose identity is set; <c>set_secom_identity</c> is left out when null (or without an editor).</param>
    /// <param name="secomIdentities">The host's stored SECOM identities, for reference ids; none when null.</param>
    public static IReadOnlyList<McpServerTool> Create(
        ILibraryReader? reader,
        ILibraryEditor? editor,
        SecomRegistry? secomRegistry = null,
        SecomServerTrust? secomTrust = null,
        ISecomIdentityReferences? secomIdentities = null)
    {
        var tools = new List<McpServerTool>();
        if (reader is not null)
        {
            tools.Add(LibraryMcpAdapters.Create(new ListLibrarySourcesTool(reader)));
            tools.Add(LibraryMcpAdapters.Create(new QueryLibraryItemsTool(reader)));
            tools.Add(LibraryMcpAdapters.Create(new DescribeLibraryItemTool(reader)));
            tools.Add(LibraryMcpAdapters.Create(new ListKnownSourcesTool(reader)));
            if (secomRegistry is not null)
                tools.Add(LibraryMcpAdapters.Create(new ListSecomServicesTool(secomRegistry)));
        }

        if (editor is not null)
        {
            tools.Add(LibraryEditMcpAdapters.Create(new AddLibrarySourceTool(editor)));
            tools.Add(LibraryEditMcpAdapters.Create(new RefreshLibrarySourceTool(editor)));
            tools.Add(LibraryEditMcpAdapters.Create(new LibraryActionTool(editor)));
            tools.Add(LibraryEditMcpAdapters.Create(new RemoveLibrarySourceTool(editor)));
            tools.Add(LibraryEditMcpAdapters.Create(new SetLibrarySourceOptionsTool(editor)));
            tools.Add(LibraryEditMcpAdapters.Create(new AwaitLibraryIdleTool(editor)));
            if (secomTrust is not null)
                tools.Add(LibraryMcpAdapters.Create(new SetSecomIdentityTool(secomTrust, references: secomIdentities)));
        }

        return tools;
    }
}
