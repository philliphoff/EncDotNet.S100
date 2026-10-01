using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Collections.RemoteCatalogues;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes an <see cref="S100CatalogueFeedSource"/> (issue #685): fetches (or
/// revalidates) a remote S-100 exchange catalogue — such as NOAA's S-102
/// bathymetry on AWS Open Data — and lists the datasets in the selected
/// folders as online items, each downloading as its own file.
/// </summary>
/// <remarks>
/// <para>
/// Caching follows <see cref="NoaaEncFeedIndexer"/>: one raw copy per URL,
/// revalidated with conditional requests, served from cache when offline.
/// Gzip-encoded catalogues are kept compressed.
/// </para>
/// <para>
/// S-100 catalogues carry no file sizes. When the catalogue is in an Amazon S3
/// bucket, the selected folders are listed (one <c>ListObjectsV2</c> request
/// per thousand files) for sizes and dates; elsewhere sizes stay unknown.
/// Folders become the index's <see cref="SourceIndex.Groups"/>.
/// </para>
/// </remarks>
public sealed class S100CatalogueFeedIndexer : ICollectionSourceIndexer
{
    private const string FingerprintVersion = "s100cat-v1";

    /// <summary>How long a bucket listing is reused (e.g. from choosing areas to indexing them).</summary>
    private static readonly TimeSpan ListingLifetime = TimeSpan.FromMinutes(15);

    private readonly HttpClient _httpClient;
    private readonly FeedCache _cache;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Uri, (DateTimeOffset At, Task<IReadOnlyList<S3Object>> Listing)> _listings = new();

    /// <summary>Creates an indexer.</summary>
    /// <param name="httpClient">The client used to fetch catalogues and list buckets.</param>
    /// <param name="cacheDirectory">Where raw catalogues are cached.</param>
    /// <param name="options">Cache options; defaults to <see cref="FeedCacheOptions.Default"/>.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public S100CatalogueFeedIndexer(
        HttpClient httpClient,
        string cacheDirectory,
        FeedCacheOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _time = timeProvider ?? TimeProvider.System;
        _cache = new FeedCache(httpClient, cacheDirectory, options, _time);
    }

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is S100CatalogueFeedSource;

    /// <inheritdoc/>
    public async ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var feed = AsFeed(source);
        try
        {
            var snapshot = await _cache.GetAsync(feed.CatalogUri, forceRevalidate: false, cancellationToken).ConfigureAwait(false);
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
        progress?.Report(new IndexProgress(0, feed.CatalogUri.AbsoluteUri));

        FeedSnapshot snapshot;
        try
        {
            snapshot = await _cache.GetAsync(feed.CatalogUri, forceRevalidate: false, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, ex.Message, feed.CatalogUri.AbsoluteUri));
            return new SourceIndex(source.Id, _time.GetUtcNow(), null, [], diagnostics);
        }

        if (snapshot.StaleReason is { } reason)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture,
                    $"Catalogue could not be refreshed ({reason}); using the copy from {snapshot.FetchedAt:u}."),
                feed.CatalogUri.AbsoluteUri));
        }

        var catalogue = await Task.Run(() => ReadSnapshot(snapshot, feed.CatalogUri), cancellationToken).ConfigureAwait(false);
        diagnostics.AddRange(catalogue.Diagnostics.Where(d => d.Severity >= IndexDiagnosticSeverity.Warning));

        IReadOnlyList<CollectionItem> items = catalogue.Items
            .Where(i => feed.Filter.Matches(
                RemoteS100Catalogue.FolderOf(i), i.Properties.GetValueOrDefault(RemoteS100Catalogue.NavigationPurposeProperty)))
            .ToArray();

        var folders = feed.Filter.Folders.Count == 0
            ? [string.Empty]
            : feed.Filter.Folders.Select(RemoteS100Catalogue.TopFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        try
        {
            if (await ListAsync(catalogue, folders, cancellationToken).ConfigureAwait(false) is { } objects)
                items = S100CatalogueFacets.WithSizes(items, objects);
        }
        catch (HttpRequestException ex)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Info, $"Download sizes are unknown: {ex.Message}", catalogue.RootUri.AbsoluteUri));
        }

        progress?.Report(new IndexProgress(items.Count, null));
        return new SourceIndex(source.Id, _time.GetUtcNow(), Fingerprint(snapshot, feed.Filter), items, diagnostics)
        {
            Groups = Groups(items),
            PublishedAt = catalogue.IssuedAt,
        };
    }

    /// <summary>
    /// Returns the (cached or freshly fetched) catalogue at
    /// <paramref name="catalogUri"/>, unfiltered and without sizes, for
    /// example to choose folders.
    /// </summary>
    /// <exception cref="HttpRequestException">The server could not be reached and nothing is cached.</exception>
    /// <exception cref="System.Xml.XmlException">The document is not an S-100 exchange catalogue.</exception>
    public async Task<RemoteS100Catalogue> GetCatalogueAsync(
        Uri catalogUri, bool forceRevalidate = false, CancellationToken cancellationToken = default)
    {
        var snapshot = await _cache.GetAsync(catalogUri, forceRevalidate, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ReadSnapshot(snapshot, catalogUri), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the files under <paramref name="folders"/> of
    /// <paramref name="catalogue"/> (folders relative to its root; empty for
    /// the whole set), keyed by URL — or returns <see langword="null"/> when
    /// the catalogue is not in an S3 bucket, so sizes cannot be listed.
    /// Listings are reused for a few minutes.
    /// </summary>
    /// <exception cref="HttpRequestException">The bucket could not be listed.</exception>
    public async Task<IReadOnlyDictionary<Uri, S3Object>?> ListAsync(
        RemoteS100Catalogue catalogue, IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(folders);
        if (!S3ObjectListing.TryParse(catalogue.RootUri, out _, out _))
            return null;

        var listings = await Task.WhenAll(folders
            .Select(catalogue.FolderUri)
            .Distinct()
            .Select(uri => ListFolderAsync(uri, cancellationToken))).ConfigureAwait(false);

        var objects = new Dictionary<Uri, S3Object>();
        foreach (var listing in listings)
        {
            foreach (var entry in listing)
                objects[entry.Uri] = entry;
        }

        return objects;
    }

    /// <summary>
    /// How the last attempt to fetch the catalogue at <paramref name="catalogUri"/>
    /// went; <see langword="null"/> before the first attempt.
    /// </summary>
    public FeedHealth? HealthOf(Uri catalogUri) => _cache.HealthOf(catalogUri);

    /// <summary>
    /// The managed folder, relative to the downloads root, that datasets of
    /// the catalogue at <paramref name="catalogUri"/> download into: one per
    /// catalogue, e.g. <c>catalogues/noaa-s102-pds.s3.amazonaws.com-3f2a9c1b</c>.
    /// </summary>
    public static string DownloadFolderFor(Uri catalogUri)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalogUri.AbsoluteUri)))[..8].ToLowerInvariant();
        var host = new string(catalogUri.Host.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray());
        return $"catalogues/{host}-{hash}";
    }

    /// <summary>
    /// The folders of <paramref name="items"/> as index groups, by region then
    /// name; a name used in two regions ("Newport") is qualified by its region.
    /// </summary>
    internal static IReadOnlyList<SourceIndexGroup> Groups(IEnumerable<CollectionItem> items)
    {
        var folders = items.Select(RemoteS100Catalogue.FolderOf)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => RemoteS100Catalogue.FolderName(RemoteS100Catalogue.TopFolder(f)), StringComparer.CurrentCulture)
            .ThenBy(RemoteS100Catalogue.FolderName, StringComparer.CurrentCulture)
            .ToArray();
        var repeated = folders.GroupBy(RemoteS100Catalogue.FolderName, StringComparer.CurrentCultureIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return folders.Select(f =>
        {
            var name = RemoteS100Catalogue.FolderName(f);
            var region = RemoteS100Catalogue.TopFolder(f);
            return new SourceIndexGroup(f, repeated.Contains(name) && region != f
                ? $"{name} ({RemoteS100Catalogue.FolderName(region)})"
                : name);
        }).ToArray();
    }

    private Task<IReadOnlyList<S3Object>> ListFolderAsync(Uri folderUri, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var entry = _listings.AddOrUpdate(
            folderUri,
            _ => (now, S3ObjectListing.ListAsync(_httpClient, folderUri, CancellationToken.None)),
            (_, existing) => now - existing.At < ListingLifetime && !existing.Listing.IsFaulted && !existing.Listing.IsCanceled
                ? existing
                : (now, S3ObjectListing.ListAsync(_httpClient, folderUri, CancellationToken.None)));
        return entry.Listing.WaitAsync(cancellationToken);
    }

    private static RemoteS100Catalogue ReadSnapshot(FeedSnapshot snapshot, Uri catalogUri)
    {
        using var stream = File.OpenRead(snapshot.FilePath);
        return RemoteS100CatalogueReader.Read(stream, catalogUri, DownloadFolderFor(catalogUri));
    }

    private static string Fingerprint(FeedSnapshot snapshot, S100CatalogueFilter filter) =>
        $"{FingerprintVersion}:{snapshot.Version}:{filter.ToCanonicalString()}";

    private static S100CatalogueFeedSource AsFeed(CollectionSource source) => source switch
    {
        S100CatalogueFeedSource feed => feed,
        null => throw new ArgumentNullException(nameof(source)),
        _ => throw new NotSupportedException($"{nameof(S100CatalogueFeedIndexer)} cannot index {source.GetType().Name}."),
    };
}
