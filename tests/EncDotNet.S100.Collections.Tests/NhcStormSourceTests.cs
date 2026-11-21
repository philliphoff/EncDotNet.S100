using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Issue #1298 (V27, V28): the "Tropical cyclones — NHC" Library source: its
/// known-sources entry, reading <c>CurrentStorms.json</c>, one item per active
/// storm dated by its advisory, the advisory's zips downloaded side by side,
/// a new advisory outdating the download, and staleness after 7 hours.
/// </summary>
public sealed class NhcStormSourceTests : IDisposable
{
    private const string ListUri = "https://www.nhc.noaa.gov/CurrentStorms.json";
    private const string Archive = "https://www.nhc.noaa.gov/gis/forecast/archive/";

    // Ian's advisory 20 (tests/datasets/TC/al092022): 1100 PM EDT Tue Sep 27 2022.
    private static readonly DateTimeOffset Issued = new(2022, 9, 28, 3, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static KnownCatalogueSource Known => KnownCatalogueSources.Find("noaa-nhc-storms")
        ?? throw new InvalidOperationException("No NHC source.");

    private static WeatherFeedSource Feed() =>
        new(Guid.NewGuid(), null, Known.CatalogUri, Known.Weather!, new WeatherSelection())
        {
            Licence = Known.Licence,
            Attribution = Known.Attribution,
        };

    /// <summary>One storm of the list, shaped as NHC writes it (its sample, NHC_JSON_Sample.json).</summary>
    private static JsonObject Storm(string id, string name, string classification, string intensity, string advisory, DateTimeOffset issued,
        bool radii = true, bool bestTrack = true, string host = "www.nhc.noaa.gov")
    {
        var file = id.ToLowerInvariant();
        string Time(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        JsonObject Product(string? zip) => new()
        {
            ["advNum"] = advisory,
            ["issuance"] = Time(issued),
            ["fileUpdateTime"] = Time(issued.AddMinutes(-4)),
            ["zipFile"] = zip,
            ["kmzFile"] = $"https://{host}/storm_graphics/api/{id.ToUpperInvariant()}_{advisory}adv_TRACK.kmz",
        };
        return new JsonObject
        {
            ["id"] = id,
            ["binNumber"] = "AT1",
            ["name"] = name,
            ["classification"] = classification,
            ["intensity"] = intensity,
            ["pressure"] = "952",
            ["latitude"] = "24.1N",
            ["longitude"] = "83.0W",
            ["latitudeNumeric"] = 24.1,
            ["longitudeNumeric"] = -83.0,
            ["movementDir"] = 345,
            ["movementSpeed"] = 9,
            ["lastUpdate"] = Time(issued),
            ["forecastTrack"] = Product($"https://{host}/gis/forecast/archive/{file}_5day_{advisory}.zip"),
            ["windWatchesWarnings"] = null,
            ["trackCone"] = Product($"https://{host}/gis/forecast/archive/{file}_5day_{advisory}.zip"),
            ["forecastWindRadiiGIS"] = radii ? Product($"https://{host}/gis/forecast/archive/{file}_fcst_{advisory}.zip") : null,
            ["bestTrackGIS"] = bestTrack
                ? new JsonObject { ["issuance"] = Time(issued.AddHours(-3)), ["zipFile"] = $"https://{host}/gis/best_track/{file}_best_track.zip" }
                : null,
        };
    }

    private static string List(params JsonObject[] storms) =>
        new JsonObject { ["activeStorms"] = new JsonArray([.. storms]) }.ToJsonString();

    private static StormServer Server(string list)
    {
        var server = new StormServer { [ListUri] = System.Text.Encoding.UTF8.GetBytes(list) };
        foreach (var file in (string[])["al092022_5day_020.zip", "al092022_fcst_020.zip"])
            server[Archive + file] = File.ReadAllBytes(TestPaths.Dataset("TC", "al092022", file));
        server["https://www.nhc.noaa.gov/gis/best_track/al092022_best_track.zip"] =
            File.ReadAllBytes(TestPaths.Dataset("TC", "al092022", "al092022_best_track.zip"));
        return server;
    }

    [Fact]
    public void The_known_source_reads_CurrentStorms_json_stale_after_7_hours_refreshing_hourly()
    {
        var known = Known;
        var service = known.Weather!;

        Assert.Equal("Tropical cyclones — NHC", known.Name);
        Assert.True(known.NotForNavigation);
        Assert.Equal("Public domain", known.Licence);
        Assert.Equal("NOAA/NWS National Hurricane Center", known.Attribution);
        Assert.Equal(WeatherServiceKind.NhcStorms, service.Kind);
        Assert.True(service.IsUsable);
        Assert.Equal(new Uri(ListUri), service.StormListUri);
        Assert.Equal(TimeSpan.FromHours(7), service.StaleAfter);
        Assert.Equal(1, service.RefreshHours);
    }

    [Fact]
    public void A_storm_entry_round_trips_through_a_known_sources_document()
    {
        using var stream = new MemoryStream();
        KnownCatalogueSources.Write(stream, [Known]);
        stream.Position = 0;

        var service = Assert.Single(KnownCatalogueSources.Read(stream)).Weather!;

        Assert.Equal(WeatherServiceKind.NhcStorms, service.Kind);
        Assert.Equal("CurrentStorms.json", service.File);
        Assert.Equal(new Uri(ListUri), service.StormListUri);
        Assert.Equal(7, service.StaleAfterHours);
    }

    [Fact]
    public void The_list_keeps_NHCs_basins_and_storms_with_a_forecast_zip_on_its_own_host()
    {
        var list = List(
            Storm("al092022", "Ian", "HU", "105", "020", Issued),
            Storm("EP142026", "Arlo", "TS", "50", "006A", Issued, radii: false, bestTrack: false),
            Storm("wp302026", "Typhoon", "TY", "120", "010", Issued),
            Storm("al152026", "Elsewhere", "TS", "40", "003", Issued, host: "example.com"));
        var withoutFiles = Storm("al162026", "Potential", "PC", "30", "001", Issued);
        withoutFiles["forecastTrack"] = null;
        withoutFiles["trackCone"] = null;
        var document = JsonNode.Parse(list)!.AsObject();
        document["activeStorms"]!.AsArray().Add(withoutFiles);

        var storms = NhcActiveStorms.Parse(document.ToJsonString(), new Uri(ListUri));

        Assert.Equal(["al092022", "ep142026"], storms.Select(s => s.AtcfId));
        var ian = storms[0];
        Assert.Equal("Ian", ian.Name);
        Assert.Equal("HU", ian.Classification);
        Assert.Equal(105, ian.IntensityKnots);
        Assert.Equal(952, ian.PressureHectopascals);
        Assert.Equal(24.1, ian.Latitude);
        Assert.Equal("020", ian.Advisory);
        Assert.Equal("20", ian.AdvisoryNumber);
        Assert.Equal("AL09", ian.ShortId);
        Assert.Equal("Atlantic", ian.Basin);
        Assert.Equal(Issued, ian.Issued);
        Assert.Equal(["al092022_5day_020.zip", "al092022_fcst_020.zip", "al092022_best_track.zip"], ian.Files.Select(f => f.FileName));
        var arlo = storms[1];
        Assert.Equal("6A", arlo.AdvisoryNumber);
        Assert.Equal("East Pacific", arlo.Basin);
        Assert.Equal(["ep142026_5day_006A.zip"], arlo.Files.Select(f => f.FileName));
    }

    [Fact]
    public void A_document_without_a_storm_list_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => NhcActiveStorms.Parse("{}", new Uri(ListUri)));
        Assert.Throws<InvalidDataException>(() => NhcActiveStorms.Parse("not json", new Uri(ListUri)));
        Assert.Empty(NhcActiveStorms.Parse("""{"activeStorms": []}""", new Uri(ListUri)));
    }

