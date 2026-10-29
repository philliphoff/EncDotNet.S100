using System.Text.Json;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Persistence;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// The Library's weather sources (#1075): the known-sources entries, the
/// requests each service is asked, runs and staleness, the downloads they
/// assemble, pinning, and "load latest run", against a fake of every service.
/// </summary>
public sealed class WeatherSourceTests : IDisposable
{
    // The fixtures' run (tests/datasets/GRIB2): 2026-10-25 00Z, at f000, f003 and f006.
    private static readonly DateTimeOffset Run = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    private static readonly GeoBounds UsEast = new(32.1, -75.9, 39.9, -68.2);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static KnownCatalogueSource Known(string id) =>
        KnownCatalogueSources.Find(id) ?? throw new InvalidOperationException($"No known source {id}.");

    private static WeatherFeedSource Feed(string id, WeatherSelection? selection = null, Guid? sourceId = null)
    {
        var known = Known(id);
        return new WeatherFeedSource(sourceId ?? Guid.NewGuid(), null, known.CatalogUri, known.Weather!, selection ?? new WeatherSelection())
        {
            Licence = known.Licence,
            Attribution = known.Attribution,
        };
    }

    /// <summary>The GFS-Wave fixture split by hour: HTSGW, PERPW, DIRPW at f000, f003, f006.</summary>
    private static WeatherFakeServer WaveServer()
    {
        var server = new WeatherFakeServer();
        server.Runs.Add(Run);
        var messages = WeatherFakeServer.Split(File.ReadAllBytes(TestPaths.Dataset("GRIB2", "gfswave-useast.grib2")));
        for (int step = 0; step < 3; step++)
        {
            server.Messages[step * 3] =
            [
                ("HTSGW", "surface", messages[step * 3]),
                ("PERPW", "surface", messages[(step * 3) + 1]),
                ("DIRPW", "surface", messages[(step * 3) + 2]),
            ];
        }

        return server;
    }

    private static FakeTimeProvider At(DateTimeOffset now) => new(now);

    private static readonly WeatherSelection ThreeHours = new(Hours: [0, 3, 6], Area: UsEast);

    [Fact]
    public void The_known_sources_list_the_weather_services_with_their_licence_and_attribution()
    {
        var weather = KnownCatalogueSources.All.Where(s => s.Format == KnownCatalogueFormat.Weather).ToArray();

        Assert.Equal(
            ["noaa-nomads-gfswave", "noaa-nomads-gfs", "noaa-aws-gfswave", "noaa-aws-gfs", "nws-marine-alerts", "noaa-erddap-ww3"],
            weather.Select(s => s.Id));
        Assert.All(weather, s =>
        {
            Assert.NotNull(s.Weather);
            Assert.True(s.Weather!.IsUsable);
            Assert.Equal("Public domain", s.Licence);
            Assert.False(string.IsNullOrEmpty(s.Attribution));
            Assert.True(s.NotForNavigation);
            Assert.Equal(LibrarySourceKind.Weather, LibrarySourceKinds.Of(s.Format));
        });
        Assert.Equal(WeatherServiceKind.GribFilter, Known("noaa-nomads-gfswave").Weather!.Kind);
        Assert.Equal(WeatherServiceKind.GribIndex, Known("noaa-aws-gfs").Weather!.Kind);
        Assert.Equal(WeatherServiceKind.NwsAlerts, Known("nws-marine-alerts").Weather!.Kind);
        Assert.Equal(WeatherServiceKind.ErddapGriddap, Known("noaa-erddap-ww3").Weather!.Kind);
        Assert.Equal(["HTSGW", "PERPW", "DIRPW", "UGRD", "VGRD"],
            Known("noaa-nomads-gfswave").Weather!.Parameters.Where(p => p.Default).Select(p => p.Id));
    }

    [Fact]
    public void A_weather_entry_round_trips_through_a_known_sources_document()
    {
        var original = Known("noaa-nomads-gfs");
        using var stream = new MemoryStream();
        KnownCatalogueSources.Write(stream, [original]);
        stream.Position = 0;

        var read = Assert.Single(KnownCatalogueSources.Read(stream));

        Assert.Equal(original.Licence, read.Licence);
        Assert.Equal(original.Attribution, read.Attribution);
        Assert.Equal(original.Weather!.Kind, read.Weather!.Kind);
        Assert.Equal(original.Weather.File, read.Weather.File);
        Assert.Equal(original.Weather.Parameters, read.Weather.Parameters);
    }

    [Fact]
    public void A_weather_entry_without_its_required_fields_is_skipped()
    {
        using var stream = new MemoryStream("""
            {"version":1,"sources":[
              {"id":"a","name":"A","format":"weather","catalogUri":"https://example.test/","weather":{"service":"gribFilter","model":"gfs"}},
              {"id":"b","name":"B","format":"weather","catalogUri":"https://example.test/","weather":{"service":"teleport","model":"x"}},
              {"id":"c","name":"C","format":"weather","catalogUri":"https://example.test/alerts/active","weather":{"service":"nwsAlerts","model":"alerts"}}
            ]}
            """u8.ToArray());

        Assert.Equal(["c"], KnownCatalogueSources.Read(stream).Select(s => s.Id));
    }

