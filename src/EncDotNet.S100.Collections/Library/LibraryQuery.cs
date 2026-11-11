using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Library;

/// <summary>Filters for <see cref="LibraryQuery.Find{T}"/>.</summary>
/// <param name="SourceId">A collection or source id, or <see langword="null"/> for the whole library.</param>
/// <param name="States">States to keep, or <see langword="null"/> (or empty) for all.</param>
/// <param name="Spec">A product specification to keep, e.g. "S-111", or <see langword="null"/>.</param>
/// <param name="Text">Text matched against name, title, spec and properties (<see cref="LibraryItemState.Matches"/>), or <see langword="null"/>.</param>
/// <param name="Bounds">Keep items whose bounds intersect this box, or <see langword="null"/>.</param>
/// <param name="Point">Keep items whose coverage contains this point, most detailed first (<see cref="LibraryQuery.HitsAt"/>), or <see langword="null"/>.</param>
public sealed record LibraryItemQuery(
    Guid? SourceId = null,
    IReadOnlySet<LibraryAvailability>? States = null,
    string? Spec = null,
    string? Text = null,
    GeoBounds? Bounds = null,
    GeoPosition? Point = null)
{
    /// <summary>Keep only items whose data covers this time (#711, <see cref="LibraryItemState.ValidWindow"/>).</summary>
    public DateTime? ValidAt { get; init; }
}

/// <summary>
/// Finds library items (issue #655): the filters behind the Library panel's
/// map taps and the MCP <c>query_library_items</c> tool.
/// </summary>
public static class LibraryQuery
{
    /// <summary>
    /// Every item matching <paramref name="query"/>, or <see langword="null"/>
    /// when its <see cref="LibraryItemQuery.SourceId"/> names no collection or
    /// source.
    /// </summary>
    /// <typeparam name="T">The host's view of an item (a row view model, or <see cref="LibraryItemState"/> itself).</typeparam>
    /// <param name="collections">The library snapshot to search.</param>
    /// <param name="query">The filters.</param>
    /// <param name="create">Creates the host's view of an item that passed the cheap filters.</param>
    /// <param name="stateOf">The <see cref="LibraryItemState"/> behind a host view.</param>
    /// <remarks>
    /// Spec and bounds filter the raw items first; only the survivors get a
    /// state, since resolving one checks the file system and session.
    /// </remarks>
    public static List<T>? Find<T>(
        IReadOnlyList<LibraryCollection> collections,
        LibraryItemQuery query,
        Func<CollectionItem, LibrarySource, T> create,
        Func<T, LibraryItemState> stateOf)
    {
        ArgumentNullException.ThrowIfNull(collections);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(stateOf);

        if (ScopeOf(collections, query.SourceId) is not { } scope)
            return null;

        IEnumerable<(CollectionItem Item, LibrarySource Source)> pairs = query.Point is { } point
            ? HitsAt(collections, point).Where(p => scope.Contains(p.Source.Id))
            : collections
                .SelectMany(c => c.Sources)
                .Where(source => scope.Contains(source.Id))
                .SelectMany(source => (source.Index?.Items ?? []).Select(item => (item, source)));
        if (query.Spec is { } spec)
            pairs = pairs.Where(p => string.Equals(p.Item.ProductSpec, spec, StringComparison.OrdinalIgnoreCase));
        if (query.Bounds is { } box)
            pairs = pairs.Where(p => p.Item.Bounds is { } bounds && bounds.Intersects(box));

        var rows = pairs.Select(p => create(p.Item, p.Source));
        if (!string.IsNullOrWhiteSpace(query.Text))
            rows = rows.Where(row => stateOf(row).Matches(query.Text));
        if (query.States is { Count: > 0 } states)
            rows = rows.Where(row => states.Contains(stateOf(row).Availability));
        if (query.ValidAt is { } at)
            rows = rows.Where(row => stateOf(row).ValidWindow is { } window && at >= window.Start && at <= window.End);

        return rows.ToList();
    }

    /// <summary>
    /// Every item covering <paramref name="position"/>, most detailed (highest
    /// usage band, then smallest extent) first.
    /// </summary>
    public static IEnumerable<(CollectionItem Item, LibrarySource Source)> HitsAt(
        IReadOnlyList<LibraryCollection> collections, GeoPosition position)
    {
        ArgumentNullException.ThrowIfNull(collections);
        return collections
            .SelectMany(c => c.Sources)
            .SelectMany(s => (s.Index?.Items ?? []).Select(i => (Item: i, Source: s)))
            .Where(p => CoverageHitTest.Contains(p.Item, position))
            .OrderByDescending(p => p.Item.UsageBand ?? 0)
            .ThenBy(p => CoverageHitTest.Area(p.Item));
    }

    /// <summary>Finds an item by its id (<see cref="LibraryItemState.Id"/>).</summary>
    /// <returns>The item and its source, or <see langword="null"/> when no item has that id.</returns>
    public static (CollectionItem Item, LibrarySource Source)? FindById(IReadOnlyList<LibraryCollection> collections, string itemId)
    {
        ArgumentNullException.ThrowIfNull(collections);
        ArgumentNullException.ThrowIfNull(itemId);
        if (!LibraryItemState.TryParseId(itemId, out var sourceId, out var key))
            return null;
        var source = collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        var item = source?.Index?.Items.FirstOrDefault(i => string.Equals(i.Key, key, StringComparison.Ordinal));
        return source is not null && item is not null ? (item, source) : null;
    }

    /// <summary>
    /// The source ids <paramref name="id"/> names: every source when it is
    /// <see langword="null"/>, a collection's sources, or one source;
    /// <see langword="null"/> when it names nothing.
    /// </summary>
    public static IReadOnlySet<Guid>? ScopeOf(IReadOnlyList<LibraryCollection> collections, Guid? id)
    {
        ArgumentNullException.ThrowIfNull(collections);
        if (id is not { } wanted)
            return collections.SelectMany(c => c.Sources).Select(s => s.Id).ToHashSet();
        if (collections.FirstOrDefault(c => c.Id == wanted) is { } collection)
            return collection.Sources.Select(s => s.Id).ToHashSet();
        return collections.SelectMany(c => c.Sources).Any(s => s.Id == wanted) ? new HashSet<Guid> { wanted } : null;
    }
}
