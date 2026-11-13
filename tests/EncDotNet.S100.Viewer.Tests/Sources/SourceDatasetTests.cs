using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Sources;
using EncDotNet.S100.Viewer.ViewModels;

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
        var notifier = new FakeNotifier { Current = Harbour };
        var processor = new FakeHostedProcessor();

        using var activator = new HostedSourceActivator(processor, SourceActivation.Always, notifier, visible: true);

        Assert.Equal(1, processor.Starts);
        Assert.True(processor.IsActive);
        Assert.True(activator.IsRunning);
        Assert.Equal(new GeographicBounds(-71.0, 42.0, -70.5, 42.5), processor.Area);
    }

    [Fact]
    public void A_hidden_source_isnt_started_until_its_shown_and_pauses_when_hidden_again()
    {
        var processor = new FakeHostedProcessor();
        using var activator = new HostedSourceActivator(processor, SourceActivation.Always, new FakeNotifier { Current = Harbour }, visible: false);

        Assert.Equal(0, processor.Starts);
        Assert.False(processor.IsActive);

        activator.SetVisible(true);
        Assert.Equal(1, processor.Starts);
        Assert.True(processor.IsActive);

        activator.SetVisible(false);
        Assert.False(processor.IsActive);
        Assert.False(activator.IsRunning);
    }

    [Fact]
    public async Task A_source_with_a_span_limit_runs_only_when_zoomed_in()
    {
        var notifier = new FakeNotifier { Current = Ocean };
        var processor = new FakeHostedProcessor();
        using var activator = new HostedSourceActivator(
            processor, SourceActivation.BelowViewportSpan(5), notifier, visible: true, debounce: TimeSpan.Zero);

        Assert.Equal(0, processor.Starts);
        Assert.False(processor.IsActive);

        notifier.Publish(Harbour);
        await WaitUntilAsync(() => processor.IsActive);
        Assert.Equal(1, processor.Starts);
        Assert.Equal(-71.0, processor.Area!.Value.MinLongitude);

        notifier.Publish(Ocean);
        await WaitUntilAsync(() => !processor.IsActive);
        Assert.Equal(-71.0, processor.Area!.Value.MinLongitude); // Not told the area it won't fetch.
    }

    [Fact]
    public void The_service_runs_a_loaded_entry_following_its_visibility()
    {
        var registry = new SourceRegistry();
        registry.Register(Registration());
        using var service = new SourceDatasetService(registry, Settings(), new FakeNotifier { Current = Harbour });
        var entry = DatasetEntry.ForSource(registry.Resolve("test.feed"));
        var processor = (FakeHostedProcessor)service.CreateProcessor("test.feed", options: null);

        service.Attach(entry, processor);
        Assert.True(service.IsRunning(entry.Id));

        entry.IsVisible = false;
        Assert.False(service.IsRunning(entry.Id));
        Assert.False(processor.IsActive);

        service.Detach(entry.Id);
        entry.IsVisible = true;
        Assert.False(processor.IsActive);
    }

    [Fact]
    public void Added_sources_are_saved_and_restored_in_order()
    {
        var settings = Settings();
        using var service = new SourceDatasetService(SourceRegistry.Default, settings);

        Assert.True(service.Remember(S100Sources.NdbcObservationsId));
        Assert.False(service.Remember("NDBC.Realtime"));
        settings.SourceDatasets.Add("acme.gone");

        Assert.Equal([S100Sources.NdbcObservations], service.SavedRegistrations());

        Assert.True(service.Forget(S100Sources.NdbcObservationsId));
        Assert.Empty(service.SavedRegistrations());
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

    private sealed class FakeNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; set; }

        public event EventHandler<MapViewportSnapshot>? ViewportChanged;

        public void Publish(MapViewportSnapshot snapshot)
        {
            Current = snapshot;
            ViewportChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakeHostedProcessor : IHostedDatasetProcessor
    {
        private volatile bool _isActive;

        public int Starts { get; private set; }

        public GeographicBounds? Area { get; private set; }

        public bool IsActive
        {
            get => _isActive;
            set => _isActive = value;
        }

        public SpecRef Spec => new(SpecName.Ndbc, default);

        public void Start() => Starts++;

        public void UpdateArea(GeographicBounds? area) => Area = area;

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
