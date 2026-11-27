using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Sources;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests.Sources;

/// <summary>
/// Data sources as dataset entries (design note D3): <see cref="DatasetEntry.ForSource"/>,
/// <see cref="DatasetsViewModel.AddSource"/>, <see cref="HostedSourceActivator"/>,
/// <see cref="SourceDatasetService"/> and the NDBC overlay migration.
/// </summary>
public sealed class SourceDatasetTests
{
    private static readonly MapViewportSnapshot Harbour = new()
    {
        MinLatitude = 42.0,
        MinLongitude = -71.0,
        MaxLatitude = 42.5,
        MaxLongitude = -70.5,
    };

    private static readonly MapViewportSnapshot Ocean = new()
    {
        MinLatitude = 20.0,
        MinLongitude = -80.0,
        MaxLatitude = 50.0,
        MaxLongitude = -30.0,
    };

    private static ViewerSettings Settings() =>
        new() { SettingsFilePath = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json") };

    private static SourceRegistration Registration(SourceActivation? activation = null) => new()
    {
        Id = "test.feed",
        DisplayName = "Test feed",
        Spec = SpecName.Ndbc,
        Activation = activation ?? SourceActivation.Always,
        Create = _ => new FakeHostedProcessor(),
    };

    [Fact]
    public void A_source_entry_has_no_file_and_a_source_id()
    {
        var entry = DatasetEntry.ForSource(S100Sources.NdbcObservations);

        Assert.True(entry.IsSource);
        Assert.Equal(S100Sources.NdbcObservationsId, entry.SourceId);
        Assert.Equal(string.Empty, entry.FilePath);
        Assert.Equal("NDBC observations", entry.DisplayName);
        Assert.Equal(SpecName.Ndbc, entry.ProductSpec);
        Assert.Equal(new MapDatasetId("source:ndbc.realtime"), entry.Id);
        Assert.False(entry.IsFromExchangeSet);
    }

    [Fact]
    public void A_source_is_added_to_the_list_once()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());

        var entry = datasets.AddSource(S100Sources.NdbcObservations);
        var again = datasets.AddSource(S100Sources.NdbcObservations);

