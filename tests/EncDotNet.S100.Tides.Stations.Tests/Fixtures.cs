using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Tides.Stations.Noaa;

namespace EncDotNet.S100.Tides.Stations.Tests;

/// <summary>
/// The NOAA CO-OPS fixtures recorded by tools/tide-fixtures/record-noaa-stations.sh,
/// served to <see cref="NoaaTideStationClient"/> by a fake handler.
/// </summary>
internal static class Fixtures
{
    public const int Year = 2026;
    public const string Seattle = "9447130";
    public const string PortTownsend = "9444900";
    public const string TacomaNarrows = "9446486";
    public const string PortLudlow = "9445017";
    public const string MarrowstonePoint = "9444972";

    /// <summary>The current stations with fixtures (tools/tide-fixtures/record-noaa-currents.sh): four harmonic, two subordinate.</summary>
    public static readonly string[] CurrentStations = ["PUG1701_33", "SFB1202_17", "LIS1001_13", "MOB1101_2", "PCT0281_1", "ACT2401_1"];

    /// <summary>The Puget Sound stations with full fixtures: two references and their subordinates.</summary>
    public static readonly string[] PugetSound = [Seattle, PortTownsend, TacomaNarrows, PortLudlow, MarrowstonePoint];

    public static string Path(params string[] parts) =>
        System.IO.Path.Combine([AppContext.BaseDirectory, "TestData", "Tides", "NOAA", .. parts]);

