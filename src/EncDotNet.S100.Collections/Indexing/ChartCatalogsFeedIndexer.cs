using System.Globalization;
using EncDotNet.S100.Collections.ChartCatalogs;
using EncDotNet.S100.Collections.Downloads;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes a <see cref="ChartCatalogsFeedSource"/>: fetches (or revalidates)
/// a community chart list and turns each selected entry into items.
/// </summary>
/// <remarks>
/// <para>
/// A community entry is a <em>package</em> — a download that may hold one
/// cell or many — and the lists carry no coverage. So until an entry is
/// downloaded it is a single online item without bounds; once it has been
/// downloaded (into <see cref="DownloadFolderFor"/> under the downloads root)
/// its cells are indexed from the local copy, with their bounds, editions and
/// updates, and listed individually. Every item keeps the package's
/// <see cref="RemoteItemLocation"/> (with <see cref="RemoteItemLocation.Package"/>
/// set) so hosts can resolve the local copy and offer re-downloads.
/// </para>
/// <para>
/// Caching follows <see cref="NoaaEncFeedIndexer"/>. The fingerprint also
/// covers the download state of the selected packages, so a download
/// re-indexes the source.
/// </para>
/// </remarks>
public sealed class ChartCatalogsFeedIndexer : ICollectionSourceIndexer
{
    private const string FingerprintVersion = "chartcatalogs-v1";

    private readonly FeedCache _cache;
    private readonly HttpClient _httpClient;
    private readonly string? _downloadsRoot;
    private readonly LocalSourceIndexer _local;

