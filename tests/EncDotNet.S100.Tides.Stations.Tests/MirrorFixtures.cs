using System.IO.Compression;
using System.Net;
using EncDotNet.S100.Tides.Stations.Mirror;

namespace EncDotNet.S100.Tides.Stations.Tests;

/// <summary>Catalogues and manifests for the mirror tests, built from the NOAA fixtures.</summary>
internal static class MirrorFixtures
{
    public static readonly Uri ManifestAddress = new("https://mirror.example/releases/download/tide-stations/tide-stations.manifest.json");

    public static readonly Uri CatalogueAddress = new("https://mirror.example/releases/download/tide-stations/tide-stations.json.gz");

    public static readonly DateTimeOffset Retrieved = new(2026, 11, 12, 16, 0, 0, TimeSpan.Zero);

    /// <summary>The Puget Sound tide stations and the current stations, dated <see cref="Retrieved"/>.</summary>
    public static TideStationCatalogue Catalogue(DateTimeOffset? retrieved = null)
    {
        var catalogue = Fixtures.CurrentCatalogue();
        return new TideStationCatalogue(catalogue.Stations, catalogue.CurrentStations, catalogue.Provider, retrieved ?? Retrieved);
    }

    /// <summary>A catalogue as a mirror publishes it: gzip-compressed JSON.</summary>
    public static byte[] Gzip(TideStationCatalogue catalogue)
    {
        var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            TideStationCatalogueFile.WriteAsync(gzip, catalogue, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        }

        return buffer.ToArray();
    }

    public static TideCatalogueManifest Manifest(byte[] content, TideStationCatalogue catalogue) =>
        TideCatalogueManifest.Create(content, catalogue, catalogue.Retrieved!.Value, catalogue.Retrieved!.Value.AddDays(7));

    public static byte[] ToJson(TideCatalogueManifest manifest)
    {
        var buffer = new MemoryStream();
        manifest.WriteAsync(buffer, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return buffer.ToArray();
    }

    public static TideCatalogueMirrorClient Client(MirrorHandler handler, int maxRetries = 2) =>
        new(new HttpClient(handler), new TideCatalogueMirrorClientOptions
        {
            ManifestAddress = ManifestAddress,
            RetryDelay = TimeSpan.Zero,
            MaxRetries = maxRetries,
        });
}

/// <summary>Serves a mirror's two files: each request takes the next queued response for its file, then the last one again.</summary>
internal sealed class MirrorHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _responses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<HttpResponseMessage>> _last = new(StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests { get; } = [];

    public int RequestsFor(Uri address) => Requests.Count(r => r.RequestUri == address);

    public MirrorHandler Serve(Uri address, byte[] content) => Serve(address, () => Ok(content));

    public MirrorHandler Serve(Uri address, HttpStatusCode status) => Serve(address, () => new HttpResponseMessage(status));

    public MirrorHandler Serve(Uri address, Func<HttpResponseMessage> response)
    {
        if (!_responses.TryGetValue(address.AbsoluteUri, out var queue))
        {
            _responses[address.AbsoluteUri] = queue = new Queue<Func<HttpResponseMessage>>();
        }

        queue.Enqueue(response);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var key = request.RequestUri!.AbsoluteUri;
        if (_responses.TryGetValue(key, out var queue) && queue.TryDequeue(out var next))
        {
            _last[key] = next;
            return Task.FromResult(next());
        }

        return Task.FromResult(_last.TryGetValue(key, out var last) ? last() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage Ok(byte[] content) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
}
