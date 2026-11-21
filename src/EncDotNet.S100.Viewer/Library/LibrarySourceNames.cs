using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// A Library source's name as its tree node shows it, for the Datasets row its
/// datasets open under (#809), the MCP Library tools and the source pickers.
/// </summary>
internal sealed class LibrarySourceNames
{
    private readonly CollectionLibrary _library;

    /// <summary>Names the sources of <paramref name="library"/>.</summary>
    /// <param name="library">The Library the sources are in.</param>
    public LibrarySourceNames(CollectionLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
    }

    /// <summary>
    /// The name of the source <paramref name="sourceId"/>: its tree node's
    /// name, or the start of its id when the Library no longer has it.
    /// </summary>
    /// <param name="sourceId">The source.</param>
    public string Of(Guid sourceId)
    {
        foreach (var collection in _library.Collections)
        {
            foreach (var source in collection.Sources)
            {
                if (source.Id == sourceId)
                    return LibraryNodeText.SourceName(source, collection);
            }
        }

        return sourceId.ToString("N")[..8];
    }

    /// <summary>The label that opens <paramref name="source"/>'s datasets under one Datasets row.</summary>
    /// <param name="source">The source.</param>
    public LibrarySourceLabel LabelOf(LibrarySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new LibrarySourceLabel(source.Id, Of(source.Id));
    }
}
