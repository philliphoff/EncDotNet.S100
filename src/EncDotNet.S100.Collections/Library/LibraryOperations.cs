using System.Diagnostics;

namespace EncDotNet.S100.Collections.Library;

/// <summary>The outcome of <see cref="LibraryOperations.DownloadAsync"/>.</summary>
/// <param name="Download">What the download batch did.</param>
/// <param name="ReindexedSources">
/// Sources re-indexed because a package was downloaded (a package lists its
/// datasets only once its source re-indexes); its datasets are not opened.
/// </param>
/// <param name="Load">What opening the downloaded items did, or <see langword="null"/> when nothing was opened.</param>
public sealed record LibraryDownloadOutcome(
    LibraryDownloadResult Download,
    IReadOnlyList<Guid> ReindexedSources,
    LibraryLoadResult? Load);

/// <summary>Whether a Library is busy, at one moment.</summary>
/// <param name="Indexing">True while a source is indexing.</param>
/// <param name="Downloads">The running download batch, or <see langword="null"/>.</param>
/// <param name="ActiveOperations">Downloads and loads still running, including the re-indexing and opening that follow a download.</param>
/// <param name="PendingDatasets">Datasets those operations have yet to open (including any still downloading).</param>
public sealed record LibraryActivitySnapshot(
    bool Indexing,
    LibraryDownloadProgress? Downloads,
    int ActiveOperations,
    int PendingDatasets)
{
    /// <summary>True when nothing is indexing, downloading or opening datasets.</summary>
    public bool IsIdle => !Indexing && Downloads is null && ActiveOperations == 0;

    /// <summary>Reads the activity of <paramref name="library"/>, its downloads and its tracked operations now.</summary>
    /// <param name="library">The library (for indexing).</param>
    /// <param name="downloads">The running download batch, or <see langword="null"/>.</param>
    /// <param name="activity">The operations tracker.</param>
    public static LibraryActivitySnapshot Capture(
        CollectionLibrary library, LibraryDownloadProgress? downloads, LibraryActivityTracker activity)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(activity);
        return new LibraryActivitySnapshot(
            library.Collections.Any(c => c.IsIndexing),
            downloads,
            activity.Active,
            activity.PendingDatasets);
    }
}

/// <summary>The outcome of <see cref="LibraryOperations.AwaitIdleAsync"/>.</summary>
/// <param name="Activity">The activity when the wait ended.</param>
/// <param name="TimedOut">True when the wait ended before the Library was idle.</param>
/// <param name="Waited">How long the call waited.</param>
public sealed record LibraryIdleWait(LibraryActivitySnapshot Activity, bool TimedOut, TimeSpan Waited);

/// <summary>
/// The Library's actions for a host without view models (issue #792): open
/// items into the host's session, download online items (then re-index or
/// open them), and tell when all that work has finished. Every operation is
/// tracked by <see cref="Activity"/> from the moment it is called until the
/// datasets it opens are open, so <see cref="AwaitIdleAsync"/> never reports
/// idle between a download finishing and its datasets opening (#790).
/// </summary>
/// <remarks>
/// The viewer drives the same pieces (<see cref="LibraryDownloads"/>,
/// <see cref="LibraryLoader"/>, <see cref="LibraryActivityTracker"/>) from its
/// Library panel; a headless host such as <c>s100 mcp serve</c> uses this
/// class with a <see cref="LibraryLoader"/> over its dataset catalog.
/// </remarks>
public sealed class LibraryOperations
{
    /// <summary>How often <see cref="AwaitIdleAsync"/> re-checks.</summary>
    public static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Creates the operations over a library, its downloads and a loader.</summary>
    /// <param name="library">The library the items come from (re-indexed after a package download).</param>
    /// <param name="downloads">Downloads of online items.</param>
    /// <param name="loader">Opens items into the host's session.</param>
    /// <param name="activity">Tracks running operations; a new tracker when <see langword="null"/>.</param>
    public LibraryOperations(
        CollectionLibrary library,
        LibraryDownloads downloads,
        LibraryLoader loader,
        LibraryActivityTracker? activity = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(downloads);
        ArgumentNullException.ThrowIfNull(loader);
        Library = library;
        Downloads = downloads;
        Loader = loader;
        Activity = activity ?? new LibraryActivityTracker();
    }

    /// <summary>The library the items come from.</summary>
    public CollectionLibrary Library { get; }

    /// <summary>Downloads of online items (progress, per-item status, cancelling).</summary>
    public LibraryDownloads Downloads { get; }

