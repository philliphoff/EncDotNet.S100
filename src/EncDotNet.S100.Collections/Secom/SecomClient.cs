using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// A read-only client for one SECOM (IEC 63173-2) service: <c>Capability</c>,
/// <c>GetSummary</c> and <c>Get</c>, over plain HTTPS without a client
/// certificate (issue #804).
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="ServiceUri"/> is the service's registered endpoint (for
/// example <c>https://s124.ccg-gcc.gc.ca/api/secom</c>); interface paths such as
/// <c>v2/object/summary</c> resolve against it. Unless a version is pinned, the
/// client tries edition 2 (<c>/v2</c>) first and falls back to edition 1
/// (<c>/v1</c>) when the service does not answer there, then keeps using the
/// version that answered.
/// </para>
/// <para>
/// A SECOM 2.0 service with no GET summary but the enveloped POST forms
/// (<c>POST …/v2/object/search/summary</c> and <c>…/v2/object/search</c>, as
/// KHRA and KRISO have) is listed and read through them, signed by
/// <see cref="Signer"/>, once a GET summary answers 404 (#838). Access
/// requests and encrypted data are not supported yet; see
/// <c>docs/design/dataset-collections.md</c> §7.5.
/// </para>
/// </remarks>
public sealed class SecomClient
{
    private readonly HttpClient _httpClient;
    private SecomApiVersion? _version;
    private bool _post;

    /// <summary>Creates a client for the service at <paramref name="serviceUri"/>.</summary>
    /// <param name="httpClient">The client used for requests.</param>
    /// <param name="serviceUri">The service's endpoint URI (http or https).</param>
    /// <param name="version">Pins the interface version; <see langword="null"/> to detect it.</param>
    public SecomClient(HttpClient httpClient, Uri serviceUri, SecomApiVersion? version = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(serviceUri);
        if (!serviceUri.IsAbsoluteUri || serviceUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A SECOM service URI must be an absolute http(s) URI.", nameof(serviceUri));

        _httpClient = httpClient;
        ServiceUri = NormalizeServiceUri(serviceUri);
        _version = version;
    }

    /// <summary>The service's endpoint URI, ending in <c>/</c>.</summary>
    public Uri ServiceUri { get; }

    /// <summary>
    /// The interface version in use: pinned, or detected by the first request
    /// that succeeded; <see langword="null"/> before then.
    /// </summary>
    public SecomApiVersion? ApiVersion => _version;

    /// <summary>
    /// The signer for SECOM 2.0 POST requests (#838): this client's MCP
    /// identity, when one is set; <see langword="null"/> or returning
    /// <see langword="null"/> when there is none.
    /// </summary>
    public Func<SecomEnvelopeSigner?>? Signer { get; init; }

    /// <summary>
    /// True when this client lists and reads through the SECOM 2.0 POST forms:
    /// set to start with them (a service known to need them), or found when a
    /// GET summary answered 404 and the POST form exists.
    /// </summary>
    public bool UsesPostInterfaces
    {
        get => _post;
        init
        {
            _post = value;
            if (value)
                _version = SecomApiVersion.V2;
        }
    }

    /// <summary>
    /// Returns the service URI in canonical form: query and fragment dropped,
    /// a trailing <c>/</c>, and any trailing <c>/v1</c> or <c>/v2</c> (or an
    /// interface path after it) removed, so users may paste an interface URL.
    /// </summary>
    public static Uri NormalizeServiceUri(Uri serviceUri)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        var path = serviceUri.AbsolutePath;
        foreach (var marker in new[] { "/v1/", "/v2/" })
        {
            var at = (path + "/").IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                path = path[..at];
                break;
            }
        }

