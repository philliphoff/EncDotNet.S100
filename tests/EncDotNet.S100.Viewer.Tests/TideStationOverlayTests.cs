using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Tides;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The predicted tide stations in SoundCharts (#1453, tide predictions design
/// D4–D8): the overlay over NOAA's Puget Sound stations, its thinning, its
/// Legend section and the station card.
/// </summary>
public sealed class TideStationOverlayTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "puget-sound.json");

    // 12 Nov 2026 22:20 UTC, 14:20 in Seattle: the design pass's view time.
    private static readonly DateTime ViewTime = new(2026, 11, 12, 22, 20, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Stations_in_view_are_published_with_their_predicted_height_and_trend()
    {
        var source = await Source((_, _) => false);
        source.UpdateView(View(47.6, -122.4, 400_000));

        var features = source.CurrentFeatures;
        Assert.Equal(5, source.StationsInView.Count);
        Assert.NotEmpty(features);
        var seattle = Assert.Single(features, f => f.Id == TideStationSource.FeatureIdFor("9447130"));
        var state = Assert.IsType<TideStationState>(TideStationSource.GetState(seattle));
        Assert.Equal("MLLW", state.DatumName);
        Assert.Equal(TideStationKind.Harmonic, state.Kind);
        Assert.InRange(state.HeightMeters, -1.5, 4.5);
        Assert.Equal(TideStationSource.FeatureKind, seattle.Kind);
    }

    [Fact]
    public async Task Nothing_is_published_below_one_to_500000_or_while_the_overlay_is_off()
    {
        var source = await Source((_, _) => false);
        source.UpdateView(View(47.6, -122.4, 1_000_000));
        Assert.Empty(source.CurrentFeatures);

        source.UpdateView(View(47.6, -122.4, 200_000));
        Assert.NotEmpty(source.CurrentFeatures);

        source.IsActive = false;
        Assert.Empty(source.CurrentFeatures);
        Assert.Empty(source.StationsInView);
    }

    [Fact]
    public async Task A_station_S104_covers_at_the_view_time_is_not_drawn()
    {
        var seattle = new GeoPosition(47.602638889, -122.339167);
        var source = await Source((p, _) => Math.Abs(p.Latitude - seattle.Latitude) < 1e-3 && Math.Abs(p.Longitude - seattle.Longitude) < 1e-3);
        source.UpdateView(View(47.6, -122.4, 100_000));

        Assert.DoesNotContain(source.CurrentFeatures, f => f.Id == TideStationSource.FeatureIdFor("9447130"));
        Assert.Contains(source.StationsInView, s => s.StationId == "9447130");
    }

    [Fact]
    public void Thinning_keeps_harmonic_stations_first_then_subordinates_by_id()
    {
        var near = new GeoPosition(47.6, -122.4);
        var nearby = new GeoPosition(47.6005, -122.4005);
        var far = new GeoPosition(48.0, -122.4);
        var kept = TideStationSource.Thin(
            [
                State("S2", TideStationKind.Subordinate, nearby),
                State("S1", TideStationKind.Subordinate, far),
                State("H1", TideStationKind.Harmonic, near),
            ],
            distance: 500);

        Assert.Equal(["H1", "S1"], kept.Select(s => s.StationId));
    }

    [Fact]
    public async Task The_legend_section_shows_while_the_overlay_is_on()
    {
        var list = TideTestLists.At(Fixture);
        await list.EnsureLoadedAsync();
        using var overlay = new TideStationsOverlay(
            list, notifier: null, new FixedViewTime(ViewTime), new NoWaterLevel(), TimeProvider.System, () => TimeFormat.Utc, () => []);
        using var legend = new TideStationLegendSource(overlay, () => PaletteType.Day, () => TimeFormat.Utc, () => ViewTime);
        var changes = 0;
        legend.Changed += (_, _) => changes++;

        Assert.Empty(legend.Sections);

        overlay.SetVisible(true);
        await list.EnsureAvailableAsync();
        overlay.Source.UpdateView(View(47.6, -122.4, 400_000));

        var section = Assert.Single(legend.Sections);
        Assert.Equal(TideStationLegendSource.SectionId, section.Id);
        Assert.Equal("NOAA CO-OPS, 5 in view", section.Source);
        Assert.Equal("22:20 UTC · predicted", section.TimeSummary);
        Assert.Equal(["Rising", "Falling", "Turning (high or low within 20 min)"], section.Rows.Select(r => r.Label));
        Assert.True(changes > 0);
    }

    [Fact]
    public void The_glyphs_are_S104_gauges_with_a_dashed_frame()
    {
        var svg = TideStationPortrayal.ResolveSymbol(TideStationPortrayal.RisingSymbol);
        Assert.NotNull(svg);
        Assert.Contains("stroke-dasharray", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("stroke-dasharray", TideStationPortrayal.ResolveSymbol("S104GAUGE_RISING")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Times_on_the_cards_say_today_and_tomorrow_in_the_chosen_zone()
    {
        var now = new DateTime(2026, 11, 12, 22, 20, 0, DateTimeKind.Utc);
        Assert.Equal("Today 23:05 UTC", TideTimeText.DayAndClock(new DateTime(2026, 11, 12, 23, 5, 0, DateTimeKind.Utc), TimeFormat.Utc, now));
        Assert.Equal("Tomorrow 04:50 UTC", TideTimeText.DayAndClock(new DateTime(2026, 11, 13, 4, 50, 0, DateTimeKind.Utc), TimeFormat.Utc, now));
        Assert.Equal("Yesterday 23:05 UTC", TideTimeText.DayAndClock(new DateTime(2026, 11, 11, 23, 5, 0, DateTimeKind.Utc), TimeFormat.Utc, now));
    }

    [AvaloniaFact]
    public async Task The_card_of_a_harmonic_station_shows_the_last_high_or_low_and_the_next_four()
    {
        var (catalogue, card) = await Card("9447130", usePredictions: false);
        using var host = ViewHost.Show(new TideStationCardView { DataContext = card }, width: 420, height: 1000);

        Assert.StartsWith("NOAA tide station 9447130 · harmonic · ", card.Subtitle, StringComparison.Ordinal);
        Assert.Equal("PREDICTED HEIGHT AT 22:20 UTC", card.NowLabel);
        Assert.Equal(5, card.Extremes.Count);
        Assert.True(card.Extremes[0].IsPast);
        Assert.All(card.Extremes.Skip(1), e => Assert.False(e.IsPast));
        Assert.True(card.HasMhhw);
        Assert.False(card.HasReference);
        Assert.Equal("No: tide predictions are off", card.UsedForDepthsText);
        Assert.True(card.CanTurnOn);
        Assert.True(host.IsShown("Pick.TideStation.UsePredictions"));
        Assert.True(host.IsShown("Pick.TideStation.NotAForecast"));
        Assert.False(host.IsShown("Pick.TideStation.Reference"));
        Assert.Contains("NOAA CO-OPS", card.StationListText, StringComparison.Ordinal);
        Capture(host, "tide-card-seattle.png");
        _ = catalogue;
        card.Release();
    }

    [AvaloniaFact]
    public async Task The_card_of_a_subordinate_station_names_its_reference_and_offsets()
    {
        var (_, card) = await Card("9446486", usePredictions: true);
        using var host = ViewHost.Show(new TideStationCardView { DataContext = card }, width: 420, height: 1100);

        Assert.Contains("subordinate · from SEATTLE (Madison St.), Elliott Bay 9447130", card.Subtitle, StringComparison.Ordinal);
        Assert.True(card.HasReference);
        Assert.StartsWith("SEATTLE (Madison St.), Elliott Bay 9447130 · ", card.ReferenceStationText, StringComparison.Ordinal);
        Assert.Contains("height", card.HighWaterOffset, StringComparison.Ordinal);
        Assert.Equal("Yes, within 10 km, where there's no S-104", card.UsedForDepthsText);
        Assert.False(card.CanTurnOn);
        Assert.True(host.IsShown("Pick.TideStation.Reference"));
        Capture(host, "tide-card-tacoma-narrows.png");
        card.Release();
    }

    [AvaloniaFact]
    public async Task The_card_follows_the_view_time()
    {
        var (_, card) = await Card("9447130", usePredictions: true);
        var first = card.NowHeight;
        card.Update(ViewTime.AddHours(6));
        Assert.Equal("PREDICTED HEIGHT AT 04:20 UTC", card.NowLabel);
        Assert.NotEqual(first, card.NowHeight);
        card.Release();
    }

    [Fact]
    public async Task The_MCP_tools_read_and_set_the_overlay_and_the_source_switch()
    {
        var folder = Directory.CreateTempSubdirectory("s100-tide-display-").FullName;
        try
        {
            var list = TideTestLists.At(Fixture);
            var settings = new ViewerSettingsService(new ViewerSettings { SettingsFilePath = Path.Combine(folder, "settings.json") });
            using var predictions = new Services.Depth.TidePredictionSourceService(
                settings,
                new EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache(new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory()),
                list,
                action => action());
            using var overlay = new TideStationsOverlay(
                list, notifier: null, new FixedViewTime(ViewTime), new NoWaterLevel(), TimeProvider.System, () => TimeFormat.Utc, () => []);
            var tools = new McpTools.TideDisplayTools(overlay, settings, predictions, list, new ImmediateInvoker());

            var before = Assert.IsType<EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<McpTools.TideDisplayDto>.OkResult>(
                await tools.GetAsync(TestContext.Current.CancellationToken)).Value;
            Assert.False(before.TideStations);
            Assert.False(before.UsePredictions);
            Assert.Equal("off", before.PredictionState);

            await tools.SetAsync(tideStations: true, usePredictions: true, cancellationToken: TestContext.Current.CancellationToken);
            await predictions.Loading;
            overlay.Source.UpdateView(View(47.6, -122.4, 400_000));
            var after = Assert.IsType<EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<McpTools.TideDisplayDto>.OkResult>(
                await tools.GetAsync(TestContext.Current.CancellationToken)).Value;

            Assert.True(after.TideStations);
            Assert.True(settings.UseTidePredictions);
            Assert.Equal("in_use", after.PredictionState);
            Assert.Equal(5, after.StationsInView);
            Assert.True(after.StationList.Present);
            Assert.Equal(5, after.StationList.TideStations);
            Assert.Equal("NOAA CO-OPS", after.StationList.Provider);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class ImmediateInvoker : McpTools.IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static async Task<(TideStationCatalogue Catalogue, TideStationCardViewModel Card)> Card(string id, bool usePredictions)
    {
        var catalogue = await TideStationCatalogueFile.LoadAsync(Fixture, TestContext.Current.CancellationToken);
        var station = catalogue.Find(id)!;
        var predictor = catalogue.TryCreatePrediction(id, VerticalDatum.MeanLowerLowWater)!.Predictor;
        var on = usePredictions;
        var context = new TideCardContext(
            TestStationCharts.Context(timeFormat: new TestStationCharts.FixedTimeFormat(TimeFormat.Utc)),
            new FixedViewTime(ViewTime),
            TimeProvider.System,
            WaterLevel: null,
            () => on,
            () => on = true,
            action => action());
        return (catalogue, new TideStationCardViewModel(catalogue, station, predictor, context));
    }

    // Set S100_TEST_CAPTURE_DIR to look at the rendered cards.
    private static void Capture(ViewHost host, string name)
    {
        if (Environment.GetEnvironmentVariable("S100_TEST_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            File.WriteAllBytes(Path.Combine(dir, name), host.CaptureFrame());
        }
    }

    private static async Task<TideStationSource> Source(Func<GeoPosition, DateTime, bool> coveredByS104)
    {
        var list = TideTestLists.At(Fixture);
        await list.EnsureLoadedAsync();
        var source = new TideStationSource(list, () => ViewTime, coveredByS104);
        source.IsActive = true;
        await list.EnsureAvailableAsync();
        return source;
    }

    private static MapViewportSnapshot View(double latitude, double longitude, double scale) => new()
    {
        MinLatitude = latitude - 0.5,
        MaxLatitude = latitude + 0.5,
        MinLongitude = longitude - 0.8,
        MaxLongitude = longitude + 0.8,
        MercatorResolution = MapScaleFormatter.ScaleDenominatorToResolution(scale, latitude),
    };

    private static TideStationState State(string id, TideStationKind kind, GeoPosition position) =>
        new(id, id, kind, position, 0, 0, TideTrend.Rising, "MLLW", ViewTime);

    private sealed class FixedViewTime(DateTime utc) : IViewTime
    {
        public DateTimeOffset Current => new(utc);

        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class NoWaterLevel : Services.Depth.IWaterLevelCatalog
    {
        public EncDotNet.S100.Navigation.IWaterLevelSource? WaterLevel => null;

        public string Key => "0";

        public event EventHandler? Changed { add { } remove { } }
    }
}
