using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes a <see cref="SecomSource"/> (issue #804): reads the service's
/// <c>GetSummary</c> list anonymously and lists each data object as an
/// online item that downloads, through SECOM <c>Get</c>, into
/// <see cref="DownloadFolderFor"/>.
/// </summary>
/// <remarks>
/// <para>
/// Summaries carry no coverage, so an object has no bounds until it is
/// downloaded; afterwards its bounds are probed from the local copy (as for
/// community lists, §7.4 of the design note). The fingerprint covers the
/// listed objects and the download records, so a download re-indexes the source.
/// </para>
/// <para>
/// The last list read is kept in memory for a short time, so a fingerprint
/// check and the index that follows it share one round of requests, and on
/// disk, so an unreachable service still lists what it last offered (with a
/// warning). Large services are capped at <c>maxItems</c> objects; narrow the
/// source to an area to see the rest. Exchange-set objects are listed but not
/// downloadable yet.
/// </para>
/// </remarks>
public sealed partial class SecomSourceIndexer : ICollectionSourceIndexer
{
    private const string FingerprintVersion = "secom-v1";

    /// <summary>The default cap on objects indexed per source.</summary>
    public const int DefaultMaxItems = 5_000;

    private static readonly CacheJsonContext CacheJson = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private readonly HttpClient _httpClient;
    private readonly string? _cacheDirectory;
    private readonly string? _downloadsRoot;
    private readonly DatasetProbe? _probe;
    private readonly int _maxItems;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, Listing> _recent = new(StringComparer.Ordinal);

    /// <summary>Creates an indexer.</summary>
    /// <param name="httpClient">The client used to call services.</param>
    /// <param name="cacheDirectory">Where the last list of each service is kept; <see langword="null"/> for none.</param>
    /// <param name="downloadsRoot">
    /// The host's downloads root, under which objects are saved in
    /// <see cref="DownloadFolderFor"/>; <see langword="null"/> when downloads
    /// are not indexed.
    /// </param>
    /// <param name="probe">Reads metadata (bounds) from downloaded objects.</param>
    /// <param name="maxItems">The most objects indexed per source.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    public SecomSourceIndexer(
        HttpClient httpClient,
        string? cacheDirectory = null,
        string? downloadsRoot = null,
        DatasetProbe? probe = null,
        int maxItems = DefaultMaxItems,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, 1);
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
        _downloadsRoot = downloadsRoot;
        _probe = probe;
        _maxItems = maxItems;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>How long a list read from a service is reused before it is read again.</summary>
    public static TimeSpan ReuseInterval { get; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is SecomSource;

    /// <inheritdoc/>
    public async ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var secom = AsSecom(source);
        var listing = await TryListAsync(secom, cancellationToken).ConfigureAwait(false);
        return listing is { Stale: null } ? Fingerprint(secom, listing) : null;
    }

    /// <inheritdoc/>
    public async ValueTask<SourceIndex> IndexAsync(
        CollectionSource source,
        IProgress<IndexProgress>? progress,
        CancellationToken cancellationToken)
    {
        var secom = AsSecom(source);
        var serviceUri = SecomClient.NormalizeServiceUri(secom.ServiceUri);
        var diagnostics = new List<IndexDiagnostic>();
        progress?.Report(new IndexProgress(0, serviceUri.AbsoluteUri));

        var listing = await TryListAsync(secom, cancellationToken).ConfigureAwait(false);
        if (listing is null)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Error, LastError(serviceUri) ?? "The SECOM service could not be read.", serviceUri.AbsoluteUri));
            return new SourceIndex(source.Id, _time.GetUtcNow(), null, [], diagnostics);
        }

        if (listing.Stale is { } reason)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture,
                    $"The service could not be reached ({reason}); listing what it offered at {listing.ReadAt:u}."),
                serviceUri.AbsoluteUri));
        }

        if (listing.Truncated)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Warning,
                listing.TotalItems is { } total
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"The service offers {total} objects; only the first {listing.Items.Count} were listed. Narrow the source to an area to see the rest.")
                    : string.Create(CultureInfo.InvariantCulture,
                        $"Only the first {listing.Items.Count} objects were listed. Narrow the source to an area to see the rest."),
                serviceUri.AbsoluteUri));
        }

        var folder = DownloadFolderFor(serviceUri);
        var downloader = _downloadsRoot is null ? null : new EncCellDownloader(_httpClient, Path.Combine(_downloadsRoot, folder));
        var client = new SecomClient(_httpClient, serviceUri, listing.Version);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<CollectionItem>();
        var exchangeSets = 0;
        // Name in a stable order, so a name made unique by suffix keeps its suffix across refreshes.
        foreach (var summary in listing.Items.OrderBy(s => s.DataReference, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = ToItem(summary, client, listing.Version, folder, names);
            if (!secom.Filter.Matches(item))
                continue;
            if (summary.ContainerType == SecomContainerType.ExchangeSet)
                exchangeSets++;
            items.Add(downloader is null ? item : WithDownload(item, downloader, diagnostics, cancellationToken));
            if (items.Count % 500 == 0)
                progress?.Report(new IndexProgress(items.Count, null));
        }

        if (exchangeSets > 0)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Info,
                string.Create(CultureInfo.InvariantCulture,
                    $"{exchangeSets} exchange-set objects are listed but cannot be downloaded yet."),
                serviceUri.AbsoluteUri));
        }

        progress?.Report(new IndexProgress(items.Count, null));
        return new SourceIndex(
            source.Id,
            _time.GetUtcNow(),
            listing.Stale is null ? Fingerprint(secom, listing) : null,
            items,
            diagnostics);
    }

    /// <summary>
    /// Reads what the service at <paramref name="serviceUri"/> offers, for
    /// choosing a filter: object counts and sizes per product. The read is
    /// reused by an index of the same service shortly after.
    /// </summary>
    /// <param name="serviceUri">The service's endpoint URI.</param>
    /// <param name="geometryWkt">An area to narrow the read to, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="HttpRequestException">The service could not be read and nothing is cached.</exception>
    public async Task<SecomServiceDescription> DescribeAsync(
        Uri serviceUri, string? geometryWkt = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        var source = new SecomSource(Guid.Empty, null, serviceUri, new SecomFilter { GeometryWkt = geometryWkt });
        var listing = await TryListAsync(source, cancellationToken).ConfigureAwait(false)
            ?? throw new HttpRequestException(LastError(SecomClient.NormalizeServiceUri(serviceUri)) ?? "The SECOM service could not be read.");
        var products = listing.Items
            .GroupBy(s => s.ProductSpec ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .Select(g => new CatalogFacetValue(g.Key, g.Count(), g.Sum(s => s.Size ?? 0)))
            .OrderBy(f => f.Value, StringComparer.Ordinal)
            .ToArray();
        return new SecomServiceDescription(products, listing.Items.Count, listing.TotalItems, listing.Truncated, listing.Version);
    }

    /// <summary>
    /// The managed folder, relative to the downloads root, that objects of the
    /// service at <paramref name="serviceUri"/> download into: one per service,
    /// e.g. <c>secom/s124.ccg-gcc.gc.ca-3f2a9c1b</c>.
    /// </summary>
    public static string DownloadFolderFor(Uri serviceUri)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        var normalized = SecomClient.NormalizeServiceUri(serviceUri);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.AbsoluteUri)))[..8].ToLowerInvariant();
        var host = new string(normalized.Host.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray());
        return $"secom/{host}-{hash}";
    }

    /// <summary>Turns one summary entry into an item, giving it a file-system-safe name unique within the source.</summary>
    internal static CollectionItem ToItem(
        SecomSummary summary, SecomClient client, SecomApiVersion version, string folder, ISet<string> names)
    {
        var product = summary.ProductSpec ?? "Unknown";
        var name = UniqueName(
            SafeName(summary.Identifier) ?? SafeName(Path.GetFileNameWithoutExtension(summary.Name)) ?? SafeName(summary.DataReference)!,
            summary.DataReference,
            names);

        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dataReference"] = summary.DataReference,
            ["secomVersion"] = version == SecomApiVersion.V1 ? "1" : "2",
        };
        if (summary.Description is { Length: > 0 } description)
            properties["description"] = description;
        if (summary.Status is { Length: > 0 } status)
            properties["secomStatus"] = status;
        if (summary.DataProtection)
            properties["protection"] = "encrypted";
        if (summary.ContainerType == SecomContainerType.ExchangeSet)
            properties["containerType"] = "exchangeSet";

        ItemLocation location = summary.ContainerType == SecomContainerType.ExchangeSet
            ? NoItemLocation.Instance
            : new RemoteItemLocation(
                client.GetObjectUri(summary.DataReference, version),
                summary.Size,
                summary.LastModified,
                folder,
                Layout: new PackageLayout(name + FileExtension(product, summary.Name), []),
                Envelope: RemoteEnvelope.Secom);

        return new CollectionItem
        {
            Key = summary.DataReference,
            ProductSpec = product,
            ProductSpecVersion = summary.ProductVersion,
            Name = name,
            Title = summary.Name is { Length: > 0 } title && !string.Equals(title, summary.Identifier, StringComparison.Ordinal)
                ? title
                : summary.Description is { Length: > 0 } && summary.Description.Length <= 80 ? summary.Description : null,
            IssueDate = summary.LastModified is { } modified ? DateOnly.FromDateTime(modified.UtcDateTime) : null,
            Status = Status(summary.Status),
            Location = location,
            Properties = properties,
        };
    }

    /// <summary>Adds what the downloaded copy (if any) knows: bounds from the probe, and the signature check.</summary>
    private CollectionItem WithDownload(
        CollectionItem item, EncCellDownloader downloader, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        if (item.Location is not RemoteItemLocation || downloader.TryGetDownloaded(item.Name) is not { } downloaded)
            return item;

        var properties = new Dictionary<string, string>(item.Properties, StringComparer.Ordinal);
        if (downloaded.Signature is { } signature)
        {
            properties["signature"] = signature.Status switch
            {
                SecomSignatureStatus.Valid => signature.SignerExpired ? "valid (signer certificate expired)" : "valid",
                SecomSignatureStatus.Unsupported => "not checked (algorithm not available)",
                SecomSignatureStatus.Unsigned => "unsigned",
                _ => "invalid",
            };
            if (signature.Signer is { } signer)
                properties["signer"] = signer;
        }

        GeoBounds? bounds = null;
        if (_probe is not null)
        {
            var path = Path.Combine(downloaded.Location.RootPath, downloaded.Location.RelativePath);
            try
            {
                if (_probe(path, cancellationToken) is { Extent: { } extent, HorizontalCrsEpsg: null or 4326 })
                    bounds = new GeoBounds(extent.SouthLatitude, extent.WestLongitude, extent.NorthLatitude, extent.EastLongitude);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.Add(new IndexDiagnostic(IndexDiagnosticSeverity.Warning, ex.Message, path));
            }
        }

        return item with { Bounds = bounds ?? item.Bounds, Properties = properties };
    }

    /// <summary>
    /// Reads the service's list (reusing a recent read), falling back to the
    /// copy on disk; <see langword="null"/> when neither is available.
    /// </summary>
    private async Task<Listing?> TryListAsync(SecomSource source, CancellationToken cancellationToken)
    {
        var serviceUri = SecomClient.NormalizeServiceUri(source.ServiceUri);
        var key = serviceUri.AbsoluteUri + "|" + (source.Filter.GeometryWkt?.Trim() ?? string.Empty);
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_recent.TryGetValue(key, out var recent) && recent.Stale is null && now - recent.ReadAt < ReuseInterval)
                return recent;
        }

        Listing listing;
        try
        {
            var client = new SecomClient(_httpClient, serviceUri);
            var query = new SecomQuery(GeometryWkt: source.Filter.GeometryWkt, PageSize: 250);
            var list = await client.GetSummariesAsync(query, _maxItems, cancellationToken).ConfigureAwait(false);
            listing = new Listing(list.Items, list.TotalItems, list.Truncated, client.ApiVersion ?? SecomApiVersion.V2, now);
            Save(key, listing);
            lock (_gate)
                _errors.Remove(serviceUri.AbsoluteUri);
        }
        catch (Exception ex) when (IsServiceFailure(ex, cancellationToken))
        {
            lock (_gate)
                _errors[serviceUri.AbsoluteUri] = ex.Message;
            if (Load(key) is not { } saved)
                return null;
            listing = saved with { Stale = ex.Message };
        }

        lock (_gate)
            _recent[key] = listing;
        return listing;
    }

    private readonly Dictionary<string, string> _errors = new(StringComparer.Ordinal);

    private string? LastError(Uri serviceUri)
    {
        lock (_gate)
            return _errors.TryGetValue(serviceUri.AbsoluteUri, out var error) ? error : null;
    }

    private static bool IsServiceFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or InvalidDataException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private void Save(string key, Listing listing)
    {
        if (_cacheDirectory is null)
            return;
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = CachePath(key);
            var partial = path + ".partial";
            File.WriteAllText(partial, JsonSerializer.Serialize(listing, CacheJson.Listing));
            File.Move(partial, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The disk copy is a convenience; the live list stands.
        }
    }

    private Listing? Load(string key)
    {
        if (_cacheDirectory is null)
            return null;
        try
        {
            var path = CachePath(key);
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), CacheJson.Listing) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string CachePath(string key) =>
        Path.Combine(_cacheDirectory!, "secom-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant() + ".json");

    /// <summary>The listed objects, the filter, and when each object of the service was last downloaded.</summary>
    private string Fingerprint(SecomSource source, Listing listing)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var summary in listing.Items.OrderBy(s => s.DataReference, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
                $"{summary.DataReference}|{summary.LastModified?.UtcTicks}|{summary.Size}|{summary.Status}\n")));
        }

        var downloads = string.Empty;
        if (_downloadsRoot is not null)
        {
            var root = Path.Combine(_downloadsRoot, DownloadFolderFor(source.ServiceUri));
            try
            {
                if (Directory.Exists(root))
                {
                    downloads = string.Join(',', Directory.EnumerateDirectories(root)
                        .Select(d => new FileInfo(Path.Combine(d, EncCellDownloader.RecordFileName)))
                        .Where(f => f.Exists)
                        .OrderBy(f => f.FullName, StringComparer.Ordinal)
                        .Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.Directory!.Name}@{f.LastWriteTimeUtc.Ticks}")));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        var listed = Convert.ToHexString(hash.GetHashAndReset())[..16];
        return $"{FingerprintVersion}:{listing.Version}:{listed}:{source.Filter.ToCanonicalString()}:{downloads}";
    }

    private static CollectionItemStatus Status(string? status) => status?.Trim().ToUpperInvariant() switch
    {
        "PUBLISHED" or "ACTIVE" or "VALID" or "CURRENT" => CollectionItemStatus.Active,
        "CANCELLED" or "CANCELED" or "WITHDRAWN" or "DELETED" or "EXPIRED" => CollectionItemStatus.Cancelled,
        "SUPERSEDED" or "REPLACED" => CollectionItemStatus.Superseded,
        "PLANNED" or "DRAFT" => CollectionItemStatus.Planned,
        _ => CollectionItemStatus.Unknown,
    };

    /// <summary>The file extension a product's data is saved with: the service's own name's, else the product's usual one.</summary>
    private static string FileExtension(string product, string? serviceName)
    {
        var stated = Path.GetExtension(serviceName ?? string.Empty);
        if (stated.ToLowerInvariant() is ".gml" or ".xml" or ".h5" or ".hdf5" or ".000" or ".rtz" or ".zip")
            return stated.ToLowerInvariant();
        return product switch
        {
            "S-101" or "S-57" or "S-401" => ".000",
            "S-102" or "S-104" or "S-111" or "S-412" or "S-413" or "S-414" => ".h5",
            "RTZ" => ".rtz",
            "Unknown" or "OTHER" => ".dat",
            _ => ".gml",
        };
    }

    private static string UniqueName(string name, string dataReference, ISet<string> names)
    {
        if (names.Add(name))
            return name;
        var reference = SafeName(dataReference) ?? "x";
        var suffixed = name + "-" + reference[..Math.Min(8, reference.Length)];
        names.Add(suffixed);
        return suffixed;
    }

    /// <summary>A single file-system-safe name, or <see langword="null"/> when nothing usable is left.</summary>
    private static string? SafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : c)
            .ToArray()).Trim('.', ' ');
        return safe.Length == 0 ? null : safe.Length > 100 ? safe[..100] : safe;
    }

    private static SecomSource AsSecom(CollectionSource source) => source switch
    {
        SecomSource secom => secom,
        null => throw new ArgumentNullException(nameof(source)),
        _ => throw new NotSupportedException($"{nameof(SecomSourceIndexer)} cannot index {source.GetType().Name}."),
    };

    /// <summary>A service's list as read at <see cref="ReadAt"/>; <see cref="Stale"/> says why it was not refreshed.</summary>
    private sealed record Listing(
        IReadOnlyList<SecomSummary> Items, int? TotalItems, bool Truncated, SecomApiVersion Version, DateTimeOffset ReadAt)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public string? Stale { get; init; }
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(Listing))]
    private sealed partial class CacheJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
}

/// <summary>What a SECOM service offers, as read by <see cref="SecomSourceIndexer.DescribeAsync"/>.</summary>
/// <param name="Products">Object counts and sizes per product.</param>
/// <param name="ListedItems">How many objects were read.</param>
/// <param name="TotalItems">How many objects the service said it offers, if it said.</param>
/// <param name="Truncated">True when the service offers more objects than were read.</param>
/// <param name="ApiVersion">The SECOM interface version that answered.</param>
public sealed record SecomServiceDescription(
    IReadOnlyList<CatalogFacetValue> Products, int ListedItems, int? TotalItems, bool Truncated, SecomApiVersion ApiVersion);