        Assert.NotNull(entry);
        Assert.Null(again);
        Assert.Same(entry, Assert.Single(datasets.Entries));
        Assert.Same(entry, datasets.SelectedDataset);
        Assert.Same(entry, datasets.FindSource(S100Sources.NdbcObservationsId));
    }

    [Fact]
    public void The_activator_starts_a_shown_source_and_tells_it_the_view()
    {
        var notifier = new FakeViewportSet { View = Harbour };
        var processor = new FakeHostedProcessor();

        using var activator = new HostedSourceActivator(processor, SourceActivation.Always, notifier, isShownIn: _ => true);

        Assert.Equal(1, processor.Starts);
        Assert.True(processor.IsActive);
        Assert.True(activator.IsRunning);
        Assert.Equal(new GeographicBounds(-71.0, 42.0, -70.5, 42.5), processor.Area);
    }

    [Fact]
    public void A_hidden_source_isnt_started_until_its_shown_and_pauses_when_hidden_again()
    {
        var processor = new FakeHostedProcessor();
        var shown = false;
        using var activator = new HostedSourceActivator(
            processor, SourceActivation.Always, new FakeViewportSet { View = Harbour }, isShownIn: _ => shown);

        Assert.Equal(0, processor.Starts);
        Assert.False(processor.IsActive);

        shown = true;
        activator.Refresh();
        Assert.Equal(1, processor.Starts);
        Assert.True(processor.IsActive);

        shown = false;
        activator.Refresh();
        Assert.False(processor.IsActive);
        Assert.False(activator.IsRunning);
    }

    [Fact]
    public async Task A_source_with_a_span_limit_runs_only_when_zoomed_in()
    {
        var notifier = new FakeViewportSet { View = Ocean };
        var processor = new FakeHostedProcessor();
        using var activator = new HostedSourceActivator(
            processor, SourceActivation.BelowViewportSpan(5), notifier, isShownIn: _ => true, debounce: TimeSpan.Zero);

        Assert.Equal(0, processor.Starts);
        Assert.False(processor.IsActive);

        notifier.Publish(Harbour);
        // The activator sets IsActive before it calls Start, so wait for both.
        await WaitUntilAsync(() => processor.IsActive && processor.Starts == 1);
        Assert.Equal(1, processor.Starts);
        Assert.Equal(-71.0, processor.Area!.Value.MinLongitude);

        notifier.Publish(Ocean);
        await WaitUntilAsync(() => !processor.IsActive);
        Assert.Equal(-71.0, processor.Area!.Value.MinLongitude); // Not told the area it won't fetch.
    }

    [Fact]
    public async Task With_two_maps_a_source_runs_while_either_is_in_range_and_is_told_only_those_views()
    {
        // Split map (#1730): the gate serves every map, not just the active one.
        var second = new MapViewId("map-2");
        var bay = new MapViewportSnapshot { MinLatitude = 41.0, MinLongitude = -72.0, MaxLatitude = 41.5, MaxLongitude = -71.5 };
        var views = new FakeViewportSet { View = Ocean };
        var processor = new FakeHostedProcessor();
        using var activator = new HostedSourceActivator(
            processor, SourceActivation.BelowViewportSpan(5), views, isShownIn: _ => true, debounce: TimeSpan.Zero);
        Assert.False(processor.IsActive);

        // The second map zooms in: the source runs for it alone, even though
        // the first map is still zoomed out.
        views.Publish(second, Harbour);
        await WaitUntilAsync(() => processor.IsActive && processor.Starts == 1);
        Assert.Equal(new GeographicBounds(-71.0, 42.0, -70.5, 42.5), processor.Area);

        // Both zoomed in: it's told the box around both views.
        views.Publish(bay);
        await WaitUntilAsync(() => processor.Area == new GeographicBounds(-72.0, 41.0, -70.5, 42.5));
        Assert.Equal(new GeographicBounds(-72.0, 41.0, -70.5, 42.5), processor.Area);

        // Both zoomed out: it waits.
        views.Publish(Ocean);
        views.Publish(second, Ocean);
        await WaitUntilAsync(() => !processor.IsActive);
        Assert.Equal(SourceRunState.WaitingForZoom, activator.State);
    }

    [Fact]
    public async Task With_two_maps_a_source_runs_only_while_a_map_that_shows_it_is_in_range()
    {
        // Split map step 2 §2.5: map 2 hides the source, so its view never
        // starts it or widens its area.
        var second = new MapViewId("map-2");
        var bay = new MapViewportSnapshot { MinLatitude = 41.0, MinLongitude = -72.0, MaxLatitude = 41.5, MaxLongitude = -71.5 };
        var entry = DatasetEntry.ForSource(S100Sources.NdbcObservations);
        entry.SetVisibleIn(second, false);
        var views = new FakeViewportSet { View = Ocean };
        var processor = new FakeHostedProcessor();
        using var activator = new HostedSourceActivator(
            processor,
            SourceActivation.BelowViewportSpan(5),
            views,
            isShownIn: map => map is { } view ? entry.IsVisibleIn(view) : entry.IsVisibleAnywhere,
            debounce: TimeSpan.Zero);

        // Map 2 zooms in, but it hides the source: it waits for map 1.
        views.Publish(second, Harbour);
        await WaitUntilAsync(() => activator.State == SourceRunState.WaitingForZoom);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.False(processor.IsActive);
        Assert.Equal(0, processor.Starts);

        // Map 1, which shows it, zooms in: it runs for map 1's view alone.
        views.Publish(bay);
        await WaitUntilAsync(() => processor.IsActive && processor.Starts == 1);
        Assert.Equal(ToBounds(bay), processor.Area);

        // Map 2 shows it too: the area grows to both views.
        entry.SetVisibleIn(second, true);
        activator.Refresh();
        Assert.Equal(new GeographicBounds(-72.0, 41.0, -70.5, 42.5), processor.Area);

        // Map 1 hides it; map 2 still shows it in range.
        entry.SetVisibleIn(FakeViewportSet.First, false);
        activator.Refresh();
        Assert.True(processor.IsActive);
        Assert.Equal(ToBounds(Harbour), processor.Area);

        // Hidden in both maps: paused.
        entry.SetVisibleIn(second, false);
        activator.Refresh();
        Assert.False(processor.IsActive);
        Assert.Equal(SourceRunState.Hidden, activator.State);
    }

    [Fact]
    public void The_service_runs_a_source_while_a_map_that_shows_it_is_in_range()
    {
        var second = new MapViewId("map-2");
        var registry = new SourceRegistry();
        registry.Register(Registration(SourceActivation.BelowViewportSpan(5)));
        var views = new FakeViewportSet { View = Harbour };
        views.Publish(second, Ocean);
        using var service = new SourceDatasetService(registry, views);
        var entry = DatasetEntry.ForSource(registry.Resolve("test.feed"));
        var processor = (FakeHostedProcessor)service.CreateProcessor("test.feed", options: null);

        service.Attach(entry, processor);
        Assert.True(service.IsRunning(entry.Id));

        // Hiding it in the zoomed-in map pauses it, though map 2 still shows it.
        entry.SetVisibleIn(FakeViewportSet.First, false);
        Assert.False(service.IsRunning(entry.Id));
        Assert.Equal(SourceStatusKind.WaitingForZoom, entry.SourceStatus!.Kind);

        entry.SetVisibleIn(FakeViewportSet.First, true);
        Assert.True(service.IsRunning(entry.Id));
    }

    private static GeographicBounds ToBounds(MapViewportSnapshot view) => HostedSourceActivator.ToBounds(view);

    [Fact]
    public async Task Quick_view_changes_apply_in_order_so_the_last_one_wins()
    {
        // With no debounce, each change's continuation is already due when
        // the next arrives; an older one finishing last mustn't win (#1747's
        // CI caught the two-map test above failing this way).
        var views = new FakeViewportSet { View = Ocean };
        var processor = new FakeHostedProcessor();
        using var activator = new HostedSourceActivator(
            processor, SourceActivation.BelowViewportSpan(5), views, isShownIn: _ => true, debounce: TimeSpan.Zero);

        for (int round = 0; round < 50; round++)
        {
            for (int i = 0; i < 20; i++)
            {
                views.Publish(Harbour);
                views.Publish(Ocean);
            }

            await WaitUntilAsync(() => activator.State == SourceRunState.WaitingForZoom && !processor.IsActive);
            // Let any older continuation still queued run: it must not turn the source back on.
            await Task.Delay(5, TestContext.Current.CancellationToken);
            Assert.False(processor.IsActive, $"round {round}: an older view won");
            Assert.Equal(SourceRunState.WaitingForZoom, activator.State);
        }
    }

    [Fact]
    public void With_no_map_only_a_source_that_always_runs_is_in_range()
    {
        var always = HostedSourceActivator.AreaInRange(SourceActivation.Always, [], out var alwaysInRange);
        var limited = HostedSourceActivator.AreaInRange(SourceActivation.BelowViewportSpan(5), [], out var limitedInRange);

        Assert.True(alwaysInRange);
        Assert.Null(always);
        Assert.False(limitedInRange);
        Assert.Null(limited);
    }

    [Fact]
    public void The_service_runs_a_loaded_entry_following_its_visibility()
    {
        var registry = new SourceRegistry();
        registry.Register(Registration());
        using var service = new SourceDatasetService(registry, new FakeViewportSet { View = Harbour });
        var entry = DatasetEntry.ForSource(registry.Resolve("test.feed"));
        var processor = (FakeHostedProcessor)service.CreateProcessor("test.feed", options: null);

        service.Attach(entry, processor);
        Assert.True(service.IsRunning(entry.Id));

        entry.SetVisible(false);
        Assert.False(service.IsRunning(entry.Id));
        Assert.False(processor.IsActive);

        service.Detach(entry.Id);
        entry.SetVisible(true);
        Assert.False(processor.IsActive);
    }

    [Fact]
    public void Host_extensions_for_the_source_attach_while_its_loaded()
    {
        var registry = new SourceRegistry();
        registry.Register(Registration());
        var mine = new RecordingExtension("test.feed");
        var other = new RecordingExtension("other.feed");
        using var service = new SourceDatasetService(registry, extensions: [mine, other]);
        var entry = DatasetEntry.ForSource(registry.Resolve("test.feed"));
        var processor = service.CreateProcessor("test.feed", options: null);

        service.Attach(entry, processor);
        Assert.Equal(1, mine.Attached);
        Assert.Equal(0, mine.Detached);
        Assert.Equal(0, other.Attached);

        service.Detach(entry.Id);
        Assert.Equal(1, mine.Detached);
    }

    [Fact]
    public void An_extension_can_say_what_a_source_row_shows_unless_its_hidden()
    {
        var registry = new SourceRegistry();
        registry.Register(Registration());
        var extension = new StatusExtension("test.feed");
        using var service = new SourceDatasetService(registry, new FakeViewportSet { View = Harbour }, extensions: [extension]);
        var entry = DatasetEntry.ForSource(registry.Resolve("test.feed"));

        service.Attach(entry, service.CreateProcessor("test.feed", options: null));
        Assert.Equal(SourceStatusKind.Starting, entry.SourceStatus!.Kind);

        extension.Set(new SourceStatus(SourceStatusKind.WaitingForZoom, "Zoom in"));
        Assert.Equal("Zoom in", entry.SourceStatus.Text);

        entry.SetVisible(false);
        Assert.Equal(SourceStatusKind.Paused, entry.SourceStatus.Kind);

        entry.SetVisible(true);
        extension.Set(null);
        Assert.Equal(SourceStatusKind.Starting, entry.SourceStatus.Kind);

        service.Detach(entry.Id);
        extension.Set(new SourceStatus(SourceStatusKind.Failed, "Not followed"));
        Assert.Equal(SourceStatusKind.Starting, entry.SourceStatus.Kind);
    }

    private static readonly DateTimeOffset Noon = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Status_says_why_a_source_shows_nothing_or_how_fresh_it_is()
    {
        var registration = Registration(SourceActivation.BelowViewportSpan(25)) with { StaleAfter = TimeSpan.FromMinutes(30) };

        Assert.Equal(SourceStatusKind.Paused, SourceStatus.Evaluate(SourceRunState.Hidden, registration, null, null, Noon).Kind);
        var zoom = SourceStatus.Evaluate(SourceRunState.WaitingForZoom, registration, null, null, Noon);
        Assert.Equal(SourceStatusKind.WaitingForZoom, zoom.Kind);
        Assert.Contains("25", zoom.Text, StringComparison.Ordinal);
        Assert.Equal(SourceStatusKind.Starting, SourceStatus.Evaluate(SourceRunState.Running, registration, null, null, Noon).Kind);

        var live = SourceStatus.Evaluate(SourceRunState.Running, registration, Noon.AddMinutes(-3), null, Noon);
        Assert.Equal(SourceStatusKind.Live, live.Kind);
        Assert.Contains("3 min", live.Text, StringComparison.Ordinal);

        var stale = SourceStatus.Evaluate(SourceRunState.Running, registration, Noon.AddMinutes(-42), null, Noon);
        Assert.Equal(SourceStatusKind.Stale, stale.Kind);
        Assert.Contains("42 min", stale.Text, StringComparison.Ordinal);

        // An error with fresh data isn't a failure; one with no data, or stale data, is.
        var error = new HttpRequestException("503 Service Unavailable");
        Assert.Equal(SourceStatusKind.Live, SourceStatus.Evaluate(SourceRunState.Running, registration, Noon.AddMinutes(-3), error, Noon).Kind);
        var failed = SourceStatus.Evaluate(SourceRunState.Running, registration, null, error, Noon);
        Assert.Equal(SourceStatusKind.Failed, failed.Kind);
        Assert.Equal("503 Service Unavailable", failed.Detail);
        Assert.Equal(SourceStatusKind.Failed, SourceStatus.Evaluate(SourceRunState.Running, registration, Noon.AddMinutes(-42), error, Noon).Kind);
    }

    [Fact]
    public void The_service_keeps_a_source_rows_status_current()
    {
        var time = new FakeTimeProvider(Noon);
        var processor = new FakeHostedProcessor();
        var registry = new SourceRegistry();
        registry.Register(Registration() with { StaleAfter = TimeSpan.FromMinutes(5), Create = _ => processor, Group = SourceGroup.Observations });
        using var service = new SourceDatasetService(registry, new FakeViewportSet { View = Harbour }, timeProvider: time);
        var entry = DatasetEntry.ForSource(registry.Resolve("test.feed"));

        service.Attach(entry, service.CreateProcessor("test.feed", options: null));
        Assert.Equal(SourceStatusKind.Starting, entry.SourceStatus!.Kind);
        Assert.Equal(entry.SourceStatus.Text, entry.Subtitle);
        Assert.Equal("Observations", entry.SourceGroupLabel);

        processor.LastUpdated = time.GetUtcNow();
        processor.RaiseChanged();
        Assert.Equal(SourceStatusKind.Live, entry.SourceStatus.Kind);

        time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(SourceStatusKind.Stale, entry.SourceStatus.Kind);
        Assert.True(entry.NeedsAttention);

        entry.SetVisible(false);
        Assert.Equal(SourceStatusKind.Paused, entry.SourceStatus.Kind);

        service.Detach(entry.Id);
        entry.SetVisible(true);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(SourceStatusKind.Paused, entry.SourceStatus.Kind);
    }

    [Fact]
    public void A_file_row_shows_its_product_and_keeps_its_reorder_controls()
    {
        var file = new DatasetEntry("/charts/101AA00DS0019.000", "S-101");
        var source = DatasetEntry.ForSource(S100Sources.NdbcObservations);

        Assert.Equal("S-101", file.Subtitle);
        Assert.True(file.IsFile);
        Assert.False(file.HasSourceGroup);
        Assert.False(source.IsFile);
        Assert.Equal("NDBC", source.Subtitle); // Until it loads and has a status.
    }

    [Fact]
    public void Reordering_skips_a_source_row()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var file = datasets.Add("/charts/a.000", "S-101");
        var source = datasets.AddSource(S100Sources.NdbcObservations)!;

        datasets.MoveDownCommand.Execute(source);

        Assert.Same(source, datasets.Entries[0]);
        Assert.Same(file, datasets.Entries[1]);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_AIS_switch_becomes_an_added_feed(bool wasOn, bool added)
    {
        var settings = Settings();
        settings.AisOverlay = new AisOverlaySettings { Enabled = wasOn };

        ViewerSettings.MigrateAisOverlay(settings);

        Assert.False(settings.AisOverlay.Enabled);
        Assert.Equal(added, settings.SourceDatasets.Contains(EncDotNet.S100.Viewer.Services.DynamicSources.Ais.AisSource.Id));
    }

    [Fact]
    public void The_AIS_extension_follows_the_loaded_entry()
    {
        var presence = new EncDotNet.S100.Viewer.Services.DynamicSources.Ais.AisPresence();
        var extension = new EncDotNet.S100.Viewer.Services.DynamicSources.Ais.AisHostExtension(
            pirateMode: null, vesselList: () => null, aisLoadedChanged: presence.Set);
        var excluding = new EncDotNet.S100.Viewer.Services.DynamicSources.Ais.ExcludingAisFeatureSource(new EmptySource());
        using var processor = new EncDotNet.S100.Datasets.Pipelines.DynamicSources.DynamicSourceProcessor(
            excluding, EncDotNet.S100.Portrayals.Application.VesselPortrayal.Ais, new SpecRef(SpecName.Ais, default));
        var entry = DatasetEntry.ForSource(Registration());

        Assert.Null(extension.Attach(entry, new FakeHostedProcessor()));
        var attachment = extension.Attach(entry, processor);
        Assert.NotNull(attachment);
        Assert.True(presence.IsLoaded);
        attachment!.Dispose();
        Assert.False(presence.IsLoaded);
    }

    private sealed class EmptySource : EncDotNet.S100.DynamicSources.IDynamicFeatureSource
    {
        public string Id => "ais";

        public EncDotNet.S100.DynamicSources.DynamicSourceMetadata Metadata { get; } = new() { DisplayName = "AIS" };

        public IReadOnlyList<EncDotNet.S100.DynamicSources.DynamicFeature> CurrentFeatures => [];

        public event EventHandler<EncDotNet.S100.DynamicSources.DynamicFeaturesChanged>? Changed
        {
            add { }
            remove { }
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_NDBC_overlay_toggle_becomes_an_added_source(bool wasVisible, bool added)
    {
        var settings = Settings();
        settings.DynamicSourceVisibility[ViewerSettings.LegacyNdbcVisibilityKey] = wasVisible;

        ViewerSettings.MigrateNdbcOverlay(settings);

        Assert.False(settings.DynamicSourceVisibility.ContainsKey(ViewerSettings.LegacyNdbcVisibilityKey));
        Assert.Equal(added, settings.SourceDatasets.Contains(S100Sources.NdbcObservationsId));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the activator.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class RecordingExtension(string sourceId) : ISourceHostExtension
    {
        public int Attached { get; private set; }

        public int Detached { get; private set; }

        public string SourceId => sourceId;

        public IDisposable? Attach(DatasetEntry entry, IDatasetProcessor processor)
        {
            Attached++;
            return new Callback(() => Detached++);
        }

        private sealed class Callback(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }

    private sealed class StatusExtension(string sourceId) : ISourceHostExtension, ISourceStatusOverride, IDisposable
    {
        public string SourceId => sourceId;

        public SourceStatus? Status { get; private set; }

        public event EventHandler? StatusChanged;

        public void Set(SourceStatus? status)
        {
            Status = status;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public IDisposable? Attach(DatasetEntry entry, IDatasetProcessor processor) => this;

        public void Dispose()
        {
        }
    }

    private sealed class FakeHostedProcessor : IHostedDatasetProcessor, ILiveDatasetProcessor
    {
        public DateTimeOffset? LastUpdated { get; set; }

        public Exception? LastError { get; set; }

        public event EventHandler<DatasetChangedEventArgs>? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, DatasetChangedEventArgs.Empty);

        private volatile bool _isActive;

        private int _starts;

        public int Starts => Volatile.Read(ref _starts);

        public GeographicBounds? Area { get; private set; }

        public bool IsActive
        {
            get => _isActive;
            set => _isActive = value;
        }

        public SpecRef Spec => new(SpecName.Ndbc, default);

        public void Start() => Interlocked.Increment(ref _starts);

        public void UpdateArea(GeographicBounds? area) => Area = area;

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
