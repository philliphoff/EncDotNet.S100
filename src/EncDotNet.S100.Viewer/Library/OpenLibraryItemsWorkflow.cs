using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Opens Library items in the viewer (#1394): now, or as the map pans to
/// them, each source's datasets under one Datasets row (#809). The Library
/// panel's Load commands, the Timeline and MCP <c>library_action</c> all open
/// items through it.
/// </summary>
/// <remarks>Runs on the UI thread.</remarks>
internal sealed class OpenLibraryItemsWorkflow
{
    private readonly ILibraryLoader _loader;
    private readonly LibrarySourceNames _names;

    /// <summary>Creates the workflow.</summary>
    /// <param name="loader">Opens items in the viewer session.</param>
    /// <param name="names">Names each item's source for its Datasets row.</param>
    public OpenLibraryItemsWorkflow(ILibraryLoader loader, LibrarySourceNames names)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(names);
        _loader = loader;
        _names = names;
    }

    /// <summary>
    /// Opens <paramref name="items"/>: now, or with <paramref name="defer"/> as
    /// the map pans to them.
    /// </summary>
    /// <param name="items">The items to open (a downloaded item's local copy), each with the source it was indexed from, or <see langword="null"/> when it has none (it opens under its own Datasets row).</param>
    /// <param name="defer">True to open the items as the map pans to them.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>What the load did.</returns>
    public Task<LibraryLoadResult> RunAsync(
        IReadOnlyList<(CollectionItem Item, LibrarySource? Source)> items, bool defer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var labels = new Dictionary<Guid, LibrarySourceLabel>();
        var sourceOf = new Dictionary<CollectionItem, LibrarySourceLabel>(ReferenceEqualityComparer.Instance);
        var open = new CollectionItem[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var (item, source) = items[i];
            open[i] = item;
            if (source is null)
                continue;
            if (!labels.TryGetValue(source.Id, out var label))
                labels[source.Id] = label = _names.LabelOf(source);
            sourceOf[item] = label;
        }

        return _loader.LoadAsync(open, defer, item => sourceOf.GetValueOrDefault(item), cancellationToken);
    }

    /// <summary>Opens the rows' items as the Library shows them: a downloaded item opens its local copy.</summary>
    /// <param name="items">The items' states.</param>
    /// <param name="defer">True to open the items as the map pans to them.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>What the load did.</returns>
    public Task<LibraryLoadResult> RunAsync(
        IReadOnlyList<LibraryItemState> items, bool defer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        return RunAsync([.. items.Select(state => (state.EffectiveItem, (LibrarySource?)state.Source))], defer, cancellationToken);
    }
}
