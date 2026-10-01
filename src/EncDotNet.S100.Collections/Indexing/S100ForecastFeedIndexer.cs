using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EncDotNet.S100.Collections.RemoteCatalogues;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>The latest run of one forecast model, summarised for choosing models.</summary>
/// <param name="Model">The model.</param>
/// <param name="Run">The run's time, or <see langword="null"/> when the catalogue could not be read.</param>
/// <param name="TileCount">How many tiles the run has.</param>
/// <param name="TileBytes">The run's tiles' total size, when listed.</param>
/// <param name="RegionalBytes">The size of the run's one-file-per-model download, when listed.</param>
/// <param name="Bounds">The model's domain (the union of its tiles' bounds).</param>
/// <param name="Error">Why the model could not be read, if it could not.</param>
public sealed record ForecastModelSummary(
    ForecastModel Model, DateTimeOffset? Run, int TileCount, long? TileBytes, long? RegionalBytes, GeoBounds? Bounds, string? Error = null)
{
    /// <summary>The bounds of each of the run's tiles (its actual footprint).</summary>
    public IReadOnlyList<GeoBounds> TileBounds { get; init; } = [];

    /// <summary>
    /// True when this model's tiles cover most of <paramref name="other"/>'s
    /// (at least half of its tiles' centres): a regional model such as
    /// <c>rtofs_east</c> over <c>cbofs</c>, not two neighbours whose boxes touch.
    /// </summary>
    public bool Covers(ForecastModelSummary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other) || TileBounds.Count == 0 || other.TileBounds.Count == 0)
            return false;
        var inside = other.TileBounds.Count(t =>
        {
            var lat = (t.South + t.North) / 2;
            var lon = t.West + t.LongitudeSpan / 2;
            return TileBounds.Any(b => b.Contains(new EncDotNet.S100.DataModel.GeoPosition(lat, lon)));
        });
        return inside * 2 >= other.TileBounds.Count;
    }
}

/// <summary>
/// Indexes an <see cref="S100ForecastFeedSource"/> (#685) — for example NOAA's
/// S-111 surface currents on AWS — by reading each chosen model's catalogue,
/// which lists only its latest run.
/// </summary>
/// <remarks>
/// <para>
/// Each run is stamped on its items: <see cref="RunProperty"/>,
/// <see cref="ValidToProperty"/> (run time plus the model's horizon) and, as
/// <see cref="RemoteItemLocation.LastModified"/>, the run time — runs have no
/// editions, so a downloaded run is outdated once the catalogue lists a later
/// one (<see cref="Downloads.DownloadedCell.IsOlderThan"/>). Items keep their
/// identity across runs (the run time is dropped from their names), so a new
/// run's download replaces the previous one.
/// </para>
/// <para>
/// Catalogues are cached like <see cref="S100CatalogueFeedIndexer"/>'s; a
/// model whose catalogue cannot be read is reported and skipped. When the feed
/// is in an S3 bucket, each run's folder is listed for sizes.
/// </para>
/// </remarks>
public sealed partial class S100ForecastFeedIndexer : ICollectionSourceIndexer
{
    private const string FingerprintVersion = "s100fc-v1";

    /// <summary>The item property naming its model (e.g. <c>cbofs</c>).</summary>
    public const string ModelProperty = "model";

    /// <summary>The item property holding its run time (ISO 8601, UTC).</summary>
    public const string RunProperty = "run";

    /// <summary>The item property holding the end of its run's valid window (ISO 8601, UTC).</summary>
    public const string ValidToProperty = "validTo";

    /// <summary>
    /// The default revalidation interval: one minute. A model's catalogue is
    /// overwritten with every run, so a Refresh ("Check for new runs") must
    /// actually ask; an unchanged catalogue answers with a cheap 304. Hosts
    /// refresh only on start and on request, never in the background.
    /// </summary>
    public static FeedCacheOptions DefaultCacheOptions { get; } = new() { RevalidationInterval = TimeSpan.FromMinutes(1) };

    private readonly HttpClient _httpClient;
    private readonly FeedCache _cache;
    private readonly TimeProvider _time;

