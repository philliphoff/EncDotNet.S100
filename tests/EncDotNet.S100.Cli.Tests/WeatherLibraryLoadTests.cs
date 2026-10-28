using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Datasets.Cap;
using EncDotNet.S100.Mcp.Tools.Library;
using EncDotNet.S100.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #1075, end to end: each weather service is added from the Library's
/// online catalogues, indexed, downloaded and opened into the headless
/// catalog, against a fake of the service; a folder holding one run loads as
/// one dataset. The live smoke tests at the end reach the real services and
/// run only when <see cref="LiveVariable"/> is set.
/// </summary>
public sealed class WeatherLibraryLoadTests : IDisposable
{
    /// <summary>Set to <c>1</c> to run the live smoke tests (off in CI; NOMADS, api.weather.gov and ERDDAP must be reachable).</summary>
    public const string LiveVariable = "S100_LIVE_WEATHER_TESTS";

    private static readonly DateTimeOffset Run = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    private static readonly GeoBounds UsEast = new(32.1, -75.9, 39.9, -68.2);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "weather-library-" + Guid.NewGuid().ToString("N"));
    private readonly HeadlessMutableCatalog _catalog = new();
    private readonly List<IDisposable> _owned = [];

    public WeatherLibraryLoadTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var owned in _owned)
            owned.Dispose();
        _catalog.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string TestData(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "TestData", .. parts]);

    private static WeatherFakeServer Server()
    {
        var server = new WeatherFakeServer();
        server.Runs.Add(Run);
        var waves = WeatherFakeServer.Split(File.ReadAllBytes(TestData("GRIB2", "gfswave-useast.grib2")));
        var wind = WeatherFakeServer.Split(File.ReadAllBytes(TestData("GRIB2", "gfs-useast-wind10m.grib2")));
        for (int step = 0; step < 3; step++)
        {
            server.Messages[step * 3] =
            [
                ("HTSGW", "surface", waves[step * 3]),
                ("PERPW", "surface", waves[(step * 3) + 1]),
                ("DIRPW", "surface", waves[(step * 3) + 2]),
                ("UGRD", "10 m above ground", wind[step * 2]),
                ("VGRD", "10 m above ground", wind[(step * 2) + 1]),
            ];
        }

        // The fixtures stop at f006: later hours, up to the services' default f048, repeat it, so runs are complete.
        for (int hour = 9; hour <= 48; hour += 3)
            server.Messages[hour] = server.Messages[6];
        server.AlertsJson = File.ReadAllText(TestData("CAP", "nws-alerts-marine-20260828.geojson"));
        return server;
    }

    /// <summary>A Library over <paramref name="http"/>, as <c>s100 mcp serve</c> builds it, and its operations.</summary>
    private (CollectionLibrary Library, LibraryOperations Operations, LibraryCatalogueReaders Readers) Library(HttpClient http, TimeProvider time)
    {
        var downloads = Path.Combine(_root, "downloads");
        var weather = new WeatherFeedIndexer(http, downloads, time);
        var library = new CollectionLibrary(
            CollectionIndexer.CreateDefault(McpServeCommand.LibraryMetadata, [weather]),
            new CollectionLibraryOptions(Path.Combine(_root, "collections.json"), Path.Combine(_root, "index-cache")));
        library.Initialize();
        _owned.Add(library);
        var opener = new CatalogLibraryOpener(_catalog);
        _owned.Add(opener);
        var operations = new LibraryOperations(
            library, new LibraryDownloads(LibraryDownloads.ManagedFolders(http, downloads)), new LibraryLoader(opener));
        var readers = new LibraryCatalogueReaders
        {
            Weather = (source, ct) => weather.DescribeAsync(source, ct),
            CurrentMapView = () => UsEast,
            MarineZones = area => MarineZoneLayer.Default.CodesIn(area.South, area.West, area.North, area.East),
        };
        return (library, operations, readers);
    }

    /// <summary>Adds the known source <paramref name="id"/> as the dialog would, with <paramref name="choose"/> ticking its choices.</summary>
    private static async Task<(LibrarySource Source, CollectionItem Item)> AddAsync(
        CollectionLibrary library, LibraryCatalogueReaders readers, string id, Action<LibrarySourceDraft>? choose = null)
    {
        var draft = LibrarySourceDraft.ForCatalogue(KnownCatalogueSources.Find(id)!, readers)!;
        Assert.Null(await draft.LoadAsync(TestContext.Current.CancellationToken));
        choose?.Invoke(draft);
        var (_, sourceId) = draft.AddTo(library);
        await library.WhenIdle().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var source = library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == sourceId);
        Assert.Equal(LibrarySourceState.Ready, source.State);
        Assert.Empty(source.Index!.Diagnostics);
        return (source, Assert.Single(source.Index.Items));
    }

    /// <summary>Ticks only day 1 of the forecast days (the fixtures reach f006, which day 1 holds).</summary>
    private static void FirstDayOnly(LibrarySourceDraft draft)
    {
        foreach (var day in draft.Groups.Single(g => g.Title == "Forecast days").Options)
            day.IsSelected = day.Value == "1";
    }

    private async Task<LoadedDatasetSummary> DownloadAndOpenAsync(LibraryOperations operations, LibrarySource source, CollectionItem item)
    {
        var outcome = await operations.DownloadAsync([(item, source)], load: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, outcome.Download.Downloaded);
        Assert.True(outcome.Load is { Opened: 1 }, string.Join("; ", outcome.Load?.Problems ?? []));
        var dataset = Assert.Single(_catalog.Datasets);
        return new LoadedDatasetSummary(dataset.Spec.Name, dataset.Id.Value);
    }

    private sealed record LoadedDatasetSummary(string Spec, string Id);

    [Fact]
    public async Task A_NOMADS_GRIB_filter_run_adds_downloads_and_opens_as_one_GRIB2_dataset()
    {
        var server = Server();
        var (library, operations, readers) = Library(new HttpClient(server), new FakeTimeProvider(Run.AddHours(4)));

        var (source, item) = await AddAsync(library, readers, "noaa-nomads-gfswave", FirstDayOnly);
        var loaded = await DownloadAndOpenAsync(operations, source, item);

        Assert.Equal("GRIB2", loaded.Spec);
        Assert.Equal("NOAA/NCEP GFS-Wave (WAVEWATCH III) · Public domain", WeatherRuns.CreditOf(item));
    }

    [Fact]
    public async Task An_AWS_run_read_by_byte_range_loads_as_one_GRIB2_dataset()
    {
        var server = Server();
        var (library, operations, readers) = Library(new HttpClient(server), new FakeTimeProvider(Run.AddHours(4)));

        var (source, item) = await AddAsync(library, readers, "noaa-aws-gfs", FirstDayOnly);
        var loaded = await DownloadAndOpenAsync(operations, source, item);

        Assert.Equal("GRIB2", loaded.Spec);
        Assert.Contains(server.Requests, r => r.Range is not null);
        Assert.DoesNotContain(server.Requests, r => r.Uri.Host == "noaa-gfs-bdp-pds.s3.amazonaws.com" && r.Method == HttpMethod.Get
            && r.Range is null && !r.Uri.AbsolutePath.EndsWith(".idx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Marine_alerts_for_the_zones_in_view_load_as_CAP()
    {
        var server = Server();
        var (library, operations, readers) = Library(new HttpClient(server), new FakeTimeProvider(Run));

        var (source, item) = await AddAsync(library, readers, "nws-marine-alerts");
        var loaded = await DownloadAndOpenAsync(operations, source, item);

        Assert.Equal("CAP", loaded.Spec);
        var zones = ((WeatherFeedSource)source.Definition).Selection.Zones;
        Assert.Contains("ANZ430", zones); // Delaware Bay, inside the view.
        Assert.All(server.Requests.Where(r => r.Uri.Host == "api.weather.gov"), r => Assert.NotNull(r.UserAgent));
    }

    [Fact]
    public async Task An_ERDDAP_dataset_loads_as_NetCDF()
    {
        var server = Server();
        var nc = NetCdfFixtures.WriteWaveNetCdf3(Path.Combine(_root, "erddap.nc"));
        server.ErddapNetCdf = File.ReadAllBytes(nc);
        server.ErddapInfoJson = """
            {"table":{"columnNames":["Row Type","Variable Name","Attribute Name","Data Type","Value"],"rows":[
              ["attribute","NC_GLOBAL","date_issued","String","2026-10-25T04:10:00Z"],
              ["attribute","NC_GLOBAL","time_coverage_end","String","2026-11-01T00:00:00Z"],
              ["dimension","time","","double","nValues=120, evenlySpaced=true, averageSpacing=3h 0m 0s"],
              ["dimension","depth","","float","nValues=1, onlyValue=0.0"],
              ["dimension","latitude","","float","nValues=311, evenlySpaced=true, averageSpacing=-0.5"],
              ["dimension","longitude","","float","nValues=720, evenlySpaced=true, averageSpacing=0.5"],
              ["attribute","longitude","actual_range","float","0.0, 359.5"]
            ]}}
            """;
        var (library, operations, readers) = Library(new HttpClient(server), new FakeTimeProvider(Run.AddHours(5)));

        var (source, item) = await AddAsync(library, readers, "noaa-erddap-ww3", FirstDayOnly);
        var loaded = await DownloadAndOpenAsync(operations, source, item);

        Assert.Equal("NetCDF", loaded.Spec);
    }

    [Fact]
    public async Task A_folder_holding_one_run_in_hourly_files_loads_as_one_dataset()
    {
        var run = Directory.CreateDirectory(Path.Combine(_root, "runs", "gfs.20261025", "00"));
        var waves = WeatherFakeServer.Split(File.ReadAllBytes(TestData("GRIB2", "gfswave-useast.grib2")));
        for (int step = 0; step < 3; step++)
            File.WriteAllBytes(Path.Combine(run.FullName, $"gfswave.t00z.global.0p25.f{step * 3:000}.grib2"), [.. waves.Skip(step * 3).Take(3).SelectMany(m => m)]);
        var (library, operations, _) = Library(new HttpClient(Server()), new FakeTimeProvider(Run));
        var folder = new LocalFolderSource(Guid.NewGuid(), null, Path.Combine(_root, "runs"));
        library.AddCollection("Runs", [folder]);
        await library.WhenIdle().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var item = Assert.Single(library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == folder.Id).Index!.Items);
        Assert.Equal("GRIB2", item.ProductSpec);

        var result = await operations.LoadAsync([item], cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Opened == 1, string.Join("; ", result.Problems ?? []));
        var dataset = Assert.Single(_catalog.Datasets);
        Assert.Equal("GRIB2", dataset.Spec.Name);
    }

    // ---------------------------------------------------------------
    // Live smoke tests: one per source, off unless S100_LIVE_WEATHER_TESTS=1.
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("noaa-nomads-gfswave")]
    [InlineData("noaa-nomads-gfs")]
    [InlineData("noaa-aws-gfswave")]
    [InlineData("noaa-aws-gfs")]
    [InlineData("nws-marine-alerts")]
    [InlineData("noaa-erddap-ww3")]
    public async Task Live_each_source_adds_downloads_and_opens(string id)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(LiveVariable) == "1", $"Set {LiveVariable}=1 to reach the live weather services.");

        using var http = new HttpClient(Collections.Net.HardenedTls.CreateHandler()) { Timeout = TimeSpan.FromMinutes(5) };
        var (library, operations, readers) = Library(http, TimeProvider.System);
        var (source, item) = await AddAsync(library, readers, id, draft =>
        {
            // Keep it small: the first day, the defaults.
            if (draft.Groups.FirstOrDefault(g => g.Title == "Forecast days") is { } days)
            {
                foreach (var day in days.Options)
                    day.IsSelected = day.Value == "1";
            }
        });

        var outcome = await operations.DownloadAsync([(item, source)], load: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Download.Downloaded);
        Assert.True(outcome.Load is { Opened: 1 }, string.Join("; ", outcome.Load?.Problems ?? []));
    }
}
