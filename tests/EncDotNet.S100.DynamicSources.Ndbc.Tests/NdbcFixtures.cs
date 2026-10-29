using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace EncDotNet.S100.DynamicSources.Ndbc.Tests;

/// <summary>The NDBC fixtures under tests/datasets/NDBC (see its README).</summary>
internal static class NdbcFixtures
{
    /// <summary>The moored buoy: BOSTON 16 NM East of Boston, MA.</summary>
    public const string Buoy = "44013";

    /// <summary>The C-MAN station: Buzzards Bay, MA.</summary>
    public const string Cman = "BUZM3";

    public static string PathOf(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "NDBC", fileName);

    public static string ReadText(string fileName) => File.ReadAllText(PathOf(fileName));

    public static byte[] ReadBytes(string fileName) => File.ReadAllBytes(PathOf(fileName));

    /// <summary>The time of a realtime2 file's newest row.</summary>
    public static DateTimeOffset NewestTime(string stationId) =>
        NdbcRealtimeParser.Parse(ReadText(stationId + ".txt"), stationId)[0].Time;
}

/// <summary>
/// An HTTP handler that serves canned responses by path and records every
/// request, so source tests run without a network.
/// </summary>
internal sealed class FakeNdbcHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    /// <summary>Serves the fixtures: the station table and the buoy and C-MAN files; anything else is a 404.</summary>
    public static FakeNdbcHandler WithFixtures()
    {
        var handler = new FakeNdbcHandler();
        handler.Serve("/activestations.xml", NdbcFixtures.ReadBytes("activestations.xml"), "text/xml");
        handler.Serve($"/data/realtime2/{NdbcFixtures.Buoy}.txt", NdbcFixtures.ReadBytes(NdbcFixtures.Buoy + ".txt"));
        handler.Serve($"/data/realtime2/{NdbcFixtures.Cman}.txt", NdbcFixtures.ReadBytes(NdbcFixtures.Cman + ".txt"));
        return handler;
    }

    public void Serve(string path, byte[] body, string mediaType = "text/plain") =>
        _routes[path] = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body) { Headers = { { "Content-Type", mediaType } } },
        };

    /// <summary>
    /// Serves <paramref name="body"/> the way NDBC answers a <c>Range</c>
    /// request: 206 with at most <paramref name="maxBytes"/> leading bytes and a
    /// <c>Content-Range</c> header.
    /// </summary>
    public void ServeRanged(string path, byte[] body, int maxBytes) =>
        _routes[path] = request =>
        {
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (range is null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            }

            long last = Math.Min(Math.Min(range.To ?? long.MaxValue, maxBytes - 1), body.Length - 1);
            var content = new ByteArrayContent(body, 0, (int)last + 1);
            content.Headers.ContentRange = new ContentRangeHeaderValue(0, last, body.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
        };

    public void Serve(string path, string body) => Serve(path, Encoding.ASCII.GetBytes(body));

    public void Fail(string path, HttpStatusCode status) =>
        _routes[path] = _ => new HttpResponseMessage(status);

    public void Throw(string path) =>
        _routes[path] = _ => throw new HttpRequestException("Connection refused (fake).");

    public int CountRequests(string path) => Requests.Count(r => r.RequestUri!.AbsolutePath == path);

    public int CountObservationRequests() => Requests.Count(r => r.RequestUri!.AbsolutePath.StartsWith("/data/realtime2/", StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        var response = _routes.TryGetValue(request.RequestUri!.AbsolutePath, out var route)
            ? route(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}
