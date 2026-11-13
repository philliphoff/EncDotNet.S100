using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Tides;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The predicted current stations in SoundCharts (#1454, tide predictions
/// design D16, D22): the overlay, S-111 suppression, its Legend section and
/// the station card, over a synthetic M2 current station.
/// </summary>
public sealed class CurrentStationOverlayTests : IDisposable
{
    private static readonly DateTime ViewTime = new(2026, 11, 12, 22, 20, 0, DateTimeKind.Utc);
    private static readonly GeoPosition Inlet = new(47.95, -122.58);
    private readonly string _folder = Directory.CreateTempSubdirectory("s100-currents-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task Current_stations_in_view_are_published_with_speed_direction_and_label()
    {
        var source = await Source((_, _) => false);
        source.UpdateView(View(47.95, -122.58, 200_000));

        var feature = Assert.Single(source.CurrentFeatures);
        var state = Assert.IsType<CurrentStationState>(CurrentStationSource.GetState(feature));
        Assert.Equal("PUG0001_1", state.StationId);
        Assert.Equal(6.1, state.DepthMeters);
        Assert.InRange(state.SpeedKnots, 0, 3);
        Assert.True(state.IsSlack || state.Label.EndsWith(" kn", StringComparison.Ordinal));
        Assert.True(state.IsSlack || Math.Abs(state.DirectionDegrees - (state.IsFlood ? 125 : 305)) < 1);
    }

    [Fact]
    public async Task A_station_S111_covers_at_the_view_time_is_not_drawn()
    {
        var source = await Source((_, _) => true);
        source.UpdateView(View(47.95, -122.58, 200_000));

        Assert.Empty(source.CurrentFeatures);
        Assert.Single(source.StationsInView);
    }

    [Fact]
    public void The_glyph_is_an_S111_band_arrow_rising_from_a_dashed_frame()
    {
        Assert.Contains("stroke-dasharray", CurrentStationPortrayal.ResolveSymbol(CurrentStationPortrayal.FrameSymbol), StringComparison.Ordinal);
        var arrow = CurrentStationPortrayal.ResolveSymbol(CurrentStationPortrayal.ArrowSymbol(5));
        Assert.NotNull(arrow);
        Assert.Contains("M 0 0 L -0.5 0", arrow, StringComparison.Ordinal);
        Assert.Equal(1, CurrentStationPortrayal.BandOf(0.2));
        Assert.Equal(4, CurrentStationPortrayal.BandOf(2.5));
        Assert.Equal(9, CurrentStationPortrayal.BandOf(20));
    }

    [Fact]
    public async Task The_legend_lists_the_speed_bands_and_slack_while_on()
    {
        var list = await List();
        using var overlay = new TideStationsOverlay(
            list, notifier: null, new FixedViewTime(ViewTime), new NoWaterLevel(), TimeProvider.System, () => TimeFormat.Utc, () => []);
        using var legend = new CurrentStationLegendSource(overlay, () => PaletteType.Day, () => TimeFormat.Utc, () => ViewTime);
        Assert.Empty(legend.Sections);

        overlay.SetCurrentsVisible(true);
        overlay.Currents.UpdateView(View(47.95, -122.58, 200_000));

        var section = Assert.Single(legend.Sections);
        Assert.Equal(CurrentStationLegendSource.SectionId, section.Id);
        Assert.Equal(10, section.Rows.Count());
        Assert.Equal("Slack, with the turn time", section.Rows.Last().Label);
    }

    [AvaloniaFact]
    public async Task The_card_lists_slacks_and_maxima_and_says_display_only()
    {
        var list = await List();
        var catalogue = list.Catalogue!;
        var station = catalogue.FindCurrentStation("PUG0001_1")!;
        var predictor = catalogue.TryCreateCurrentPrediction(station.Id)!.Predictor;
        var card = new CurrentStationCardViewModel(
            catalogue,
            station,
            predictor,
            new CurrentCardContext(
                TestStationCharts.Context(timeFormat: new TestStationCharts.FixedTimeFormat(TimeFormat.Utc)),
                new FixedViewTime(ViewTime),
                TimeProvider.System,
                action => action()));
        using var host = ViewHost.Show(new CurrentStationCardView { DataContext = card }, width: 420, height: 900);

        Assert.Equal($"NOAA current station PUG0001_1 · harmonic · {6.1.ToString(System.Globalization.CultureInfo.CurrentCulture)} m below the surface", card.Subtitle);
        Assert.Equal("PREDICTED AT 22:20 UTC", card.NowLabel);
        Assert.Equal(6, card.Events.Count);
        Assert.True(card.Events[0].IsPast);
        Assert.Contains(card.Events, e => e.Kind == "Slack");
        Assert.Contains(card.Events, e => e.Kind is "Max flood" or "Max ebb");
        Assert.Equal("125°", card.FloodDirection);
        Assert.True(host.IsShown("Pick.CurrentStation.UsedFor"));
        if (Environment.GetEnvironmentVariable("S100_TEST_CAPTURE_DIR") is { Length: > 0 } dir)
        {
            File.WriteAllBytes(Path.Combine(dir, "current-card.png"), host.CaptureFrame());
        }

        card.Release();
    }

    private async Task<TideStationListService> List()
    {
        var path = Path.Combine(_folder, "tide-stations.json.gz");
        var station = new CurrentStationEntry
        {
            Id = "PUG0001_1",
            Name = "Test Inlet",
            Position = Inlet,
            Kind = TideStationKind.Harmonic,
            DepthMeters = 6.1,
            FloodDirectionDegrees = 125,
            EbbDirectionDegrees = 305,
            MajorAxisDirectionDegrees = 125,
            Constituents = [new CurrentConstituent("M2", 1.0, 40)],
        };
        await TideStationCatalogueFile.SaveAsync(path, new TideStationCatalogue([], [station], "NOAA CO-OPS", ViewTime), TestContext.Current.CancellationToken);
        var list = TideTestLists.At(path);
        await list.EnsureLoadedAsync();
        return list;
    }

    private async Task<CurrentStationSource> Source(Func<GeoPosition, DateTime, bool> coveredByS111)
    {
        var list = await List();
        var source = new CurrentStationSource(list, () => ViewTime, () => TimeFormat.Utc, coveredByS111);
        source.IsActive = true;
        await list.EnsureAvailableAsync();
        return source;
    }

    private static MapViewportSnapshot View(double latitude, double longitude, double scale) => new()
    {
        MinLatitude = latitude - 0.2,
        MaxLatitude = latitude + 0.2,
        MinLongitude = longitude - 0.3,
        MaxLongitude = longitude + 0.3,
        MercatorResolution = MapScaleFormatter.ScaleDenominatorToResolution(scale, latitude),
    };

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