    /// <summary>The recorded station list JSON.</summary>
    public static JsonObject StationList()
    {
        using var file = File.OpenRead(Path("stations.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonNode.Parse(gzip)!.AsObject();
    }

    /// <summary>The recorded station list cut to some stations.</summary>
    public static JsonObject StationList(IReadOnlyCollection<string> ids)
    {
        var list = StationList();
        var stations = list["stations"]!.AsArray();
        foreach (var station in stations.ToArray())
        {
            if (!ids.Contains(station!["id"]!.GetValue<string>()))
            {
                stations.Remove(station);
            }
        }

        list["count"] = stations.Count;
        return list;
    }

    /// <summary>The recorded current prediction station list JSON.</summary>
    public static JsonObject CurrentStationList()
    {
        using var file = File.OpenRead(Path("Currents", "current-stations.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonNode.Parse(gzip)!.AsObject();
    }

    /// <summary>The recorded current station list cut to some stations, given as <c>&lt;id&gt;_&lt;bin&gt;</c>.</summary>
    public static JsonObject CurrentStationList(IReadOnlyCollection<string> ids)
    {
        var list = CurrentStationList();
        var stations = list["stations"]!.AsArray();
        foreach (var station in stations.ToArray())
        {
            if (!ids.Contains($"{station!["id"]!.GetValue<string>()}_{station["currbin"]!.GetValue<int>()}"))
            {
                stations.Remove(station);
            }
        }

        list["count"] = stations.Count;
        return list;
    }

    /// <summary>The options for a download that includes current stations.</summary>
    public static NoaaTideStationClientOptions CurrentOptions() => new()
    {
        RequestInterval = TimeSpan.Zero,
        RetryDelay = TimeSpan.Zero,
        SentinelStationIds = [Seattle],
        IncludeCurrentStations = true,
    };

    /// <summary>The catalogue of the Puget Sound tide stations and the current stations, built through the client from the fixtures.</summary>
    public static TideStationCatalogue CurrentCatalogue() =>
        Client(new FixtureHandler(), CurrentOptions()).DownloadCatalogueAsync(cancellationToken: TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    /// <summary>A client over a fake handler serving the fixtures, with the station list cut to the Puget Sound stations.</summary>
    public static NoaaTideStationClient Client(FixtureHandler handler, NoaaTideStationClientOptions? options = null) =>
        new(new HttpClient(handler), options ?? FastOptions());

    public static NoaaTideStationClientOptions FastOptions() => new()
    {
        RequestInterval = TimeSpan.Zero,
        RetryDelay = TimeSpan.Zero,
        SentinelStationIds = [Seattle],
        IncludeCurrentStations = false,
    };

    /// <summary>The Puget Sound catalogue, built through the client from the fixtures.</summary>
    public static TideStationCatalogue PugetSoundCatalogue() =>
        Client(new FixtureHandler()).DownloadCatalogueAsync(cancellationToken: TestContext.Current.CancellationToken).GetAwaiter().GetResult();

    /// <summary>NOAA's published highs and lows on MLLW for a subordinate station.</summary>
    public static IReadOnlyList<TideExtreme> MllwHighsAndLows(string id)
    {
        var result = new List<TideExtreme>();
        foreach (var line in File.ReadLines(Path(id, "mllw-hilo.csv")).Skip(1))
        {
            var fields = line.Split(',');
            var time = DateTime.SpecifyKind(
                DateTime.ParseExact(fields[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                DateTimeKind.Utc);
            result.Add(new TideExtreme(
                time,
                double.Parse(fields[1], CultureInfo.InvariantCulture),
                fields[2].Trim() == "H" ? TideExtremeKind.High : TideExtremeKind.Low));
        }

        return result;
    }
}

/// <summary>
/// Serves the CO-OPS metadata API from the recorded fixtures: the station
/// list (gzipped, as NOAA sends it), harcon.json and datums.json.
/// </summary>
internal sealed class FixtureHandler : HttpMessageHandler
{
    private int _requests;

    /// <summary>The station list to serve; the Puget Sound stations by default.</summary>
    public JsonObject StationList { get; set; } = Fixtures.StationList(Fixtures.PugetSound);

    /// <summary>The current station list to serve; the stations with current fixtures by default.</summary>
    public JsonObject CurrentStationList { get; set; } = Fixtures.CurrentStationList(Fixtures.CurrentStations);

    /// <summary>Serves an empty harcon.json for a current station with no fixture, instead of 404.</summary>
    public bool EmptyCurrentHarmonics { get; set; }

    /// <summary>Changes a datums.json response before it's served.</summary>
    public Action<string, JsonObject>? EditDatums { get; set; }

    /// <summary>Responses to give before the real one, such as a refusal.</summary>
    public Queue<HttpStatusCode> Failures { get; } = new();

    public int Requests => _requests;

    public List<Uri> Uris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        var uri = request.RequestUri!;
        lock (Uris)
        {
            Uris.Add(uri);
        }

        Assert.Contains("units=metric", uri.Query, StringComparison.Ordinal);
        lock (Failures)
        {
            if (Failures.TryDequeue(out var failure))
            {
                return Task.FromResult(new HttpResponseMessage(failure));
            }
        }

        var path = uri.AbsolutePath;
        Assert.StartsWith("/mdapi/prod/webapi/", path, StringComparison.Ordinal);
        var segments = path["/mdapi/prod/webapi/".Length..].Split('/');
        if (segments is ["stations.json"] && uri.Query.Contains("type=currentpredictions", StringComparison.Ordinal))
        {
            Assert.Contains("expand=currentpredictionoffsets", uri.Query, StringComparison.Ordinal);
            return Task.FromResult(Gzipped(CurrentStationList.ToJsonString()));
        }

        if (segments is ["stations", var currentId, "harcon.json"] && !char.IsDigit(currentId[0]))
        {
            // A current station: the recorded harcon.json of its one recorded bin.
            var directory = Directory.GetDirectories(Fixtures.Path("Currents"), currentId + "_*").SingleOrDefault();
            if (directory is not null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(File.ReadAllText(System.IO.Path.Combine(directory, "harcon.json"))),
                });
            }

            return Task.FromResult(EmptyCurrentHarmonics
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"units\":\"meters, centimeters/second\",\"HarmonicConstituents\":[]}") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (segments is ["stations.json"])
        {
            Assert.Contains("type=tidepredictions", uri.Query, StringComparison.Ordinal);
            Assert.Contains("expand=tidepredoffsets", uri.Query, StringComparison.Ordinal);
            return Task.FromResult(Gzipped(StationList.ToJsonString()));
        }

        if (segments is ["stations", var id, var file])
        {
            var fixture = Fixtures.Path(id, file);
            if (!File.Exists(fixture))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var text = File.ReadAllText(fixture);
            if (file == "datums.json" && EditDatums is { } edit)
            {
                var json = JsonNode.Parse(text)!.AsObject();
                edit(id, json);
                text = json.ToJsonString();
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage Gzipped(string text)
    {
        var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            JsonSerializer.Serialize(gzip, JsonDocument.Parse(text).RootElement);
        }

        var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new("application/json");
        content.Headers.ContentEncoding.Add("gzip");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
