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
        var (overlay, list) = await OverlayAndList();
        using var _ = overlay;
        var registration = TideFeeds.TideStations(() => overlay);

        Assert.Equal(TideFeeds.TideStationsId, registration.Id);
        Assert.Equal(SourceGroup.Predictions, registration.Group);
        var processor = Assert.IsType<DynamicSourceProcessor>(registration.Create(new SourceCreationContext()));
        processor.IsActive = true;
        Assert.True(overlay.Source.IsActive);

        processor.Dispose();
        // The processor doesn't own the source; the host extension turns it off.
        Assert.True(overlay.Source.IsActive);
        var extension = new TideFeedHostExtension(TideFeeds.TideStationsId, () => overlay.Source, () => list);
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

    [Fact]
    public void A_station_feed_row_says_what_the_station_list_is_doing()
    {
        var downloading = TideFeedHostExtension.StatusFor(
            TideStationListActivity.Downloading, new(120, 2134), hasList: false, lastError: null, viewTooWide: false)!;
        Assert.Equal(Services.Sources.SourceStatusKind.Starting, downloading.Kind);
        Assert.Equal(
            string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.Strings.Tides_FeedStatus_DownloadingListFormat, 120, 2134),
            downloading.Text);
        Assert.NotNull(downloading.Detail);

        Assert.Equal(
            Resources.Strings.Tides_FeedStatus_DownloadingList,
            TideFeedHostExtension.StatusFor(TideStationListActivity.Downloading, null, false, null, false)!.Text);
        Assert.Equal(
            Resources.Strings.Tides_FeedStatus_ReadingList,
            TideFeedHostExtension.StatusFor(TideStationListActivity.Loading, null, false, null, false)!.Text);
        Assert.Equal(
            Resources.Strings.Tides_FeedStatus_WaitingForList,
            TideFeedHostExtension.StatusFor(TideStationListActivity.Idle, null, false, null, false)!.Text);

        var failed = TideFeedHostExtension.StatusFor(TideStationListActivity.Idle, null, false, "Forbidden.", false)!;
        Assert.Equal(Services.Sources.SourceStatusKind.Failed, failed.Kind);
        Assert.Contains("Forbidden.", failed.Detail, StringComparison.Ordinal);

        Assert.Equal(
            Services.Sources.SourceStatusKind.WaitingForZoom,
            TideFeedHostExtension.StatusFor(TideStationListActivity.Idle, null, true, null, viewTooWide: true)!.Kind);
        Assert.Null(TideFeedHostExtension.StatusFor(TideStationListActivity.Idle, null, true, null, viewTooWide: false));
        Assert.Null(TideFeedHostExtension.StatusFor(TideStationListActivity.Checking, null, true, "Old error.", false));
    }

    [Fact]
    public async Task A_station_feed_row_follows_the_list_and_the_zoom()
    {
        var (overlay, list) = await OverlayAndList();
        using var _ = overlay;
        var registration = TideFeeds.TideStations(() => overlay);
        var extension = new TideFeedHostExtension(TideFeeds.TideStationsId, () => overlay.Source, () => list);
        using var attachment = extension.Attach(DatasetEntry.ForSource(registration), new NoProcessor())!;
        var status = Assert.IsAssignableFrom<Services.Sources.ISourceStatusOverride>(attachment);
        var changes = 0;
        status.StatusChanged += (_, _) => changes++;

        overlay.Source.IsActive = true;
        overlay.Source.UpdateView(View(47.6, -122.4, 2_000_000));
        Assert.Equal(Services.Sources.SourceStatusKind.WaitingForZoom, status.Status!.Kind);
        Assert.True(changes > 0);

        overlay.Source.UpdateView(View(47.6, -122.4, 400_000));
        Assert.Null(status.Status);
    }

    [Fact]
    public async Task A_station_feed_row_says_when_the_list_download_failed()
    {
        var list = TideTestLists.Missing();
        using var overlay = new TideStationsOverlay(
            list, notifier: null, new FixedViewTime(ViewTime), new NoWaterLevel(), TimeProvider.System, () => TimeFormat.Utc, () => []);
        var extension = new TideFeedHostExtension(TideFeeds.CurrentStationsId, () => overlay.Currents, () => list);
        using var attachment = extension.Attach(DatasetEntry.ForSource(TideFeeds.CurrentStations(() => overlay)), new NoProcessor())!;
        var status = Assert.IsAssignableFrom<Services.Sources.ISourceStatusOverride>(attachment);

        Assert.Equal(Services.Sources.SourceStatusKind.Starting, status.Status!.Kind);
        await list.EnsureAvailableAsync();

        Assert.Equal(Services.Sources.SourceStatusKind.Failed, status.Status!.Kind);
        Assert.Contains("Offline in tests.", status.Status.Detail, StringComparison.Ordinal);
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

    private sealed class NoProcessor : IDatasetProcessor
    {
        public EncDotNet.S100.Core.SpecRef Spec => new(EncDotNet.S100.Core.SpecName.TidePredictions, default);

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

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
