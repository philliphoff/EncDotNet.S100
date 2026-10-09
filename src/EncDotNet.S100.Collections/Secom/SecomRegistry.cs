using System.Globalization;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>The lifecycle status a service instance is registered with.</summary>
public enum SecomRegistryStatus
{
    /// <summary>The registry states no status we recognise.</summary>
    Unknown = 0,

    /// <summary>Registered as released (in service).</summary>
    Released,

    /// <summary>Registered as provisional (test or pre-release).</summary>
    Provisional,
}

/// <summary>
/// A SECOM service instance listed in a Maritime Service Registry (issue
/// #822), cleaned: a usable endpoint, a canonical product, and bounds repaired
/// from the registered geometry.
/// </summary>
/// <param name="InstanceId">The instance's MRN.</param>
/// <param name="Name">The registered name, trimmed.</param>
/// <param name="Version">The registered version.</param>
/// <param name="Status">The registered lifecycle status.</param>
/// <param name="ProductSpec">The data product in canonical short form (<c>"S-124"</c>), or <c>"OTHER"</c>.</param>
/// <param name="EndpointUri">The service's SECOM endpoint.</param>
/// <param name="OrganizationId">The registering organisation's MRN, if stated.</param>
/// <param name="Description">The registered comment or description, if any.</param>
/// <param name="Keywords">The registered keywords, if any.</param>
/// <param name="Bounds">
/// The area the service covers, from its registered geometry — longitudes
/// registered off by whole turns are shifted back — or <see langword="null"/>
/// when the geometry is missing or unusable.
/// </param>
public sealed record SecomRegistryService(
    string InstanceId,
    string Name,
    string? Version,
    SecomRegistryStatus Status,
    string ProductSpec,
    Uri EndpointUri,
    string? OrganizationId,
    string? Description,
    string? Keywords,
    GeoBounds? Bounds)
{
    /// <summary>True when the product is an S-100 (or S-57-family) data product, not <c>OTHER</c>, RTZ or EPC.</summary>
    public bool IsS100Product => ProductSpec.StartsWith("S-", StringComparison.Ordinal);

    /// <summary>
    /// A short name for the registering organisation: the last part of its MRN
    /// (<c>urn:mrn:mcp:org:mcc:ccg</c> → <c>ccg</c>), or <see langword="null"/>.
    /// </summary>
    public string? OrganizationName => OrganizationId?.Split(':', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts
        ? parts[^1]
        : null;
}

/// <summary>A registry listing: the usable services, and what was dropped.</summary>
/// <param name="Services">The usable services, released first.</param>
/// <param name="Listed">How many instances the registry listed.</param>
/// <param name="Dropped">How many were dropped (deleted, unusable endpoint, duplicate).</param>
/// <param name="FetchedAt">When the listing was fetched from the registry.</param>
/// <param name="Stale">Why the registry could not be refreshed when a cached copy was served; otherwise <see langword="null"/>.</param>
public sealed record SecomRegistryListing(
    IReadOnlyList<SecomRegistryService> Services, int Listed, int Dropped, DateTimeOffset FetchedAt, string? Stale = null);

/// <summary>Whether a SECOM service can be read as this client stands (issue #822).</summary>
public enum SecomReachability
{
    /// <summary>It answers <c>GetSummary</c> without a certificate.</summary>
    Open,

    /// <summary>It answers, but refuses <c>GetSummary</c> without a client certificate, and no identity is set.</summary>
    NeedsCertificate,

    /// <summary>It answers <c>GetSummary</c> when this client presents its MCP identity (#832), and not without.</summary>
    OpenWithCertificate,

    /// <summary>It needs a client certificate, and refused the identity this client presented (#832).</summary>
    CertificateRefused,

    /// <summary>
    /// It has no <c>GetSummary</c> GET (404), only SECOM 2.0's POST GetSummary
    /// (<c>POST …/v2/object/search/summary</c>), whose filter must be signed
    /// with an MCP identity (#838). Without one it cannot be listed; with one
    /// that the service neither accepts nor refuses outright, the
    /// <see cref="SecomProbeResult.Detail"/> holds its answer.
    /// </summary>
    NeedsSecom2Search,

    /// <summary>
    /// Its TLS certificate is not trusted here: issued under no trusted root,
    /// expired, or naming another host. MCP-issued certificates are trusted
    /// through <see cref="SecomServerTrust"/> when the probe's client uses it (#829).
    /// </summary>
    UntrustedServer,

    /// <summary>It does not answer at that endpoint, or not as a SECOM service.</summary>
    Unreachable,
}

/// <summary>The result of <see cref="SecomRegistry.ProbeAsync"/>.</summary>
/// <param name="Reachability">Whether the service can be read.</param>
/// <param name="Detail">What the service answered, for display.</param>
public sealed record SecomProbeResult(SecomReachability Reachability, string? Detail = null)
{
    /// <summary>
    /// What <see cref="SecomServerTrust"/> decided about the server's TLS
    /// certificate, when the registry was given one and the connection got
    /// that far (#829): e.g. trusted through "MCP MCC", or refused as naming
    /// another host.
    /// </summary>
    public SecomServerTrustResult? ServerTrust { get; init; }

    /// <summary>
    /// The identity presented, for <see cref="SecomReachability.OpenWithCertificate"/>
    /// and <see cref="SecomReachability.CertificateRefused"/> (#832): its MRN, or its subject.
    /// </summary>
    public string? Identity { get; init; }

    /// <summary>True when the service actively refused (401, 403, or a TLS-level refusal), not merely failed.</summary>
    internal bool Refusal { get; init; }
}

/// <summary>
/// Reads SECOM service instances from a Maritime Service Registry (issue
/// #822) through its anonymous v1 search
/// (<c>POST …/api/secom/v1/searchService</c>), and probes whether a listed
/// service can be read.
/// </summary>
/// <remarks>
/// <para>
/// Registry data is uneven, so the listing is cleaned: deleted instances,
/// endpoints that are not absolute http(s) URLs on a real host (localhost,
/// bare host names, MRNs, reserved example domains) and duplicate endpoints
/// are dropped; product types are normalised (<c>S124</c> → <c>S-124</c>); and
/// geometry registered with longitudes off by whole turns (−414° for −54°) is
/// shifted back.
/// </para>
/// <para>
/// The listing is cached on disk and reused for <see cref="RefreshInterval"/>;
/// when the registry cannot be reached, the cached copy is served with
/// <see cref="SecomRegistryListing.Stale"/> set.
/// </para>
/// </remarks>
public sealed class SecomRegistry
{
    /// <summary>The MCC Maritime Service Registry's SECOM search.</summary>
    public static Uri DefaultSearchUri { get; } = new("https://msr.maritimeconnectivity.net/api/secom/v1/searchService");

    /// <summary>
    /// The canonical short form of a SECOM product type (<c>S124</c>,
    /// <c>s-124</c> → <c>S-124</c>), as the listing gives it; <see langword="null"/> for none.
    /// </summary>
    public static string? NormalizeProduct(string? value) => SecomJson.NormalizeProduct(value);

    /// <summary>How long a fetched listing is reused before the registry is asked again.</summary>
    public static TimeSpan RefreshInterval { get; } = TimeSpan.FromHours(1);

    private static readonly string[] ReservedHosts = ["example.com", "example.org", "example.net"];

    private readonly HttpClient _httpClient;
    private readonly Uri _searchUri;
    private readonly string? _cacheDirectory;
    private readonly TimeProvider _time;
    private readonly SecomServerTrust? _serverTrust;
    private HttpClient? _anonymousClient;

    /// <summary>Creates a registry reader.</summary>
    /// <param name="httpClient">
    /// The client used for the registry and for probes. To trust MCP-issued
    /// server certificates, give it a handler from <paramref name="serverTrust"/>.
    /// </param>
    /// <param name="cacheDirectory">Where the last listing is kept; <see langword="null"/> for none.</param>
    /// <param name="searchUri">The registry's SECOM search; defaults to <see cref="DefaultSearchUri"/>.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="serverTrust">
    /// The validator behind <paramref name="httpClient"/>'s handler, if any;
    /// probes then report what it decided (<see cref="SecomProbeResult.ServerTrust"/>).
    /// </param>
    public SecomRegistry(
        HttpClient httpClient,
        string? cacheDirectory = null,
        Uri? searchUri = null,
        TimeProvider? timeProvider = null,
        SecomServerTrust? serverTrust = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory;
        _searchUri = searchUri ?? DefaultSearchUri;
        _time = timeProvider ?? TimeProvider.System;
        _serverTrust = serverTrust;
    }

    /// <summary>
    /// Lists the registry's usable SECOM services, from the cache when it is
    /// recent (unless <paramref name="forceRefresh"/>).
    /// </summary>
    /// <exception cref="HttpRequestException">The registry could not be read and nothing is cached.</exception>
    public async Task<SecomRegistryListing> GetServicesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var cached = ReadCache();
        if (!forceRefresh && cached is { } recent && _time.GetUtcNow() - recent.FetchedAt < RefreshInterval)
            return Parse(recent.Body, recent.FetchedAt);

        string body;
        try
        {
            body = await FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            if (cached is not { } stale)
                throw new HttpRequestException($"The service registry could not be read: {ex.Message}", ex);
            return Parse(stale.Body, stale.FetchedAt) with { Stale = ex.Message };
        }

        var fetchedAt = _time.GetUtcNow();
        WriteCache(body, fetchedAt);
        return Parse(body, fetchedAt);
    }

    /// <summary>
    /// Finds out whether the service at <paramref name="serviceUri"/> can be
    /// read without a certificate: <c>Capability</c>, then one
    /// <c>GetSummary</c> page.
    /// </summary>
    public async Task<SecomProbeResult> ProbeAsync(Uri serviceUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        var host = serviceUri.IdnHost;
        _serverTrust?.Forget(host);

        // Anonymously first: a service that answers without the identity is Open.
        var result = await ProbeServiceAsync(AnonymousClient, serviceUri, cancellationToken).ConfigureAwait(false);
        if (result.Reachability == SecomReachability.NeedsSecom2Search && _serverTrust?.Identity is { } signing)
            result = await ProbeSignedSearchAsync(serviceUri, signing, cancellationToken).ConfigureAwait(false);
        else if (result.Reachability == SecomReachability.NeedsCertificate && _serverTrust?.Identity is { } identity)
        {
            var identified = await ProbeServiceAsync(_httpClient, serviceUri, cancellationToken).ConfigureAwait(false);
            result = identified switch
            {
                { Reachability: SecomReachability.Open } => new SecomProbeResult(SecomReachability.OpenWithCertificate, identity.DisplayName)
                {
                    Identity = identity.DisplayName,
                },
                // Only an active refusal is the identity's: other failures say nothing about it.
                { Reachability: SecomReachability.NeedsCertificate, Refusal: true } =>
                    new SecomProbeResult(SecomReachability.CertificateRefused, identified.Detail) { Identity = identity.DisplayName },
                _ => identified,
            };
        }

        if (result.Reachability == SecomReachability.Unreachable || _serverTrust?.ResultFor(host) is not { } trust)
            return result;

        // A refused certificate: say why, rather than the platform's TLS message.
        return result.Reachability == SecomReachability.UntrustedServer && RefusalReason(trust) is { } reason
            ? result with { Detail = reason, ServerTrust = trust }
            : result with { ServerTrust = trust };
    }

    /// <summary>
    /// Lists one page through SECOM 2.0's signed POST summary with the
    /// identity (#838): readable, refused (401/403), or still not readable,
    /// with the service's answer.
    /// </summary>
    private async Task<SecomProbeResult> ProbeSignedSearchAsync(Uri serviceUri, SecomClientIdentity identity, CancellationToken cancellationToken)
    {
        var client = new SecomClient(_httpClient, serviceUri) { Signer = _serverTrust!.CreateSigner, UsesPostInterfaces = true };
        try
        {
            await client.GetSummaryPageAsync(new SecomQuery(PageSize: 1), 1, cancellationToken).ConfigureAwait(false);
            return new SecomProbeResult(SecomReachability.OpenWithCertificate, identity.DisplayName) { Identity = identity.DisplayName };
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new SecomProbeResult(SecomReachability.CertificateRefused, ex.Message) { Identity = identity.DisplayName };
        }
        catch (Exception ex) when (IsProbeFailure(ex, cancellationToken))
        {
            return new SecomProbeResult(SecomReachability.NeedsSecom2Search, ex.Message) { Identity = identity.DisplayName };
        }
    }

    private static string? RefusalReason(SecomServerTrustResult trust) => trust.Outcome switch
    {
        SecomServerTrustOutcome.NotTrusted => "Its certificate is not issued under a trusted root.",
        SecomServerTrustOutcome.Expired => $"Its certificate from {trust.Anchor} has expired or is not yet valid.",
        SecomServerTrustOutcome.WrongHost => "Its certificate does not name this host.",
        SecomServerTrustOutcome.Revoked => $"Its certificate from {trust.Anchor} has been revoked.",
        _ => null,
    };

    /// <summary>
    /// The client anonymous probes use: one from <see cref="SecomServerTrust"/>
    /// that never presents the identity, when the registry was given the
    /// validator; otherwise the registry's client.
    /// </summary>
    private HttpClient AnonymousClient => _serverTrust is null
        ? _httpClient
        : LazyInitializer.EnsureInitialized(ref _anonymousClient, () =>
            new HttpClient(_serverTrust.CreateHandler(presentIdentity: false)) { Timeout = _httpClient.Timeout });

    private async Task<SecomProbeResult> ProbeServiceAsync(HttpClient httpClient, Uri serviceUri, CancellationToken cancellationToken)
    {
        var host = serviceUri.IdnHost;
        var client = new SecomClient(httpClient, serviceUri);
        SecomCapability? capability = null;
        try
        {
            capability = await client.GetCapabilityAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsProbeFailure(ex, cancellationToken))
        {
            // A refusal (401/403) or a dead endpoint is an answer in itself; a 404
            // or a non-SECOM body is not yet: GetSummary may still answer.
            if (Classify(ex, host) is { } early)
                return early;
        }

        try
        {
            await client.GetSummaryPageAsync(new SecomQuery(PageSize: 1), 1, cancellationToken).ConfigureAwait(false);
            return new SecomProbeResult(SecomReachability.Open);
        }
        catch (SecomIdentityRequiredException)
        {
            // SECOM 2.0 services (KHRA, KRISO) list only through the signed POST
            // summary and have no GET summary (404): not a certificate matter yet.
            // The client found the POST form; their serviceVersion is no guide.
            return new SecomProbeResult(SecomReachability.NeedsSecom2Search, "It lists its objects only through SECOM 2.0's signed requests, which need an MCP identity.");
        }
        catch (Exception ex) when (IsProbeFailure(ex, cancellationToken))
        {
            if (Classify(ex, host) is { } failed)
                return failed;

            // It answers Capability as SECOM but not an anonymous GetSummary;
            // otherwise it is not a SECOM service.
            return capability is not null
                ? new SecomProbeResult(SecomReachability.NeedsCertificate, ex.Message)
                : new SecomProbeResult(SecomReachability.Unreachable, "It does not answer as a SECOM service.");
        }
    }

    /// <summary>A TLS trust failure or a dead endpoint, recognised from the exception; otherwise <see langword="null"/>.</summary>
    private SecomProbeResult? Classify(Exception ex, string host)
    {
        // The server's certificate was accepted on this probe's connection, yet
        // the connection failed: the service refused the TLS handshake for want
        // of an (acceptable) client certificate.
        if (ex is HttpRequestException { StatusCode: null } && _serverTrust?.ResultFor(host) is { Allowed: true })
            return new SecomProbeResult(SecomReachability.NeedsCertificate, "It asks for a client certificate when connecting.") { Refusal = true };

        for (var inner = ex; inner is not null; inner = inner.InnerException)
        {
            if (inner is AuthenticationException)
                return new SecomProbeResult(SecomReachability.UntrustedServer, inner.Message);
        }

        return ex switch
        {
            HttpRequestException { StatusCode: null } => new SecomProbeResult(SecomReachability.Unreachable, ex.Message),
            TaskCanceledException => new SecomProbeResult(SecomReachability.Unreachable, "The service did not answer in time."),
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
                new SecomProbeResult(SecomReachability.NeedsCertificate, ex.Message) { Refusal = true },
            // Request/response services (KRISO's port call, TCS, …) list nothing.
            HttpRequestException { StatusCode: HttpStatusCode.NotImplemented } =>
                new SecomProbeResult(SecomReachability.Unreachable, "It does not offer GetSummary, so it cannot be listed."),
            _ => null,
        };
    }

    private static bool IsProbeFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or InvalidDataException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>Reads a registry search response into cleaned services (for tests and the cache).</summary>
    /// <exception cref="InvalidDataException">The body is not a registry search response.</exception>
    public static SecomRegistryListing Parse(string body, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(body);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The service registry did not answer with JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            var list = root.ValueKind switch
            {
                JsonValueKind.Array => root,
                JsonValueKind.Object when Property(root, "searchServiceResult") is { ValueKind: JsonValueKind.Array } results => results,
                _ => throw new InvalidDataException("The service registry's answer has no service list."),
            };

            var listed = 0;
            var candidates = new List<SecomRegistryService>();
            foreach (var entry in list.EnumerateArray())
            {
                listed++;
                if (ReadService(entry) is { } service)
                    candidates.Add(service);
            }

            // One per endpoint and product: released first, then the newest version.
            var services = candidates
                .GroupBy(s => (Endpoint: SecomClient.NormalizeServiceUri(s.EndpointUri).AbsoluteUri.ToLowerInvariant(), s.ProductSpec))
                .Select(g => g.OrderBy(s => s.Status == SecomRegistryStatus.Released ? 0 : 1)
                    .ThenByDescending(s => s.Version, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(s => s.Status == SecomRegistryStatus.Released ? 0 : 1)
                .ThenBy(s => s.ProductSpec, StringComparer.Ordinal)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            return new SecomRegistryListing(services, listed, listed - services.Length, fetchedAt);
        }
    }

    private static SecomRegistryService? ReadService(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return null;

        var status = String(entry, "status")?.Trim().ToUpperInvariant() switch
        {
            "RELEASED" => SecomRegistryStatus.Released,
            "PROVISIONAL" => SecomRegistryStatus.Provisional,
            "DELETED" or "DEPRECATED" or "INACTIVE" => (SecomRegistryStatus?)null,
            _ => SecomRegistryStatus.Unknown,
        };
        if (status is not { } known || String(entry, "instanceId") is not { Length: > 0 } instanceId)
            return null;
        if (UsableEndpoint(String(entry, "endpointUri")) is not { } endpoint)
            return null;

        var name = String(entry, "name")?.Trim();
        return new SecomRegistryService(
            instanceId.Trim(),
            string.IsNullOrEmpty(name) ? instanceId.Trim() : name,
            String(entry, "version")?.Trim(),
            known,
            SecomJson.NormalizeProduct(String(entry, "dataProductType")) ?? "OTHER",
            endpoint,
            String(entry, "organizationId")?.Trim() is { Length: > 0 } org ? org : null,
            (String(entry, "description") ?? String(entry, "comment"))?.Trim() is { Length: > 0 } text ? text : null,
            String(entry, "keywords")?.Trim() is { Length: > 0 } keywords ? keywords : null,
            Property(entry, "geometry") is { } geometry ? BoundsOf(geometry) : null);
    }

    /// <summary>An absolute http(s) endpoint on a real host, or <see langword="null"/>.</summary>
    internal static Uri? UsableEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var host = uri.Host;
        if (uri.IsLoopback || !host.Contains('.', StringComparison.Ordinal)
            || ReservedHosts.Any(r => host.Equals(r, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + r, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return uri;
    }

    /// <summary>
    /// The bounds of a GeoJSON geometry (any type, including collections),
    /// each part shifted by whole turns so its middle longitude lies in
    /// −180…180; <see langword="null"/> when it has no usable coordinates.
    /// </summary>
    internal static GeoBounds? BoundsOf(JsonElement geometry)
    {
        var parts = new List<List<(double Lon, double Lat)>>();
        CollectParts(geometry, parts);

        double south = double.MaxValue, north = double.MinValue, west = double.MaxValue, east = double.MinValue;
        foreach (var part in parts.Where(p => p.Count > 0))
        {
            if (part.Any(p => !double.IsFinite(p.Lon) || !double.IsFinite(p.Lat) || Math.Abs(p.Lat) > 90))
                return null;
            var middle = (part.Min(p => p.Lon) + part.Max(p => p.Lon)) / 2;
            var shift = Math.Round(middle / 360) * 360;
            foreach (var (lon, lat) in part)
            {
                south = Math.Min(south, lat);
                north = Math.Max(north, lat);
                west = Math.Min(west, lon - shift);
                east = Math.Max(east, lon - shift);
            }
        }

        if (south > north)
            return null;
        if (east - west >= 360)
            (west, east) = (-180, 180);
        return new GeoBounds(south, Math.Max(-180, west), north, Math.Min(180, east));
    }

    private static void CollectParts(JsonElement geometry, List<List<(double Lon, double Lat)>> parts)
    {
        if (geometry.ValueKind != JsonValueKind.Object)
            return;
        if (Property(geometry, "geometries") is { ValueKind: JsonValueKind.Array } children)
        {
            foreach (var child in children.EnumerateArray())
                CollectParts(child, parts);
            return;
        }

        if (Property(geometry, "coordinates") is not { } coordinates)
            return;

        // Each innermost array of positions is one part (a ring, a line, or a point).
        void Walk(JsonElement node)
        {
            if (node.ValueKind != JsonValueKind.Array)
                return;
            if (IsPosition(node))
            {
                parts.Add([Position(node)]);
                return;
            }

            if (node.EnumerateArray().All(IsPosition))
            {
                parts.Add([.. node.EnumerateArray().Select(Position)]);
                return;
            }

            foreach (var child in node.EnumerateArray())
                Walk(child);
        }

        Walk(coordinates);
    }

    private static bool IsPosition(JsonElement node) =>
        node.ValueKind == JsonValueKind.Array && node.GetArrayLength() >= 2
        && node[0].ValueKind == JsonValueKind.Number && node[1].ValueKind == JsonValueKind.Number;

    private static (double Lon, double Lat) Position(JsonElement node) => (node[0].GetDouble(), node[1].GetDouble());

    private async Task<string> FetchAsync(CancellationToken cancellationToken)
    {
        var uri = new UriBuilder(_searchUri) { Query = "page=0&pageSize=1000" }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"The service registry answered {(int)response.StatusCode} {response.ReasonPhrase}."),
                null, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _ = Parse(body, _time.GetUtcNow());  // fail now, not later, on a body that is not a listing
        return body;
    }

    private (string Body, DateTimeOffset FetchedAt)? ReadCache()
    {
        if (CachePath() is not { } path)
            return null;
        try
        {
            return File.Exists(path) ? (File.ReadAllText(path), new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteCache(string body, DateTimeOffset fetchedAt)
    {
        if (CachePath() is not { } path)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var partial = path + ".partial";
            File.WriteAllText(partial, body);
            File.SetLastWriteTimeUtc(partial, fetchedAt.UtcDateTime);
            File.Move(partial, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is a convenience; the live listing stands.
        }
    }

    private string? CachePath() => _cacheDirectory is null
        ? null
        : Path.Combine(_cacheDirectory, "secom-registry-"
            + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_searchUri.AbsoluteUri)))[..12].ToLowerInvariant() + ".json");

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        if (element.TryGetProperty(name, out var exact))
            return exact;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private static string? String(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.String } s
        ? s.GetString()
        : null;
}
