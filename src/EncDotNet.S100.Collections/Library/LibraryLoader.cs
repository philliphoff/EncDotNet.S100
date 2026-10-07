namespace EncDotNet.S100.Collections.Library;

/// <summary>The outcome of <see cref="LibraryLoader"/>.<c>LoadAsync</c>.</summary>
/// <param name="Opened">Items opened (loaded, or registered to load as you pan).</param>
/// <param name="Skipped">Items that could not be opened (online, missing, catalogue-only, unknown product, or failed).</param>
/// <param name="Problems">
/// Why whole groups or single items failed to open, in the order they arose;
/// <see langword="null"/> when nothing failed (skipping an item that cannot be
/// opened at all, such as an online one, is not a problem).
/// </param>
public sealed record LibraryLoadResult(int Opened, int Skipped, IReadOnlyList<string>? Problems = null);

/// <summary>
/// The Library source items were loaded from (#809), so a host can show a
/// source's datasets together (one row per source) however many exchange
/// sets or folders they come from.
/// </summary>
/// <param name="SourceId">The source's id.</param>
/// <param name="Name">The source's display name.</param>
public sealed record LibrarySourceLabel(Guid SourceId, string Name);

/// <summary>
/// Library items that open together: the local items of one exchange set
/// (or one loose-dataset folder), so a host gives the set one source, one
/// header and one catalogue read.
/// </summary>
/// <param name="RootPath">The exchange-set folder, ZIP, or loose-dataset folder.</param>
/// <param name="CatalogueRelativePath">The catalogue within the root, or <see langword="null"/> for loose datasets.</param>
/// <param name="IsZip">True when <paramref name="RootPath"/> is a ZIP archive.</param>
/// <param name="Items">The items, each with a <see cref="LocalItemLocation"/> under the root, in request order.</param>
public sealed record LibraryOpenGroup(
    string RootPath,
    string? CatalogueRelativePath,
    bool IsZip,
    IReadOnlyList<CollectionItem> Items)
{
    /// <summary>The Library source the items come from, when the caller says; <see langword="null"/> otherwise.</summary>
    public LibrarySourceLabel? Source { get; init; }

    /// <summary>The local location of <paramref name="item"/> (every group item has one).</summary>
    /// <param name="item">One of <see cref="Items"/>.</param>
    public static LocalItemLocation LocationOf(CollectionItem item) => (LocalItemLocation)item.Location;
}

/// <summary>What a host's opener managed for one <see cref="LibraryOpenGroup"/>.</summary>
/// <param name="Opened">Items opened (or already open).</param>
/// <param name="Problems">Why other items in the group did not open; empty when all did.</param>
public sealed record LibraryOpenOutcome(int Opened, IReadOnlyList<string> Problems);

/// <summary>
/// The host seam <see cref="LibraryLoader"/> opens datasets through: the
/// viewer registers a group with its exchange-set service (one Datasets-panel
/// header, optional load-as-you-pan); a headless host loads each item into
/// its dataset catalog. The opener also remembers what each item became, so
/// the Library can show it as loaded.
/// </summary>
public interface ILibraryDatasetOpener
{
    /// <summary>Raised when an opened item's state changes (loaded, deferred, closed).</summary>
    event EventHandler? Changed;

    /// <summary>The session state of the dataset at <paramref name="location"/>.</summary>
    /// <param name="location">A local item location.</param>
    LibraryLoadState StateOf(LocalItemLocation location);

    /// <summary>
    /// Opens <paramref name="group"/>'s items: loads them now or, with
    /// <paramref name="defer"/> (where the host supports it), registers them to
    /// load as they come into view. Reports per-item problems in the outcome;
    /// throws <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>
    /// or <see cref="InvalidDataException"/> when the whole group cannot be read.
    /// </summary>
    /// <param name="group">The items of one exchange set or folder.</param>
    /// <param name="defer">True to load as the items come into view.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    Task<LibraryOpenOutcome> OpenAsync(LibraryOpenGroup group, bool defer, CancellationToken cancellationToken);
}

/// <summary>
/// Opens library items in a host's session (issues #655, #792): skips what
/// cannot be opened (online, missing, catalogue-only, unknown product),
/// groups the rest by exchange set (<see cref="Plan"/>) and opens each group
/// through the host's <see cref="ILibraryDatasetOpener"/>. UI-free, so the
/// viewer and a headless host share it.
/// </summary>
public sealed class LibraryLoader : IDisposable
{
    private readonly ILibraryDatasetOpener _opener;