    [Fact]
    public async Task Each_active_storm_is_one_item_dated_by_its_advisory_stale_after_7_hours()
    {
        var server = Server(List(Storm("al092022", "Ian", "HU", "105", "020", Issued)));
        var index = await new WeatherFeedIndexer(new HttpClient(server), timeProvider: new FakeTimeProvider(Issued.AddHours(1)))
            .IndexAsync(Feed(), null, TestContext.Current.CancellationToken);

        Assert.Empty(index.Diagnostics);
        var item = Assert.Single(index.Items);
        Assert.Equal("al092022", item.Key);
        Assert.Equal("al092022", item.Name);
        Assert.Equal("TC", item.ProductSpec);
        Assert.Equal("Ian (AL092022), advisory 20", item.Title);
        Assert.Null(item.Bounds);
        Assert.Equal(Issued, S100ForecastFeedIndexer.RunOf(item));
        // The forecast runs 120 hours from the synoptic time, 00Z for an advisory issued at 03Z.
        Assert.Equal(new DateTimeOffset(2022, 10, 3, 0, 0, 0, TimeSpan.Zero), ForecastRuns.ShownWindow(item, null)?.ValidTo);
        Assert.Equal("20", item.Properties[WeatherFeedIndexer.AdvisoryProperty]);
        Assert.Equal("Ian", item.Properties[WeatherFeedIndexer.StormNameProperty]);
        Assert.Equal("HU", item.Properties[WeatherFeedIndexer.StormClassProperty]);
        Assert.Equal("105", item.Properties[WeatherFeedIndexer.IntensityProperty]);
        Assert.Equal("Atlantic", item.Properties[WeatherFeedIndexer.BasinProperty]);
        Assert.Equal("NOAA/NWS National Hurricane Center · Public domain", WeatherRuns.CreditOf(item));
        Assert.True(WeatherRuns.IsWeatherRun(item));
        Assert.Equal(TimeSpan.FromHours(7), WeatherRuns.StaleAfterOf(item));
        Assert.False(WeatherRuns.IsStale(item, null, Issued.AddHours(6)));
        Assert.True(WeatherRuns.IsStale(item, null, Issued.AddHours(8)));
        var remote = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.Equal(Issued, remote.LastModified);
        Assert.Equal("al092022_5day_020.zip", remote.Layout?.RelativePath);
        Assert.Equal(["al092022_5day_020.zip", "al092022_fcst_020.zip", "al092022_best_track.zip"], remote.Parts!.Select(p => p.FileName));
        Assert.Equal(Issued, index.PublishedAt);
    }

