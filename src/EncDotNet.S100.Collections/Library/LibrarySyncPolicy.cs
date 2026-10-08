using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// The per-kind rules <see cref="LibrarySync"/> keeps a source in sync by:
/// whether the source is synced, which of its items should have a current
/// local copy, and where its copies live and when they may be pruned. The
/// sync itself (downloading, the size cap, safe pruning, settling) is the same
/// for every kind.
/// </summary>
public interface ILibrarySyncPolicy
{
    /// <summary>True when this policy can keep <paramref name="source"/>'s kind in sync (whether or not it is asked to).</summary>
    bool Supports(CollectionSource source);

    /// <summary>True when this policy keeps <paramref name="source"/> in sync: it supports it and <see cref="CollectionSource.Sync"/> is on.</summary>
    bool IsSynced(CollectionSource source) => source.Sync && Supports(source);

    /// <summary>The items of <paramref name="index"/> that should have a current local copy.</summary>
    IReadOnlyList<CollectionItem> Wanted(SourceIndex index);

    /// <summary>
    /// A download location of the managed folder the source's copies live in,
    /// so copies no source lists any more can be pruned there; <see langword="null"/>
    /// when the source never prunes.
    /// </summary>
    RemoteItemLocation? PruneFolder(CollectionSource source, SourceIndex index);

    /// <summary>
    /// True when <paramref name="index"/> lists everything the source offers
    /// now (complete and fresh), so copies it does not list may be pruned.
    /// </summary>
    bool CanPrune(SourceIndex index);
}

/// <summary>
/// Sync rules for SECOM services (issue #807): every listed object that is not
/// cancelled is kept downloaded, in the service's managed folder.
/// </summary>
public sealed class SecomSyncPolicy : ILibrarySyncPolicy
{
    /// <inheritdoc/>
    public bool Supports(CollectionSource source) => source is SecomSource;

    /// <inheritdoc/>
    public IReadOnlyList<CollectionItem> Wanted(SourceIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.Items
            .Where(i => i.Location is RemoteItemLocation && i.Status != CollectionItemStatus.Cancelled)
            .ToArray();
    }

    /// <inheritdoc/>
    public RemoteItemLocation? PruneFolder(CollectionSource source, SourceIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (source is not SecomSource secom)
            return null;
        return index.Items.Select(i => i.Location).OfType<RemoteItemLocation>().FirstOrDefault()
            ?? new RemoteItemLocation(secom.ServiceUri, DownloadFolder: SecomSourceIndexer.DownloadFolderFor(secom.ServiceUri));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A SECOM index without a fingerprint was served from the copy on disk
    /// (the service was down); a warning means it was capped.
    /// </remarks>
    public bool CanPrune(SourceIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.Fingerprint is not null && !index.Diagnostics.Any(d => d.Severity >= IndexDiagnosticSeverity.Warning);
    }
}

/// <summary>
/// Sync rules for the other online kinds (issue #809): NOAA and USACE ENC
/// feeds, community chart lists, S-100 feeds, remote S-100 catalogues and
/// forecast feeds. Every listed item that is not cancelled is kept downloaded
/// and current — a newer edition or update, a newer package, a forecast's
/// latest run (which replaces the last in place).
/// </summary>
/// <remarks>
/// Nothing is pruned: these kinds share managed folders (all NOAA cells, for
/// one) with downloads made by hand, which are not caches.
/// </remarks>
public sealed class OnlineSyncPolicy : ILibrarySyncPolicy
{
    /// <inheritdoc/>
    public bool Supports(CollectionSource source) => source is NoaaEncFeedSource or UsaceIencFeedSource
        or ChartCatalogsFeedSource or S100FeedSource or S100CatalogueFeedSource or S100ForecastFeedSource;

    /// <inheritdoc/>
    public IReadOnlyList<CollectionItem> Wanted(SourceIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return index.Items
            .Where(i => i.Location is RemoteItemLocation && i.Status != CollectionItemStatus.Cancelled)
            .ToArray();
    }

    /// <inheritdoc/>
    public RemoteItemLocation? PruneFolder(CollectionSource source, SourceIndex index) => null;

    /// <inheritdoc/>
    public bool CanPrune(SourceIndex index) => false;
}
