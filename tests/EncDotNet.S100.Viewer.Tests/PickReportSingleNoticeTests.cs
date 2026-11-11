using Avalonia.Controls;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.DynamicSources.Ndbc;
using EncDotNet.S100.DynamicSources.Nmea;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #1096: a pick on a product that isn't S-100 shows the
/// not-for-navigation notice once. For a weather grid (NetCDF, GRIB2) or a CAP
/// warning the panel leads with <see cref="PickReportViewModel.NavigationNotice"/>
/// and hides the processor's own notice row
/// (<see cref="PickAttribute.IsNavigationNotice"/>), which MCP and the CLI
/// still show. A dynamic source (NDBC, NMEA own-ship) has no product
/// registration, so the panel adds no notice of its own and the source's
/// "Source … Not for navigation." row is the one.
/// </summary>
[Collection(nameof(ProductRegistryDefaultCollection))]
public sealed class PickReportSingleNoticeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pick-notice-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [AvaloniaFact]
    public void A_NetCDF_grid_pick_shows_the_notice_once()
    {
        using var processor = new NetCdfDatasetProcessor(
            NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc")), new ProjNetCrsTransformFactory());
        var grid = NetCdfFixtures.ChartGrid;
        var info = processor.GetCoverageInfo(grid.South + 4 * grid.Spacing, grid.West + 7 * grid.Spacing, NetCdfFixtures.Run)!;

        AssertNoticeOnce(info, "waves.nc", "NetCDF", WeatherGridProcessorBase.NotForNavigationNotice);
    }

    [AvaloniaFact]
    public void A_GRIB2_grid_pick_shows_the_notice_once()
    {
        using var processor = new GribDatasetProcessor(
            GribFixtures.CopyFixture(GribFixtures.WaveFilter, Path.Combine(_dir, "filter_gfswave.pl")), new ProjNetCrsTransformFactory());
        var (latitude, longitude) = GribFixtures.CropPoint(566);
        var info = processor.GetCoverageInfo(latitude, longitude, GribFixtures.Run)!;

        AssertNoticeOnce(info, "filter_gfswave.pl", "GRIB2", WeatherGridProcessorBase.NotForNavigationNotice);
    }

    [AvaloniaFact]
    public void A_CAP_warning_pick_shows_the_notice_once()
    {
        var processor = new CapDatasetProcessor(CapFixtures.CopyFixture(CapFixtures.GeoJson, Path.Combine(_dir, "active")));
        var info = processor.GetFeatureInfoAt(0)!;

        AssertNoticeOnce(info, "active", "CAP", CapDatasetProcessor.NotForNavigationNotice);
    }

    [AvaloniaFact]
    public void An_NDBC_station_pick_shows_the_notice_once()
    {
        var now = new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);
        var observation = new NdbcObservation
        {
            StationId = "44013",
            Time = now.AddMinutes(-10),
            PressureHectopascals = 1012.3,
            WaterTemperatureCelsius = 14.1,
        };
        var hit = DynamicHit(_ => NdbcObservationFormatter.Describe(observation, "BOSTON", "buoy", stale: false, now));

        AssertDynamicNoticeOnce(hit, "NOAA National Data Buoy Center. Not for navigation.");
    }

    [AvaloniaFact]
    public void An_own_ship_NMEA_weather_pick_shows_the_notice_once()
    {
        var now = new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);
        var weather = new OwnShipWeather { Time = now.AddSeconds(-5), WaterTemperatureCelsius = 12.4, PressureHectopascals = 1009.8 };
        var hit = DynamicHit(_ => NmeaWeatherFormatter.Describe(weather, now));

        AssertDynamicNoticeOnce(hit, "Own-ship NMEA 0183 instruments. Not for navigation.");
    }

    [Fact]
    public void An_S100_pick_keeps_a_notice_row_the_panel_does_not_lead_with()
    {
        var vm = new PickReportViewModel();

        vm.SetPick("DepthArea", "Depth Area", "300", "test.000", "S-101",
            [PickAttribute.NavigationNotice("Survey data, not for navigation.")]);

        Assert.Null(vm.NavigationNotice);
        Assert.True(Assert.Single(vm.Attributes).IsNavigationNotice);
    }

    private void AssertNoticeOnce(FeatureInfo info, string fileName, string spec, string notice)
    {
        // The processor's info still leads with the notice for MCP and the CLI.
        Assert.True(info.Attributes[0].IsNavigationNotice);
        Assert.Equal(PickAttribute.NavigationNoticeCode, info.Attributes[0].Code);
        Assert.Equal(notice, info.Attributes[0].DisplayText);

        var vm = new PickReportViewModel();
        vm.SetPicks(
        [
            new PickHit
            {
                FeatureType = info.FeatureType,
                FeatureTypeName = info.FeatureTypeName,
                FeatureRef = info.FeatureRef,
                DatasetFileName = fileName,
                ProductSpec = spec,
                Attributes = info.Attributes,
                Credit = "NOAA/NCEP · Public domain",
            },
        ]);

        Assert.Equal(notice, vm.NavigationNotice);
        Assert.DoesNotContain(vm.Attributes, a => a.IsNavigationNotice);
        Assert.NotEmpty(vm.Attributes);
        Assert.Equal("Data: NOAA/NCEP · Public domain", vm.DataCredit);

        using var host = ViewHost.Show(new PickReportView { DataContext = vm }, width: 420, height: 900);
        var shown = NoticeTexts(host);
        Assert.Equal(notice, Assert.Single(shown));
        Assert.Equal(notice, host.Find<TextBlock>("PickReport.NavigationNotice").Text);
    }

    private static void AssertDynamicNoticeOnce(DynamicPickHit hit, string sourceRow)
    {
        var vm = new PickReportViewModel();
        vm.SetPicks([], [hit]);

        Assert.Null(vm.NavigationNotice);
        Assert.Single(hit.Attributes, a => a.Value.Contains("not for navigation", StringComparison.OrdinalIgnoreCase));

        using var host = ViewHost.Show(new PickReportView { DataContext = vm }, width: 420, height: 900);
        Assert.Equal(sourceRow, Assert.Single(NoticeTexts(host)));
        Assert.False(host.IsShown("PickReport.NavigationNotice"));
    }

    /// <summary>Every visible text in the panel that says "not for navigation".</summary>
    private static List<string> NoticeTexts(ViewHost host) =>
        host.All<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Text?.Contains("not for navigation", StringComparison.OrdinalIgnoreCase) == true)
            .Select(t => t.Text!)
            .ToList();

    private static DynamicPickHit DynamicHit(Func<DynamicFeature, DynamicFeatureDescription> describe)
    {
        const double latitude = 42.35, longitude = -70.65;
        var source = new DescribedSource(describe)
        {
            CurrentFeatures =
            [
                new DynamicFeature
                {
                    Id = "feature",
                    Kind = "weather.observation",
                    GeometryType = GeometryType.Point,
                    Coordinates = [new GeoPosition(latitude, longitude)],
                    LastUpdated = DateTimeOffset.UnixEpoch,
                },
            ],
        };
        var service = new DynamicSourcePickService(new SingleSourceRegistry(source));
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(longitude, latitude);
        return Assert.Single(service.Pick(new MPoint(x, y), resolution: 10.0));
    }

    private sealed class DescribedSource(Func<DynamicFeature, DynamicFeatureDescription> describe)
        : IDynamicFeatureSource, IDynamicFeatureDescriber
    {
        public string Id => "obs";
        public DynamicSourceMetadata Metadata { get; } = new() { DisplayName = "Observations" };
        public IReadOnlyList<DynamicFeature> CurrentFeatures { get; init; } = [];
        public event EventHandler<DynamicFeaturesChanged>? Changed { add { } remove { } }

        public DynamicFeatureDescription? Describe(DynamicFeature feature, IFormatProvider? formatProvider = null) => describe(feature);
    }

    private sealed class SingleSourceRegistry(IDynamicFeatureSource source) : IS100DynamicSourceRegistry
    {
        public IDisposable Register(IDynamicFeatureSource source) => throw new NotSupportedException();

        public IReadOnlyList<DynamicSourceRegistrationInfo> Sources =>
            [new DynamicSourceRegistrationInfo(source.Id, source.Metadata.DisplayName, source.Metadata.Description)];

        public bool GetVisible(string sourceId) => sourceId == source.Id;

        public void SetVisible(string sourceId, bool visible) { }

        public IReadOnlyList<IDynamicFeatureSource> GetVisibleSourceInstances() => [source];

        public IReadOnlyList<DynamicSourceHit> HitTest(MPoint mapPoint, double resolution) =>
            source.CurrentFeatures.Select(f => new DynamicSourceHit(source, f, 0.0)).ToList();

        public event Action? SourcesChanged { add { } remove { } }
    }
}
