using EncDotNet.S100.Core;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Turns one kind of <see cref="CollectionSource"/> into a
/// <see cref="SourceIndex"/> without loading the datasets it describes.
/// </summary>
public interface ICollectionSourceIndexer
{
    /// <summary>Returns true when this indexer handles <paramref name="source"/>.</summary>
    bool CanIndex(CollectionSource source);

    /// <summary>
    /// Cheaply computes a fingerprint of the source's current state. When it
    /// equals a previous index's <see cref="SourceIndex.Fingerprint"/>, that
    /// index is still valid. Returns <see langword="null"/> when the state
    /// cannot be determined (for example the source is unavailable).
    /// </summary>
    ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken);

    /// <summary>Indexes <paramref name="source"/>.</summary>
    ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads cheap metadata (product specification, extent, display scale) from a
/// loose dataset file that is not described by an exchange-set catalogue.
/// </summary>
/// <remarks>
/// Hosts supply the probe so the collections library does not depend on the
/// dataset pipelines; the viewer backs it with its cached metadata reader.
/// Return <see langword="null"/> when the file is not a recognised dataset.
/// The <see cref="DatasetMetadata.Extent"/> is only used when it is
/// geographic (<see cref="DatasetMetadata.HorizontalCrsEpsg"/> is
/// <see langword="null"/> or 4326).
/// </remarks>
/// <param name="path">The absolute path of the candidate file.</param>
/// <param name="cancellationToken">Cancels the probe.</param>
public delegate DatasetMetadata? DatasetProbe(string path, CancellationToken cancellationToken);
