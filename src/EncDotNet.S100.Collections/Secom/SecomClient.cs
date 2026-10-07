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
/// Signed requests (the edition 2 <c>POST</c> search interfaces), mutual TLS,
/// access requests and encrypted data need an MCP certificate and are not
/// supported yet; see <c>docs/design/dataset-collections.md</c> §7.5.
/// </para>
/// </remarks>
public sealed class SecomClient
{
    private readonly HttpClient _httpClient;
    private SecomApiVersion? _version;

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
    public Task<SecomSummaryPage> GetSummaryPageAsync(
        SecomQuery query, int page = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        return SendAsync(
            v => Resolve(v, "object/summary", SummaryParameters(query, page)),
            (root, _) => SecomJson.ReadSummaryPage(root),
            cancellationToken);
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
        return SendAsync(
            v => GetObjectUri(dataReference, v),
            (root, _) => SecomJson.ReadDataObjects(root).FirstOrDefault(),
            cancellationToken);
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
                string.Create(CultureInfo.InvariantCulture, $"SECOM {uri.AbsolutePath} answered {(int)response.StatusCode} {response.ReasonPhrase}."),
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