    [Fact]
    public void The_grib_filter_url_names_the_runs_file_the_parameters_their_levels_and_the_area_rounded_out_to_the_grid()
    {
        var service = Known("noaa-nomads-gfs").Weather!;
        var parameters = service.Parameters.Where(p => p.Default).ToArray();

        var uri = WeatherRequests.GribFilterUri(Known("noaa-nomads-gfs").CatalogUri, service, parameters, UsEast, Run.AddHours(6), 3);

        Assert.Equal(
            "https://nomads.ncep.noaa.gov/cgi-bin/filter_gfs_0p25.pl?dir=%2Fgfs.20261025%2F06%2Fatmos&file=gfs.t06z.pgrb2.0p25.f003"
            + "&var_UGRD=on&var_VGRD=on&var_PRMSL=on&lev_10_m_above_ground=on&lev_mean_sea_level=on"
            + "&subregion=&toplat=40&leftlon=-76&rightlon=-68&bottomlat=32",
            uri.AbsoluteUri);
    }

    [Fact]
    public void An_area_across_the_antimeridian_is_asked_for_with_an_east_edge_past_180()
    {
        var (south, west, north, east) = WeatherRequests.Rounded(new GeoBounds(50.1, 170.1, 55.9, -170.1), 0.25);

        Assert.Equal((50, 170, 56, 190), (south, west, north, east));
    }

    [Fact]
    public void Whole_files_and_their_inventories_are_addressed_under_the_data_root()
    {
        var service = Known("noaa-aws-gfswave").Weather!;

        Assert.Equal(
            "https://noaa-gfs-bdp-pds.s3.amazonaws.com/gfs.20261025/00/wave/gridded/gfswave.t00z.global.0p25.f120.grib2",
            WeatherRequests.FileUri(service, Run, 120).AbsoluteUri);
        Assert.Equal(
            "https://noaa-gfs-bdp-pds.s3.amazonaws.com/gfs.20261025/00/wave/gridded/gfswave.t00z.global.0p25.f120.grib2.idx",
            WeatherRequests.IndexUri(service, Run, 120).AbsoluteUri);
        Assert.Equal(
            "https://noaa-gfs-bdp-pds.s3.amazonaws.com/gfs.20261025/00/atmos/gfs.t00z.pgrb2.0p50.f003",
            WeatherRequests.FileUri(Known("noaa-aws-gfs").Weather!, Run, 3).AbsoluteUri);
    }

    [Fact]
    public void Alerts_are_asked_for_by_zone_by_marine_area_past_fifty_zones_or_every_marine_alert()
    {
        var alerts = Known("nws-marine-alerts").CatalogUri;
        var many = Enumerable.Range(0, 60).Select(i => $"ANZ{500 + i}").Append("PZZ530").ToArray();

        Assert.Equal("https://api.weather.gov/alerts/active?zone=ANZ535,PZZ530", WeatherRequests.NwsAlertsUri(alerts, ["pzz530", "ANZ535"]).AbsoluteUri);
        Assert.Equal("https://api.weather.gov/alerts/active?area=AN,PZ", WeatherRequests.NwsAlertsUri(alerts, many).AbsoluteUri);
        Assert.Equal("https://api.weather.gov/alerts/active?region_type=marine", WeatherRequests.NwsAlertsUri(alerts, []).AbsoluteUri);
    }

    [Fact]
    public void An_erddap_subset_constrains_every_axis_in_order_with_a_descending_latitude_and_0_to_360_longitudes()
    {
        var dataset = Known("noaa-erddap-ww3").Weather!.Datasets[0];
        ErddapAxis[] axes =
        [
            new("time"), new("depth"), new("latitude", Descending: true, -77.5, 77.5), new("longitude", false, 0, 359.5),
        ];

        var uri = WeatherRequests.ErddapGriddapUri(
            new Uri("https://coastwatch.pfeg.noaa.gov/erddap/"), dataset, axes, UsEast, Run, Run.AddHours(48));

        var constraint = "[(2026-10-25T00:00:00Z):1:(2026-10-27T00:00:00Z)][0][(39.9):1:(32.1)][(284.1):1:(291.8)]";
        Assert.Equal("https://coastwatch.pfeg.noaa.gov/erddap/griddap/NWW3_Global_Best.nc", uri.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            string.Join(',', dataset.Variables.Select(v => v + constraint)),
            Uri.UnescapeDataString(uri.Query.TrimStart('?')));
    }

    [Fact]
    public void Candidate_runs_are_the_last_cycles_newest_first()
    {
        Assert.Equal(
            [Run.AddHours(6), Run, Run.AddHours(-6)],
            WeatherRequests.CandidateRuns(new DateTimeOffset(2026, 10, 25, 11, 59, 0, TimeSpan.Zero), 6, 3));
    }

