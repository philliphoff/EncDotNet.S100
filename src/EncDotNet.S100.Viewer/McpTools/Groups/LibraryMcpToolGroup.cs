using EncDotNet.S100.Mcp.Library;
using EncDotNet.S100.Mcp.Tools.Library;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools.Groups;

/// <summary>The Library tools, including the SECOM registry, server trust and identities.</summary>
internal sealed class LibraryMcpToolGroup : IViewerMcpToolGroup
{
    private readonly ILibraryReader _library;
    private readonly ILibraryEditor _libraryEditor;
    private readonly EncDotNet.S100.Collections.Secom.SecomRegistry _secomRegistry;
    private readonly EncDotNet.S100.Collections.Secom.SecomServerTrust _secomServerTrust;
    private readonly ISecomIdentityReferences _secomIdentities;

    public LibraryMcpToolGroup(
        ILibraryReader library,
        ILibraryEditor libraryEditor,
        EncDotNet.S100.Collections.Secom.SecomRegistry secomRegistry,
        EncDotNet.S100.Collections.Secom.SecomServerTrust secomServerTrust,
        ISecomIdentityReferences secomIdentities)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(libraryEditor);
        ArgumentNullException.ThrowIfNull(secomRegistry);
        ArgumentNullException.ThrowIfNull(secomServerTrust);
        ArgumentNullException.ThrowIfNull(secomIdentities);

        _library = library;
        _libraryEditor = libraryEditor;
        _secomRegistry = secomRegistry;
        _secomServerTrust = secomServerTrust;
        _secomIdentities = secomIdentities;
    }

    public IEnumerable<McpServerTool> CreateTools()
    {
        return LibraryMcpTools.Create(_library, _libraryEditor, _secomRegistry, _secomServerTrust, _secomIdentities);
    }
}