    /// <summary>Opens items into the host's session (and knows which are open).</summary>
    public LibraryLoader Loader { get; }

    /// <summary>The running operations.</summary>
    public LibraryActivityTracker Activity { get; }

    /// <summary>The state of <paramref name="item"/> as the Library shows it: its downloaded copy and whether it is open.</summary>
    /// <param name="item">The indexed item.</param>
    /// <param name="source">The source it was indexed from.</param>
    /// <param name="time">The clock that decides whether a downloaded forecast run has ended.</param>
    public LibraryItemState StateOf(CollectionItem item, LibrarySource source, TimeProvider? time = null) =>
        new(item, source, Downloads, Loader.StateOf, time);

    /// <summary>
    /// Opens <paramref name="items"/> (downloaded online items open their
    /// copy): now, or with <paramref name="defer"/> as they come into view
    /// where the host supports it. Tracked until the opens finish.
    /// </summary>
    /// <param name="items">The items to open.</param>
    /// <param name="defer">True to load as the items come into view.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    public async Task<LibraryLoadResult> LoadAsync(
        IReadOnlyList<CollectionItem> items, bool defer = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var localized = items.Select(Downloads.Localize).ToArray();
        using var scope = Activity.Begin(defer ? 0 : LibraryLoader.Plan(localized).Groups.Sum(g => g.Items.Count));
        return await Loader.LoadAsync(localized, defer, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Downloads the downloadable items among <paramref name="items"/>, then
    /// re-indexes the sources of any downloaded packages (whose datasets the
    /// Library lists only after that) or, with <paramref name="load"/>, opens
    /// the downloaded items. The operation is tracked from the call (before
    /// the first await), so a caller may start it without awaiting and an
    /// <see cref="AwaitIdleAsync"/> that follows still waits for all of it.
    /// </summary>
    /// <param name="items">The items to download, each with the source it was indexed from.</param>
    /// <param name="load">True to open the downloaded items afterwards.</param>
    /// <param name="progress">Told the batch's progress as it starts and as each item finishes, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the download (and what follows).</param>
    public async Task<LibraryDownloadOutcome> DownloadAsync(
        IReadOnlyList<(CollectionItem Item, LibrarySource Source)> items,
        bool load,
        IProgress<LibraryDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var downloadable = items.Where(i => Downloads.CanDownload(i.Item)).ToArray();
        using var scope = Activity.Begin(load ? downloadable.Length : 0);

        var download = await Downloads.DownloadAsync([.. items.Select(i => i.Item)], progress, cancellationToken).ConfigureAwait(true);
        if (download.Downloaded == 0)
            return new LibraryDownloadOutcome(download, [], null);

        // A package's datasets (and their coverage) appear once its source
        // re-indexes; items with a stated layout (S-100 feeds) are listed as
        // themselves already.
        var packageSources = items
            .Where(i => i.Item.Location is RemoteItemLocation { Package: not null, Layout: null })
            .Select(i => i.Source.Id)
            .Distinct()
            .ToArray();
        if (packageSources.Length > 0)
        {
            foreach (var source in packageSources)
                Library.Refresh(sourceId: source);
            await Library.WhenIdle().WaitAsync(cancellationToken).ConfigureAwait(true);
            return new LibraryDownloadOutcome(download, packageSources, null);
        }

        if (!load)
            return new LibraryDownloadOutcome(download, [], null);

        var localized = items.Select(i => Downloads.Localize(i.Item)).ToArray();
        var opened = await Loader.LoadAsync(localized, defer: false, cancellationToken).ConfigureAwait(true);
        return new LibraryDownloadOutcome(download, [], opened);
    }

    /// <summary>The Library's activity now.</summary>
    public LibraryActivitySnapshot GetActivity() =>
        LibraryActivitySnapshot.Capture(Library, Downloads.Progress, Activity);

    /// <summary>
    /// Waits until nothing is indexing, downloading or opening datasets, or
    /// until <paramref name="timeout"/> passes.
    /// </summary>
    /// <param name="timeout">How long to wait at most (zero to just look).</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public async Task<LibraryIdleWait> AwaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var activity = GetActivity();
            if (activity.IsIdle)
                return new LibraryIdleWait(activity, false, clock.Elapsed);
            if (clock.Elapsed >= timeout)
                return new LibraryIdleWait(activity, true, clock.Elapsed);
            await Task.Delay(IdlePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
