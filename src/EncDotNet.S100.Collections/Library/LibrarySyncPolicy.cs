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
    /// <summary>True when this policy keeps <paramref name="source"/> in sync.</summary>
    bool IsSynced(CollectionSource source);

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
    public bool IsSynced(CollectionSource source) => source is SecomSource { Sync: true };

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