        if (!path.EndsWith('/'))
            path += "/";
        return new UriBuilder(serviceUri) { Path = path, Query = string.Empty, Fragment = string.Empty }.Uri;
    }

    /// <summary>The URI that <c>Get</c>s the object <paramref name="dataReference"/> on <paramref name="version"/>.</summary>
    public Uri GetObjectUri(string dataReference, SecomApiVersion version)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataReference);
        return Resolve(version, "object", [("dataReference", dataReference)]);
    }

    /// <summary>Calls <c>Capability</c>.</summary>
    /// <exception cref="HttpRequestException">The service did not answer on any interface version.</exception>
    /// <exception cref="InvalidDataException">The response is not a SECOM capability response.</exception>
    public Task<SecomCapability> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
        SendAsync(
            v => Resolve(v, "capability", []),
            (root, v) => SecomJson.ReadCapability(root, v),
            cancellationToken);

    /// <summary>Calls <c>GetSummary</c> for one page (pages count from 1).</summary>
    /// <exception cref="HttpRequestException">The service did not answer on any interface version.</exception>
    /// <exception cref="InvalidDataException">The response is not a SECOM summary response.</exception>
    /// <remarks>
    /// Some services answer a filtered request that matches nothing with
    /// 404. So for a filtered query the interface version is detected first,
    /// with an unfiltered request; once it is known, a 404 means an empty page.
    /// </remarks>
    public async Task<SecomSummaryPage> GetSummaryPageAsync(
        SecomQuery query, int page = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);

        var filtered = query.GeometryWkt is { Length: > 0 } || query.ValidFrom is not null || query.ValidTo is not null;
        if (!_post)
        {
            try
            {
                return await GetSummaryPageByGetAsync(query, page, filtered, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // No GET summary, but the SECOM 2.0 POST form: list through it from now on.
                var post = await PostSummaryStatusAsync(cancellationToken).ConfigureAwait(false);
                if (post == HttpStatusCode.NotImplemented)
                {
                    throw new HttpRequestException(
                        "The service does not implement GetSummary (501).", ex, HttpStatusCode.NotImplemented);
                }

                if (!HasPostSummary(post))
                    throw;
                _post = true;
                _version = SecomApiVersion.V2;
            }
        }

        try
        {
            return await SendPostAsync(
                Resolve(SecomApiVersion.V2, "object/search/summary", []),
                signer => signer.SummaryRequest(query, page, DateTimeOffset.UtcNow),
                root => SecomJson.ReadSummaryPage(root),
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (filtered && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new SecomSummaryPage([], 0, null);
        }
    }

    private async Task<SecomSummaryPage> GetSummaryPageByGetAsync(SecomQuery query, int page, bool filtered, CancellationToken cancellationToken)
    {
        if (filtered && _version is null)
            await GetSummaryPageByGetAsync(new SecomQuery(PageSize: 1), 1, filtered: false, cancellationToken).ConfigureAwait(false);

        try
        {
            return await SendAsync(
                v => Resolve(v, "object/summary", SummaryParameters(query, page)),
                (root, _) => SecomJson.ReadSummaryPage(root),
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (filtered && _version is not null && ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new SecomSummaryPage([], 0, null);
        }
    }

    /// <summary>
    /// Calls <c>GetSummary</c> page by page until every entry is read, at most
    /// <paramref name="maxItems"/> entries are read, or a page adds nothing new
    /// (some services ignore paging). Entries are returned once per data reference.
    /// </summary>
    /// <exception cref="HttpRequestException">The service did not answer on any interface version.</exception>
    /// <exception cref="InvalidDataException">A response is not a SECOM summary response.</exception>
    public async Task<SecomSummaryList> GetSummariesAsync(
        SecomQuery query, int maxItems = 10_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxItems, 1);

        var items = new List<SecomSummary>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int? reported = null;
        var truncated = false;
        for (var page = 1; items.Count < maxItems; page++)
        {
            var result = await GetSummaryPageAsync(query, page, cancellationToken).ConfigureAwait(false);
            reported ??= result.TotalItems;
            var added = 0;
            foreach (var item in result.Items)
            {
                if (!seen.Add(item.DataReference))
                    continue;
                if (items.Count >= maxItems)
                {
                    truncated = true;
                    break;
                }

                items.Add(item);
                added++;
            }

            var pageSize = result.MaxItemsPerPage is { } size and > 0 ? size : query.PageSize;
            if (added == 0
                || (result.TotalItems is { } total && items.Count >= total)
                || (result.TotalItems is null && result.Items.Count < pageSize))
            {
                break;
            }
        }

        // Services sometimes report more than they return; only the cap truncates.
        truncated |= items.Count >= maxItems && reported is { } all && all > items.Count;
        return new SecomSummaryList(items, reported, truncated);
    }

    /// <summary>Calls <c>Get</c> for one data object.</summary>
    /// <returns>The object, or <see langword="null"/> when the service returned none.</returns>
    /// <exception cref="HttpRequestException">The service did not answer on any interface version.</exception>
    /// <exception cref="InvalidDataException">The response is not a SECOM get response.</exception>
    public Task<SecomDataObject?> GetAsync(string dataReference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataReference);
        if (_post)
        {
            return SendPostAsync(
                Resolve(SecomApiVersion.V2, "object/search", []),
                signer => signer.GetRequest(dataReference, DateTimeOffset.UtcNow),
                root => SecomJson.ReadDataObjects(root).FirstOrDefault(),
                cancellationToken);
        }

        return SendAsync(
            v => GetObjectUri(dataReference, v),
            (root, _) => SecomJson.ReadDataObjects(root).FirstOrDefault(),
            cancellationToken);
    }

    /// <summary>
    /// The request that <c>Get</c>s an object through the SECOM 2.0 POST form
    /// (<c>POST …/v2/object/search</c>), for hosts that fetch it themselves
    /// (the downloader). <paramref name="getObjectUri"/> is the object's GET
    /// URI (<see cref="GetObjectUri"/>), which names the service and the data
    /// reference.
    /// </summary>
    /// <exception cref="ArgumentException">The URI names no data reference.</exception>
    public static HttpRequestMessage CreatePostGetRequest(Uri getObjectUri, SecomEnvelopeSigner signer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(getObjectUri);
        ArgumentNullException.ThrowIfNull(signer);
        var dataReference = getObjectUri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2 && p[0] == "dataReference")
            .Select(p => Uri.UnescapeDataString(p[1]))
            .FirstOrDefault()
            ?? throw new ArgumentException("The URI names no dataReference.", nameof(getObjectUri));
        var service = NormalizeServiceUri(getObjectUri);
        return PostRequest(new Uri(service, "v2/object/search"), signer.GetRequest(dataReference, now));
    }

    /// <summary>
    /// Reads the first data object of a <c>Get</c> response body, for hosts
    /// that fetched it themselves (for example the downloader).
    /// </summary>
    /// <exception cref="InvalidDataException">The body is not a SECOM get response with data.</exception>
    public static SecomDataObject ReadDataObject(Stream body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            using var document = JsonDocument.Parse(body);
            return SecomJson.ReadDataObjects(document.RootElement).FirstOrDefault()
                ?? throw new InvalidDataException("The SECOM get response holds no data.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The SECOM get response is not JSON.", ex);
        }
    }

    private async Task<T> SendAsync<T>(
        Func<SecomApiVersion, Uri> uriFor,
        Func<JsonElement, SecomApiVersion, T> read,
        CancellationToken cancellationToken)
    {
        if (_version is { } pinned)
            return await SendOnceAsync(uriFor(pinned), pinned, read, cancellationToken).ConfigureAwait(false);

        Exception? first = null;
        foreach (var version in new[] { SecomApiVersion.V2, SecomApiVersion.V1 })
        {
            try
            {
                var result = await SendOnceAsync(uriFor(version), version, read, cancellationToken).ConfigureAwait(false);
                _version = version;
                return result;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException && !cancellationToken.IsCancellationRequested)
            {
                // Unreachable hosts fail the same way on both versions; don't wait twice.
                if (ex is HttpRequestException { StatusCode: null })
                    throw;
                first ??= ex;
            }
        }

        throw first!;
    }

    private async Task<T> SendOnceAsync<T>(
        Uri uri, SecomApiVersion version, Func<JsonElement, SecomApiVersion, T> read, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"SECOM {uri.AbsolutePath} answered {Status(response)}."),
                null,
                response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"SECOM {uri.AbsolutePath} did not answer with JSON.", ex);
        }

        using (document)
        {
            return read(document.RootElement, version);
        }
    }

    private async Task<T> SendPostAsync<T>(
        Uri uri, Func<SecomEnvelopeSigner, System.Text.Json.Nodes.JsonObject> body, Func<JsonElement, T> read, CancellationToken cancellationToken)
    {
        var signer = Signer?.Invoke() ?? throw new SecomIdentityRequiredException(
            $"SECOM {uri.AbsolutePath} lists and serves objects only through SECOM 2.0's signed requests, which need an MCP identity.");
        using var request = PostRequest(uri, body(signer));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                string.Create(CultureInfo.InvariantCulture, $"SECOM POST {uri.AbsolutePath} answered {Status(response)}."),
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"SECOM POST {uri.AbsolutePath} did not answer with JSON.", ex);
        }

        using (document)
        {
            return read(document.RootElement);
        }
    }

    /// <summary>"404 Not Found", or just "404" when the service sends no reason phrase.</summary>
    private static string Status(HttpResponseMessage response) =>
        string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{(int)response.StatusCode} {response.ReasonPhrase}");

    private static HttpRequestMessage PostRequest(Uri uri, System.Text.Json.Nodes.JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    /// <summary>
    /// True when the service has SECOM 2.0's POST GetSummary
    /// (<c>POST …/v2/object/search/summary</c>, an enveloped and signed
    /// filter): an empty request is answered, if not with 404, 405 or 501.
    /// KHRA and KRISO list objects only through it; they have no GET summary.
    /// </summary>
    internal async Task<bool> HasPostSummaryAsync(CancellationToken cancellationToken) =>
        HasPostSummary(await PostSummaryStatusAsync(cancellationToken).ConfigureAwait(false));

    private static bool HasPostSummary(HttpStatusCode? status) =>
        status is { } code and not (HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented);

    /// <summary>What an empty POST GetSummary is answered with, or <see langword="null"/> when nothing answered.</summary>
    private async Task<HttpStatusCode?> PostSummaryStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _httpClient
                .PostAsync(Resolve(SecomApiVersion.V2, "object/search/summary", []), content, cancellationToken)
                .ConfigureAwait(false);
            return response.StatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null;
        }
    }

    private Uri Resolve(SecomApiVersion version, string path, IEnumerable<(string Name, string Value)> parameters)
    {
        var query = new StringBuilder();
        foreach (var (name, value) in parameters)
        {
            query.Append(query.Length == 0 ? '?' : '&')
                .Append(Uri.EscapeDataString(name))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        var prefix = version == SecomApiVersion.V1 ? "v1/" : "v2/";
        return new Uri(ServiceUri, prefix + path + query);
    }

    private static IEnumerable<(string, string)> SummaryParameters(SecomQuery query, int page)
    {
        if (query.GeometryWkt is { Length: > 0 } geometry)
            yield return ("geometry", geometry);
        if (query.ValidFrom is { } from)
            yield return ("validFrom", from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        if (query.ValidTo is { } to)
            yield return ("validTo", to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        yield return ("page", page.ToString(CultureInfo.InvariantCulture));
        yield return ("pageSize", Math.Max(1, query.PageSize).ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// A SECOM 2.0 service needs a signed request, and no MCP identity is set to
/// sign it (#838).
/// </summary>
public sealed class SecomIdentityRequiredException : HttpRequestException
{
    /// <summary>Creates the exception.</summary>
    public SecomIdentityRequiredException(string message)
        : base(message)
    {
    }
}
