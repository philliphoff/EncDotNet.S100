using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.DynamicSources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Tides;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests.Sources;

/// <summary>
/// Predicted tide and current stations as live feeds (data source design note
/// D8, slice 7d): their registrations, the pick section that brings back the
/// station card, and the Layer stack toggles' migration.
/// </summary>
public sealed class TideFeedsTests
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "puget-sound.json");
    private static readonly DateTime ViewTime = new(2026, 11, 12, 22, 20, 0, DateTimeKind.Utc);

    private static async Task<TideStationsOverlay> Overlay() => (await OverlayAndList()).Overlay;

    private static async Task<(TideStationsOverlay Overlay, TideStationListService List)> OverlayAndList()
    {
        var list = TideTestLists.At(Fixture);
        await list.EnsureLoadedAsync();
        return (new TideStationsOverlay(
            list, notifier: null, new FixedViewTime(ViewTime), new NoWaterLevel(), TimeProvider.System, () => TimeFormat.Utc, () => []), list);
    }

    [Fact]
    public async Task A_tide_feed_draws_the_shared_source_and_leaves_it_running_for_others()
    {
        using var overlay = await Overlay();
        var registration = TideFeeds.TideStations(() => overlay);

        Assert.Equal(TideFeeds.TideStationsId, registration.Id);
        Assert.Equal(SourceGroup.Predictions, registration.Group);
        var processor = Assert.IsType<DynamicSourceProcessor>(registration.Create(new SourceCreationContext()));
        processor.IsActive = true;
        Assert.True(overlay.Source.IsActive);

        processor.Dispose();
        // The processor doesn't own the source; the host extension turns it off.
        Assert.True(overlay.Source.IsActive);
        var extension = new TideFeedHostExtension(TideFeeds.TideStationsId, overlay.Source);
        extension.Attach(DatasetEntry.ForSource(registration), processor)!.Dispose();
        Assert.False(overlay.Source.IsActive);
    }

    [Fact]
    public async Task A_picked_station_carries_a_section_that_brings_back_its_card()
    {
        var (overlay, list) = await OverlayAndList();
        using var _ = overlay;
        overlay.Source.IsActive = true;
        await list.EnsureAvailableAsync();
        overlay.Source.UpdateView(View(47.6, -122.4, 400_000));
        var feature = Assert.Single(overlay.Source.CurrentFeatures, f => f.Id == TideStationSource.FeatureIdFor("9447130"));

        var section = Assert.IsType<PredictedStationSection>(Assert.Single(overlay.Source.Describe(feature)!.Sections));
        Assert.Equal(PredictedStationSection.TideKind, section.Kind);
        Assert.Equal("9447130", section.StationId);
        Assert.Equal("9447130", Assert.Single(section.ToRows()).RawValue);
    }

    [Fact]
    public void An_unknown_section_or_station_shows_rows()
    {
        var list = TideTestLists.Missing();
        using var overlay = new TideStationsOverlay(
            list, notifier: null, new FixedViewTime(ViewTime), new NoWaterLevel(), TimeProvider.System, () => TimeFormat.Utc, () => []);
        var cards = new TideStationCardFactory(list, overlay.Source, () => throw new InvalidOperationException("Not reached."));

        Assert.Null(cards.Present(new PredictedStationSection(PredictedStationSection.TideKind, "9447130", "Seattle")));
        Assert.Null(cards.Present(new PredictedStationSection(PredictedStationSection.CurrentKind, "PUG0001_1", "Somewhere")));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_Layer_stack_toggles_become_subscribed_feeds(bool wasOn, bool subscribed)
    {
        var settings = new ViewerSettings { SettingsFilePath = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json") };
        settings.DynamicSourceVisibility[TideStationSource.SourceId] = wasOn;
        settings.DynamicSourceVisibility[CurrentStationSource.SourceId] = wasOn;

        ViewerSettings.MigrateTideOverlays(settings);

        Assert.False(settings.DynamicSourceVisibility.ContainsKey(TideStationSource.SourceId));
        Assert.False(settings.DynamicSourceVisibility.ContainsKey(CurrentStationSource.SourceId));
        Assert.Equal(subscribed, settings.SourceDatasets.Contains(TideFeeds.TideStationsId));
        Assert.Equal(subscribed, settings.SourceDatasets.Contains(TideFeeds.CurrentStationsId));
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
