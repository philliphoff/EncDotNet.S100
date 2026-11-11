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
    /// <param name="library">The library the items come from (their sources are re-indexed after a download).</param>
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
    /// Downloads the downloadable items among <paramref name="items"/> and
    /// re-indexes their sources (so what an index reads from a downloaded copy
    /// appears), then waits for that when a package was downloaded (whose
    /// datasets the Library lists only after it) or, with <paramref name="load"/>,
    /// opens the downloaded items. The operation is tracked from the call (before
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

        // An open copy holds its files open, and Windows won't replace them
        // (#1075): close what the download replaces, and open it again after
        // (the new copy, or the old one if the download failed) unless the load
        // below already has.
        var replaced = LibraryReplacedCopies.Close(
            downloadable.Select(i => (i.Item, (LibrarySource?)i.Source)), Downloads, Loader.StateOf, Loader.Close);
        try
        {
            return await DownloadAndOpenAsync(items, downloadable, load, progress, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            await replaced.ReopenAsync((reopen, defer) => Loader.LoadAsync([.. reopen.Select(r => r.Local)], defer, CancellationToken.None)).ConfigureAwait(true);
        }
    }

    private async Task<LibraryDownloadOutcome> DownloadAndOpenAsync(
        IReadOnlyList<(CollectionItem Item, LibrarySource Source)> items,
        (CollectionItem Item, LibrarySource Source)[] downloadable,
        bool load,
        IProgress<LibraryDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var download = await Downloads.DownloadAsync([.. items.Select(i => i.Item)], progress, cancellationToken).ConfigureAwait(true);
        if (download.Downloaded == 0)
            return new LibraryDownloadOutcome(download, [], null);

        // What an index takes from a downloaded copy (a package's datasets, a
        // SECOM object's bounds and signature) appears once its source
        // re-indexes, so every downloaded item's source does; one whose index
        // does not read its copies is confirmed unchanged by its fingerprint.
        foreach (var source in downloadable.Select(i => i.Source.Id).Distinct())
            Library.Refresh(sourceId: source);

        // A package lists its datasets only after that; items with a stated
        // layout (S-100 feeds) are listed as themselves already.
        var packageSources = items
            .Where(i => i.Item.Location is RemoteItemLocation { Package: not null, Layout: null })
            .Select(i => i.Source.Id)
            .Distinct()
            .ToArray();
        if (packageSources.Length > 0)
        {
            await Library.WhenIdle().WaitAsync(cancellationToken).ConfigureAwait(true);
            return new LibraryDownloadOutcome(download, packageSources, null);
        }

        if (!load)
            return new LibraryDownloadOutcome(download, [], null);

        var localized = items.Select(i => Downloads.Localize(i.Item)).ToArray();
        var opened = await Loader.LoadAsync(localized, defer: false, cancellationToken).ConfigureAwait(true);
        return new LibraryDownloadOutcome(download, [], opened);
    }

    /// <summary>
    /// "Load latest run" for a weather source (#1075): re-reads the source, so
    /// its latest published run is listed, downloads that run (replacing the
    /// previous, unless it was pinned) and opens it. A run already downloaded
    /// is just opened. Pinned runs are left alone.
    /// </summary>
    /// <param name="sourceId">The source.</param>
    /// <param name="cancellationToken">Cancels the refresh, download and load.</param>
    /// <returns>What the download and load did, or null when the source isn't in the Library or lists no run.</returns>
    public async Task<LibraryDownloadOutcome?> LoadLatestRunAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        using var scope = Activity.Begin(0);
        Library.Refresh(sourceId: sourceId);
        await Library.WhenIdle().WaitAsync(cancellationToken).ConfigureAwait(true);

        var source = Library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source?.Index is not { } index)
            return null;
        var latest = index.Items
            .Where(i => i.Location is RemoteItemLocation && !Weather.WeatherRuns.IsPinned(i))
            .Select(i => (i, source))
            .ToArray();
        if (latest.Length == 0)
            return null;

        var missing = latest.Where(l => Downloads.Localize(l.i).Location is RemoteItemLocation || Downloads.IsOutdated(l.i)).ToArray();
        if (missing.Length > 0)
            return await DownloadAsync(latest, load: true, cancellationToken: cancellationToken).ConfigureAwait(true);

        // Already the latest on disk: just open it.
        var opened = await LoadAsync([.. latest.Select(l => l.i)], cancellationToken: cancellationToken).ConfigureAwait(true);
        return new LibraryDownloadOutcome(new LibraryDownloadResult(0, 0, false), [], opened);
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
