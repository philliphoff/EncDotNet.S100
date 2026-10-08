using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The viewer's <see cref="ILibraryReader"/>: item state, tags and details come
/// from the Library panel's own row view models, so they match what the user sees.
/// </summary>
internal sealed class ViewerLibraryController : ILibraryReader
{
    private readonly LibraryPanelViewModel _panel;
    private readonly Func<IReadOnlyList<KnownCatalogueSource>> _userCatalogues;
    private readonly Func<Action, Task> _dispatch;

    public ViewerLibraryController(
        LibraryPanelViewModel panel,
        Func<IReadOnlyList<KnownCatalogueSource>>? userCatalogues = null,
        Func<Action, Task>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(panel);
        _panel = panel;
        _userCatalogues = userCatalogues ?? (() => []);
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.InvokeAsync(action).GetTask());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibraryCollectionInfo>> ListSourcesAsync(bool counts, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<LibraryCollectionInfo> result = [];
        await _dispatch(() => result = _panel.Nodes.Select(node => new LibraryCollectionInfo(
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
            if (_panel.CurrentViewTime is not { } viewTime)
                return LibraryQuery.ScopeOf(_panel.Collections, query.SourceId) is null ? null : [];
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
        return LibraryQuery.Find(_panel.Collections, core, _panel.CreateItem, row => row.State);
    }

    /// <inheritdoc />
    public async Task<LibraryItemDetail?> DescribeItemAsync(string itemId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ct.ThrowIfCancellationRequested();
        LibraryItemDetail? detail = null;
        await _dispatch(() =>
        {
            if (LibraryQuery.FindById(_panel.Collections, itemId) is not var (item, source))
                return;
            var row = _panel.CreateItem(item, source);
            detail = new LibraryItemDetail(
                Info(row),
                [.. row.Details.Select(group => new LibraryDetailGroupInfo(
                    group.Title,
                    [.. group.Fields.Select(field => new LibraryDetailFieldInfo(field.Label, field.Value))]))]);
        }).ConfigureAwait(false);
        return detail;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KnownSourceInfo>> ListKnownSourcesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<KnownSourceInfo> list =
        [
            .. KnownCatalogueSources.All.Select(source => Known(source, userAdded: false)),
            .. _userCatalogues().Select(source => Known(source, userAdded: true)),
        ];
        return Task.FromResult(list);
    }

    private LibrarySourceInfo Source(LibraryNodeViewModel node, bool counts)
    {
        var source = node.Source!;
        IReadOnlyDictionary<string, int>? tally = null;
        if (counts)
        {
            tally = (source.Index?.Items ?? [])
                .Select(item => LibraryAvailabilityNames.Of(_panel.CreateItem(item, source).Availability))
                .GroupBy(state => state, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        }
        var url = node.SourceUrl is { } uri
            ? source.Definition is S100FeedSource ? LibraryNodeViewModel.MaskToken(uri) : uri.AbsoluteUri
            : null;
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
            source.Definition.ShowOnMap);
    }

    internal static LibraryItemInfo Info(LibraryItemViewModel row)
    {
        var item = row.Item;
        var run = S100ForecastFeedIndexer.RunOf(item);
        var horizon = ForecastRuns.Horizon(item);
        var effective = row.EffectiveItem;
        return new LibraryItemInfo(
            row.State.Id,
            row.Source.Id,
            item.Name,
            item.Title,
            item.ProductSpec,
            LibraryAvailabilityNames.Of(row.Availability),
            [.. row.Tags.Select(tag => tag.Text)],
            item.Edition,
            item.Update,
            item.IssueDate,
            item.Location is RemoteItemLocation remote ? remote.SizeBytes : null,
            item.Bounds is { } b ? new LibraryBounds(b.South, b.West, b.North, b.East) : null,
            effective.Location is LocalItemLocation local ? LibraryAvailabilityResolver.ResolvePath(local) : null,
            ForecastRuns.ModelOf(item),
            run,
            run is { } start && horizon is { } length ? start + length : null,
            row.NotForNavigation);
    }

    private static KnownSourceInfo Known(KnownCatalogueSource source, bool userAdded) => new(
        source.Id,
        source.Name,
        source.Provider,
        source.Region,
        source.Format.ToString(),
        source.Format == KnownCatalogueFormat.S100Feed ? LibraryNodeViewModel.MaskToken(source.CatalogUri) : source.CatalogUri.AbsoluteUri,
        source.Homepage?.AbsoluteUri,
        source.Coverage.ToString(),
        source.Editions,
        source.Sizes,
        source.Product,
        source.Note,
        source.NotForNavigation,
        source.Pilot,
        userAdded,
        [.. source.Models.Select(model => new KnownForecastModelInfo(model.Id, model.Name, model.CadenceHours, model.HorizonHours))]);
}