    [Fact]
    public void A_real_inventory_gives_the_byte_ranges_of_the_chosen_fields_joining_neighbours()
    {
        var index = GribIndex.Parse(File.ReadAllText(TestPaths.Dataset("GRIB2", "gfs.t18z.pgrb2.1p00.f003.idx")));

        Assert.Equal(743, index.Entries.Count);
        var ranges = index.RangesFor([":UGRD:10 m above ground:", ":VGRD:10 m above ground:", ":PRMSL:mean sea level:"]);

        // PRMSL is message 1; UGRD and VGRD at 10 m are messages 588 and 589, read as one range up to message 590.
        Assert.Equal(2, ranges.Count);
        Assert.Equal(new GribByteRange(0, 83617), ranges[0]);
        var next = index.Entries[589].Offset;
        Assert.Equal(new GribByteRange(index.Entries[587].Offset, next - 1), ranges[1]);
        Assert.Equal("bytes=0-83617", ranges[0].HeaderValue);
    }

    [Fact]
    public void The_last_message_of_an_inventory_runs_to_the_end_of_the_file()
    {
        var index = GribIndex.Parse(File.ReadAllText(TestPaths.Dataset("GRIB2", "gfswave.t18z.global.0p25.f003.grib2.idx")));

        var range = Assert.Single(index.RangesFor([":SWDIR:3 in sequence:"]));

        Assert.Equal(new GribByteRange(10906438, null), range);
        Assert.Equal("bytes=10906438-", range.HeaderValue);
        Assert.Empty(index.RangesFor([":TMP:2 m above ground:"]));
    }

    [Fact]
    public void A_line_that_isnt_an_inventory_line_is_refused()
    {
        Assert.Throws<FormatException>(() => GribIndex.Parse("<html>Not found</html>"));
    }

