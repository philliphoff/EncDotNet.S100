using System.ComponentModel;
using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;

namespace EncDotNet.S100.Mcp.Tools.Library;

// The host-neutral seams behind the Library MCP tools (#715, shared in #792),
// and the requests and results they exchange. The viewer implements them over
// its Library panel; s100 mcp serve over the headless Library core.

/// <summary>
/// Reads the Library for agents (MCP <c>list_library_sources</c>,
/// <c>query_library_items</c>, <c>describe_library_item</c>,
/// <c>list_known_sources</c>, #715). Item state, tags and details come from
/// the host; the viewer reports them as its Library panel shows them.
/// </summary>
public interface ILibraryReader
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

/// <summary>Filters for <see cref="ILibraryReader.QueryItemsAsync"/>.</summary>
/// <param name="SourceId">A collection or source id, or null for the whole Library.</param>
/// <param name="States">State names to keep (see <see cref="LibraryItemInfo.State"/>), or null for all.</param>
/// <param name="Spec">A product specification to keep, e.g. "S-111", or null.</param>
/// <param name="Text">Text matched against name, title and properties as the panel's filter box does, or null.</param>
/// <param name="Bounds">Keep items whose bounds intersect this box, or null.</param>
/// <param name="Point">Keep items whose coverage contains this point (as tapping the map does), or null.</param>
/// <param name="Page">The 0-based page.</param>
/// <param name="PageSize">Items per page.</param>
public sealed record LibraryItemPageQuery(
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
public sealed record LibraryCollectionInfo(
    [property: Description("Collection id; pass it as sourceId to query_library_items.")] Guid Id,
    [property: Description("Collection name as shown in the Library.")] string Name,
    [property: Description("Kind tag as shown: DIR, ZIP, WEB, AWS, LIST, FEED, SECOM, JSON (collection manifest) or S-128.")] string Kind,
    [property: Description("Number of items indexed across its sources.")] int ItemCount,
    [property: Description("The status line shown under the node (indexing, downloads, updates, forecast runs, problems), or null when all is normal.")] string? StatusLine,
    [property: Description("True for the session collection (S-128 catalogues opened this session, not kept).")] bool IsSession,
    [property: Description("The collection's sources.")] IReadOnlyList<LibrarySourceInfo> Sources);

/// <summary>A source within a Library collection.</summary>
[Description("A source of a Library collection: a folder, exchange set, catalogue or feed.")]
public sealed record LibrarySourceInfo(
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
    [property: Description("For a synced (kept downloaded) source: its last sync (when, objects local and listed, downloaded, pruned, failed, and the bytes needed when too large to sync), or null.")] EncDotNet.S100.Collections.Library.LibrarySyncStatus? Sync = null,
    [property: Description("True when the source's local datasets are kept on the map (loading as you pan).")] bool ShowOnMap = false);

/// <summary>A page of Library items.</summary>
[Description("A page of Library items.")]
public sealed record LibraryItemPage(
    [property: Description("Items matching every filter.")] int Total,
    [property: Description("The 0-based page returned.")] int Page,
    [property: Description("Items per page.")] int PageSize,
    [property: Description("True when later pages exist.")] bool HasMore,
    [property: Description("The items on this page.")] IReadOnlyList<LibraryItemInfo> Items);

/// <summary>One Library item, as its row shows it.</summary>
[Description("One Library item (a dataset the Library knows about), as its row shows it.")]
public sealed record LibraryItemInfo(
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
public sealed record LibraryBounds(
    [property: Description("Southern latitude.")] double South,
    [property: Description("Western longitude.")] double West,
    [property: Description("Northern latitude.")] double North,
    [property: Description("Eastern longitude.")] double East);

/// <summary>One item with its details pane.</summary>
[Description("One Library item with the groups of fields its details pane shows.")]
public sealed record LibraryItemDetail(
    [property: Description("The item, as query_library_items reports it.")] LibraryItemInfo Item,
    [property: Description("The details pane's groups (Forecast, Product, Coverage, Source, …).")] IReadOnlyList<LibraryDetailGroupInfo> Details);

/// <summary>A details-pane group.</summary>
[Description("A group of fields in the Library details pane.")]
public sealed record LibraryDetailGroupInfo(
    [property: Description("Group title.")] string Title,
    [property: Description("Its fields.")] IReadOnlyList<LibraryDetailFieldInfo> Fields);

/// <summary>A details-pane field.</summary>
[Description("A labelled value in the Library details pane.")]
public sealed record LibraryDetailFieldInfo(
    [property: Description("Field label.")] string Label,
    [property: Description("Field value as shown.")] string Value);

/// <summary>An Online Catalogue directory entry.</summary>
[Description("An entry in the Online Catalogue directory: a known online source that can be added to the Library.")]
public sealed record KnownSourceInfo(
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
public sealed record KnownForecastModelInfo(
    [property: Description("Model id, e.g. 'cbofs'.")] string Id,
    [property: Description("Model name, e.g. 'Chesapeake Bay'.")] string Name,
    [property: Description("Hours between runs.")] int CadenceHours,
    [property: Description("Forecast length of a run, in hours.")] int HorizonHours);

/// <summary>
/// Changes the Library for agents (MCP <c>add_library_source</c>,
/// <c>refresh_library_source</c>, <c>library_action</c>,
/// <c>remove_library_source</c>, <c>await_library_idle</c>, #715). The viewer
/// goes through the code paths its UI uses; a headless host through the
/// Library core (#792).
/// </summary>
public interface ILibraryEditor
{
    /// <summary>Previews or adds a source, as the Add-to-Library dialog does.</summary>
    Task<LibraryEditOutcome<AddSourceResult>> AddSourceAsync(AddSourceRequest request, CancellationToken ct = default);

    /// <summary>Re-indexes a collection, a source, or everything, and reports what changed.</summary>
    Task<LibraryEditOutcome<RefreshResult>> RefreshAsync(Guid? id, TimeSpan wait, CancellationToken ct = default);

    /// <summary>Loads, downloads, updates or cancels items, or previews doing so.</summary>
    Task<LibraryEditOutcome<LibraryActionResult>> ActAsync(LibraryActionRequest request, CancellationToken ct = default);

    /// <summary>Removes a collection or source from the Library.</summary>
    Task<LibraryEditOutcome<RemoveSourceResult>> RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Waits until no source is indexing, no download is running, and every
    /// dataset a Library action opens has opened.
    /// </summary>
    Task<LibraryIdleResult> AwaitIdleAsync(TimeSpan timeout, CancellationToken ct = default);

    /// <summary>Cancels every running download, as the bulk bar's Cancel does.</summary>
    Task CancelAllDownloadsAsync(CancellationToken ct = default);
}

/// <summary>A result, or the error explaining why there is none.</summary>
public readonly record struct LibraryEditOutcome<T>(T? Value, ToolError? Error)
{
    public static LibraryEditOutcome<T> Ok(T value) => new(value, null);

    public static LibraryEditOutcome<T> Fail(ToolError error) => new(default, error);
}

/// <summary>What <see cref="ILibraryEditor.AddSourceAsync"/> adds.</summary>
/// <param name="KnownSourceId">A <c>list_known_sources</c> id.</param>
/// <param name="Path">A local folder, exchange set (folder, ZIP or catalogue), collection manifest or S-128 catalogue.</param>
/// <param name="Url">An online catalogue or feed URL, recognised by its format, or a SECOM service endpoint.</param>
/// <param name="Kind">For a path: folder, exchange_set, manifest or s128; inferred when null.</param>
/// <param name="Choices">Choice values or labels to include (states, districts, rivers, models, areas, groups, products, charts).</param>
/// <param name="IncludeAll">True to include everything the catalogue lists; defaults to true without choices.</param>
/// <param name="CollectionId">An existing collection to add to; a new one when null.</param>
/// <param name="CollectionName">The new collection's name; the dialog's default when null.</param>
/// <param name="Shape">For a forecast feed: tiles or regional.</param>
/// <param name="Resolution">For an S-100 catalogue with several resolutions: its value or label.</param>
/// <param name="Preview">True to only load the catalogue and report the choices.</param>
/// <param name="Sync">For an online source: keep its items downloaded and current (#807, #809); the dialog's default when null (on for a small SECOM service, otherwise off).</param>
/// <param name="InMapView">For a SECOM service: read only the objects in the current map view.</param>
/// <param name="ShowOnMap">Keep the source's local datasets on the map, loading as you pan (#809); the kind's default when null.</param>
public sealed record AddSourceRequest(
    [property: Description("A list_known_sources id.")] string? KnownSourceId,
    [property: Description("A local folder, exchange set, collection manifest or S-128 catalogue.")] string? Path,
    [property: Description("An online catalogue or feed URL, or a SECOM service endpoint.")] string? Url,
    [property: Description("For a path: folder, exchange_set, manifest or s128; inferred when null.")] string? Kind,
    [property: Description("Choice values or labels to include, from a preview.")] IReadOnlyList<string>? Choices,
    [property: Description("True to include everything the catalogue lists.")] bool? IncludeAll,
    [property: Description("An existing collection to add to; a new one when null.")] Guid? CollectionId,
    [property: Description("The new collection's name.")] string? CollectionName,
    [property: Description("For a forecast feed: tiles or regional.")] string? Shape,
    [property: Description("For an S-100 catalogue with several resolutions: one from the preview.")] string? Resolution,
    [property: Description("True to only load the catalogue and report its choices.")] bool Preview,
    [property: Description("For an online source: keep its items downloaded and current.")] bool? Sync = null,
    [property: Description("For a SECOM service: read only the objects in the current map view.")] bool InMapView = false,
    [property: Description("Keep the source's local datasets on the map, loading as you pan.")] bool? ShowOnMap = null);

/// <summary>What add_library_source found or added.</summary>
[Description("What add_library_source found (preview) or added.")]
public sealed record AddSourceResult(
    [property: Description("True when a source was added; false for a preview.")] bool Added,
    [property: Description("What is being added: Folder, ExchangeSet, S128Catalogue, LocalManifest, NoaaFeed, UsaceFeed, CommunityFeed, S100Feed, S100Catalogue, S100Forecast or Secom.")] string Kind,
    [property: Description("The dialog's title for it, e.g. the known source's name.")] string Title,
    [property: Description("What the catalogue says about itself (date, size), or null.")] string? CatalogueDetail,
    [property: Description("True when the catalogue is over a year old.")] bool CatalogueStale,
    [property: Description("A note shown in the dialog, e.g. that a forecast has ended with no newer run, or null.")] string? Note,
    [property: Description("The scope as the dialog summarises it.")] string Scope,
    [property: Description("Groups of choices (pass values or labels in 'choices'); empty when there is nothing to choose.")] IReadOnlyList<AddChoiceGroup> Choices,
    [property: Description("For a forecast feed, the download shapes (pass one as 'shape'); otherwise empty.")] IReadOnlyList<AddChoiceOption> Shapes,
    [property: Description("For an S-100 catalogue with several resolutions, the choices (pass one as 'resolution'); otherwise empty.")] IReadOnlyList<AddChoiceOption> Resolutions,
    [property: Description("Existing collections the source can be added to (pass an id as 'collectionId').")] IReadOnlyList<AddChoiceOption> Collections,
    [property: Description("The collection the source was added to, or null for a preview.")] Guid? CollectionId,
    [property: Description("The new source's id, or null for a preview.")] Guid? SourceId,
    [property: Description("For an online source: whether its items are kept downloaded and current on each refresh; null for local sources.")] bool? Sync = null,
    [property: Description("For a SECOM service: why syncing is or is not advised (its size), or null.")] string? SyncNote = null);

/// <summary>A group of add choices.</summary>
[Description("A group of choices in the Add-to-Library dialog, e.g. States or Forecast models.")]
public sealed record AddChoiceGroup(
    [property: Description("Group title.")] string Title,
    [property: Description("Its options.")] IReadOnlyList<AddChoiceOption> Options);

/// <summary>One add choice.</summary>
[Description("One choice in the Add-to-Library dialog.")]
public sealed record AddChoiceOption(
    [property: Description("Value to pass back.")] string Value,
    [property: Description("Label as shown (also accepted).")] string Label,
    [property: Description("Detail as shown, e.g. cell count and size, or null.")] string? Detail,
    [property: Description("True when ticked.")] bool Selected);

/// <summary>What refresh_library_source changed.</summary>
[Description("What a refresh changed.")]
public sealed record RefreshResult(
    [property: Description("True when the call waited for indexing to finish.")] bool Waited,
    [property: Description("True when indexing was still running when the wait ended.")] bool TimedOut,
    [property: Description("Items in scope after the refresh.")] int Total,
    [property: Description("Items that were not listed before.")] int Added,
    [property: Description("Items no longer listed.")] int Removed,
    [property: Description("Items whose state changed, counted by their new state (e.g. 'update', 'expired').")] IReadOnlyDictionary<string, int> Changed,
    [property: Description("Item counts by state after the refresh.")] IReadOnlyDictionary<string, int> Counts);

/// <summary>What library_action does to which items.</summary>
/// <param name="Action">load, load_as_you_pan, download, download_only, update or cancel.</param>
/// <param name="ItemIds">Item ids, or null to use <paramref name="Filter"/>.</param>
/// <param name="Filter">The items to act on, as query_library_items filters them, or null.</param>
/// <param name="DryRun">True to report what would happen without doing it.</param>
/// <param name="MaxBytes">Refuse a download larger than this.</param>
public sealed record LibraryActionRequest(
    [property: Description("load, load_as_you_pan, download, download_only, update or cancel.")] string Action,
    [property: Description("Item ids, or null to use the filter.")] IReadOnlyList<string>? ItemIds,
    [property: Description("The items to act on, as query_library_items filters them, or null.")] LibraryItemPageQuery? Filter,
    [property: Description("True to report what would happen without doing it.")] bool DryRun,
    [property: Description("Refuse a download larger than this.")] long? MaxBytes);

/// <summary>What library_action did or would do.</summary>
[Description("What library_action did, or would do on a dry run.")]
public sealed record LibraryActionResult(
    [property: Description("The action.")] string Action,
    [property: Description("True for a dry run: nothing was done.")] bool DryRun,
    [property: Description("Items the action applies to.")] int Eligible,
    [property: Description("Selected items it does not apply to, counted by their state.")] IReadOnlyDictionary<string, int> Skipped,
    [property: Description("Known download size in bytes (download, download_only, update).")] long Bytes,
    [property: Description("Eligible items whose download size is unknown.")] int UnknownSizes,
    [property: Description("Names of up to 50 eligible items.")] IReadOnlyList<string> Items,
    [property: Description("For load and load_as_you_pan, how many datasets were opened or registered; otherwise null.")] int? Opened,
    [property: Description("True when downloads were started; they continue in the background (await_library_idle waits for them).")] bool Started);

/// <summary>What remove_library_source removed.</summary>
[Description("What was removed from the Library.")]
public sealed record RemoveSourceResult(
    [property: Description("The id removed.")] Guid Id,
    [property: Description("Its name.")] string Name,
    [property: Description("True for a whole collection, false for one source.")] bool WasCollection,
    [property: Description("How many items it listed.")] int ItemCount);

/// <summary>The Library's background work.</summary>
[Description("Whether the Library is busy indexing, downloading or opening datasets.")]
public sealed record LibraryIdleResult(
    [property: Description("True when nothing is indexing, downloading or opening datasets.")] bool Idle,
    [property: Description("True when the wait ended before the Library was idle.")] bool TimedOut,
    [property: Description("How long the call waited, in milliseconds.")] long WaitedMs,
    [property: Description("True while a source is indexing.")] bool Indexing,
    [property: Description("Datasets a library_action download or load has yet to open (including any still downloading).")] int Loading,
    [property: Description("The running download batch, or null.")] LibraryDownloadInfo? Downloads);

/// <summary>A running download batch.</summary>
[Description("Progress of the running download batch.")]
public sealed record LibraryDownloadInfo(
    [property: Description("Items finished.")] int Completed,
    [property: Description("Items failed.")] int Failed,
    [property: Description("Items in the batch.")] int Total,
    [property: Description("Bytes downloaded.")] long BytesDone,
    [property: Description("Bytes in the batch, as far as known.")] long BytesTotal);

/// <summary>A library_action, add or remove could not be done as asked.</summary>
[Description("Raised when a Library change cannot be done as asked; the reason says why (e.g. the catalogue failed to load, nothing is selected, or a download exceeds maxBytes).")]
public sealed record LibraryChangeRejected(
    [property: Description("Why the change was not made.")] string Reason)
    : ToolError("library_change_rejected", $"The Library was not changed: {Reason}.");
