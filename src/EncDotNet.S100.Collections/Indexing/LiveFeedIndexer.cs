namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes a <see cref="LiveFeedSource"/> as no items: a live feed's data is
/// drawn by its host, not listed or downloaded (data source design note D8).
/// Without it the Library would mark a live feed as failed.
/// </summary>
public sealed class LiveFeedIndexer : ICollectionSourceIndexer
{
    /// <summary>The fingerprint of every live feed's index, which never changes.</summary>
    public const string Fingerprint = "live-feed";

    /// <inheritdoc />
    public bool CanIndex(CollectionSource source) => source is LiveFeedSource;

    /// <inheritdoc />
    public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
        ValueTask.FromResult<string?>(Fingerprint);

    /// <inheritdoc />
    public ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, Fingerprint, [], []));
    }
}