    /// <summary>Creates a loader that opens through <paramref name="opener"/>.</summary>
    /// <param name="opener">The host's opener.</param>
    public LibraryLoader(ILibraryDatasetOpener opener)
    {
        ArgumentNullException.ThrowIfNull(opener);
        _opener = opener;
        _opener.Changed += OnOpenerChanged;
    }

    /// <summary>Raised after a load, and when any opened item's state changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The session state of <paramref name="item"/>.</summary>
    /// <param name="item">The library item (its downloaded copy for a downloaded online item).</param>
    public LibraryLoadState StateOf(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Location is LocalItemLocation local ? _opener.StateOf(local) : LibraryLoadState.None;
    }

    /// <summary>
    /// Splits <paramref name="items"/> into the groups that can be opened and
    /// a count of those that cannot (not local, unknown product, or missing).
    /// </summary>
    /// <param name="items">The items to open (downloaded online items localised first).</param>
    /// <param name="sourceOf">The Library source of an item, labelling its group; <see langword="null"/> for none.</param>
    /// <returns>One group per exchange set or folder, in first-seen order, and the skipped count.</returns>
    public static (IReadOnlyList<LibraryOpenGroup> Groups, int Skipped) Plan(
        IEnumerable<CollectionItem> items, Func<CollectionItem, LibrarySourceLabel?>? sourceOf = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var skipped = 0;
        var groups = new Dictionary<(string Root, string? Catalogue), (bool IsZip, List<CollectionItem> Items)>();
        var order = new List<(string, string?)>();
        foreach (var item in items)
        {
            if (item.Location is not LocalItemLocation local
                || item.ProductSpec == "Unknown"
                || LibraryAvailabilityResolver.Resolve(item) != LibraryAvailability.Local)
            {
                skipped++;
                continue;
            }

            var key = (local.RootPath, local.CatalogueRelativePath);
            if (!groups.TryGetValue(key, out var group))
            {
                groups[key] = group = (local.IsZip, []);
                order.Add(key);
            }
            group.Items.Add(item);
        }

        return ([.. order.Select(k => new LibraryOpenGroup(k.Item1, k.Item2, groups[k].IsZip, groups[k].Items)
        {
            Source = sourceOf?.Invoke(groups[k].Items[0]),
        })], skipped);
    }

    /// <summary>
    /// Opens the local items among <paramref name="items"/>: loads them now,
    /// or with <paramref name="defer"/> registers them to load as they come
    /// into view. Never throws for an unreadable group or item: the result
    /// counts it as skipped and says why.
    /// </summary>
    /// <param name="items">The items to open (downloaded online items localised first).</param>
    /// <param name="defer">True to load as the items come into view (where the host supports it).</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    public Task<LibraryLoadResult> LoadAsync(
        IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
        LoadAsync(items, defer, sourceOf: null, cancellationToken);

    /// <summary>
    /// Opens the local items among <paramref name="items"/> as
    /// <see cref="LoadAsync(IReadOnlyList{CollectionItem}, bool, CancellationToken)"/>
    /// does, labelling each group with its Library source.
    /// </summary>
    /// <param name="items">The items to open (downloaded online items localised first).</param>
    /// <param name="defer">True to load as the items come into view (where the host supports it).</param>
    /// <param name="sourceOf">The Library source of an item; <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    public async Task<LibraryLoadResult> LoadAsync(
        IReadOnlyList<CollectionItem> items, bool defer, Func<CollectionItem, LibrarySourceLabel?>? sourceOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var (groups, skipped) = Plan(items, sourceOf);
        var opened = 0;
        var problems = new List<string>();
        foreach (var group in groups)
        {
            LibraryOpenOutcome outcome;
            try
            {
                // Resume on the caller's context: the viewer's opener touches its view models.
                outcome = await _opener.OpenAsync(group, defer, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                skipped += group.Items.Count;
                problems.Add(ex.Message);
                continue;
            }

            opened += outcome.Opened;
            skipped += Math.Max(0, group.Items.Count - outcome.Opened);
            problems.AddRange(outcome.Problems);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return new LibraryLoadResult(opened, skipped, problems.Count > 0 ? problems : null);
    }

    private void OnOpenerChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Stops listening to the opener.</summary>
    public void Dispose() => _opener.Changed -= OnOpenerChanged;
}
