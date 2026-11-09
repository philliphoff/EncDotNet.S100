using System.Globalization;
using Avalonia.Controls;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Routes.Rtz;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views.Settings;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Check route's weather check in SoundCharts (#1093): the mariner's limits
/// in Settings, the verdict card's Weather row, the Weather filter, the RTZ
/// route's own limits and the MCP settings tools. The route runs 0.1° east
/// from GFS-Wave file index 566 (35.75°N, 74.75°W), where the wave model has
/// about 20 kn of wind and 2.54 m of sea at f003.
/// </summary>
public sealed class RouteWeatherCheckTests : IDisposable
{
    private static readonly DateTimeOffset Departure = new(GribFixtures.Run.AddHours(3));

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly string _settingsFile = Path.Combine(Path.GetTempPath(), "s100-route-weather-" + Guid.NewGuid().ToString("N") + ".json");

    public RouteWeatherCheckTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        if (File.Exists(_settingsFile))
            File.Delete(_settingsFile);
    }

    private static LoadedDataset Grib()
    {
        using var stream = File.OpenRead(GribFixtures.Fixture(GribFixtures.WaveFilter));
        return LoadedDatasetProjector.Project(new DatasetId("gfswave"), "GRIB2", stream)!;
    }

    private static Route Route(RoutesService routes)
    {
        var (lat, lon) = GribFixtures.CropPoint(566);
        var route = routes.Routes.CreateRoute("Weather");
        route.AppendWaypoint(new GeoPosition(lat, lon));
        route.AppendWaypoint(new GeoPosition(lat, lon + 0.1));
        route.SetPassageValue(RoutePassageField.Speed, 6);
        route.SetDeparture(Departure);
        return route;
    }

    private static async Task<RouteCheckReport> Settled(PassageCheckService check)
    {
        await check.WhenIdle();
        return check.Report!;
    }

    private sealed record Setup(RoutesService Routes, PassageCheckService Check, FixedWeatherLimits Limits) : IDisposable
    {
        public void Dispose() => Check.Dispose();

        public RouteCheckCard Card => RouteCheckCard.For(Check, null, DepthUnit.Metres, TimeFormat.Utc)!;
    }

    private static Setup Start(IReadOnlyList<LoadedDataset> datasets, Func<RoutesService, Route>? route = null)
    {
        var routes = new RoutesService();
        var active = (route ?? Route)(routes);
        if (!routes.Routes.Routes.Contains(active))
            routes.Routes.Add(active);
        routes.Routes.SetActiveRoute(active);
        var limits = new FixedWeatherLimits();
        var check = new PassageCheckService(routes, new PassageCheckFixture.FixedCatalog(datasets), PassageCheckFixture.SerialPost, limits, debounce: TimeSpan.Zero, sources: PassageCheckFixture.NewSources(), checks: EncDotNet.S100.Viewer.Services.PassageCheck.PassageCheckService.BuiltInChecks);
        routes.IsCheckModeActive = true;
        return new Setup(routes, check, limits);
    }

    [Fact]
    public async Task Without_weather_loaded_there_is_no_weather_row()
    {
        using var s = Start([]);
        s.Limits.Limits = new WeatherLimits { MaxWind = Speed.FromKnots(15) };

        var report = await Settled(s.Check);

        Assert.Equal(RouteCheckReasons.WeatherMissing, report.Status(WeatherCheck.CheckName)!.Reason);
        Assert.DoesNotContain(s.Card.Rows, r => r.Filter == FindingsFilter.Weather);
    }

    [Fact]
    public async Task With_weather_but_no_limits_the_row_says_so()
    {
        using var s = Start([Grib()]);

        await Settled(s.Check);

        var row = Assert.Single(s.Card.Rows, r => r.Filter == FindingsFilter.Weather);
        Assert.Equal("Weather", row.Label);
        Assert.Equal("No limits set", row.Text);
        Assert.Null(row.Chip);
        Assert.Contains("Mariner Settings", row.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setting_a_limit_re_runs_the_check_and_the_row_counts_what_is_above_it()
    {
        using var s = Start([Grib()]);
        await Settled(s.Check);

        s.Limits.Limits = new WeatherLimits { MaxWind = Speed.FromKnots(15), MaxWaveHeight = Length.FromMetres(3) };
        var report = await Settled(s.Check);

        var finding = Assert.Single(report.Findings, f => f.Category == RouteFindingCategory.Weather);
        Assert.Equal(RouteFindingCodes.WindAboveLimit, finding.Code);
        Assert.Equal(FindingsFilter.Weather, RouteCheckCard.FilterOf(finding));
        var row = Assert.Single(s.Card.Rows, r => r.Filter == FindingsFilter.Weather);
        Assert.Equal("1 above limits", row.Text);
        Assert.Equal("1", row.Chip);
        Assert.Equal(CheckTone.Warning, row.ChipTone);
        Assert.StartsWith("Wind 15 kn (Settings) · Waves 3.0 m (Settings).", row.Tooltip, StringComparison.Ordinal);
        Assert.Contains("not for navigation", row.Tooltip, StringComparison.Ordinal);
        Assert.EndsWith("weather forecast for your settings", s.Card.Footer, StringComparison.Ordinal);
        Assert.Equal(1, RouteFindingsViewModel.Counts(report.Findings)[FindingsFilter.Weather]);

        // Raising the limits clears the finding: the row is within them.
        s.Limits.Limits = new WeatherLimits { MaxWind = Speed.FromKnots(30), MaxWaveHeight = Length.FromMetres(3) };
        await Settled(s.Check);
        var within = Assert.Single(s.Card.Rows, r => r.Filter == FindingsFilter.Weather);
        Assert.Equal(("Within your limits", "0", CheckTone.Success), (within.Text, within.Chip, within.ChipTone));
    }

    [Fact]
    public async Task A_departure_after_the_forecast_is_not_checked()
    {
        using var s = Start([Grib()], routes =>
        {
            var route = Route(routes);
            route.SetDeparture(Departure.AddDays(3));
            return route;
        });
        s.Limits.Limits = new WeatherLimits { MaxWind = Speed.FromKnots(15) };

        var report = await Settled(s.Check);

        Assert.Equal(WeatherVerdict.ForecastEndsBeforeArrival, report.Weather!.Verdict);
        var row = Assert.Single(s.Card.Rows, r => r.Filter == FindingsFilter.Weather);
        Assert.Equal("Forecast ends before arrival", row.Text);
        Assert.Equal(CheckTone.Neutral, row.ChipTone);
    }

    [Fact]
    public async Task The_viewer_words_weather_findings_from_its_strings()
    {
        using var s = Start([Grib()]);
        s.Limits.Limits = new WeatherLimits { MaxWaveHeight = Length.FromMetres(2) };
        var report = await Settled(s.Check);

        var formatter = new ViewerFindingFormatter(DepthUnit.Feet, TimeFormat.Utc, Departure);
        var waves = formatter.Render(Assert.Single(report.Findings, f => f.Category == RouteFindingCategory.Weather));

        Assert.StartsWith("Waves above 7 ft for ", waves.Title, StringComparison.Ordinal);
        Assert.Contains("max 8 ft at", waves.Meta, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_rtz_routes_own_limits_are_used_where_the_mariner_sets_none()
    {
        var (lat, lon) = GribFixtures.CropPoint(566);
        var rtz = new RtzRoute
        {
            Version = RtzVersions.Latest,
            Info = new RtzRouteInfo { RouteName = "Received", VesselMaxWind = 15, VesselMaxWave = 3 },
            Waypoints =
            [
                new RtzWaypoint { Id = 1, Position = new(lat, lon) },
                new RtzWaypoint { Id = 2, Position = new(lat, lon + 0.1) },
            ],
            Schedules =
            [
                new RtzSchedule
                {
                    Id = 1,
                    Name = "Plan",
                    Manual = new RtzScheduleTable { Elements = [new RtzScheduleElement { WaypointId = 1, Etd = Departure }, new RtzScheduleElement { WaypointId = 2, Speed = 6 }] },
                },
            ],
        };
        var received = RouteRtzMapping.ToReceived(rtz, "received.rtz");
        Assert.Equal(15, RouteVesselLimits.Of(received)!.MaxWind!.Value.TotalKnots, 9);

        using var s = Start([Grib()], routes => received);
        var report = await Settled(s.Check);

        Assert.Equal(
            [WeatherLimitOrigin.Route, WeatherLimitOrigin.Route],
            report.Weather!.Limits.Select(l => l.Origin));
        Assert.Single(report.Findings, f => f.Code == RouteFindingCodes.WindAboveLimit);

        // The mariner's wind limit replaces the route's; its wave limit stays.
        s.Limits.Limits = new WeatherLimits { MaxWind = Speed.FromKnots(30) };
        report = await Settled(s.Check);
        Assert.Equal([WeatherLimitOrigin.Settings, WeatherLimitOrigin.Route], report.Weather!.Limits.Select(l => l.Origin));
        Assert.DoesNotContain(report.Findings, f => f.Category == RouteFindingCategory.Weather);
        Assert.Contains("Waves 3.0 m (route)", Assert.Single(s.Card.Rows, r => r.Filter == FindingsFilter.Weather).Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void The_settings_keep_the_limits_and_show_waves_in_the_depth_unit()
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsFile };
        var vm = new SettingsViewModel(settings);
        var raised = 0;
        vm.RouteWeatherLimitsChanged += () => raised++;

        Assert.Null(vm.Limits);
        Assert.Equal(string.Empty, vm.RouteMaxWindDisplay);

        vm.RouteMaxWindDisplay = "25";
        vm.RouteMaxWaveDisplay = "3.5";

        Assert.Equal(2, raised);
        Assert.Equal(25, settings.RouteMaxWindKnots);
        Assert.Equal(3.5, settings.RouteMaxWaveHeightMetres);
        Assert.Equal(25, vm.Limits!.MaxWind!.Value.TotalKnots, 9);
        Assert.Equal("Wave height limit (m)", vm.RouteMaxWaveLabel);

        vm.SelectedDepthUnit = DepthUnit.Feet;
        Assert.Equal("Wave height limit (ft)", vm.RouteMaxWaveLabel);
        Assert.Equal("11.5", vm.RouteMaxWaveDisplay);
        vm.RouteMaxWaveDisplay = "10";
        Assert.Equal(3.048, settings.RouteMaxWaveHeightMetres!.Value, 6);

        // Not a number leaves the limit; empty turns it off.
        vm.RouteMaxWindDisplay = "lots";
        Assert.Equal(25, vm.RouteMaxWindKnots);
        vm.RouteMaxWindDisplay = "";
        Assert.Null(vm.RouteMaxWindKnots);
        Assert.Null(settings.RouteMaxWindKnots);

        var reloaded = new SettingsViewModel(ViewerSettings.Load(_settingsFile));
        Assert.Null(reloaded.RouteMaxWindKnots);
        Assert.Equal(3.048, reloaded.RouteMaxWaveHeightMetres!.Value, 6);
    }

    [AvaloniaFact]
    public void The_settings_page_has_the_two_limit_boxes()
    {
        var vm = new SettingsViewModel(new ViewerSettings { SettingsFilePath = _settingsFile, RouteMaxWindKnots = 28 });
        using var host = ViewHost.Show(new ChartSettingsPage { DataContext = vm }, width: 500, height: 2400);

        var wind = host.Find<TextBox>("Settings.RouteMaxWind");
        var wave = host.Find<TextBox>("Settings.RouteMaxWave");
        Assert.Equal("28", wind.Text);
        Assert.True(string.IsNullOrEmpty(wave.Text));
        Assert.Equal("Off", wave.PlaceholderText);
        Assert.NotNull(ToolTip.GetTip(wind));
        Assert.NotNull(ToolTip.GetTip(wave));
    }

    [Fact]
    public async Task The_mcp_tools_read_and_set_the_limits()
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsFile };
        var vm = new SettingsViewModel(settings);
        var tools = new RouteWeatherSettingsTools(vm, new ImmediateInvoker());
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await tools.GetAsync(ct)).TryGetValue(out var empty));
        Assert.Equal(((double?)null, (double?)null), (empty!.MaxWindKnots, empty.MaxWaveHeightMeters));

        Assert.True((await tools.SetAsync(25, 3, null, null, null, null, ct)).TryGetValue(out var set));
        Assert.Equal(((double?)25, (double?)3), (set!.MaxWindKnots, set.MaxWaveHeightMeters));
        Assert.Equal(25, settings.RouteMaxWindKnots);

        Assert.True((await tools.SetAsync(null, null, ["maxWindKnots"], null, null, null, ct)).TryGetValue(out var cleared));
        Assert.Equal(((double?)null, (double?)3), (cleared!.MaxWindKnots, cleared.MaxWaveHeightMeters));

        Assert.True((await tools.SetAsync(-1, null, null, null, null, null, ct)).TryGetError(out var bad));
        Assert.Equal("invalid_argument", bad!.Code);
        Assert.True((await tools.SetAsync(null, null, ["gusts"], null, null, null, ct)).TryGetError(out _));
    }

    private sealed class ImmediateInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