    /// <summary>Creates an indexer.</summary>
    /// <param name="httpClient">The client used to fetch lists.</param>
    /// <param name="cacheDirectory">Where raw lists are cached.</param>
    /// <param name="downloadsRoot">
    /// The host's downloads root, under which packages are saved in
    /// <see cref="DownloadFolderFor"/>; <see langword="null"/> when downloads
    /// are not indexed.
    /// </param>
    /// <param name="probe">Reads metadata (bounds) from downloaded loose cells.</param>
    /// <param name="options">Cache options; defaults to <see cref="FeedCacheOptions.Default"/>.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public ChartCatalogsFeedIndexer(
        HttpClient httpClient,
        string cacheDirectory,
        string? downloadsRoot = null,
        DatasetProbe? probe = null,
        FeedCacheOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _cache = new FeedCache(httpClient, cacheDirectory, options, timeProvider);
        _downloadsRoot = downloadsRoot;
        _local = new LocalSourceIndexer(probe);
    }

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is ChartCatalogsFeedSource;

    /// <inheritdoc/>
    public async ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var feed = AsFeed(source);
        try
        {
            var snapshot = await _cache.GetAsync(feed.CatalogUri, forceRevalidate: false, cancellationToken)
                .ConfigureAwait(false);
            var catalog = await Task.Run(() => ChartCatalogsProductCatalogReader.Read(snapshot.FilePath), cancellationToken)
                .ConfigureAwait(false);
            return Fingerprint(snapshot, feed, catalog);
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
            snapshot = await _cache.GetAsync(feed.CatalogUri, forceRevalidate: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, ex.Message, feed.CatalogUri.AbsoluteUri));
            return new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [], diagnostics);
        }

        if (snapshot.StaleReason is { } reason)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture,
                    $"Catalogue could not be refreshed ({reason}); using the copy from {snapshot.FetchedAt:u}."),
                feed.CatalogUri.AbsoluteUri));
        }

        var catalog = await Task.Run(() => ChartCatalogsProductCatalogReader.Read(snapshot.FilePath), cancellationToken)
            .ConfigureAwait(false);
        var folder = DownloadFolderFor(feed.CatalogUri);
        var downloader = Downloader(folder);

        var items = new List<CollectionItem>();
        foreach (var chart in Selected(catalog, feed.Filter))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var package = PackageName(chart);
            var remote = new RemoteItemLocation(chart.DownloadUri, null, chart.PublishedAt, folder, package);

            var cells = downloader?.TryGetDownloaded(package) is { IsPackage: true } downloaded
                ? await IndexDownloadedAsync(downloader, downloaded, chart, remote, diagnostics, cancellationToken)
                    .ConfigureAwait(false)
                : [];
            items.AddRange(cells.Count > 0 ? cells : [PackageItem(chart, remote)]);
            progress?.Report(new IndexProgress(items.Count, null));
        }

        return new SourceIndex(source.Id, DateTimeOffset.UtcNow, Fingerprint(snapshot, feed, catalog), items, diagnostics);
    }

    /// <summary>
    /// Returns the (cached or freshly fetched) list at
    /// <paramref name="catalogUri"/>, for example to choose entries.
    /// </summary>
    /// <exception cref="HttpRequestException">The server could not be reached and nothing is cached.</exception>
    public async Task<ChartCatalogsProductCatalog> GetCatalogAsync(
        Uri catalogUri, bool forceRevalidate = false, CancellationToken cancellationToken = default)
    {
        var snapshot = await _cache.GetAsync(catalogUri, forceRevalidate, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => ChartCatalogsProductCatalogReader.Read(snapshot.FilePath), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The managed folder, relative to the downloads root, that entries of
    /// the list at <paramref name="catalogUri"/> download into (e.g.
    /// <c>community/RO_IENC_Catalog</c>).
    /// </summary>
    public static string DownloadFolderFor(Uri catalogUri)
    {
        ArgumentNullException.ThrowIfNull(catalogUri);
        var stem = Path.GetFileNameWithoutExtension(catalogUri.AbsolutePath);
        return "community/" + Sanitize(string.IsNullOrEmpty(stem) ? catalogUri.Host : stem);
    }

    /// <summary>The name an entry's download is saved under: its number, made safe for the file system.</summary>
    public static string PackageName(ChartCatalogsChart chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return Sanitize(chart.Number);
    }

    /// <summary>The selected entries, one per package name (lists sometimes repeat an entry).</summary>
    public static IEnumerable<ChartCatalogsChart> Selected(ChartCatalogsProductCatalog catalog, ChartCatalogsFilter filter)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(filter);
        return catalog.Charts.Where(filter.Matches).DistinctBy(PackageName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The single online item for an entry that has not been downloaded.</summary>
    private static CollectionItem PackageItem(ChartCatalogsChart chart, RemoteItemLocation remote) => new()
    {
        Key = remote.Package!,
        ProductSpec = "S-57",
        Name = chart.Number,
        Title = chart.Title,
        IssueDate = chart.PublishedAt is { } published ? DateOnly.FromDateTime(published.UtcDateTime) : null,
        Status = CollectionItemStatus.Active,
        Location = remote,
        Properties = PackageProperties(new Dictionary<string, string>(), remote.Package!, null),
    };

    /// <summary>Indexes a downloaded package's cells, keeping the package's remote location.</summary>
    private async Task<IReadOnlyList<CollectionItem>> IndexDownloadedAsync(
        EncCellDownloader downloader,
        DownloadedCell downloaded,
        ChartCatalogsChart chart,
        RemoteItemLocation remote,
        List<IndexDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(downloader.Root, remote.Package!);
        var index = await _local.IndexAsync(new LocalFolderSource(Guid.Empty, null, folder), null, cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(index.Diagnostics.Where(d => d.Severity != IndexDiagnosticSeverity.Info));

        return index.Items
            .Where(i => downloaded.Datasets.ContainsKey(i.Name))
            .DistinctBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => i with
            {
                Key = remote.Package + "/" + i.Name,
                GroupKey = i.GroupKey is null ? null : remote.Package + "/" + i.GroupKey,
                Title = i.Title ?? chart.Title,
                Location = remote,
                Properties = PackageProperties(i.Properties, remote.Package!, chart.Title),
            })
            .ToArray();
    }

    private static Dictionary<string, string> PackageProperties(
        IReadOnlyDictionary<string, string> properties, string package, string? title)
    {
        var result = new Dictionary<string, string>(properties, StringComparer.Ordinal) { ["package"] = package };
        if (title is not null)
            result["packageTitle"] = title;
        return result;
    }

    private EncCellDownloader? Downloader(string folder) =>
        _downloadsRoot is null ? null : new EncCellDownloader(_httpClient, Path.Combine(_downloadsRoot, folder));

    /// <summary>The list version, filter, and when each selected package was last downloaded.</summary>
    private string Fingerprint(FeedSnapshot snapshot, ChartCatalogsFeedSource feed, ChartCatalogsProductCatalog catalog)
    {
        var downloads = string.Empty;
        if (_downloadsRoot is not null)
        {
            var root = Path.Combine(_downloadsRoot, DownloadFolderFor(feed.CatalogUri));
            downloads = string.Join(',', Selected(catalog, feed.Filter)
                .Select(PackageName)
                .Select(p => (Package: p, Record: new FileInfo(Path.Combine(root, p, EncCellDownloader.RecordFileName))))
                .Where(p => p.Record.Exists)
                .Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Package}@{p.Record.LastWriteTimeUtc.Ticks}")));
        }

        return $"{FingerprintVersion}:{snapshot.Version}:{feed.Filter.ToCanonicalString()}:{downloads}";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c)
            .ToArray();
        var safe = new string(chars).Trim('.', ' ');
        return safe.Length == 0 ? "_" : safe;
    }

    private static ChartCatalogsFeedSource AsFeed(CollectionSource source) => source switch
    {
        ChartCatalogsFeedSource feed => feed,
        null => throw new ArgumentNullException(nameof(source)),
        _ => throw new NotSupportedException($"{nameof(ChartCatalogsFeedIndexer)} cannot index {source.GetType().Name}."),
    };
}
