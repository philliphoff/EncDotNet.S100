using System.Globalization;
using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// One library item as a host sees it now: where its data can be had
/// (<see cref="Availability"/>), the copy that would open
/// (<see cref="EffectiveItem"/>) and the time its data covers
/// (<see cref="ValidWindow"/>). The viewer's Library rows and the MCP Library
/// tools both read items through it, so they agree.
/// </summary>
/// <remarks>
/// Values are resolved on first access and then kept; create a new state when
/// the item is downloaded, opened or closed.
/// </remarks>
public sealed class LibraryItemState
{
    private readonly ILibraryLocalCopies? _copies;
    private readonly Func<CollectionItem, LibraryLoadState>? _loadState;
    private readonly TimeProvider _time;
    private CollectionItem? _effective;
    private LibraryAvailability? _availability;

    /// <summary>Creates the state of <paramref name="item"/> from <paramref name="source"/>.</summary>
    /// <param name="item">The indexed item.</param>
    /// <param name="source">The source it was indexed from.</param>
    /// <param name="copies">Downloaded copies of online items, or <see langword="null"/> when the host downloads nothing.</param>
    /// <param name="loadState">Whether an item is open in the host's session, or <see langword="null"/> when nothing is.</param>
    /// <param name="time">The clock that decides whether a downloaded forecast run has ended.</param>
    public LibraryItemState(
        CollectionItem item,
        LibrarySource source,
        ILibraryLocalCopies? copies = null,
        Func<CollectionItem, LibraryLoadState>? loadState = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        Item = item;
        Source = source;
        _copies = copies;
        _loadState = loadState;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The indexed item.</summary>
    public CollectionItem Item { get; }

    /// <summary>The source the item was indexed from.</summary>
    public LibrarySource Source { get; }

    /// <summary>The item's id across the library: <c>&lt;sourceId&gt;:&lt;key&gt;</c>.</summary>
    public string Id => FormatId(Source.Id, Item.Key);

    /// <summary>
    /// The item as it can be opened now: an online item that has been
    /// downloaded, with its downloaded (local) location; otherwise
    /// <see cref="Item"/>.
    /// </summary>
    public CollectionItem EffectiveItem => _effective ??= _copies?.Localize(Item) ?? Item;

    /// <summary>True for a dataset of one forecast run (#685).</summary>
    public bool IsForecast => ForecastRuns.IsForecast(Item);

    /// <summary>The run of the downloaded copy, if any.</summary>
    public DateTimeOffset? LocalRun => _copies?.LocalPublishedAtOf(Item);

    /// <summary>
    /// The time the item's data covers (#711), in UTC: a forecast run's valid
    /// window (the downloaded copy's run, else the catalogue's), or a dataset's
    /// own time coverage; <see langword="null"/> for none.
    /// </summary>
    public (DateTime Start, DateTime End)? ValidWindow =>
        IsForecast
            ? ForecastRuns.ShownWindow(Item, LocalRun) is { } window ? (window.Run.UtcDateTime, window.ValidTo.UtcDateTime) : null
            : TimeCoverage(Item);

    /// <summary>The catalogue's latest run and its valid window, for a forecast (UTC).</summary>
    public (DateTime Run, DateTime End)? CatalogueRun =>
        S100ForecastFeedIndexer.RunOf(Item) is { } run && S100ForecastFeedIndexer.ValidToOf(Item) is { } end
            ? (run.UtcDateTime, end.UtcDateTime)
            : null;

    /// <summary>Where the data can be had now.</summary>
    public LibraryAvailability Availability => _availability ??= ResolveAvailability();

    /// <summary>True when the item's downloaded copy is loaded now.</summary>
    public bool IsLoadedNow => _loadState?.Invoke(EffectiveItem) == LibraryLoadState.Loaded;

    /// <summary>True when the source flags the item not for navigation.</summary>
    public bool NotForNavigation =>
        Item.Properties.TryGetValue("notForNavigation", out var value)
        && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the item matches a free-text filter (name, title, spec, or a
    /// property value such as a state code), case-insensitively.
    /// </summary>
    public bool Matches(string filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (Item.Title?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
            || Item.ProductSpec.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || Item.Properties.Values.Any(v => v.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A dataset's own time coverage, as a local index records it (<c>timeStart</c>/<c>timeEnd</c>).</summary>
    public static (DateTime Start, DateTime End)? TimeCoverage(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Properties.TryGetValue("timeStart", out var start) && item.Properties.TryGetValue("timeEnd", out var end)
            && DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var s)
            && DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var e)
            && e >= s
                ? (s.UtcDateTime, e.UtcDateTime)
                : null;
    }

    /// <summary>Formats an item id: <c>&lt;sourceId&gt;:&lt;key&gt;</c>.</summary>
    public static string FormatId(Guid sourceId, string key) => $"{sourceId}:{key}";

    /// <summary>Parses an item id (<c>&lt;sourceId&gt;:&lt;key&gt;</c>).</summary>
    public static bool TryParseId(string itemId, out Guid sourceId, out string key)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        var colon = itemId.IndexOf(':', StringComparison.Ordinal);
        key = colon >= 0 ? itemId[(colon + 1)..] : string.Empty;
        sourceId = Guid.Empty;
        return colon > 0 && key.Length > 0 && Guid.TryParse(itemId[..colon], out sourceId);
    }

    /// <summary>
    /// Forecasts first: a newer run online makes the item an update, an ended
    /// downloaded run makes it expired (#685). Otherwise the session's load
    /// state, then a newer edition online, then the file system.
    /// </summary>
    private LibraryAvailability ResolveAvailability()
    {
        if (IsForecast && _copies?.IsOutdated(Item) == true)
            return LibraryAvailability.Outdated;
        if (IsForecast && IsRunEnded())
            return LibraryAvailability.Expired;

        return _loadState?.Invoke(EffectiveItem) switch
        {
            LibraryLoadState.Loaded => LibraryAvailability.Loaded,
            LibraryLoadState.Deferred => LibraryAvailability.Deferred,
            _ when _copies?.IsOutdated(Item) == true => LibraryAvailability.Outdated,
            _ => LibraryAvailabilityResolver.Resolve(EffectiveItem),
        };
    }

    /// <summary>True when the downloaded copy's run has ended (whatever is online).</summary>
    private bool IsRunEnded() =>
        LocalRun is { } local
        && ForecastRuns.ShownWindow(Item, local) is { } window
        && window.ValidTo <= _time.GetUtcNow();
}
