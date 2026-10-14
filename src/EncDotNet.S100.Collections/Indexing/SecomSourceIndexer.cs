using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Indexes a <see cref="SecomSource"/> (issue #804): reads the service's
/// <c>GetSummary</c> list, presenting the source's own identity when the
/// service asks for one (#883), and lists each data object as an online item
/// that downloads, through SECOM <c>Get</c>, into <see cref="DownloadFolderFor"/>.
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
/// source to an area to see the rest.
/// </para>
/// <para>
/// An exchange-set object (<c>containerType</c> 1, #916) is a package, as a
/// community-list entry is (§7.4): until it is downloaded it is one online
/// item without bounds; afterwards the current datasets of its exchange set
/// are indexed from the local copy and listed individually, each keeping the
/// object's <see cref="RemoteItemLocation"/>. Items are named from the
/// identifier without the generation timestamp some services append (AMSA's
/// <c>NAVAREA X_2026-10-13T04:47:23Z</c>), so names stay put across reads.
/// </para>
/// <para>
/// Each read resolves the source's <see cref="SecomSource.Identity"/> through
/// <see cref="Trust"/> and presents that identity only; a refused identity is
/// never followed by another (#883). What was presented, and the outcome, is
/// recorded on the index (<see cref="SourceIndex.Presentation"/>), with the
/// identities the source has seen refused, which are remembered per source.
/// </para>
/// </remarks>
public sealed partial class SecomSourceIndexer : ICollectionSourceIndexer
{
    // v2: hosts now probe downloaded GML objects (S-124, S-122) for bounds (#809);
    // indexes built before then had none, so they are rebuilt once.
    // v3: exchange-set objects are packages, and names drop identifier timestamps (#916).
    private const string FingerprintVersion = "secom-v3";

    /// <summary>The default cap on objects indexed per source.</summary>
    public const int DefaultMaxItems = 5_000;

    private static readonly CacheJsonContext CacheJson = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private readonly HttpClient _httpClient;
    private readonly string? _cacheDirectory;
    private readonly string? _downloadsRoot;
    private readonly DatasetProbe? _probe;
    private readonly LocalSourceIndexer _local;
    private readonly int _maxItems;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, Listing> _recent = new(StringComparer.Ordinal);
    private Dictionary<Guid, List<SecomRefusal>>? _refusals;

    /// <summary>
    /// Resolves each source's <see cref="SecomSource.Identity"/> to the MCP
    /// identity its requests present (the <see cref="HttpClient"/> should use a
    /// handler from the same instance) and sign SECOM 2.0 requests with (#838,
    /// #883). Without it, every source reads anonymously, and a service that
    /// lists only through signed requests fails with <see cref="SecomIdentityRequiredException"/>.
    /// </summary>
    public SecomServerTrust? Trust { get; init; }

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
        _local = new LocalSourceIndexer(probe);
        _maxItems = maxItems;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Checks the signers of downloaded objects for revocation (issue #833) when
    /// their <c>signature</c> property is made; <see langword="null"/> skips it
    /// and reads "revocation not checked".
    /// </summary>
    public SecomRevocation? Revocation { get; init; }

    /// <summary>How long a list read from a service is reused before it is read again.</summary>
    public static TimeSpan ReuseInterval { get; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc/>
    public bool CanIndex(CollectionSource source) => source is SecomSource;

    /// <inheritdoc/>
    public async ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken)
    {
        var secom = AsSecom(source);
        var (listing, attempt) = await TryListAsync(secom, cancellationToken).ConfigureAwait(false);
        return listing is { Stale: null } ? Fingerprint(secom, listing, attempt) : null;
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

        var (listing, attempt) = await TryListAsync(secom, cancellationToken).ConfigureAwait(false);
        var presentation = Remember(secom, attempt);
        if (listing is null)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Error, attempt.Failure ?? "The SECOM service could not be read.", serviceUri.AbsoluteUri)
            {
                Code = attempt.Presentation.Code,
            });
            return new SourceIndex(source.Id, _time.GetUtcNow(), null, [], diagnostics) { Presentation = presentation };
        }

        if (listing.Stale is { } reason)
        {
            diagnostics.Add(new IndexDiagnostic(
                IndexDiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture,
                    $"The service could not be reached ({reason}); listing what it offered at {listing.ReadAt:u}."),
                serviceUri.AbsoluteUri)
            {
                Code = attempt.Presentation.Code,
            });
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

        // Fingerprinted before the downloads are read: a download that lands
        // while this runs (a sync of an earlier index) then changes the next
        // fingerprint, so the re-index after it is not skipped as unchanged.
        var fingerprint = listing.Stale is null ? Fingerprint(secom, listing, attempt) : null;
        var folder = DownloadFolderFor(serviceUri);
        var downloader = _downloadsRoot is null ? null : new EncCellDownloader(_httpClient, Path.Combine(_downloadsRoot, folder)) { Revocation = Revocation };
        var client = new SecomClient(_httpClient, serviceUri, listing.Version) { UsesPostInterfaces = listing.Post };
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<CollectionItem>();
        // Name in a stable order, so a name made unique by suffix keeps its suffix across refreshes.
        foreach (var summary in listing.Items.OrderBy(s => s.DataReference, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = ToItem(summary, client, listing.Version, folder, names, secom.Identity);
            if (!secom.Filter.Matches(item))
                continue;
            if (downloader is null)
                items.Add(item);
            else if (item.Location is RemoteItemLocation { Package: not null })
                items.AddRange(await WithPackageAsync(item, downloader, diagnostics, cancellationToken).ConfigureAwait(false));
            else
                items.Add(WithDownload(item, downloader, diagnostics, cancellationToken));
            if (items.Count % 500 == 0)
                progress?.Report(new IndexProgress(items.Count, null));
        }

        progress?.Report(new IndexProgress(items.Count, null));
        return new SourceIndex(
            source.Id,
            _time.GetUtcNow(),
            fingerprint,
            items,
            diagnostics)
        {
            Presentation = presentation,
        };
    }

    /// <summary>
    /// Reads what the service at <paramref name="serviceUri"/> offers, for
    /// choosing a filter: object counts and sizes per product. The read is
    /// reused by an index of the same service shortly after.
    /// </summary>
    /// <param name="serviceUri">The service's endpoint URI.</param>
    /// <param name="geometryWkt">An area to narrow the read to, or <see langword="null"/>.</param>
    /// <param name="identity">The <see cref="SecomSource.Identity"/> setting to read with; the default when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="HttpRequestException">The service could not be read and nothing is cached.</exception>
    public async Task<SecomServiceDescription> DescribeAsync(
        Uri serviceUri, string? geometryWkt = null, string? identity = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        var source = new SecomSource(Guid.Empty, null, serviceUri, new SecomFilter { GeometryWkt = geometryWkt })
        {
            Identity = identity ?? SecomIdentitySetting.Default,
        };
        var (listing, attempt) = await TryListAsync(source, cancellationToken).ConfigureAwait(false);
        if (listing is null)
            throw new HttpRequestException(attempt.Failure ?? "The SECOM service could not be read.");
        var products = listing.Items
            .GroupBy(s => s.ProductSpec ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            // An exchange set's info_size is no byte count (AMSA gives 5–58, #916).
            .Select(g => new CatalogFacetValue(g.Key, g.Count(), g.Sum(s => s.ContainerType == SecomContainerType.ExchangeSet ? 0 : s.Size ?? 0)))
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
        SecomSummary summary, SecomClient client, SecomApiVersion version, string folder, ISet<string> names, string? identity = null)
    {
        var product = summary.ProductSpec ?? "Unknown";
        var identifier = WithoutTimestamp(summary.Identifier);
        var name = UniqueName(
            SafeName(identifier) ?? SafeName(Path.GetFileNameWithoutExtension(summary.Name)) ?? SafeName(summary.DataReference)!,
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

        var envelope = client.UsesPostInterfaces ? RemoteEnvelope.SecomPost : RemoteEnvelope.Secom;
        // Without a modification date, what the listing says of the object stands in for one.
        var revision = summary.LastModified is null ? Revision(summary) : null;
        // An exchange set is a package (#916): saved under its name, its datasets found
        // after download. Its info_size is no byte count (AMSA gives 5–58), so it is left out.
        var location = summary.ContainerType == SecomContainerType.ExchangeSet
            ? new RemoteItemLocation(
                client.GetObjectUri(summary.DataReference, version),
                LastModified: summary.LastModified,
                DownloadFolder: folder,
                Package: name,
                Envelope: envelope)
            {
                Identity = identity,
                Revision = revision,
            }
            : new RemoteItemLocation(
                client.GetObjectUri(summary.DataReference, version),
                summary.Size,
                summary.LastModified,
                folder,
                Layout: new PackageLayout(name + FileExtension(product, summary.Name), []),
                Envelope: envelope)
            {
                Identity = identity,
                Revision = revision,
            };

        return new CollectionItem
        {
            Key = summary.DataReference,
            ProductSpec = product,
            ProductSpecVersion = summary.ProductVersion,
            Name = name,
            Title = summary.Name is { Length: > 0 } title
                && !string.Equals(title, summary.Identifier, StringComparison.Ordinal)
                && !string.Equals(title, identifier, StringComparison.Ordinal)
                ? title
                // An exchange set's description lists its files (AMSA): no title.
                : summary.ContainerType != SecomContainerType.ExchangeSet && summary.Description is { Length: > 0 } && summary.Description.Length <= 80
                    ? summary.Description
                    : null,
            IssueDate = summary.LastModified is { } modified ? DateOnly.FromDateTime(modified.UtcDateTime) : null,
            Status = Status(summary.Status),
            Location = location,
            Properties = properties,
        };
    }

    /// <summary>
    /// The signature check as item details show it (#823): whether the data
    /// matches, whether the signer is trusted, and whether its certificate has
    /// expired, e.g. "valid · trusted (MCP MCC) · signer certificate expired".
    /// Trust and expiry are independent: an expired signer from a trusted root
    /// is still attributable to it.
    /// </summary>
    /// <remarks>
    /// For a trusted signer, revocation (#833) is shown between trust and
    /// expiry: " · signer certificate revoked", or " · revocation not checked"
    /// when no current CRL could be had. A signer found not revoked adds
    /// nothing, so the shortest string means every check passed.
    /// </remarks>
    internal static string Describe(SecomSignatureCheck signature)
    {
        if (signature.Status != SecomSignatureStatus.Valid)
        {
            return signature.Status switch
            {
                SecomSignatureStatus.Unsupported => "not checked (unknown algorithm)",
                SecomSignatureStatus.Encrypted => "not checked (data encrypted)",
                SecomSignatureStatus.Unsigned => "unsigned",
                _ => "invalid",
            };
        }

        var trust = signature.SignerTrusted switch
        {
            true => signature.TrustAnchor is { } anchor ? $"trusted ({anchor})" : "trusted",
            false => "signer not trusted",
            null => "signer trust not checked",
        };
        var revocation = signature.SignerTrusted is not true ? null : signature.SignerRevocation switch
        {
            SecomRevocationStatus.Revoked => " · signer certificate revoked",
            SecomRevocationStatus.NotChecked => " · revocation not checked",
            _ => null,
        };
        var expiry = signature.SignerExpired ? " · signer certificate expired" : null;
        return $"valid · {trust}{revocation}{expiry}";
    }

    /// <summary>Adds what the downloaded copy (if any) knows: bounds from the probe, and the signature check.</summary>
    private CollectionItem WithDownload(
        CollectionItem item, EncCellDownloader downloader, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        // Index runs are off the UI thread, so they may fetch the CRLs revocation needs.
        if (item.Location is not RemoteItemLocation || downloader.TryGetDownloaded(item.Name, fetchRevocation: true) is not { } downloaded)
            return item;

        var properties = new Dictionary<string, string>(item.Properties, StringComparer.Ordinal);
        if (downloaded.Signature is { } signature)
        {
            properties["signature"] = Describe(signature);
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
    /// Lists a downloaded exchange-set object's current datasets (#916), each
    /// keeping the object's location and properties, with the signature check;
    /// or the object itself while it is not downloaded, or when its set holds
    /// no current datasets (every one withdrawn).
    /// </summary>
    private async Task<IReadOnlyList<CollectionItem>> WithPackageAsync(
        CollectionItem item, EncCellDownloader downloader, List<IndexDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var remote = (RemoteItemLocation)item.Location;
        var package = remote.Package!;
        if (downloader.TryGetDownloaded(package, fetchRevocation: true) is not { IsPackage: true } downloaded)
            return [item];

        var properties = new Dictionary<string, string>(item.Properties, StringComparer.Ordinal) { ["package"] = package };
        if (item.Title is { } title)
            properties["packageTitle"] = title;
        if (downloaded.Signature is { } signature)
        {
            properties["signature"] = Describe(signature);
            if (signature.Signer is { } signer)
                properties["signer"] = signer;
        }

        if (downloaded.Datasets.Count == 0)
        {
            properties["contents"] = "no current datasets: every dataset in it is cancelled";
            return [item with { Properties = properties }];
        }

        var folder = Path.Combine(downloader.Root, package);
        var index = await _local.IndexAsync(new LocalFolderSource(Guid.Empty, null, folder), null, cancellationToken)
            .ConfigureAwait(false);
        diagnostics.AddRange(index.Diagnostics.Where(d => d.Severity != IndexDiagnosticSeverity.Info));

        var datasets = index.Items
            .Where(i => downloaded.Datasets.ContainsKey(i.Name))
            .DistinctBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => i with
            {
                Key = package + "/" + i.Name,
                GroupKey = package,
                Title = i.Title ?? item.Title,
                Status = item.Status == CollectionItemStatus.Unknown ? i.Status : item.Status,
                Location = remote,
                Properties = Merge(i.Properties, properties),
            })
            .ToArray();
        return datasets.Length > 0 ? datasets : [item with { Properties = properties }];

        static Dictionary<string, string> Merge(IReadOnlyDictionary<string, string> own, IReadOnlyDictionary<string, string> package)
        {
            var merged = new Dictionary<string, string>(package, StringComparer.Ordinal);
            foreach (var (key, value) in own)
                merged[key] = value;
            return merged;
        }
    }

    /// <summary>
    /// Reads the service's list with the source's identity (reusing a recent
    /// read with the same identity), falling back to the copy on disk;
    /// <see langword="null"/> when neither is available. The attempt says what
    /// was presented and how the read went.
    /// </summary>
    private async Task<(Listing? Listing, Attempt Attempt)> TryListAsync(SecomSource source, CancellationToken cancellationToken)
    {
        var serviceUri = SecomClient.NormalizeServiceUri(source.ServiceUri);
        var resolved = Trust?.Resolve(source.Identity) ?? SecomResolvedIdentity.None;
        var identity = resolved.Identity;
        // Anonymous reads keep the key they had before identities were per source.
        var identityKey = identity is null ? string.Empty : "|" + identity.Certificate.Thumbprint;
        var key = serviceUri.AbsoluteUri + "|" + (source.Filter.GeometryWkt?.Trim() ?? string.Empty) + identityKey;
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_recent.TryGetValue(key, out var recent) && recent.Stale is null && now - recent.ReadAt < ReuseInterval && _attempts.TryGetValue(key, out var reused))
                return (recent, reused with { Presentation = reused.Presentation with { Setting = resolved.Setting } });
        }

        var trust = Trust;
        var client = new SecomClient(_httpClient, serviceUri)
        {
            Identity = identity,
            Signer = identity is null || trust is null ? null : () => trust.CreateSigner(identity),
        };
        Listing listing;
        Attempt attempt;
        try
        {
            // 100 is SECOMLib's default page size; AMSA answers 400 to larger pages (#882).
            var query = new SecomQuery(GeometryWkt: source.Filter.GeometryWkt, PageSize: 100);
            var list = await client.GetSummariesAsync(query, _maxItems, cancellationToken).ConfigureAwait(false);
            listing = new Listing(list.Items, list.TotalItems, list.Truncated, client.ApiVersion ?? SecomApiVersion.V2, now)
            {
                Post = client.UsesPostInterfaces,
            };
            attempt = Attempted(serviceUri, resolved, client, failure: null, now);
            Save(key, listing);
        }
        catch (Exception ex) when (IsServiceFailure(ex, cancellationToken))
        {
            attempt = Attempted(serviceUri, resolved, client, ex, now);
            lock (_gate)
                _attempts[key] = attempt;
            if (Load(key) is not { } saved)
                return (null, attempt);
            listing = saved with { Stale = ex.Message };
        }

        lock (_gate)
        {
            _recent[key] = listing;
            _attempts[key] = attempt;
        }

        return (listing, attempt);
    }

    private readonly Dictionary<string, Attempt> _attempts = new(StringComparer.Ordinal);

    /// <summary>One read of a service: what was presented, the outcome, and why it failed (if it did).</summary>
    private sealed record Attempt(SecomPresentation Presentation, string? Failure);

    /// <summary>What a read presented and how it went, from the client and the handshake the trust saw.</summary>
    private Attempt Attempted(Uri serviceUri, SecomResolvedIdentity resolved, SecomClient client, Exception? failure, DateTimeOffset at)
    {
        var host = serviceUri.IdnHost;
        var identity = resolved.Identity;
        var handshake = Trust?.HandshakeFor(host, identity);
        var serverTrust = Trust?.ResultFor(host);
        var signed = client.SentSigned;
        var presented = signed || (identity is not null && (handshake?.CertificateRequested ?? true));
        var code = failure is null ? null : SecomDiagnosticCodes.Classify(failure, identity, signed, serverTrust, handshake);
        var outcome = failure is null
            ? presented ? SecomPresentationOutcome.Accepted
                : handshake?.CertificateRequested == true ? SecomPresentationOutcome.NotPresented : SecomPresentationOutcome.NotAsked
            : code switch
            {
                SecomDiagnosticCodes.ServerUntrusted => SecomPresentationOutcome.Stopped,
                SecomDiagnosticCodes.CertificateRefused or SecomDiagnosticCodes.SignedRequestRefused => SecomPresentationOutcome.Refused,
                SecomDiagnosticCodes.NeedsCertificate or SecomDiagnosticCodes.NeedsSignedRequest => SecomPresentationOutcome.NotPresented,
                _ => SecomPresentationOutcome.NotReached,
            };
        var shown = outcome is SecomPresentationOutcome.Accepted or SecomPresentationOutcome.Refused ? identity?.DisplayName : null;
        var presentation = new SecomPresentation(
            resolved.Setting, resolved.Id, shown, handshake?.CertificateRequested, handshake?.AcceptableIssuers ?? [], signed && identity is not null, outcome, at)
        {
            Code = code,
            ServerAnchor = serverTrust?.Anchor,
            ServerTrust = serverTrust?.Outcome,
        };
        return new Attempt(presentation, failure?.Message);
    }

    /// <summary>
    /// Remembers a refusal of the source's identity (and forgets one it has
    /// since been accepted with), and returns the attempt's presentation with
    /// every identity the source has seen refused.
    /// </summary>
    private SecomPresentation Remember(SecomSource source, Attempt attempt)
    {
        var presentation = attempt.Presentation;
        if (source.Id == Guid.Empty)
            return presentation;

        lock (_gate)
        {
            var all = _refusals ??= LoadRefusals();
            all.TryGetValue(source.Id, out var refusals);
            var changed = false;
            if (presentation.IdentityId is { } id)
            {
                if (presentation.Outcome == SecomPresentationOutcome.Refused)
                {
                    refusals ??= all[source.Id] = [];
                    refusals.RemoveAll(r => string.Equals(r.IdentityId, id, StringComparison.OrdinalIgnoreCase));
                    refusals.Add(new SecomRefusal(id, presentation.At, presentation.Signed));
                    changed = true;
                }
                else if (presentation.Outcome == SecomPresentationOutcome.Accepted && refusals is not null)
                {
                    changed = refusals.RemoveAll(r => string.Equals(r.IdentityId, id, StringComparison.OrdinalIgnoreCase)) > 0;
                }
            }

            if (changed)
                SaveRefusals(all);
            return refusals is { Count: > 0 } ? presentation with { Refusals = [.. refusals] } : presentation;
        }
    }

    private string? RefusalsPath => _cacheDirectory is null ? null : Path.Combine(_cacheDirectory, "secom-refusals.json");

    private Dictionary<Guid, List<SecomRefusal>> LoadRefusals()
    {
        try
        {
            if (RefusalsPath is { } path && File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllText(path), CacheJson.DictionaryGuidListSecomRefusal) ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Forget them: refusals are hints, and the next read records them again.
        }

        return [];
    }

    private void SaveRefusals(Dictionary<Guid, List<SecomRefusal>> refusals)
    {
        if (RefusalsPath is not { } path)
            return;
        try
        {
            Directory.CreateDirectory(_cacheDirectory!);
            var partial = path + ".partial";
            File.WriteAllText(partial, JsonSerializer.Serialize(refusals, CacheJson.DictionaryGuidListSecomRefusal));
            File.Move(partial, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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

    /// <summary>
    /// The listed objects, the filter, the identity read with (#883), and when
    /// each object of the service was last downloaded.
    /// </summary>
    private string Fingerprint(SecomSource source, Listing listing, Attempt attempt)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var summary in listing.Items.OrderBy(s => s.DataReference, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
                $"{summary.DataReference}|{summary.LastModified?.UtcTicks}|{summary.Size}|{summary.Status}|{Revision(summary)}\n")));
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
        var post = listing.Post ? "+post" : string.Empty;
        // The identity and its outcome, so a change of identity indexes again
        // even when the list is the same; anonymous reads keep their old fingerprint.
        var presentation = attempt.Presentation;
        var identity = presentation.IdentityId is { } id ? $"@{id}/{presentation.Outcome}" : string.Empty;
        return $"{FingerprintVersion}:{listing.Version}{post}{identity}:{listed}:{source.Filter.ToCanonicalString()}:{downloads}";
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
            // S-412 (weather warnings) is GML; S-413/S-414 are unpublished but expected to carry HDF5 grids.
            "S-102" or "S-104" or "S-111" or "S-413" or "S-414" => ".h5",
            "RTZ" => ".rtz",
            "Unknown" or "OTHER" => ".dat",
            _ => ".gml",
        };
    }

    /// <summary>
    /// A revision of an object for a service that gives no modification date
    /// (#916): a hash of what its summary says, without the identifier's
    /// generation timestamp. AMSA's description lists an exchange set's files,
    /// cancellations included, so it changes when a warning is issued or cancelled.
    /// </summary>
    internal static string Revision(SecomSummary summary)
    {
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{WithoutTimestamp(summary.Identifier)}|{summary.Name}|{summary.Description}|{summary.Size}|{summary.Status}|{summary.ProductVersion}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// The identifier without a trailing generation timestamp, which some services
    /// append on every read (AMSA: <c>NAVAREA X_2026-10-13T04:47:23.685467965Z</c>).
    /// </summary>
    internal static string? WithoutTimestamp(string? identifier)
    {
        if (identifier is null)
            return null;
        var match = TrailingTimestamp().Match(identifier);
        return match.Success && match.Index > 0 ? identifier[..match.Index] : identifier;
    }

    [GeneratedRegex(@"[_ ]\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingTimestamp();

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

        /// <summary>True when the service is listed and read through the signed SECOM 2.0 POST forms (#838).</summary>
        public bool Post { get; init; }
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(Listing))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<Guid, List<SecomRefusal>>))]
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