    /// <summary>Creates an indexer.</summary>
    /// <param name="httpClient">The client used to fetch catalogues and list buckets.</param>
    /// <param name="cacheDirectory">Where raw catalogues are cached.</param>
    /// <param name="options">Cache options; defaults to <see cref="DefaultCacheOptions"/>.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public S100ForecastFeedIndexer(
        HttpClient httpClient,
        string cacheDirectory,
        FeedCacheOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _time = timeProvider ?? TimeProvider.System;
        _cache = new FeedCache(httpClient, cacheDirectory, options ?? DefaultCacheOptions, _time);
    }

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is S100ForecastFeedSource;

    /// <inheritdoc/>
    public async ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var feed = AsFeed(source);
        var versions = new List<string>(feed.Models.Count);
        foreach (var model in feed.Models)
        {
            try
            {
                var snapshot = await _cache.GetAsync(CatalogUri(feed.ModelsUri, model), forceRevalidate: false, cancellationToken)
                    .ConfigureAwait(false);
                versions.Add(snapshot.Version);
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }

        return Fingerprint(feed, versions);
    }

    /// <inheritdoc/>
    public async ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        var feed = AsFeed(source);
        var diagnostics = new List<IndexDiagnostic>();
        var items = new List<CollectionItem>();
        var versions = new List<string>();
        DateTimeOffset? latest = null;

        foreach (var model in feed.Models)
        {
            var catalogUri = CatalogUri(feed.ModelsUri, model);
            progress?.Report(new IndexProgress(items.Count, catalogUri.AbsoluteUri));
            FeedSnapshot snapshot;
            try
            {
                snapshot = await _cache.GetAsync(catalogUri, forceRevalidate: false, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, ex.Message, catalogUri.AbsoluteUri));
                continue;
            }

            versions.Add(snapshot.Version);
            if (snapshot.StaleReason is { } reason)
            {
                diagnostics.Add(new IndexDiagnostic(
                    IndexDiagnosticSeverity.Warning,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Catalogue could not be refreshed ({reason}); using the copy from {snapshot.FetchedAt:u}."),
                    catalogUri.AbsoluteUri));
            }

            RemoteS100Catalogue catalogue;
            try
            {
                catalogue = await Task.Run(() => Read(snapshot, catalogUri, feed.ModelsUri), cancellationToken).ConfigureAwait(false);
            }
            catch (System.Xml.XmlException ex)
            {
                diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Error, ex.Message, catalogUri.AbsoluteUri));
                continue;
            }

            var run = ToRun(model, catalogue.Items, feed.Shape);
            if (run.Items.Count == 0)
                continue;

            IReadOnlyList<CollectionItem> runItems = run.Items;
            try
            {
                if (await ListRunAsync(run.FolderUri, cancellationToken).ConfigureAwait(false) is { } sizes)
                    runItems = WithSizes(runItems, sizes);
            }
            catch (HttpRequestException ex)
            {
                diagnostics.Add(new IndexDiagnostic(
                    IndexDiagnosticSeverity.Info, $"Download sizes are unknown: {ex.Message}", run.FolderUri.AbsoluteUri));
            }

            items.AddRange(runItems);
            if (run.Time is { } time && (latest is null || time > latest))
                latest = time;
        }

        progress?.Report(new IndexProgress(items.Count, null));
        var fingerprint = versions.Count == feed.Models.Count ? Fingerprint(feed, versions) : null;
        return new SourceIndex(source.Id, _time.GetUtcNow(), fingerprint, items, diagnostics) { PublishedAt = latest };
    }

    /// <summary>
    /// Summarises the latest run of each of <paramref name="models"/> under
    /// <paramref name="modelsUri"/> (run time, tiles, sizes and domain), for
    /// choosing models; a model that cannot be read carries its error.
    /// </summary>
    public async Task<IReadOnlyList<ForecastModelSummary>> GetModelsAsync(
        Uri modelsUri, IReadOnlyList<ForecastModel> models, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modelsUri);
        ArgumentNullException.ThrowIfNull(models);

        return await Task.WhenAll(models.Select(async model =>
        {
            var catalogUri = CatalogUri(modelsUri, model);
            try
            {
                var snapshot = await _cache.GetAsync(catalogUri, forceRevalidate: false, cancellationToken).ConfigureAwait(false);
                var catalogue = await Task.Run(() => Read(snapshot, catalogUri, modelsUri), cancellationToken).ConfigureAwait(false);
                var tiles = ToRun(model, catalogue.Items, ForecastShape.Tiles);
                long? tileBytes = null, regionalBytes = null;
                try
                {
                    if (await ListRunAsync(tiles.FolderUri, cancellationToken).ConfigureAwait(false) is { } sizes)
                    {
                        tileBytes = WithSizes(tiles.Items, sizes).Sum(i => ((RemoteItemLocation)i.Location).SizeBytes ?? 0);
                        var regional = ToRun(model, catalogue.Items, ForecastShape.Regional).Items;
                        regionalBytes = WithSizes(regional, sizes).Sum(i => ((RemoteItemLocation)i.Location).SizeBytes ?? 0);
                    }
                }
                catch (HttpRequestException)
                {
                    // Sizes stay unknown.
                }

                return new ForecastModelSummary(model, tiles.Time, tiles.Items.Count, tileBytes, regionalBytes,
                    GeoBounds.UnionAll(tiles.Items.Select(i => i.Bounds).OfType<GeoBounds>()))
                {
                    TileBounds = tiles.Items.Select(i => i.Bounds).OfType<GeoBounds>().ToArray(),
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException)
            {
                return new ForecastModelSummary(model, null, 0, null, null, null, ex.Message);
            }
        })).ConfigureAwait(false);
    }

    /// <summary>
    /// How the last attempt to reach <paramref name="feed"/>'s catalogues
    /// went: the first failing model's, else the most recent check;
    /// <see langword="null"/> before the first attempt.
    /// </summary>
    public FeedHealth? HealthOf(S100ForecastFeedSource feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var health = feed.Models.Select(m => _cache.HealthOf(CatalogUri(feed.ModelsUri, m))).OfType<FeedHealth>().ToArray();
        return health.FirstOrDefault(h => !h.IsReachable) ?? health.MaxBy(h => h.CheckedAt);
    }

    /// <summary>The run time of a forecast item, from <see cref="RunProperty"/>.</summary>
    public static DateTimeOffset? RunOf(CollectionItem item) => ParseTime(item, RunProperty);

    /// <summary>The end of a forecast item's valid window, from <see cref="ValidToProperty"/>.</summary>
    public static DateTimeOffset? ValidToOf(CollectionItem item) => ParseTime(item, ValidToProperty);

    /// <summary>
    /// The managed folder, relative to the downloads root, that a feed's runs
    /// download into: one per feed, e.g. <c>forecasts/noaa-s111-pds.s3.amazonaws.com-3f2a9c1b</c>.
    /// </summary>
    public static string DownloadFolderFor(Uri modelsUri)
    {
        ArgumentNullException.ThrowIfNull(modelsUri);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modelsUri.AbsoluteUri)))[..8].ToLowerInvariant();
        var host = new string(modelsUri.Host.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray());
        return $"forecasts/{host}-{hash}";
    }

    /// <summary>The catalogue of <paramref name="model"/> under <paramref name="modelsUri"/>.</summary>
    public static Uri CatalogUri(Uri modelsUri, ForecastModel model)
    {
        ArgumentNullException.ThrowIfNull(modelsUri);
        ArgumentNullException.ThrowIfNull(model);
        var folder = modelsUri.AbsoluteUri.EndsWith('/') ? modelsUri : new Uri(modelsUri.AbsoluteUri + "/");
        return new Uri(folder, Uri.EscapeDataString(model.Id) + "/CATALOG.XML");
    }

    /// <summary>
    /// Turns a model catalogue's items into its run: tiles (as listed) or one
    /// item for the model's whole-domain file beside them, stamped with the
    /// run time and valid window.
    /// </summary>
    internal static (DateTimeOffset? Time, Uri FolderUri, IReadOnlyList<CollectionItem> Items) ToRun(
        ForecastModel model, IReadOnlyList<CollectionItem> tiles, ForecastShape shape)
    {
        var located = tiles
            .Select(t => (Item: t, Remote: (RemoteItemLocation)t.Location, Run: RunFromFileName(((RemoteItemLocation)t.Location).Uri)))
            .Where(t => t.Run is not null)
            .ToArray();
        if (located.Length == 0)
            return (null, new Uri("about:blank"), []);

        var run = located.Max(t => t.Run!.Value);
        located = located.Where(t => t.Run == run).ToArray();
        var validTo = run.AddHours(model.HorizonHours);
        // The run's folder: <run>/dcf2/ above its tiles/ (and regional/) folders.
        var folderUri = new Uri(located[0].Remote.Uri, "../");

        Dictionary<string, string> Stamp(CollectionItem item) => new(item.Properties
            .Where(p => p.Key is not (LocalManifestIndexer.GroupProperty or LocalManifestIndexer.GroupNameProperty)), StringComparer.Ordinal)
        {
            [ModelProperty] = model.Id,
            [RunProperty] = run.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            [ValidToProperty] = validTo.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };

        if (shape == ForecastShape.Tiles)
        {
            var items = located.Select(t => t.Item with
            {
                Key = model.Id + "/" + t.Item.Name,
                Title = model.Name,
                Edition = null,
                Update = null,
                IssueDate = DateOnly.FromDateTime(run.UtcDateTime),
                Location = t.Remote with { LastModified = run },
                Properties = Stamp(t.Item),
            }).ToArray();
            return (run, folderUri, items);
        }

        // One file per model: <run>/dcf2/regional/<product>_<MODEL>_<run>Z.h5, covering every tile.
        var first = located[0];
        var stem = Path.GetFileNameWithoutExtension(first.Remote.Uri.LocalPath);
        var regionalName = TileSuffix().Replace(stem, string.Empty);
        var regionalUri = new Uri(folderUri, "regional/" + Uri.EscapeDataString(regionalName + ".h5"));
        var coverage = GeoCoverage.FromPolygons(located.SelectMany(t => t.Item.Coverage?.Polygons ?? []));
        var regional = first.Item with
        {
            Key = model.Id,
            Name = RemoteS100CatalogueReader.StableName(regionalName),
            Title = model.Name,
            Edition = null,
            Update = null,
            IssueDate = DateOnly.FromDateTime(run.UtcDateTime),
            Bounds = GeoBounds.UnionAll(located.Select(t => t.Item.Bounds).OfType<GeoBounds>()),
            Coverage = coverage,
            Location = first.Remote with
            {
                Uri = regionalUri,
                SizeBytes = null,
                LastModified = run,
                Layout = new PackageLayout(regionalName + ".h5", []),
            },
            Properties = Stamp(first.Item),
        };
        return (run, folderUri, [regional]);
    }

    /// <summary>The run time in a forecast file name (<c>…_20260930T18Z…</c>), or <see langword="null"/>.</summary>
    internal static DateTimeOffset? RunFromFileName(Uri uri)
    {
        var match = RunToken().Match(Path.GetFileName(uri.LocalPath));
        return match.Success
            && DateTimeOffset.TryParseExact(match.Groups[1].Value, "yyyyMMdd'T'HH", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var run)
            ? run
            : null;
    }

    private async Task<IReadOnlyDictionary<Uri, S3Object>?> ListRunAsync(Uri runFolder, CancellationToken cancellationToken)
    {
        if (!S3ObjectListing.TryParse(runFolder, out _, out _))
            return null;
        var objects = await S3ObjectListing.ListAsync(_httpClient, runFolder, cancellationToken).ConfigureAwait(false);
        return objects.ToDictionary(o => o.Uri);
    }

    /// <summary>Applies listed sizes, keeping each item's run time as its publication date.</summary>
    private static IReadOnlyList<CollectionItem> WithSizes(IEnumerable<CollectionItem> items, IReadOnlyDictionary<Uri, S3Object> objects) =>
        items.Select(i => i.Location is RemoteItemLocation remote && objects.TryGetValue(remote.Uri, out var listed)
                ? i with { Location = remote with { SizeBytes = listed.SizeBytes } }
                : i)
            .ToArray();

    private static RemoteS100Catalogue Read(FeedSnapshot snapshot, Uri catalogUri, Uri modelsUri)
    {
        using var stream = File.OpenRead(snapshot.FilePath);
        return RemoteS100CatalogueReader.Read(stream, catalogUri, DownloadFolderFor(modelsUri));
    }

    private static DateTimeOffset? ParseTime(CollectionItem item, string property)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Properties.TryGetValue(property, out var text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
                ? time
                : null;
    }

    private static string Fingerprint(S100ForecastFeedSource feed, IEnumerable<string> versions) =>
        $"{FingerprintVersion}:{feed.Shape}:{string.Join(',', feed.Models.Select(m => $"{m.Id}/{m.HorizonHours}"))}:{string.Join('|', versions)}";

    private static S100ForecastFeedSource AsFeed(CollectionSource source) => source switch
    {
        S100ForecastFeedSource feed => feed,
        null => throw new ArgumentNullException(nameof(source)),
        _ => throw new NotSupportedException($"{nameof(S100ForecastFeedIndexer)} cannot index {source.GetType().Name}."),
    };

    /// <summary>A run-time token such as <c>_20260930T18Z</c>.</summary>
    [GeneratedRegex(@"_(\d{8}T\d{2})Z", RegexOptions.CultureInvariant)]
    private static partial Regex RunToken();

    /// <summary>A tile's cell suffix after the run time (<c>_US4VA1DD</c>).</summary>
    [GeneratedRegex(@"_[A-Z]{2}\d[A-Z0-9]{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex TileSuffix();
}
