using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// What the Library tree's status lines say about each node (#1396): a
/// shared feed's reachability, a remote catalogue's local and update counts,
/// a forecast feed's runs, a synced source's last sync, and the downloads
/// running from it.
/// </summary>
internal sealed class LibraryNodeStatus
{
    private readonly ILibraryDownloader _downloader;
    private readonly LibrarySync? _sync;
    private readonly TimeProvider _time;
    private readonly Dictionary<Guid, List<CollectionItem>> _downloadingBySource = [];

    /// <summary>Creates the status lines' source.</summary>
    /// <param name="downloader">Says what is downloaded, and how downloads go.</param>
    /// <param name="download">Says which sources' items start downloading, from the panel, the Timeline or MCP.</param>
    /// <param name="sync">Keeps synced sources downloaded; <see langword="null"/> without syncing.</param>
    /// <param name="feedHealth">A shared feed's last reachability; <see langword="null"/> shows none.</param>
    /// <param name="time">The clock forecast runs age by.</param>
    public LibraryNodeStatus(
        ILibraryDownloader downloader,
        DownloadLibraryItemsWorkflow download,
        LibrarySync? sync,
        Func<CollectionSource, FeedHealth?>? feedHealth,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(time);
        _downloader = downloader;
        _sync = sync;
        FeedHealth = feedHealth;
        _time = time;
        download.Starting += (_, items) => Track(items);
        if (sync is not null)
            sync.Synced += (_, _) => Synced?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A shared feed's last reachability, for a new node's status line.</summary>
    public Func<CollectionSource, FeedHealth?>? FeedHealth { get; }

    /// <summary>Raised after a sync pass, when sync statuses changed.</summary>
    public event EventHandler? Synced;

    /// <summary>True when <paramref name="source"/> can be kept downloaded.</summary>
    public bool CanSync(CollectionSource source) => _sync?.CanSync(source) == true;

    /// <summary>
    /// Gives each remote S-100 catalogue node its local and update counts for
    /// its status line ("Catalogue 30.09 · 140 updates"), each forecast feed
    /// its runs and each synced source its last sync; a one-source collection
    /// shows its source's.
    /// </summary>
    public void ApplyCounts(IEnumerable<LibraryNodeViewModel> collections)
    {
        foreach (var collection in collections)
        {
            LibraryCatalogueCounts? only = null;
            LibraryForecastCounts? onlyForecast = null;
            LibrarySyncStatus? onlySync = null;
            foreach (var child in collection.Children)
            {
                // A synced source's last sync (#807, #809); its kind's own counts follow.
                child.SyncStatus = _sync is not null && child.Source is { } synced && _sync.IsSynced(synced.Definition)
                    ? _sync.StatusOf(synced.Id)
                    : null;
                if (collection.Children.Count == 1)
                    onlySync = child.SyncStatus;

                if (child.Source is { Definition: S100ForecastFeedSource, Index: { } runs })
                {
                    child.ForecastCounts = LibraryNodeText.ForecastCounts(runs, _downloader, _time.GetUtcNow());
                    if (collection.Children.Count == 1)
                        onlyForecast = child.ForecastCounts;
                    continue;
                }

                if (child.Source is not { Definition: S100CatalogueFeedSource, Index: { } index })
                    continue;

                child.CatalogueCounts = LibraryNodeText.CatalogueCounts(index, _downloader);
                if (collection.Children.Count == 1)
                    only = child.CatalogueCounts;
            }

            collection.CatalogueCounts = only;
            collection.ForecastCounts = onlyForecast;
            collection.SyncStatus = onlySync;
        }
    }

    /// <summary>Sets "Downloading 2 of 5 · 4,3 MB left" on the nodes whose datasets are downloading.</summary>
    public void ApplyDownloads(IEnumerable<LibraryNodeViewModel> collections)
    {
        var bySource = new Dictionary<Guid, string?>();
        lock (_downloadingBySource)
        {
            foreach (var (sourceId, items) in _downloadingBySource.ToArray())
            {
                var statuses = items.Select(_downloader.StatusOf).ToArray();
                var pending = statuses.Count(s => s is { State: not LibraryDownloadItemState.Failed });
                if (pending == 0 || _downloader.Progress is null)
                {
                    _downloadingBySource.Remove(sourceId);
                    bySource[sourceId] = null;
                    continue;
                }

                var left = items.Zip(statuses)
                    .Where(p => p.Second is { State: not LibraryDownloadItemState.Failed })
                    .Sum(p => Math.Max(0, ((p.First.Location as RemoteItemLocation)?.SizeBytes ?? 0) - p.Second!.BytesReceived));
                bySource[sourceId] = LibraryNodeText.Downloading(items.Count - pending + 1, items.Count, left);
            }
        }

        foreach (var collection in collections)
        {
            string? collectionStatus = null;
            foreach (var child in collection.Children)
            {
                if (bySource.TryGetValue(child.Id, out var status))
                    child.DownloadStatus = status;
                collectionStatus ??= child.DownloadStatus;
            }

            collection.DownloadStatus = collectionStatus;
        }
    }

    /// <summary>Remembers which sources' datasets are downloading.</summary>
    private void Track(IEnumerable<(CollectionItem Item, LibrarySource Source)> items)
    {
        lock (_downloadingBySource)
        {
            foreach (var group in items.GroupBy(i => i.Source.Id))
                _downloadingBySource[group.Key] = group.Select(i => i.Item).ToList();
        }
    }
}
