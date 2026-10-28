using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Weather;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #1075 in the viewer: a weather run's row (stale, load latest run,
/// pin), its credit in the Datasets panel and pick report, the catalogue
/// directory's chips and the Add-to-Library dialog for a weather service.
/// </summary>
public sealed class WeatherLibraryViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Run = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    private readonly LibraryTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private static KnownCatalogueSource Known(string id) => KnownCatalogueSources.Find(id)!;

    private static LibrarySource Source() => new(
        new WeatherFeedSource(Guid.NewGuid(), null, Known("noaa-nomads-gfswave").CatalogUri, Known("noaa-nomads-gfswave").Weather!, new WeatherSelection()),
        null, LibrarySourceState.Ready);

    private static CollectionItem Run00(bool pinned = false)
    {
        var properties = new Dictionary<string, string>
        {
            [S100ForecastFeedIndexer.RunProperty] = "2026-10-25T00:00:00Z",
            [S100ForecastFeedIndexer.ValidToProperty] = "2026-10-27T00:00:00Z",
            [WeatherRuns.StaleAfterProperty] = "12",
            [WeatherRuns.LicenceProperty] = "Public domain",
            [WeatherRuns.AttributionProperty] = "NOAA/NCEP GFS-Wave (WAVEWATCH III)",
        };
        if (pinned)
            properties[WeatherRuns.PinnedProperty] = "true";
        return new CollectionItem
        {
            Key = pinned ? "gfswave@2026102500" : "gfswave",
            ProductSpec = "GRIB2",
            Name = pinned ? "gfswave.run-2026102500" : "gfswave",
            Location = pinned
                ? new LocalItemLocation("/tmp/x", "gfswave.t00z.20261025.grib2", [])
                : new RemoteItemLocation(new Uri("https://nomads.ncep.noaa.gov/cgi-bin/filter_gfswave.pl?file=x"), LastModified: Run),
            Properties = properties,
        };
    }

    [Fact]
    public void A_weather_run_over_12_hours_old_is_stale_and_offers_the_latest_run()
    {
        var row = new LibraryItemViewModel(Run00(), Source(), downloader: new Downloads(Run), time: new FakeTimeProvider(Run.AddHours(13)));

        Assert.True(row.IsWeatherRun);
        Assert.True(row.IsForecastRunRow);
        Assert.True(row.IsStaleRun);
        Assert.True(row.CanLoadLatestRun);
        Assert.True(row.CanPinRun);
        // The run in the user's Local/UTC format (#730).
        Assert.Matches("^Run .+ is over 12 hours old$", row.StaleRunText);
        Assert.Contains(row.Tags, t => t.Text == "Stale" && t.IsUpdate);
        Assert.Equal("NOAA/NCEP GFS-Wave (WAVEWATCH III) · Public domain", row.Credit);
    }

    [Fact]
    public void A_fresh_run_is_not_stale_and_one_not_downloaded_cannot_be_pinned()
    {
        var row = new LibraryItemViewModel(Run00(), Source(), downloader: new Downloads(null), time: new FakeTimeProvider(Run.AddHours(5)));

        Assert.False(row.IsStaleRun);
        Assert.Null(row.StaleRunText);
        Assert.True(row.CanLoadLatestRun);
        Assert.False(row.CanPinRun);
    }

    [Fact]
    public void A_pinned_run_is_never_stale_and_can_only_be_unpinned()
    {
        var row = new LibraryItemViewModel(Run00(pinned: true), Source(), downloader: new Downloads(null), time: new FakeTimeProvider(Run.AddDays(5)));

        Assert.True(row.IsPinnedRun);
        Assert.False(row.IsStaleRun);
        Assert.False(row.CanLoadLatestRun);
        Assert.False(row.CanPinRun);
        Assert.Contains(row.Tags, t => t.Text == "Pinned");
    }

    [Fact]
    public void A_layers_credit_shows_in_the_datasets_panel_and_its_pick_reports()
    {
        var entry = new DatasetEntry("/tmp/gfswave.t00z.20261025.grib2", "GRIB2");
        Assert.False(entry.HasCredit);

        entry.Credit = "NOAA/NCEP GFS-Wave (WAVEWATCH III) · Public domain";

        Assert.True(entry.HasCredit);
        Assert.Equal(string.Format(Strings.Library_DataCreditFormat, entry.Credit), entry.CreditText);

        var pick = new PickReportViewModel();
        pick.SetPicks([new PickHit { FeatureType = "WaveHeight", FeatureRef = "1", ProductSpec = "GRIB2", Credit = entry.Credit }]);
        Assert.True(pick.HasDataCredit);
        Assert.Equal(entry.CreditText, pick.DataCredit);
        pick.SetPicks([new PickHit { FeatureType = "DepthArea", FeatureRef = "2", ProductSpec = "S-101" }]);
        Assert.False(pick.HasDataCredit);
    }

    [Fact]
    public void The_directory_labels_weather_services_and_shows_their_forecast_chips()
    {
        var directory = new CatalogueDirectoryDialogViewModel(KnownCatalogueSources.All);
        var aws = directory.Entries.Single(e => e.Source.Id == "noaa-aws-gfswave");
        var nomads = directory.Entries.Single(e => e.Source.Id == "noaa-nomads-gfs");

        Assert.Equal(Strings.Library_Format_Weather, CatalogueEntryViewModel.FormatLabel(KnownCatalogueFormat.Weather));
        Assert.True(aws.IsForecast);
        Assert.Equal("GRIB2 · " + Strings.Library_Host_Aws, aws.FormatText);
        Assert.Equal("GRIB2 · " + Strings.Library_Host_Web, nomads.FormatText);
    }

    [Fact]
    public async Task Adding_a_weather_service_starts_with_its_defaults_ticked_and_shows_the_latest_run()
    {
        using var library = _context.CreateService();
        var readers = new LibraryCatalogueReaders
        {
            Weather = (_, _) => Task.FromResult(new WeatherServiceSummary(Run, 1)),
            CurrentMapView = () => new GeoBounds(32, -76, 40, -68),
        };
        var vm = new AddToLibraryDialogViewModel(library, readers, new FakeTimeProvider(Run.AddHours(5)));

        vm.Initialize(Known("noaa-nomads-gfs"), targetCollectionId: null);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsWeather);
        Assert.False(vm.IncludeAll);
        Assert.True(vm.OnlySelected);
        Assert.Equal(["Parameters", "Forecast days"], vm.FacetGroups.Select(g => g.Title));
        Assert.True(vm.HasWeatherRunNote);
        Assert.StartsWith("Latest run 2026-10-25 00Z · Area 32°N–40°N, 76°W–68°W", vm.WeatherRunNote, StringComparison.Ordinal);
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        vm.ConfirmCommand.Execute(null);

        var source = Assert.IsType<WeatherFeedSource>(Assert.Single(Assert.Single(library.Collections).Sources).Definition);
        Assert.Equal([":UGRD:10 m above ground:", ":VGRD:10 m above ground:", ":PRMSL:mean sea level:"], source.Selection.Parameters);
        Assert.Equal("NOAA/NCEP GFS", source.Attribution);
    }

    private sealed class Downloads(DateTimeOffset? localRun) : ILibraryDownloader
    {
        public event EventHandler? Changed { add { } remove { } }

        public CollectionItem Localize(CollectionItem item) => item;

        public bool IsOutdated(CollectionItem item) => false;

        public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => localRun;

        public bool CanDownload(CollectionItem item) => item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryDownloadResult(0, 0, false));
    }
}
