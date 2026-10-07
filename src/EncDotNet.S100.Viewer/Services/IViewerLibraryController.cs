using System.ComponentModel;
using Avalonia.Threading;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Reads the Library for agents (MCP <c>list_library_sources</c>,
/// <c>query_library_items</c>, <c>describe_library_item</c>,
/// <c>list_known_sources</c>, #715). Item state, tags and details come from
/// the Library panel's own row view models, so they match what the user sees.
/// </summary>
internal interface IViewerLibraryController
{
    /// <summary>Lists the Library's collections and their sources.</summary>
    /// <param name="counts">True to count each source's items by state (reads every item).</param>
    /// <param name="ct">A cancellation token.</param>
    Task<IReadOnlyList<LibraryCollectionInfo>> ListSourcesAsync(bool counts, CancellationToken ct = default);

    /// <summary>Finds Library items, paged.</summary>
    /// <param name="query">The filters and page.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The page, or null when <see cref="LibraryItemPageQuery.SourceId"/> matches no collection or source.</returns>
    Task<LibraryItemPage?> QueryItemsAsync(LibraryItemPageQuery query, CancellationToken ct = default);

    /// <summary>Describes one item as the details pane does.</summary>
    /// <param name="itemId">The item id from <see cref="QueryItemsAsync"/>.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The item, or null when no item has that id.</returns>
    Task<LibraryItemDetail?> DescribeItemAsync(string itemId, CancellationToken ct = default);

    /// <summary>Lists the Online Catalogue directory: the curated sources and the user's own.</summary>
    /// <param name="ct">A cancellation token.</param>
    Task<IReadOnlyList<KnownSourceInfo>> ListKnownSourcesAsync(CancellationToken ct = default);
}

/// <summary>Filters for <see cref="IViewerLibraryController.QueryItemsAsync"/>.</summary>
/// <param name="SourceId">A collection or source id, or null for the whole Library.</param>
/// <param name="States">State names to keep (see <see cref="LibraryItemInfo.State"/>), or null for all.</param>
/// <param name="Spec">A product specification to keep, e.g. "S-111", or null.</param>
/// <param name="Text">Text matched against name, title and properties as the panel's filter box does, or null.</param>
/// <param name="Bounds">Keep items whose bounds intersect this box, or null.</param>
/// <param name="Point">Keep items whose coverage contains this point (as tapping the map does), or null.</param>
/// <param name="Page">The 0-based page.</param>
/// <param name="PageSize">Items per page.</param>
internal sealed record LibraryItemPageQuery(
    Guid? SourceId,
    IReadOnlySet<string>? States,
    string? Spec,
    string? Text,
    GeoBounds? Bounds,
    GeoPosition? Point,
    int Page,
    int PageSize)
{
    /// <summary>Keep only items whose data covers this time (#711).</summary>
    public DateTime? ValidAt { get; init; }

    /// <summary>Keep only items whose data covers the Timeline's view time (#711, the Library's Valid at view time toggle).</summary>
    public bool ValidAtViewTime { get; init; }
}

/// <summary>A Library collection: a top-level node of the Library tree.</summary>
[Description("A Library collection (a top-level node of the Library tree) and its sources.")]
internal sealed record LibraryCollectionInfo(
    [property: Description("Collection id; pass it as sourceId to query_library_items.")] Guid Id,
    [property: Description("Collection name as shown in the Library.")] string Name,
    [property: Description("Kind tag as shown: DIR, ZIP, WEB, AWS, LIST, FEED, SECOM, JSON (collection manifest) or S-128.")] string Kind,
    [property: Description("Number of items indexed across its sources.")] int ItemCount,
    [property: Description("The status line shown under the node (indexing, downloads, updates, forecast runs, problems), or null when all is normal.")] string? StatusLine,
    [property: Description("True for the session collection (S-128 catalogues opened this session, not kept).")] bool IsSession,
    [property: Description("The collection's sources.")] IReadOnlyList<LibrarySourceInfo> Sources);

/// <summary>A source within a Library collection.</summary>
[Description("A source of a Library collection: a folder, exchange set, catalogue or feed.")]
internal sealed record LibrarySourceInfo(
    [property: Description("Source id; pass it as sourceId to query_library_items.")] Guid Id,
    [property: Description("Source name as shown.")] string Name,
    [property: Description("Kind tag as shown: DIR, ZIP, WEB, AWS, LIST, FEED, JSON (collection manifest) or S-128.")] string Kind,
    [property: Description("'pending', 'indexing', 'ready' or 'failed'.")] string IndexState,
    [property: Description("Why indexing failed, or null.")] string? Error,
    [property: Description("Number of items in its index.")] int ItemCount,
    [property: Description("When its index was built (UTC); its age shows how stale an online catalogue's cached copy is. Null before the first index.")] DateTimeOffset? IndexedAt,
    [property: Description("The status line shown under the node, or null when all is normal.")] string? StatusLine,
    [property: Description("Online catalogue or feed URL (a shared feed's access token is masked), or null for local sources.")] string? Url,
    [property: Description("Item counts by state (see query_library_items), when requested.")] IReadOnlyDictionary<string, int>? Counts,
    [property: Description("For a synced SECOM source: its last sync (when, objects local and listed, downloaded, pruned, failed, and the bytes needed when too large to sync), or null.")] EncDotNet.S100.Collections.Library.LibrarySyncStatus? Sync = null);

/// <summary>A page of Library items.</summary>
[Description("A page of Library items.")]
internal sealed record LibraryItemPage(
    [property: Description("Items matching every filter.")] int Total,
    [property: Description("The 0-based page returned.")] int Page,
    [property: Description("Items per page.")] int PageSize,
    [property: Description("True when later pages exist.")] bool HasMore,
    [property: Description("The items on this page.")] IReadOnlyList<LibraryItemInfo> Items);

/// <summary>One Library item, as its row shows it.</summary>
[Description("One Library item (a dataset the Library knows about), as its row shows it.")]
internal sealed record LibraryItemInfo(
    [property: Description("Item id ('<sourceId>:<key>'); pass it to describe_library_item.")] string Id,
    [property: Description("Id of the source listing the item.")] Guid SourceId,
    [property: Description("Dataset name, e.g. a cell or file name.")] string Name,
    [property: Description("Title, or null.")] string? Title,
    [property: Description("Product specification, e.g. 'S-101', 'S-111'.")] string Spec,
    [property: Description("Where the data is: 'listed' (no location), 'online' (not downloaded), 'local' (on disk), 'missing' (local file gone), 'on_pan' (registered, loads as you pan), 'loaded', 'update' (a newer edition or run is online), or 'expired' (a forecast run that has ended).")] string State,
    [property: Description("Tags shown after the name, e.g. 'Ed 46 available', 'Loaded', 'Queued'.")] IReadOnlyList<string> Tags,
    [property: Description("Edition number, or null.")] int? Edition,
    [property: Description("Update number, or null.")] int? Update,
    [property: Description("Issue date (yyyy-MM-dd), or null.")] DateOnly? IssueDate,
    [property: Description("Download size in bytes, or null when unknown or local.")] long? SizeBytes,
    [property: Description("Bounding box in WGS-84 decimal degrees, or null.")] LibraryBounds? Bounds,
    [property: Description("Full local path when on disk, or null.")] string? LocalPath,
    [property: Description("Forecast model id (e.g. 'cbofs'), or null.")] string? Model,
    [property: Description("Forecast run time (UTC), or null.")] DateTimeOffset? Run,
    [property: Description("End of the forecast's valid window (UTC), or null.")] DateTimeOffset? ValidUntil,
    [property: Description("True when the provider marks it not for navigation.")] bool NotForNavigation);

/// <summary>A WGS-84 bounding box.</summary>
[Description("A WGS-84 bounding box in decimal degrees.")]
internal sealed record LibraryBounds(
    [property: Description("Southern latitude.")] double South,
    [property: Description("Western longitude.")] double West,
    [property: Description("Northern latitude.")] double North,
    [property: Description("Eastern longitude.")] double East);

/// <summary>One item with its details pane.</summary>
[Description("One Library item with the groups of fields its details pane shows.")]
internal sealed record LibraryItemDetail(
    [property: Description("The item, as query_library_items reports it.")] LibraryItemInfo Item,
    [property: Description("The details pane's groups (Forecast, Product, Coverage, Source, …).")] IReadOnlyList<LibraryDetailGroupInfo> Details);

/// <summary>A details-pane group.</summary>
[Description("A group of fields in the Library details pane.")]
internal sealed record LibraryDetailGroupInfo(
    [property: Description("Group title.")] string Title,
    [property: Description("Its fields.")] IReadOnlyList<LibraryDetailFieldInfo> Fields);

/// <summary>A details-pane field.</summary>
[Description("A labelled value in the Library details pane.")]
internal sealed record LibraryDetailFieldInfo(
    [property: Description("Field label.")] string Label,
    [property: Description("Field value as shown.")] string Value);

/// <summary>An Online Catalogue directory entry.</summary>
[Description("An entry in the Online Catalogue directory: a known online source that can be added to the Library.")]
internal sealed record KnownSourceInfo(
    [property: Description("Stable id, e.g. 'noaa-enc', 'noaa-s111'.")] string Id,
    [property: Description("Display name.")] string Name,
    [property: Description("Who publishes it.")] string Provider,
    [property: Description("Where it applies, broadest first.")] IReadOnlyList<string> Region,
    [property: Description("Catalogue format, e.g. 'NoaaEnc', 'S100ExchangeCatalogue', 'S100ForecastModels'.")] string Format,
    [property: Description("Catalogue URL (a shared feed's access token is masked).")] string Url,
    [property: Description("Provider's page for the data, or null.")] string? Homepage,
    [property: Description("Coverage it publishes: 'None', 'BoundingBoxes' or 'Polygons'.")] string Coverage,
    [property: Description("True when it lists editions and updates, so out-of-date downloads are detected.")] bool Editions,
    [property: Description("True when it lists download sizes.")] bool Sizes,
    [property: Description("The one product it publishes, e.g. 'S-102', or null.")] string? Product,
    [property: Description("Short description, or null.")] string? Note,
    [property: Description("True when all its data is marked not for navigation.")] bool NotForNavigation,
    [property: Description("True for a pilot service that may cover little and lapse.")] bool Pilot,
    [property: Description("True when the user added it (Custom), false for the curated list.")] bool UserAdded,
    [property: Description("For a forecast feed, its models; otherwise empty.")] IReadOnlyList<KnownForecastModelInfo> Models);

/// <summary>A forecast model of a known source.</summary>
[Description("A forecast model offered by a known forecast feed.")]
internal sealed record KnownForecastModelInfo(
    [property: Description("Model id, e.g. 'cbofs'.")] string Id,
    [property: Description("Model name, e.g. 'Chesapeake Bay'.")] string Name,
    [property: Description("Hours between runs.")] int CadenceHours,
    [property: Description("Forecast length of a run, in hours.")] int HorizonHours);

/// <summary>Default <see cref="IViewerLibraryController"/> over <see cref="LibraryPanelViewModel"/>.</summary>
internal sealed class ViewerLibraryController : IViewerLibraryController
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
            node.SyncStatus);
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