    [Fact]
    public async Task An_empty_list_has_no_items_and_no_errors()
    {
        var server = Server(List());
        var index = await new WeatherFeedIndexer(new HttpClient(server)).IndexAsync(Feed(), null, TestContext.Current.CancellationToken);

        Assert.Empty(index.Items);
        Assert.Empty(index.Diagnostics);
    }

    [Fact]
    public async Task A_download_saves_the_advisorys_zips_side_by_side_and_the_next_advisory_outdates_it()
    {
        var server = Server(List(Storm("al092022", "Ian", "HU", "105", "020", Issued)));
        var http = new HttpClient(server);
        var feed = Feed();
        var item = Assert.Single((await new WeatherFeedIndexer(http).IndexAsync(feed, null, TestContext.Current.CancellationToken)).Items);
        var downloader = new EncCellDownloader(http, Path.Combine(_temp.Path, "nhc"));

        var cell = await downloader.DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("al092022_5day_020.zip", cell.Location.RelativePath);
        var folder = Path.GetDirectoryName(Path.Combine(cell.Location.RootPath, cell.Location.RelativePath))!;
        foreach (var file in (string[])["al092022_5day_020.zip", "al092022_fcst_020.zip", "al092022_best_track.zip"])
            Assert.Equal(File.ReadAllBytes(TestPaths.Dataset("TC", "al092022", file)), File.ReadAllBytes(Path.Combine(folder, file)));
        Assert.Equal(Issued, cell.PublishedAt);

        // Advisory 21, six hours later, makes the download outdated.
        var next = Issued.AddHours(6);
        server[ListUri] = System.Text.Encoding.UTF8.GetBytes(List(Storm("al092022", "Ian", "HU", "110", "021", next)));
        var newer = Assert.Single((await new WeatherFeedIndexer(http).IndexAsync(feed, null, TestContext.Current.CancellationToken)).Items);
        var downloads = new LibraryDownloads(downloader);
        Assert.False(downloads.IsOutdated(item));
        Assert.True(downloads.IsOutdated(newer));
        Assert.Equal("21", newer.Properties[WeatherFeedIndexer.AdvisoryProperty]);
    }

