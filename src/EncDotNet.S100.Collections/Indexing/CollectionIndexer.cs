namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes any supported <see cref="CollectionSource"/> by delegating to the
/// first <see cref="ICollectionSourceIndexer"/> that claims it, reusing a
/// previous index when the source's fingerprint is unchanged.
/// </summary>
public sealed class CollectionIndexer
{
    private readonly IReadOnlyList<ICollectionSourceIndexer> _indexers;

    /// <summary>Creates an indexer over <paramref name="indexers"/>, tried in order.</summary>
    public CollectionIndexer(IEnumerable<ICollectionSourceIndexer> indexers)
    {
        ArgumentNullException.ThrowIfNull(indexers);
        _indexers = indexers.ToArray();
    }

    /// <summary>
    /// Creates an indexer for the built-in source kinds: folders, exchange
    /// sets and S-128 catalogues, plus any online <paramref name="feeds"/>
    /// (e.g. <see cref="NoaaEncFeedIndexer"/>, <see cref="UsaceIencFeedIndexer"/>).
    /// </summary>
    /// <param name="probe">
    /// Reads metadata from loose dataset files; when <see langword="null"/>,
    /// only loose S-57 cells are recognised (without bounds).
    /// </param>
    /// <param name="feeds">Indexers for online feeds; none when <see langword="null"/>.</param>
    public static CollectionIndexer CreateDefault(
        DatasetProbe? probe = null, IEnumerable<ICollectionSourceIndexer>? feeds = null)
    {
        var indexers = new List<ICollectionSourceIndexer> { new LocalSourceIndexer(probe), new S128CatalogueIndexer() };
        if (feeds is not null)
            indexers.AddRange(feeds);
        return new CollectionIndexer(indexers);
    }

    /// <summary>Returns true when some indexer handles <paramref name="source"/>.</summary>
    public bool CanIndex(CollectionSource source) => Find(source) is not null;

    /// <summary>
    /// Indexes <paramref name="source"/>, or returns <paramref name="previous"/>
    /// unchanged when it was built from the same source state.
    /// </summary>
    /// <param name="source">The source to index.</param>
    /// <param name="previous">A previously built index of the same source, if any.</param>
    /// <param name="progress">Receives progress reports.</param>
    /// <param name="cancellationToken">Cancels indexing.</param>
    /// <exception cref="NotSupportedException">No indexer handles <paramref name="source"/>.</exception>
    public async ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        SourceIndex? previous = null,
        IProgress<IndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var indexer = Find(source)
            ?? throw new NotSupportedException($"No indexer handles sources of type {source.GetType().Name}.");

        if (previous is { Fingerprint: { } previousFingerprint } && previous.SourceId == source.Id)
        {
            var current = await indexer.GetFingerprintAsync(source, cancellationToken).ConfigureAwait(false);
            if (current == previousFingerprint)
                return previous;
        }

        return await indexer.IndexAsync(source, progress, cancellationToken).ConfigureAwait(false);
    }

    private ICollectionSourceIndexer? Find(CollectionSource source) =>
        _indexers.FirstOrDefault(i => i.CanIndex(source));
}