    [Fact]
    public async Task A_grib_filter_source_lists_the_latest_published_run_as_one_download_of_a_request_per_hour()
    {
        var server = WaveServer();
        var indexer = new WeatherFeedIndexer(new HttpClient(server), timeProvider: At(Run.AddHours(5.5)));
        var feed = Feed("noaa-nomads-gfswave", ThreeHours);

        var index = await indexer.IndexAsync(feed, null, TestContext.Current.CancellationToken);

        Assert.Empty(index.Diagnostics);
        var item = Assert.Single(index.Items);
        Assert.Equal("GRIB2", item.ProductSpec);
        Assert.Equal("gfswave", item.Name);
        Assert.Equal("GFS-Wave", item.Title);
        Assert.Equal(Run, S100ForecastFeedIndexer.RunOf(item));
        Assert.Equal(Run.AddHours(6), S100ForecastFeedIndexer.ValidToOf(item));
        Assert.Equal(Run, index.PublishedAt);
        Assert.True(ForecastRuns.IsForecast(item));
        Assert.True(WeatherRuns.IsWeatherRun(item));
        Assert.Equal("Public domain", WeatherRuns.LicenceOf(item));
        Assert.Equal("NOAA/NCEP GFS-Wave (WAVEWATCH III) · Public domain", WeatherRuns.CreditOf(item));
        Assert.Equal(new GeoBounds(32, -76, 40, -68), item.Bounds);

        var remote = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.Equal(Run, remote.LastModified);
        Assert.Equal("gfswave.t00z.20261025.grib2", remote.Layout!.RelativePath);
        Assert.StartsWith("weather/nomads.ncep.noaa.gov-", remote.DownloadFolder, StringComparison.Ordinal);
        Assert.Equal(3, remote.Parts!.Count);
        Assert.All(remote.Parts, p => Assert.Null(p.GribFields));
        Assert.Contains("file=gfswave.t00z.global.0p25.f006.grib2", remote.Parts[2].Uri.Query, StringComparison.Ordinal);

        // The 06Z run isn't published yet (its last hour's .idx answers 404), so 00Z is the latest.
        var probes = server.Requests.Where(r => r.Method == HttpMethod.Head).Select(r => r.Uri.AbsolutePath).ToArray();
        Assert.Equal(
            [
                "/pub/data/nccf/com/gfs/prod/gfs.20261025/00/wave/gridded/gfswave.t00z.global.0p25.f006.grib2.idx",
            ],
            probes);
        Assert.All(server.Requests, r => Assert.StartsWith("EncDotNet.S100/", r.UserAgent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_still_being_published_is_passed_over_for_the_one_before()
    {
        var server = WaveServer();
        var indexer = new WeatherFeedIndexer(new HttpClient(server), timeProvider: At(Run.AddHours(7)));

        var index = await indexer.IndexAsync(Feed("noaa-nomads-gfswave", ThreeHours), null, TestContext.Current.CancellationToken);

        Assert.Equal(Run, S100ForecastFeedIndexer.RunOf(Assert.Single(index.Items)));
        Assert.Equal(2, server.Requests.Count(r => r.Method == HttpMethod.Head));
    }

    [Fact]
    public async Task No_published_run_is_an_error_and_lists_nothing()
    {
        var server = WaveServer();
        server.Runs.Clear();
        var indexer = new WeatherFeedIndexer(new HttpClient(server), timeProvider: At(Run.AddHours(5)));

        var index = await indexer.IndexAsync(Feed("noaa-nomads-gfswave", ThreeHours), null, TestContext.Current.CancellationToken);

        Assert.Empty(index.Items);
        Assert.Contains("No GFS-Wave run of the last 30 hours", Assert.Single(index.Diagnostics).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_grib_filter_run_downloads_as_the_hours_appended_into_one_file()
    {
        var server = WaveServer();
        var http = new HttpClient(server);
        var feed = Feed("noaa-nomads-gfswave", ThreeHours);
        var item = Assert.Single((await new WeatherFeedIndexer(http, timeProvider: At(Run.AddHours(5))).IndexAsync(feed, null, TestContext.Current.CancellationToken)).Items);
        var remote = (RemoteItemLocation)item.Location;
        var downloader = new EncCellDownloader(http, Path.Combine(_temp.Path, remote.DownloadFolder!));

        var cell = await downloader.DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken);

        var file = Path.Combine(cell.Location.RootPath, cell.Location.RelativePath);
        Assert.Equal("gfswave.t00z.20261025.grib2", Path.GetFileName(file));
        Assert.Equal(File.ReadAllBytes(TestPaths.Dataset("GRIB2", "gfswave-useast.grib2")), File.ReadAllBytes(file));
        Assert.Equal(Run, cell.PublishedAt);
        Assert.All(server.Requests.Where(r => r.Uri.AbsolutePath.StartsWith("/cgi-bin/", StringComparison.Ordinal)),
            r => Assert.False(string.IsNullOrEmpty(r.UserAgent)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_aws_run_reads_only_the_chosen_fields_by_byte_range_from_each_hours_inventory(bool serverIgnoresRanges)
    {
        var server = WaveServer();
        server.IgnoreRanges = serverIgnoresRanges;
        var http = new HttpClient(server);
        var feed = Feed("noaa-aws-gfswave", new WeatherSelection([":HTSGW:surface:", ":DIRPW:surface:"], [0, 3, 6]));
        var index = await new WeatherFeedIndexer(http, timeProvider: At(Run.AddHours(5))).IndexAsync(feed, null, TestContext.Current.CancellationToken);
        var item = Assert.Single(index.Items);
        var remote = (RemoteItemLocation)item.Location;
        Assert.Equal(new GeoBounds(-90, -180, 90, 180), item.Bounds);
        Assert.All(remote.Parts!, p => Assert.Equal([":HTSGW:surface:", ":DIRPW:surface:"], p.GribFields));

        var cell = await new EncCellDownloader(http, Path.Combine(_temp.Path, "aws"))
            .DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken);

        var messages = WeatherFakeServer.Split(File.ReadAllBytes(TestPaths.Dataset("GRIB2", "gfswave-useast.grib2")));
        byte[] expected = [.. new[] { 0, 2, 3, 5, 6, 8 }.SelectMany(i => messages[i])];
        Assert.Equal(expected, File.ReadAllBytes(Path.Combine(cell.Location.RootPath, cell.Location.RelativePath)));
        // HTSGW and DIRPW aren't neighbours, so two ranges per hour, the filler message never read.
        var ranged = server.Requests.Where(r => r.Range is not null).ToArray();
        Assert.Equal(6, ranged.Length);
        Assert.DoesNotContain(ranged, r => r.Range!.StartsWith("bytes=0-", StringComparison.Ordinal));
        Assert.Equal(3, server.Requests.Count(r => r.Method == HttpMethod.Get && r.Uri.AbsolutePath.EndsWith(".idx", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_aws_run_whose_inventories_lack_the_chosen_fields_fails_and_keeps_nothing()
    {
        var server = WaveServer();
        var http = new HttpClient(server);
        var feed = Feed("noaa-aws-gfswave", new WeatherSelection([":WVHGT:surface:"], [0]));
        var item = Assert.Single((await new WeatherFeedIndexer(http, timeProvider: At(Run.AddHours(5))).IndexAsync(feed, null, TestContext.Current.CancellationToken)).Items);
        var downloader = new EncCellDownloader(http, Path.Combine(_temp.Path, "aws"));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("None of the chosen fields", error.Message, StringComparison.Ordinal);
        Assert.Empty(downloader.ListDownloaded());
    }

    [Fact]
    public async Task Alerts_are_read_with_an_identifying_user_agent_and_downloaded_as_geojson()
    {
        var server = WaveServer();
        server.AlertsJson = File.ReadAllText(TestPaths.Dataset("CAP", "nws-alerts-marine-20260828.geojson"));
        var http = new HttpClient(server);
        var feed = Feed("nws-marine-alerts", new WeatherSelection(Area: UsEast, Zones: ["ANZ430", "ANZ431"]));

        var index = await new WeatherFeedIndexer(http, timeProvider: At(Run)).IndexAsync(feed, null, TestContext.Current.CancellationToken);

        var item = Assert.Single(index.Items);
        Assert.Equal("CAP", item.ProductSpec);
        Assert.Equal("2", item.Properties[WeatherFeedIndexer.AlertCountProperty]);
        Assert.False(ForecastRuns.IsForecast(item));
        var remote = (RemoteItemLocation)item.Location;
        Assert.Equal("https://api.weather.gov/alerts/active?zone=ANZ430,ANZ431", remote.Uri.AbsoluteUri);
        Assert.Equal("application/geo+json", remote.Accept);

        var cell = await new EncCellDownloader(http, Path.Combine(_temp.Path, "alerts"))
            .DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("nws-marine-alerts.json", cell.Location.RelativePath);
        Assert.Equal(server.AlertsJson, File.ReadAllText(Path.Combine(cell.Location.RootPath, cell.Location.RelativePath)));
        var alertRequests = server.Requests.Where(r => r.Uri.Host == "api.weather.gov").ToArray();
        Assert.Equal(2, alertRequests.Length);
        Assert.All(alertRequests, r =>
        {
            Assert.Matches(@"^EncDotNet\.S100/\S+ \(\+https://github\.com/philliphoff/EncDotNet\.S100\)$", r.UserAgent);
            Assert.DoesNotContain("@", r.UserAgent, StringComparison.Ordinal);
            Assert.Contains("application/geo+json", r.Accept, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_default_user_agent_names_the_project_not_a_person()
    {
        Assert.Matches(@"^EncDotNet\.S100/[0-9][^ ]* \(\+https://github\.com/philliphoff/EncDotNet\.S100\)$", Net.LibraryHttp.DefaultUserAgent);
    }

    private const string ErddapInfo = """
        {"table":{"columnNames":["Row Type","Variable Name","Attribute Name","Data Type","Value"],"rows":[
          ["attribute","NC_GLOBAL","date_issued","String","2026-10-25T04:10:00Z"],
          ["attribute","NC_GLOBAL","time_coverage_start","String","2026-10-17T00:00:00Z"],
          ["attribute","NC_GLOBAL","time_coverage_end","String","2026-11-01T00:00:00Z"],
          ["attribute","NC_GLOBAL","title","String","Wave Watch 3 (NWW3) Global Wave Model, Best Time Series"],
          ["dimension","time","","double","nValues=120, evenlySpaced=true, averageSpacing=3h 0m 0s"],
          ["attribute","time","actual_range","double","1.7606592E9, 1.7619552E9"],
          ["dimension","depth","","float","nValues=1, onlyValue=0.0"],
          ["dimension","latitude","","float","nValues=311, evenlySpaced=true, averageSpacing=-0.5"],
          ["attribute","latitude","actual_range","float","-77.5, 77.5"],
          ["dimension","longitude","","float","nValues=720, evenlySpaced=true, averageSpacing=0.5"],
          ["attribute","longitude","actual_range","float","0.0, 359.5"],
          ["variable","Thgt","","float","time, depth, latitude, longitude"]
        ]}}
        """;

    [Fact]
    public async Task An_erddap_dataset_is_subset_from_now_to_the_chosen_hours_as_netcdf()
    {
        var server = WaveServer();
        server.ErddapInfoJson = ErddapInfo;
        server.ErddapNetCdf = "CDF\u0001fake"u8.ToArray();
        var http = new HttpClient(server);
        var feed = Feed("noaa-erddap-ww3", new WeatherSelection(Hours: [0, 3, 6, 9, 12, 15, 18, 21, 24], Area: UsEast));

        var index = await new WeatherFeedIndexer(http, timeProvider: At(Run.AddHours(5))).IndexAsync(feed, null, TestContext.Current.CancellationToken);

        var item = Assert.Single(index.Items);
        Assert.Equal("NetCDF", item.ProductSpec);
        Assert.Equal("NWW3_Global_Best", item.Name);
        Assert.Equal("Wave Watch 3 (NWW3) Global Wave Model, Best Time Series", item.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 4, 10, 0, TimeSpan.Zero), S100ForecastFeedIndexer.RunOf(item));
        var remote = (RemoteItemLocation)item.Location;
        Assert.Equal(
            "Thgt[(2026-10-25T02:00:00Z):1:(2026-10-26T05:00:00Z)][0][(39.9):1:(32.1)][(284.1):1:(291.8)]",
            Uri.UnescapeDataString(remote.Uri.Query.TrimStart('?')).Split(',')[0]);

        var cell = await new EncCellDownloader(http, Path.Combine(_temp.Path, "erddap"))
            .DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("NWW3_Global_Best.nc", cell.Location.RelativePath);
    }

    [Fact]
    public void Erddap_metadata_gives_the_axes_in_order_their_direction_and_ranges()
    {
        using var document = JsonDocument.Parse(ErddapInfo);

        var info = WeatherFeedIndexer.ParseErddapInfo(document.RootElement);

        Assert.Equal(["time", "depth", "latitude", "longitude"], info.Axes.Select(a => a.Name));
        Assert.True(info.Axes[2].Descending);
        Assert.False(info.Axes[3].Descending);
        Assert.Equal(359.5, info.Axes[3].Maximum);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), info.End);
    }

    [Fact]
    public void A_run_older_than_12_hours_is_stale_and_a_pinned_one_never_is()
    {
        var item = new CollectionItem
        {
            Key = "gfswave",
            ProductSpec = "GRIB2",
            Name = "gfswave",
            Location = new RemoteItemLocation(new Uri("https://example.test/x")),
            Properties = new Dictionary<string, string>
            {
                [S100ForecastFeedIndexer.RunProperty] = "2026-10-25T00:00:00Z",
                [S100ForecastFeedIndexer.ValidToProperty] = "2026-10-30T00:00:00Z",
                [WeatherRuns.StaleAfterProperty] = "12",
            },
        };

        Assert.False(WeatherRuns.IsStale(item, null, Run.AddHours(12)));
        Assert.True(WeatherRuns.IsStale(item, null, Run.AddHours(12.1)));
        // The downloaded copy's run is what's shown: a newer listed run doesn't make an old copy fresh.
        Assert.True(WeatherRuns.IsStale(item, Run.AddHours(-6), Run.AddHours(7)));
        Assert.False(WeatherRuns.IsStale(item with { Properties = new Dictionary<string, string>(item.Properties) { [WeatherRuns.PinnedProperty] = "true" } }, null, Run.AddDays(3)));
        // An S-100 forecast feed's run has no staleness rule.
        Assert.False(WeatherRuns.IsStale(item with { Properties = new Dictionary<string, string> { [S100ForecastFeedIndexer.RunProperty] = "2026-10-25T00:00:00Z" } }, null, Run.AddDays(3)));

        var source = new LibrarySource(Feed("noaa-nomads-gfswave"), null, LibrarySourceState.Ready);
        Assert.True(new LibraryItemState(item, source, time: At(Run.AddHours(13))).IsStaleRun);
        Assert.False(new LibraryItemState(item, source, time: At(Run.AddHours(1))).IsStaleRun);
    }

    [Fact]
    public async Task A_pinned_run_is_kept_beside_the_latest_and_listed_as_a_local_item_until_unpinned()
    {
        var server = WaveServer();
        var http = new HttpClient(server);
        var downloads = Path.Combine(_temp.Path, "downloads");
        var feed = Feed("noaa-nomads-gfswave", ThreeHours);
        var clock = At(Run.AddHours(5));
        var indexer = new WeatherFeedIndexer(http, downloads, clock);
        var library = new LibraryDownloads(LibraryDownloads.ManagedFolders(http, downloads));
        var first = Assert.Single((await indexer.IndexAsync(feed, null, TestContext.Current.CancellationToken)).Items);
        await library.DownloadAsync([first], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("gfswave.run-2026102500", library.Pin(first));

        // The next run is published and downloaded: it replaces the latest copy, not the pinned one.
        server.Runs.Add(Run.AddHours(6));
        clock.SetUtcNow(Run.AddHours(11));
        var index = await indexer.IndexAsync(feed, null, TestContext.Current.CancellationToken);
        Assert.Equal(2, index.Items.Count);
        var latest = index.Items.Single(i => !WeatherRuns.IsPinned(i));
        var pinned = index.Items.Single(WeatherRuns.IsPinned);
        Assert.Equal(Run.AddHours(6), S100ForecastFeedIndexer.RunOf(latest));
        Assert.Equal("gfswave@2026102500", pinned.Key);
        Assert.Equal(Run, S100ForecastFeedIndexer.RunOf(pinned));
        Assert.IsType<LocalItemLocation>(pinned.Location);
        Assert.Equal("GFS-Wave 2026-10-25 00Z", pinned.Title);
        Assert.True(library.IsOutdated(latest));

        await library.DownloadAsync([latest], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(Run.AddHours(6), library.LocalPublishedAtOf(latest));
        var kept = (LocalItemLocation)pinned.Location;
        Assert.True(File.Exists(Path.Combine(kept.RootPath, kept.RelativePath)));

        Assert.True(library.Unpin(pinned));
        Assert.DoesNotContain((await indexer.IndexAsync(feed, null, TestContext.Current.CancellationToken)).Items, WeatherRuns.IsPinned);
    }

    [Fact]
    public async Task An_unreachable_service_lists_its_downloaded_run_with_the_error()
    {
        var server = WaveServer();
        var http = new HttpClient(server);
        var downloads = Path.Combine(_temp.Path, "downloads");
        var feed = Feed("noaa-nomads-gfswave", ThreeHours);
        var indexer = new WeatherFeedIndexer(http, downloads, At(Run.AddHours(5)));
        var item = Assert.Single((await indexer.IndexAsync(feed, null, TestContext.Current.CancellationToken)).Items);
        await new LibraryDownloads(LibraryDownloads.ManagedFolders(http, downloads)).DownloadAsync([item], cancellationToken: TestContext.Current.CancellationToken);

        var offline = new WeatherFeedIndexer(new HttpClient(new FailingHandler()), downloads, At(Run.AddHours(20)));
        var index = await offline.IndexAsync(feed, null, TestContext.Current.CancellationToken);

        Assert.Single(index.Diagnostics);
        var listed = Assert.Single(index.Items);
        Assert.IsType<LocalItemLocation>(listed.Location);
        Assert.Equal(Run, S100ForecastFeedIndexer.RunOf(listed));
        Assert.True(WeatherRuns.IsStale(listed, null, Run.AddHours(20)));
    }

    [Fact]
    public async Task Adding_a_service_from_the_known_sources_reads_its_latest_run_and_builds_the_ticked_selection()
    {
        var server = WaveServer();
        server.Messages[48] = server.Messages[6]; // The default hours reach f048.
        var indexer = new WeatherFeedIndexer(new HttpClient(server), timeProvider: At(Run.AddHours(5)));
        var readers = new LibraryCatalogueReaders
        {
            Weather = (source, ct) => indexer.DescribeAsync(source, ct),
            CurrentMapView = () => UsEast,
        };
        var draft = LibrarySourceDraft.ForCatalogue(Known("noaa-nomads-gfswave"), readers)!;

        Assert.Null(await draft.LoadAsync(TestContext.Current.CancellationToken));

        var scope = Assert.IsType<WeatherScope>(draft.Scope);
        Assert.False(draft.IncludeAll);
        Assert.Equal(["Parameters", "Forecast days"], draft.Groups.Select(g => g.Title));
        Assert.Equal(Run, scope.LatestRun);
        Assert.Contains("Latest run 2026-10-25 00Z", scope.RunNote, StringComparison.Ordinal);
        Assert.Equal("5 parameters · 17 forecast hours to +48 h · Area 32.1°N–39.9°N, 75.9°W–68.2°W", draft.ScopeSummary);
        Assert.Equal("GFS-Wave wave forecast — NOMADS GRIB filter", draft.SuggestedName);

        // Untick the wind and day 2.
        foreach (var choice in draft.Groups[0].Options.Where(o => o.Value.Contains("GRD", StringComparison.Ordinal)))
            choice.IsSelected = false;
        draft.Groups[1].Options[1].IsSelected = false;

        var source = Assert.IsType<WeatherFeedSource>(draft.Build());
        Assert.Equal([":HTSGW:surface:", ":PERPW:surface:", ":DIRPW:surface:"], source.Selection.Parameters);
        Assert.Equal([0, 3, 6, 9, 12, 15, 18, 21, 24], source.Selection.Hours);
        Assert.Equal(UsEast, source.Selection.Area);
        Assert.Equal("Public domain", source.Licence);
        Assert.Equal("NOAA/NCEP GFS-Wave (WAVEWATCH III)", source.Attribution);

        // The source is saved and read back whole.
        var store = CollectionJson.SerializeStore(new CollectionStoreDocument(1, [new DatasetCollection(Guid.NewGuid(), "Waves", [source], Run)]));
        var read = Assert.IsType<WeatherFeedSource>(Assert.Single(Assert.Single(CollectionJson.DeserializeStore(store).Collections).Sources));
        Assert.Equal(source.Selection.Parameters, read.Selection.Parameters);
        Assert.Equal(source.Selection.Hours, read.Selection.Hours);
        Assert.Equal(source.Selection.Area, read.Selection.Area);
        Assert.Equal(source.Service.Parameters, read.Service.Parameters);
        Assert.Equal(source.Attribution, read.Attribution);
        Assert.Contains("\"kind\": \"weatherFeed\"", store, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"gribFilter\"", store, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Adding_alerts_takes_the_marine_zones_in_view_and_has_nothing_to_choose()
    {
        var server = WaveServer();
        server.AlertsJson = File.ReadAllText(TestPaths.Dataset("CAP", "nws-alerts-marine-20260828.geojson"));
        var indexer = new WeatherFeedIndexer(new HttpClient(server), timeProvider: At(Run));
        GeoBounds? asked = null;
        var readers = new LibraryCatalogueReaders
        {
            Weather = (source, ct) => indexer.DescribeAsync(source, ct),
            CurrentMapView = () => UsEast,
            MarineZones = area => { asked = area; return ["ANZ430", "ANZ431"]; },
        };
        var draft = LibrarySourceDraft.ForCatalogue(Known("nws-marine-alerts"), readers)!;

        Assert.Null(await draft.LoadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(UsEast, asked);
        Assert.Empty(draft.Groups);
        Assert.Equal("2 alerts in force · 2 marine zones in view", draft.Scope!.SingleEntry!.Detail);
        var source = Assert.IsType<WeatherFeedSource>(draft.Build());
        Assert.Equal(["ANZ430", "ANZ431"], source.Selection.Zones);
    }

    [Fact]
    public async Task An_aws_source_takes_the_whole_globe_whatever_the_view()
    {
        var indexer = new WeatherFeedIndexer(new HttpClient(WaveServer()), timeProvider: At(Run.AddHours(5)));
        var draft = LibrarySourceDraft.ForCatalogue(Known("noaa-aws-gfswave"), new LibraryCatalogueReaders
        {
            Weather = (source, ct) => indexer.DescribeAsync(source, ct),
            CurrentMapView = () => UsEast,
        })!;

        // The default hours reach f024, which the fake doesn't publish: no run is found.
        Assert.Contains("No GFS-Wave run", await draft.LoadAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Null(Assert.IsType<WeatherScope>(draft.Scope).Area);
    }

    [Fact]
    public async Task Load_latest_run_refreshes_the_source_downloads_its_newest_run_and_opens_it()
    {
        using var context = new LibraryContext();
        var server = WaveServer();
        var http = new HttpClient(server);
        var downloads = Path.Combine(context.Root, "downloads");
        var clock = At(Run.AddHours(5));
        var indexer = new WeatherFeedIndexer(http, downloads, clock);
        using var library = context.CreateLibrary(CollectionIndexer.CreateDefault(feeds: [indexer]));
        library.Initialize();
        var opener = new RecordingOpener();
        var operations = new LibraryOperations(library, new LibraryDownloads(LibraryDownloads.ManagedFolders(http, downloads)), new LibraryLoader(opener));
        var feed = Feed("noaa-nomads-gfswave", ThreeHours);
        library.AddCollection("Waves", [feed]);
        await library.WhenIdle();

        var first = await operations.LoadLatestRunAsync(feed.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, first!.Download.Downloaded);
        Assert.Equal(1, first.Load!.Opened);

        // Thirteen hours on, the copy is stale; a newer run is out, and "load latest run" fetches it.
        clock.SetUtcNow(Run.AddHours(13));
        server.Runs.Add(Run.AddHours(6));
        var source = library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == feed.Id);
        Assert.True(operations.StateOf(source.Index!.Items[0], source, clock).IsStaleRun);

        var second = await operations.LoadLatestRunAsync(feed.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, second!.Download.Downloaded);
        source = library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == feed.Id);
        var state = operations.StateOf(source.Index!.Items[0], source, clock);
        Assert.Equal(Run.AddHours(6), state.LocalRun);
        Assert.False(state.IsStaleRun);

        // Already the latest: opened again without a download.
        var third = await operations.LoadLatestRunAsync(feed.Id, TestContext.Current.CancellationToken);
        Assert.Equal(0, third!.Download.Downloaded);
        Assert.Equal(1, third.Load!.Opened);
    }

    [Fact]
    public async Task A_folder_of_grib2_files_indexes_as_one_dataset_the_probe_reads_whole()
    {
        var run = Directory.CreateDirectory(Path.Combine(_temp.Path, "charts", "gfs.20261025", "00"));
        var messages = WeatherFakeServer.Split(File.ReadAllBytes(TestPaths.Dataset("GRIB2", "gfswave-useast.grib2")));
        for (int step = 0; step < 3; step++)
            File.WriteAllBytes(Path.Combine(run.FullName, $"gfswave.t00z.global.0p25.f{step * 3:000}.grib2"), [.. messages.Skip(step * 3).Take(3).SelectMany(m => m)]);
        File.WriteAllText(Path.Combine(run.FullName, "gfswave.t00z.global.0p25.f000.grib2.idx"), "1:0:d=2026102500:HTSGW:surface:anl:\n");
        var probed = new List<string>();
        DatasetProbe probe = (path, _) =>
        {
            probed.Add(path);
            return Directory.Exists(path)
                ? new Core.DatasetMetadata { Spec = Core.SpecRef.Parse("GRIB2/2.0.0"), Extent = new EncDotNet.S100.Pipelines.BoundingBox(32, -76, 40, -68) }
                : null;
        };

        var index = await CollectionIndexer.CreateDefault(probe)
            .IndexAsync(new LocalFolderSource(Guid.NewGuid(), null, Path.Combine(_temp.Path, "charts")), cancellationToken: TestContext.Current.CancellationToken);

        var item = Assert.Single(index.Items);
        Assert.Equal("GRIB2", item.ProductSpec);
        Assert.Equal("00", item.Name);
        Assert.Equal("gfs.20261025/00/", item.Key);
        Assert.Equal("3", item.Properties["files"]);
        var location = Assert.IsType<LocalItemLocation>(item.Location);
        Assert.Equal(run.FullName, Path.Combine(location.RootPath, location.RelativePath));
        Assert.Equal([run.FullName], probed);
    }

    [Fact]
    public async Task A_lone_grib2_file_still_indexes_as_itself()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_temp.Path, "one"));
        File.Copy(TestPaths.Dataset("GRIB2", "gfs-useast-wind10m.grib2"), Path.Combine(folder.FullName, "gfs.t00z.pgrb2.0p25.f003"));
        DatasetProbe probe = (path, _) => new Core.DatasetMetadata { Spec = Core.SpecRef.Parse("GRIB2/2.0.0") };

        var index = await CollectionIndexer.CreateDefault(probe)
            .IndexAsync(new LocalFolderSource(Guid.NewGuid(), null, folder.FullName), cancellationToken: TestContext.Current.CancellationToken);

        var item = Assert.Single(index.Items);
        Assert.Equal("gfs.t00z.pgrb2.0p25.f003", item.Key);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No route to host.");
    }

    private sealed class RecordingOpener : ILibraryDatasetOpener
    {
        public event EventHandler? Changed;

        public List<string> Opened { get; } = [];

        public LibraryLoadState StateOf(LocalItemLocation location) => LibraryLoadState.None;

        public Task<LibraryOpenOutcome> OpenAsync(LibraryOpenGroup group, bool defer, CancellationToken cancellationToken)
        {
            foreach (var item in group.Items)
                Opened.Add(LibraryOpenGroup.LocationOf(item).RelativePath);
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(new LibraryOpenOutcome(group.Items.Count, []));
        }

        public int Close(IReadOnlyList<LocalItemLocation> locations) => 0;
    }
}