    [Fact]
    public async Task A_part_that_isnt_there_fails_the_download_and_leaves_no_copy()
    {
        var server = Server(List(Storm("al092022", "Ian", "HU", "105", "020", Issued)));
        server.Remove("https://www.nhc.noaa.gov/gis/best_track/al092022_best_track.zip");
        var http = new HttpClient(server);
        var item = Assert.Single((await new WeatherFeedIndexer(http).IndexAsync(Feed(), null, TestContext.Current.CancellationToken)).Items);
        var downloader = new EncCellDownloader(http, Path.Combine(_temp.Path, "nhc"));

        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Null(downloader.TryGetDownloaded("al092022"));
    }

    [Fact]
    public async Task While_NHC_cant_be_reached_the_downloaded_storms_are_listed_with_the_error()
    {
        var server = Server(List(Storm("al092022", "Ian", "HU", "105", "020", Issued)));
        var http = new HttpClient(server);
        var feed = Feed();
        var downloads = Path.Combine(_temp.Path, "downloads");
        var indexer = new WeatherFeedIndexer(http, downloads);
        var item = Assert.Single((await indexer.IndexAsync(feed, null, TestContext.Current.CancellationToken)).Items);
        await new EncCellDownloader(http, Path.Combine(downloads, WeatherFeedIndexer.DownloadFolderFor(feed)))
            .DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken);
        server.Remove(ListUri);

        var index = await indexer.IndexAsync(feed, null, TestContext.Current.CancellationToken);

        Assert.Equal(IndexDiagnosticSeverity.Error, Assert.Single(index.Diagnostics).Severity);
        var offline = Assert.Single(index.Items);
        Assert.Equal("al092022", offline.Key);
        Assert.IsType<LocalItemLocation>(offline.Location);
        Assert.Equal(Issued, S100ForecastFeedIndexer.RunOf(offline));
    }

    [Fact]
    public async Task The_scope_has_nothing_to_choose_and_says_how_many_storms_are_active_as_a_FEED()
    {
        var server = Server(List(Storm("al092022", "Ian", "HU", "105", "020", Issued), Storm("ep142026", "Arlo", "TS", "50", "006", Issued)));
        var indexer = new WeatherFeedIndexer(new HttpClient(server));
        var scope = new WeatherScope(Known, (feed, ct) => indexer.DescribeAsync(feed, ct), () => new GeoBounds(30, -80, 40, -70));

        Assert.Null(await scope.LoadAsync(TestContext.Current.CancellationToken));

        Assert.Empty(scope.Groups);
        Assert.Null(scope.Area);
        Assert.Equal(Issued, scope.LatestRun);
        Assert.Equal("2 active · Atlantic, East and Central Pacific", scope.SelectionSummary);
        Assert.Equal("2 active · Atlantic, East and Central Pacific", scope.SingleEntry?.Detail);
        Assert.Equal("FEED", LibraryNodeText.KindOf(scope.Build(Guid.NewGuid(), null)));
    }

    /// <summary>Serves files by address; 404 for anything else.</summary>
    private sealed class StormServer : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public byte[] this[string uri]
        {
            set => _files[uri] = value;
        }

        public void Remove(string uri) => _files.Remove(uri);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_files.TryGetValue(request.RequestUri!.AbsoluteUri, out var bytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
