using System.Globalization;
using EncDotNet.S100.Collections.Usace;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes a <see cref="UsaceIencFeedSource"/>: fetches (or revalidates) a
/// USACE Inland ENC product catalogue and turns each cell on the selected
/// rivers into an online item with its bounding box, edition, update and
/// download.
/// </summary>
/// <remarks>
/// Caching follows <see cref="NoaaEncFeedIndexer"/>: one raw copy per URL,
/// revalidated with conditional requests, served from cache when offline.
/// USACE catalogues carry a bounding box per cell but no coverage polygon.
/// </remarks>
public sealed class UsaceIencFeedIndexer : ICollectionSourceIndexer
{
    private const string FingerprintVersion = "usace-v1";

    private readonly FeedCache _cache;

    /// <summary>Creates an indexer.</summary>
    /// <param name="httpClient">The client used to fetch catalogues.</param>
    /// <param name="cacheDirectory">Where raw catalogues are cached.</param>
    /// <param name="options">Cache options; defaults to <see cref="FeedCacheOptions.Default"/>.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public UsaceIencFeedIndexer(
        HttpClient httpClient,
        string cacheDirectory,
        FeedCacheOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _cache = new FeedCache(httpClient, cacheDirectory, options, timeProvider);
    }

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is UsaceIencFeedSource;

    /// <inheritdoc/>
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
            var catalog = UsaceIencProductCatalogReader.Read(snapshot.FilePath);
            return catalog.Cells.Where(feed.Filter.Matches).Select(Map).ToArray();
        }, cancellationToken).ConfigureAwait(false);

        progress?.Report(new IndexProgress(items.Length, null));
        return new SourceIndex(source.Id, DateTimeOffset.UtcNow, Fingerprint(snapshot, feed.Filter), items, diagnostics);
    }

    /// <summary>
    /// Returns the (cached or freshly fetched) catalogue at
    /// <paramref name="catalogUri"/>, for example to list its rivers when
    /// choosing a filter.
    /// </summary>
    /// <exception cref="HttpRequestException">The server could not be reached and nothing is cached.</exception>
    public async Task<UsaceIencProductCatalog> GetCatalogAsync(
        Uri catalogUri, bool forceRevalidate = false, CancellationToken cancellationToken = default)
    {
        var snapshot = await _cache.GetAsync(catalogUri, forceRevalidate, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() => UsaceIencProductCatalogReader.Read(snapshot.FilePath), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Summarises the rivers in <paramref name="catalog"/> (cell counts and
    /// download sizes), for choosing a <see cref="UsaceIencFilter"/>.
    /// </summary>
    public static IReadOnlyList<CatalogFacetValue> Rivers(UsaceIencProductCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return catalog.Cells
            .Where(c => c.River is not null)
            .GroupBy(c => c.River!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CatalogFacetValue(g.Key, g.Count(), g.Sum(c => c.ZipSize ?? 0)))
            .OrderBy(f => f.Value, StringComparer.CurrentCulture)
            .ToArray();
    }

    /// <summary>Maps one catalogue cell onto a neutral item.</summary>
    public static CollectionItem Map(UsaceIencCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (cell.River is { } river)
            properties["river"] = river;
        if (cell.RiverMileBegin is { } begin && cell.RiverMileEnd is { } end)
            properties["riverMiles"] = string.Create(CultureInfo.InvariantCulture, $"{begin:0.#}–{end:0.#}");

        return new CollectionItem
        {
            Key = cell.Name,
            ProductSpec = "S-57",
            Name = cell.Name,
            Title = Title(cell),
            Edition = cell.Edition,
            Update = cell.Update,
            IssueDate = cell.PostedOn,
            Status = CollectionItemStatus.Active,
            Bounds = cell.Bounds,
            Location = cell.ZipUri is { } uri
                ? new RemoteItemLocation(uri, cell.ZipSize, cell.PostedOn?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                : NoItemLocation.Instance,
            Properties = properties,
        };
    }

    /// <summary>"Pittsburgh, PA → Allegheny Lock No. 8 (Allegheny, mi 1–46)".</summary>
    private static string? Title(UsaceIencCell cell)
    {
        var span = (cell.From, cell.To) switch
        {
            ({ } from, { } to) => $"{from} → {to}",
            ({ } from, null) => from,
            (null, { } to) => to,
            _ => null,
        };
        var miles = cell.RiverMileBegin is { } b && cell.RiverMileEnd is { } e
            ? string.Create(CultureInfo.InvariantCulture, $"mi {b:0.#}–{e:0.#}")
            : null;
        var detail = string.Join(", ", new[] { cell.River, miles }.OfType<string>());

        return (span, detail.Length) switch
        {
            (null, 0) => null,
            (null, _) => detail,
            (_, 0) => span,
            _ => $"{span} ({detail})",
        };
    }

    private static string Fingerprint(FeedSnapshot snapshot, UsaceIencFilter filter) =>
        $"{FingerprintVersion}:{snapshot.Version}:{filter.ToCanonicalString()}";

    private static UsaceIencFeedSource AsFeed(CollectionSource source) => source switch
    {
        UsaceIencFeedSource feed => feed,
        null => throw new ArgumentNullException(nameof(source)),
        _ => throw new NotSupportedException($"{nameof(UsaceIencFeedIndexer)} cannot index {source.GetType().Name}."),
    };
}
