using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The viewer's <see cref="ILibraryReader"/>: item state, tags and details come
/// from the Library's rows (<see cref="LibraryRows"/>), and the collections and
/// sources from the Library tree, so they match what the user sees.
/// </summary>
internal sealed class ViewerLibraryController : ILibraryReader
{
    private readonly LibraryTreeViewModel _tree;
    private readonly LibraryRows _rows;
    private readonly GlobalTimeService _viewTime;
    private readonly Func<IReadOnlyList<KnownCatalogueSource>> _userCatalogues;
    private readonly Func<Action, Task> _dispatch;

    /// <summary>Creates the reader.</summary>
    /// <param name="tree">The Library tree, whose collections, sources and status lines the tools report.</param>
    /// <param name="rows">The Library's rows.</param>
    /// <param name="viewTime">The Timeline's view time, for the <c>validAt</c> filter.</param>
    /// <param name="userCatalogues">The catalogues the user added to the known sources.</param>
    /// <param name="dispatch">Runs a read on the UI thread.</param>
    public ViewerLibraryController(
        LibraryTreeViewModel tree,
        LibraryRows rows,
        GlobalTimeService viewTime,
        Func<IReadOnlyList<KnownCatalogueSource>> userCatalogues,
        Func<Action, Task> dispatch)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(viewTime);
        ArgumentNullException.ThrowIfNull(userCatalogues);
        ArgumentNullException.ThrowIfNull(dispatch);
        _tree = tree;
        _rows = rows;
        _viewTime = viewTime;
        _userCatalogues = userCatalogues;
        _dispatch = dispatch;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryCollectionInfo>> ListSourcesAsync(bool counts, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<LibraryCollectionInfo> result = [];
        await _dispatch(() => result = _tree.Nodes.Select(node => new LibraryCollectionInfo(
            node.Collection.Id,
            node.Name,
            node.KindTag,
            node.Collection.ItemCount,
            node.StatusLine,
            node.Collection.IsSession,
            [.. node.Children.Where(child => child.Source is not null && !child.IsGroup).Select(child => Source(child, counts))]))
            .ToArray()).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task<LibraryItemPage?> QueryItemsAsync(LibraryItemPageQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        LibraryItemPage? page = null;
        await _dispatch(() =>
        {
            if (FindRows(query) is not { } matched)
                return;
            var items = matched
                .Skip(query.Page * query.PageSize)
                .Take(query.PageSize)
                .Select(Info)
                .ToArray();
            page = new LibraryItemPage(matched.Count, query.Page, query.PageSize, (query.Page + 1) * query.PageSize < matched.Count, items);
        }).ConfigureAwait(false);
        return page;
    }

    /// <summary>
    /// Every row matching <paramref name="query"/>'s filters (ignoring its
    /// page), or <see langword="null"/> when its source id matches nothing.
    /// Call on the UI thread.
    /// </summary>
    internal List<LibraryItemViewModel>? FindRows(LibraryItemPageQuery query)
    {
        DateTime? validAt = query.ValidAt;
        if (query.ValidAtViewTime)
        {
            // No view time (no time-aware data loaded): nothing is valid at it.
            if ((_viewTime.IsActive ? _viewTime.CurrentTime : (DateTime?)null) is not { } viewTime)
                return LibraryQuery.ScopeOf(_rows.Collections, query.SourceId) is null ? null : [];
            validAt = viewTime;
        }

        IReadOnlySet<LibraryAvailability>? states = query.States?
            .Select(name => LibraryAvailabilityNames.TryParse(name, out var state)
                ? state
                : throw new ArgumentException($"Unknown Library state '{name}'.", nameof(query)))
            .ToHashSet();
        var core = new Collections.Library.LibraryItemQuery(query.SourceId, states, query.Spec, query.Text, query.Bounds, query.Point)
        {
            ValidAt = validAt,
        };
        return LibraryQuery.Find(_rows.Collections, core, _rows.Create, row => row.State);
    }

    /// <inheritdoc />
    public async Task<LibraryItemDetail?> DescribeItemAsync(string itemId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ct.ThrowIfCancellationRequested();
        LibraryItemDetail? detail = null;
        await _dispatch(() =>
        {
            if (_rows.Find(itemId) is not { } row)
                return;
            detail = LibraryToolResults.Detail(row.State, row.Tags.Select(tag => tag.Text), row.Details);
        }).ConfigureAwait(false);
        return detail;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KnownSourceInfo>> ListKnownSourcesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<KnownSourceInfo> list =
        [
            .. KnownCatalogueSources.All.Select(source => LibraryToolResults.KnownSource(source, userAdded: false)),
            .. _userCatalogues().Select(source => LibraryToolResults.KnownSource(source, userAdded: true)),
        ];
        return Task.FromResult(list);
    }

    private LibrarySourceInfo Source(LibraryNodeViewModel node, bool counts)
    {
        var source = node.Source!;
        IReadOnlyDictionary<string, int>? tally = null;
        if (counts)
        {
            tally = LibraryToolResults.Counts((source.Index?.Items ?? []).Select(item => _rows.Create(item, source).Availability));
        }
        var url = LibraryToolResults.SourceUrl(source.Definition);
        return new LibrarySourceInfo(
            source.Id,
            node.Name,
            node.KindTag,
            source.State.ToString().ToLowerInvariant(),
            source.Error,
            source.Index?.Items.Count ?? 0,
            source.Index?.IndexedAt,
            node.StatusLine,
            url,
            tally,
            node.SyncStatus,
            source.Definition.ShowOnMap)
        {
            Identity = (source.Definition as EncDotNet.S100.Collections.SecomSource)?.Identity,
        };
    }

    internal static LibraryItemInfo Info(LibraryItemViewModel row) =>
        LibraryToolResults.Item(row.State, row.Tags.Select(tag => tag.Text));
}
