namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// The open datasets a download is about to replace (#1075): a downloaded
/// copy that is open in the host's session keeps its files open (a GRIB2 run,
/// a lazily read S-111 grid), and Windows won't move or delete an open file,
/// so the download that replaces the copy would fail. <see cref="Close"/>
/// closes them before the download; <see cref="ReopenAsync"/> opens them
/// again afterwards (the new copy, or the old one when the download failed),
/// unless the caller has opened them already.
/// </summary>
/// <remarks>
/// Closing also means the session shows the new copy afterwards: a host
/// opener treats a location that is already open as opened, so without it
/// the old run (same folder) would stay on screen.
/// </remarks>
public sealed class LibraryReplacedCopies
{
    private readonly ILibraryLocalCopies _copies;
    private readonly Func<CollectionItem, LibraryLoadState> _stateOf;
    private readonly IReadOnlyList<(CollectionItem Item, LibrarySource? Source, bool Deferred)> _closed;

    private LibraryReplacedCopies(
        ILibraryLocalCopies copies, Func<CollectionItem, LibraryLoadState> stateOf, IReadOnlyList<(CollectionItem, LibrarySource?, bool)> closed)
    {
        _copies = copies;
        _stateOf = stateOf;
        _closed = closed;
    }

    /// <summary>The items whose copies were closed (as their sources list them), each with its source and whether it was only deferred.</summary>
    public IReadOnlyList<(CollectionItem Item, LibrarySource? Source, bool Deferred)> Closed => _closed;

    /// <summary>
    /// Closes the open copies that downloading <paramref name="downloading"/>
    /// would replace: each item's own copy, and for a package every item of
    /// its source in the same package (they share the package's folder).
    /// </summary>
    /// <param name="downloading">The items about to download, each with its source (for a package's other items); a null source considers the item alone.</param>
    /// <param name="copies">Resolves downloaded copies.</param>
    /// <param name="stateOf">The session state of a localised item.</param>
    /// <param name="close">Closes localised items in the session; returns how many were closed.</param>
    public static LibraryReplacedCopies Close(
        IEnumerable<(CollectionItem Item, LibrarySource? Source)> downloading,
        ILibraryLocalCopies copies,
        Func<CollectionItem, LibraryLoadState> stateOf,
        Func<IReadOnlyList<CollectionItem>, int> close)
    {
        ArgumentNullException.ThrowIfNull(downloading);
        ArgumentNullException.ThrowIfNull(copies);
        ArgumentNullException.ThrowIfNull(stateOf);
        ArgumentNullException.ThrowIfNull(close);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var open = new List<(CollectionItem Original, LibrarySource? Source, CollectionItem Local, bool Deferred)>();
        foreach (var (item, source) in downloading)
        {
            // Only an online item has a downloaded copy a download replaces.
            if (item.Location is not RemoteItemLocation)
                continue;
            foreach (var replaced in SharingCopy(item, source))
            {
                var local = copies.Localize(replaced);
                if (local.Location is not LocalItemLocation location || !seen.Add(Key(location)))
                    continue;
                var state = stateOf(local);
                if (state != LibraryLoadState.None)
                    open.Add((replaced, source, local, state == LibraryLoadState.Deferred));
            }
        }

        if (open.Count > 0)
            close([.. open.Select(o => o.Local)]);
        return new LibraryReplacedCopies(copies, stateOf, [.. open.Select(o => (o.Original, o.Source, o.Deferred))]);
    }

    /// <summary>
    /// Opens the closed items again (their current copy) as they were, loaded
    /// or deferred, skipping any the caller has opened since.
    /// </summary>
    /// <param name="load">Opens localised items (each with its source, for a label), deferred or not.</param>
    public async Task ReopenAsync(Func<IReadOnlyList<(CollectionItem Local, LibrarySource? Source)>, bool, Task> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        var reopen = _closed
            .Select(c => (Local: _copies.Localize(c.Item), c.Source, c.Deferred))
            .Where(c => c.Local.Location is LocalItemLocation && _stateOf(c.Local) == LibraryLoadState.None)
            .ToArray();
        foreach (var group in reopen.GroupBy(c => c.Deferred))
            await load([.. group.Select(c => (c.Local, c.Source))], group.Key).ConfigureAwait(true);
    }

    // The items whose copy a download of item replaces: a package's items share its folder.
    private static IEnumerable<CollectionItem> SharingCopy(CollectionItem item, LibrarySource? source)
    {
        yield return item;
        if (item.Location is not RemoteItemLocation { Package: { } package, Layout: null } remote || source?.Index is not { } index)
            yield break;
        foreach (var other in index.Items)
        {
            if (!ReferenceEquals(other, item)
                && other.Location is RemoteItemLocation { Layout: null } candidate
                && string.Equals(candidate.Package, package, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.DownloadFolder, remote.DownloadFolder, StringComparison.Ordinal))
            {
                yield return other;
            }
        }
    }

    private static string Key(LocalItemLocation location) =>
        Path.TrimEndingDirectorySeparator(location.RootPath) + "|" + location.RelativePath.Replace('\\', '/');
}
