using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Collections.Feeds;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes an <see cref="S100FeedSource"/> (issue #680): fetches (or
/// revalidates) the feed — for example one served by <c>s100 feed serve</c>
/// — and lists its items, with their coverage, as online items that download
/// into <see cref="DownloadFolderFor"/>.
/// </summary>
/// <remarks>
/// Caching follows <see cref="NoaaEncFeedIndexer"/>, with a shorter default
/// revalidation interval (one minute), because a served feed follows a
/// folder that may change and an unchanged one answers with a cheap 304.
/// Items keep the publisher's layout, so a downloaded item is resolved to its
/// local copy without re-indexing.
/// </remarks>
public sealed class S100FeedIndexer : ICollectionSourceIndexer
{
    private const string FingerprintVersion = "s100feed-v1";

    /// <summary>The default revalidation interval for feeds.</summary>
    public static FeedCacheOptions DefaultCacheOptions { get; } = new() { RevalidationInterval = TimeSpan.FromMinutes(1) };

    private readonly FeedCache _cache;

    /// <summary>Creates an indexer.</summary>
    /// <param name="httpClient">The client used to fetch feeds.</param>
    /// <param name="cacheDirectory">Where raw feeds are cached.</param>
    /// <param name="options">Cache options; defaults to <see cref="DefaultCacheOptions"/>.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public S100FeedIndexer(
        HttpClient httpClient,
        string cacheDirectory,
        FeedCacheOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _cache = new FeedCache(httpClient, cacheDirectory, options ?? DefaultCacheOptions, timeProvider);
    }

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is S100FeedSource;

    /// <inheritdoc/>
    public async ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var feed = AsFeed(source);
        try
        {
            var snapshot = await _cache.GetAsync(feed.FeedUri, forceRevalidate: false, cancellationToken).ConfigureAwait(false);
            return Fingerprint(snapshot, feed.Filter);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        var feed = AsFeed(source);
        var diagnostics = new List<IndexDiagnostic>();
        progress?.Report(new IndexProgress(0, feed.FeedUri.AbsoluteUri));

        FeedSnapshot snapshot;
        try
        {
            snapshot = await _cache.GetAsync(feed.FeedUri, forceRevalidate: false, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, ex.Message, feed.FeedUri.AbsoluteUri));
            return new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [], diagnostics);
        }

        if (snapshot.StaleReason is { } reason)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture,
                    $"Feed could not be refreshed ({reason}); using the copy from {snapshot.FetchedAt:u}."),
                feed.FeedUri.AbsoluteUri));
        }

        var document = await Task.Run(() => ReadSnapshot(snapshot, feed.FeedUri), cancellationToken).ConfigureAwait(false);
        var folder = DownloadFolderFor(feed.FeedUri);
        var items = document.Items
            .Where(feed.Filter.Matches)
            .Select(i => Localize(i, folder))
            .OfType<CollectionItem>()
            .ToArray();

        progress?.Report(new IndexProgress(items.Length, null));
        return new SourceIndex(source.Id, DateTimeOffset.UtcNow, Fingerprint(snapshot, feed.Filter), items, diagnostics);
    }

    /// <summary>Returns the (cached or freshly fetched) feed at <paramref name="feedUri"/>, e.g. to choose products.</summary>
    /// <exception cref="HttpRequestException">The server could not be reached and nothing is cached.</exception>
    public async Task<S100FeedDocument> GetFeedAsync(Uri feedUri, bool forceRevalidate = false, CancellationToken cancellationToken = default)
    {
        var snapshot = await _cache.GetAsync(feedUri, forceRevalidate, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ReadSnapshot(snapshot, feedUri), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Summarises the products in <paramref name="feed"/> (item counts and sizes), for choosing a filter.</summary>
    public static IReadOnlyList<CatalogFacetValue> Products(S100FeedDocument feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        return feed.Items
            .GroupBy(i => i.ProductSpec, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CatalogFacetValue(g.Key, g.Count(), g.Sum(i => (i.Location as RemoteItemLocation)?.SizeBytes ?? 0)))
            .OrderBy(f => f.Value, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The managed folder, relative to the downloads root, that items of the
    /// feed at <paramref name="feedUri"/> download into: one per feed, e.g.
    /// <c>feeds/192.168.1.20-8100-3f2a9c1b</c>.
    /// </summary>
    public static string DownloadFolderFor(Uri feedUri)
    {
        ArgumentNullException.ThrowIfNull(feedUri);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(feedUri.AbsoluteUri)))[..8].ToLowerInvariant();
        var host = new string(feedUri.Host.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray());
        return string.Create(CultureInfo.InvariantCulture, $"feeds/{host}-{feedUri.Port}-{hash}");
    }

    private static S100FeedDocument ReadSnapshot(FeedSnapshot snapshot, Uri feedUri)
    {
        using var stream = File.OpenRead(snapshot.FilePath);
        return S100Feed.Read(stream, feedUri);
    }

    /// <summary>
    /// Points a feed item's download at this feed's managed folder. Items
    /// without a downloadable, absolute location are kept as listed-only.
    /// </summary>
    private static CollectionItem? Localize(CollectionItem item, string folder) => item.Location switch
    {
        RemoteItemLocation { Uri.IsAbsoluteUri: true } remote when remote.Uri.Scheme is "http" or "https" =>
            item with { Location = remote with { DownloadFolder = folder } },
        RemoteItemLocation => item with { Location = NoItemLocation.Instance },
        _ => item with { Location = NoItemLocation.Instance },
    };

    private static string Fingerprint(FeedSnapshot snapshot, S100FeedFilter filter) =>
        $"{FingerprintVersion}:{snapshot.Version}:{filter.ToCanonicalString()}";

    private static S100FeedSource AsFeed(CollectionSource source) => source switch
    {
        S100FeedSource feed => feed,
        null => throw new ArgumentNullException(nameof(source)),
        _ => throw new NotSupportedException($"{nameof(S100FeedIndexer)} cannot index {source.GetType().Name}."),
    };
}
