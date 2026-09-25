using System.Globalization;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes a <see cref="NoaaEncFeedSource"/>: fetches (or revalidates) the
/// NOAA ENC product catalogue and turns each cell passing the source's
/// <see cref="NoaaEncFilter"/> into an online item carrying its coverage,
/// edition and download.
/// </summary>
/// <remarks>
/// <para>
/// The raw catalogue is cached on disk once per URL and shared by every NOAA
/// source, whatever its filter. See <see cref="FeedCacheOptions"/> for how
/// often the server is asked for changes. Nothing is downloaded but the
/// catalogue itself.
/// </para>
/// <para>
/// Items are <see cref="RemoteItemLocation"/>s. Whether a downloaded copy
/// exists locally is decided by the host at runtime, so the index does not
/// change when cells are downloaded.
/// </para>
/// </remarks>
public sealed class NoaaEncFeedIndexer : ICollectionSourceIndexer
{
    private const string FingerprintVersion = "noaa-v1";

    private readonly FeedCache _cache;

    /// <summary>Creates an indexer.</summary>
    /// <param name="httpClient">The client used to fetch catalogues.</param>
    /// <param name="cacheDirectory">Where raw catalogues are cached.</param>
    /// <param name="options">Cache options; defaults to <see cref="FeedCacheOptions.Default"/>.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public NoaaEncFeedIndexer(
        HttpClient httpClient,
        string cacheDirectory,
        FeedCacheOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _cache = new FeedCache(httpClient, cacheDirectory, options, timeProvider);
    }

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is NoaaEncFeedSource;

    /// <inheritdoc/>
    /// <remarks>
    /// Revalidates the cached catalogue when due (a cheap conditional
    /// request). When the server cannot be reached, the cached catalogue's
    /// version is returned, so an existing index is kept rather than rebuilt.
    /// </remarks>
    public async ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var feed = AsFeed(source);
        try
        {
            var snapshot = await _cache.GetAsync(feed.CatalogUri, forceRevalidate: false, cancellationToken)
                .ConfigureAwait(false);
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

        var items = await Task.Run(() =>
        {
            var catalog = NoaaEncProductCatalogReader.Read(snapshot.FilePath);
            return catalog.Cells.Where(feed.Filter.Matches).Select(Map).ToArray();
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report(new IndexProgress(items.Length, null));
        return new SourceIndex(source.Id, DateTimeOffset.UtcNow, Fingerprint(snapshot, feed.Filter), items, diagnostics);
    }

    /// <summary>
    /// Returns the (cached or freshly fetched) catalogue at
    /// <paramref name="catalogUri"/>, for example to present its
    /// <see cref="NoaaEncFacets"/> when choosing a filter.
    /// </summary>
    /// <param name="catalogUri">The catalogue URL.</param>
    /// <param name="forceRevalidate">Ask the server for changes even if the cached copy is recent.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="HttpRequestException">The server could not be reached and nothing is cached.</exception>
    public async Task<NoaaEncProductCatalog> GetCatalogAsync(
        Uri catalogUri, bool forceRevalidate = false, CancellationToken cancellationToken = default)
    {
        var snapshot = await _cache.GetAsync(catalogUri, forceRevalidate, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => NoaaEncProductCatalogReader.Read(snapshot.FilePath), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Maps one catalogue cell onto a neutral item.</summary>
    public static CollectionItem Map(NoaaEncCell cell)
    {
        var coverage = ToCoverage(cell.Panels);

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (cell.States.Count > 0)
            properties["states"] = string.Join(',', cell.States);
        if (cell.CoastGuardDistricts.Count > 0)
            properties["coastGuardDistricts"] = string.Join(',', cell.CoastGuardDistricts);
        if (cell.Regions.Count > 0)
            properties["regions"] = string.Join(',', cell.Regions);

        return new CollectionItem
        {
            Key = cell.Name,
            ProductSpec = "S-57",
            Name = cell.Name,
            Title = cell.LongName,
            Edition = cell.Edition,
            Update = cell.Update,
            IssueDate = cell.IssueDate,
            UpdateApplicationDate = cell.UpdateApplicationDate,
            CompilationScale = cell.CompilationScale,
            UsageBand = UsageBand.FromCellName(cell.Name),
            Status = cell.IsCancelled ? CollectionItemStatus.Cancelled : CollectionItemStatus.Active,
            Bounds = coverage?.ComputeBounds(),
            Coverage = coverage,
            Location = cell.ZipUri is { } uri
                ? new RemoteItemLocation(uri, cell.ZipSize, cell.ZipDateTime)
                : NoItemLocation.Instance,
            Properties = properties,
        };
    }

    /// <summary>
    /// Builds coverage from catalogue panels: exterior panels become polygons
    /// and each interior panel becomes a hole of the exterior containing it.
    /// </summary>
    private static GeoCoverage? ToCoverage(IReadOnlyList<NoaaEncPanel> panels)
    {
        var exteriors = panels.Where(p => !p.IsInterior).Select(p => p.Vertices).ToArray();
        if (exteriors.Length == 0)
            return null;

        var holes = exteriors.Select(_ => new List<IReadOnlyList<GeoPosition>>()).ToArray();
        foreach (var interior in panels.Where(p => p.IsInterior))
        {
            var owner = Array.FindIndex(exteriors, e => Contains(e, interior.Vertices[0]));
            holes[owner < 0 ? 0 : owner].Add(interior.Vertices);
        }

        return GeoCoverage.FromPolygons(exteriors.Select((ring, i) => new GeoPolygon(ring, holes[i])));
    }

    /// <summary>Even-odd ray-casting point-in-ring test in raw (continuous) coordinates.</summary>
    private static bool Contains(IReadOnlyList<GeoPosition> ring, GeoPosition point)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (latI, lonI) = ring[i];
            var (latJ, lonJ) = ring[j];
            if ((latI > point.Latitude) != (latJ > point.Latitude)
                && point.Longitude < (lonJ - lonI) * (point.Latitude - latI) / (latJ - latI) + lonI)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static string Fingerprint(FeedSnapshot snapshot, NoaaEncFilter filter) =>
        $"{FingerprintVersion}:{snapshot.Version}:{filter.ToCanonicalString()}";

    private static NoaaEncFeedSource AsFeed(CollectionSource source) => source switch
    {
        NoaaEncFeedSource feed => feed,
        null => throw new ArgumentNullException(nameof(source)),
        _ => throw new NotSupportedException($"{nameof(NoaaEncFeedIndexer)} cannot index {source.GetType().Name}."),
    };
}
