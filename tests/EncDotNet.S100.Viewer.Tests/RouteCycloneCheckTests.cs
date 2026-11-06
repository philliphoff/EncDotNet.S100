using System.Globalization;
using Avalonia.Controls;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views.Settings;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Check route's tropical cyclone rule in SoundCharts (#1299, design note
/// V38/V39): the three Settings rows under "Tropical cyclones on a route",
/// the verdict card's Weather row and its tooltip, the findings' wording, the
/// storm-at-ETA ghost and the MCP settings tools. The route runs due east
/// through Hurricane Ian's estimated centre six hours after advisory 20's
/// synoptic time (00:00 UTC 28 Sep 2022), at 10 kn.
/// </summary>
public sealed class RouteCycloneCheckTests : IDisposable
{
    private static readonly DateTimeOffset Synoptic = new(2022, 9, 28, 0, 0, 0, TimeSpan.Zero);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly string _settingsFile = Path.Combine(Path.GetTempPath(), "s100-route-cyclone-" + Guid.NewGuid().ToString("N") + ".json");

    public RouteCycloneCheckTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        if (File.Exists(_settingsFile))
            File.Delete(_settingsFile);
    }

    private static LoadedDataset Ian() =>
        LoadedDatasetProjector.Project(new DatasetId("ian"), new TropicalCycloneDatasetProcessor(TropicalCycloneFixtures.IanForecast))!;

    private static Route Through(RoutesService routes)
    {
        var data = (TropicalCycloneDatasetData)Ian().Data;
        var centre = data.Advisory.StormAt(Synoptic.UtcDateTime.AddHours(6))!.Point.Position;
        var west = new GeoPosition(centre.Latitude, centre.Longitude - 1.5);
        var half = Wgs84Geodesy.RhumbDistanceMetres(west, centre) / PassageGeodesy.MetersPerNauticalMile;
        var route = routes.Routes.CreateRoute("Through Ian");
        route.AppendWaypoint(west);
        route.AppendWaypoint(centre);
        route.AppendWaypoint(new GeoPosition(centre.Latitude, centre.Longitude + 1.5));
        route.SetPassageValue(RoutePassageField.Speed, 10);
        route.SetDeparture(Synoptic.AddHours(6) - TimeSpan.FromHours(half / 10));
        return route;
    }

    private sealed record Setup(RoutesService Routes, PassageCheckService Check, FixedWeatherLimits Limits) : IDisposable
    {
        public void Dispose() => Check.Dispose();

        public RouteCheckCard Card => RouteCheckCard.For(Check, null, DepthUnit.Metres, TimeFormat.Utc)!;

        public RouteCheckSummaryRow Row => Assert.Single(Card.Rows, r => r.Filter == FindingsFilter.Weather);
    }

    private static Setup Start()
    {
        var routes = new RoutesService();
        var route = Through(routes);
        if (!routes.Routes.Routes.Contains(route))
            routes.Routes.Add(route);
        routes.Routes.SetActiveRoute(route);
        var limits = new FixedWeatherLimits();
        var check = new PassageCheckService(routes, new PassageCheckFixture.FixedCatalog([Ian()]), PassageCheckFixture.SerialPost, limits, debounce: TimeSpan.Zero);
        routes.IsCheckModeActive = true;
        return new Setup(routes, check, limits);
    }

    private static async Task<RouteCheckReport> Settled(PassageCheckService check)
    {
        await check.WhenIdle();
        return check.Report!;
    }

    [Fact]
    public async Task A_route_through_the_storm_gets_its_findings_and_the_weather_row_names_the_settings()
    {
        using var s = Start();

        var report = await Settled(s.Check);

        Assert.Equal(WeatherVerdict.InsideCycloneRadius, report.Weather!.Verdict);
        var weather = report.Findings.Where(f => f.Category == RouteFindingCategory.Weather).ToList();
        Assert.Equal(RouteFindingGroup.MustReview, weather.Single(f => f.Code == RouteFindingCodes.CycloneRadius64).Group);
        Assert.Equal("Inside the 64 kt radius", weather.Single(f => f.Code == RouteFindingCodes.CycloneRadius64).Title);
        Assert.EndsWith("Ian adv 20", weather.Single(f => f.Code == RouteFindingCodes.CycloneRadius50).Meta, StringComparison.Ordinal);
        Assert.StartsWith("Closest approach ", weather.Single(f => f.Code == RouteFindingCodes.CycloneClosestApproach).Title, StringComparison.Ordinal);

        var row = s.Row;
        Assert.Equal("Weather", row.Label);
        Assert.StartsWith("2 must review · 2 to check · CPA ", row.Text, StringComparison.Ordinal);
        Assert.Equal("5", row.Chip);
        Assert.Equal(CheckTone.Destructive, row.ChipTone);
        Assert.Contains("34 kt radius and cone · Must review inside 50 kt", row.Tooltip, StringComparison.Ordinal);
        Assert.Contains("NHC advisory", s.Card.Footer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changing_a_setting_re_runs_the_check()
    {
        using var s = Start();
        await Settled(s.Check);

        s.Limits.Cyclones = new TropicalCycloneCheckSettings
        {
            CheckAgainst = TropicalCycloneRadius.Knots64,
            ReportClosestApproach = false,
            MustReviewInside = null,
        };
        var report = await Settled(s.Check);

        Assert.Equal(
            [("weather.tc_cone", RouteFindingGroup.Check), ("weather.tc_radius_64", RouteFindingGroup.Check)],
            report.Findings.Where(f => f.Category == RouteFindingCategory.Weather).Select(f => (f.Code.Value, f.Group)).OrderBy(c => c.Value, StringComparer.Ordinal));
        Assert.Equal("2 to check", s.Row.Text);
        Assert.Equal(CheckTone.Warning, s.Row.ChipTone);
        Assert.Contains("64 kt radius and cone · Must review never", s.Row.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_map_draws_the_storm_at_the_eta_of_the_closest_approach()
    {
        using var s = Start();
        var report = await Settled(s.Check);
        var route = s.Routes.Routes.ActiveRoute!;

        var map = RouteCheckMap.Build(route, report.Findings, null, RouteCheckColors.For(PaletteType.Day), SKColors.Blue, PaletteType.Day, Synoptic, TimeFormat.Utc, 50);

        var ghost = Assert.Single(map.Storms);
        var cpa = (TropicalCycloneDetails)report.Findings.Single(f => f.Code == RouteFindingCodes.CycloneClosestApproach).Details!;
        Assert.Equal(cpa.StormAtClosest!.Centre, ghost.Centre);
        Assert.Equal(cpa.RoutePosition, ghost.RoutePoint);
        Assert.Equal("M", ghost.ClassLetter); // Ian was a major hurricane.
        Assert.StartsWith("at ETA ", ghost.Label, StringComparison.Ordinal);
        Assert.EndsWith(" NM", ghost.Label, StringComparison.Ordinal);

        // Without the closest approach there's no ghost.
        s.Limits.Cyclones = TropicalCycloneCheckSettings.Default with { ReportClosestApproach = false };
        report = await Settled(s.Check);
        Assert.Empty(RouteCheckMap.Build(route, report.Findings, null, RouteCheckColors.For(PaletteType.Day), SKColors.Blue, PaletteType.Day, Synoptic, TimeFormat.Utc, 50).Storms);
    }

    [Fact]
    public void The_settings_keep_the_cyclone_rows_and_must_review_never_sits_outside_check_against()
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsFile };
        var vm = new SettingsViewModel(settings);
        var raised = 0;
        vm.RouteWeatherLimitsChanged += () => raised++;

        Assert.Equal(TropicalCycloneCheckSettings.Default, vm.Cyclones);
        Assert.Equal(34, vm.CycloneCheckAgainstKnots);
        Assert.True(vm.CycloneReportClosestApproach);
        Assert.Equal(50, vm.CycloneMustReviewInsideKnots);
        Assert.Equal("Inside the 50 kt and 64 kt radii: Must review. The 34 kt radius and the cone: Check.", vm.CycloneMappingText);
        Assert.Equal(["34 kt radius", "50 kt radius", "64 kt radius"], vm.AvailableCycloneCheckAgainst.Select(o => o.DisplayName));
        Assert.Equal(["64 kt radius", "50 kt radius", "34 kt radius", "Never"], vm.AvailableCycloneMustReviewInside.Select(o => o.DisplayName));

        // Narrowing Check against past Must review inside moves it in too.
        vm.CycloneCheckAgainstKnots = 64;
        Assert.Equal(64, vm.CycloneMustReviewInsideKnots);
        Assert.Equal([64, 0], vm.AvailableCycloneMustReviewInside.Select(o => o.Knots));
        Assert.Equal("Inside the 64 kt radius: Must review. The cone: Check.", vm.CycloneMappingText);

        // A radius outside Check against isn't taken.
        vm.CycloneMustReviewInsideKnots = 34;
        Assert.Equal(64, vm.CycloneMustReviewInsideKnots);

        vm.CycloneMustReviewInsideKnots = 0;
        vm.CycloneReportClosestApproach = false;
        Assert.Equal("Every radius and the cone: Check.", vm.CycloneMappingText);
        Assert.Equal(3, raised);
        Assert.Equal(new TropicalCycloneCheckSettings { CheckAgainst = TropicalCycloneRadius.Knots64, ReportClosestApproach = false, MustReviewInside = null }, vm.Cyclones);

        var reloaded = new SettingsViewModel(ViewerSettings.Load(_settingsFile));
        Assert.Equal(vm.Cyclones, reloaded.Cyclones);

        vm.CycloneCheckAgainstKnots = 34;
        vm.CycloneMustReviewInsideKnots = 34;
        Assert.Equal("Inside the 34 kt, 50 kt and 64 kt radii: Must review. The cone: Check.", vm.CycloneMappingText);
    }

    [AvaloniaFact]
    public void The_settings_page_has_the_three_cyclone_rows()
    {
        var vm = new SettingsViewModel(new ViewerSettings { SettingsFilePath = _settingsFile });
        using var host = ViewHost.Show(new ChartSettingsPage { DataContext = vm }, width: 500, height: 2800);

        var against = host.Find<ComboBox>("Settings.CycloneCheckAgainst");
        var cpa = host.Find<ToggleSwitch>("Settings.CycloneReportClosestApproach");
        var inside = host.Find<ComboBox>("Settings.CycloneMustReviewInside");
        var mapping = host.Find<TextBlock>("Settings.CycloneMapping");
        Assert.Equal("34 kt radius", Assert.IsType<CycloneRadiusOption>(against.SelectedItem).DisplayName);
        Assert.True(cpa.IsChecked);
        Assert.Equal("50 kt radius", Assert.IsType<CycloneRadiusOption>(inside.SelectedItem).DisplayName);
        Assert.Equal(4, inside.ItemCount);
        Assert.Equal(vm.CycloneMappingText, mapping.Text);
        Assert.NotNull(ToolTip.GetTip(against));
        Assert.NotNull(ToolTip.GetTip(inside));
        Assert.False(string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetHelpText(cpa)));

        // Choosing 64 kt narrows Must review inside to 64 kt or Never.
        against.SelectedItem = vm.AvailableCycloneCheckAgainst.Single(o => o.Knots == 64);
        Assert.Equal(64, vm.CycloneCheckAgainstKnots);
        Assert.Equal(2, inside.ItemCount);
        Assert.Equal("64 kt radius", Assert.IsType<CycloneRadiusOption>(inside.SelectedItem).DisplayName);

        cpa.IsChecked = false;
        Assert.False(vm.CycloneReportClosestApproach);
    }

    [Fact]
    public async Task The_mcp_tools_read_and_set_the_cyclone_settings()
    {
        var settings = new ViewerSettings { SettingsFilePath = _settingsFile };
        var vm = new SettingsViewModel(settings);
        var tools = new RouteWeatherSettingsTools(vm, new ImmediateInvoker());
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await tools.GetAsync(ct)).TryGetValue(out var initial));
        Assert.Equal((34, true, 50), (initial!.CycloneCheckAgainstKnots, initial.CycloneReportClosestApproach, initial.CycloneMustReviewInsideKnots));
        Assert.Equal(vm.CycloneMappingText, initial.CycloneMapping);

        Assert.True((await tools.SetAsync(null, null, null, 50, false, null, ct)).TryGetValue(out var set));
        Assert.Equal((50, false, 50), (set!.CycloneCheckAgainstKnots, set.CycloneReportClosestApproach, set.CycloneMustReviewInsideKnots));
        Assert.Equal(50, settings.RouteCycloneCheckAgainstKnots);

        Assert.True((await tools.SetAsync(null, null, null, null, null, 0, ct)).TryGetValue(out var never));
        Assert.Equal(0, never!.CycloneMustReviewInsideKnots);
        Assert.Null(vm.Cyclones.MustReviewInside);

        Assert.True((await tools.SetAsync(null, null, null, null, null, 34, ct)).TryGetError(out var outside));
        Assert.Equal("invalid_argument", outside!.Code);
        Assert.True((await tools.SetAsync(null, null, null, 40, null, null, ct)).TryGetError(out _));
        Assert.True((await tools.SetAsync(null, null, null, null, null, 40, ct)).TryGetError(out _));
    }

    private sealed class ImmediateInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
