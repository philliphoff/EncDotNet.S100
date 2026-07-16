using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.DynamicSources;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Integration tests for <see cref="DatasetExtentIndicatorController"/>, which
/// keeps the out-of-scale extent-indicator overlay in sync with the dataset
/// entries (issue #446). Uses a synchronous marshal and a fake map host so no
/// Avalonia dispatcher is required.
/// </summary>
public class DatasetExtentIndicatorControllerTests
{
    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }

    private static SettingsViewModel NewSettings(bool indicatorsOn = true)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        var settings = new ViewerSettings
        {
            SettingsFilePath = path,
            ShowOutOfScaleExtentIndicators = indicatorsOn,
        };
        return new SettingsViewModel(settings);
    }

    private static DatasetEntry LoadedOutOfScaleEntry(DatasetsViewModel vm)
    {
        var entry = vm.Add("/a.000", "S-101");
        entry.IsLoaded = true;
        entry.IsVisible = true;
        entry.MercatorExtent = new MRect(0, 0, 100, 100);
        entry.ContentMaxVisibleResolution = 50.0;
        return entry;
    }

    private static int FeatureCount(FakeMapHost host)
    {
        var layer = host.OverlayLayers.OfType<MemoryLayer>()
            .Single(l => l.Name == DatasetExtentIndicatorOverlayLayer.LayerName);
        return layer.Features.Count();
    }

    [Fact]
    public void Construction_AddsOverlayLayer()
    {
        var host = new FakeMapHost();
        var vm = new DatasetsViewModel(new NoopLoader());

        using var controller = new DatasetExtentIndicatorController(
            host, vm, new StubMeasureOverlayAppearanceProvider(), NewSettings(),
            marshal: a => a());

        Assert.Contains(host.OverlayLayers, l => l.Name == DatasetExtentIndicatorOverlayLayer.LayerName);
    }

    [Fact]
    public void QualifyingEntry_ProducesOneIndicator()
    {
        var host = new FakeMapHost();
        var vm = new DatasetsViewModel(new NoopLoader());
        using var controller = new DatasetExtentIndicatorController(
            host, vm, new StubMeasureOverlayAppearanceProvider(), NewSettings(),
            marshal: a => a());

        LoadedOutOfScaleEntry(vm);

        Assert.Equal(1, FeatureCount(host));
    }

    [Fact]
    public void EntryWithoutContentCutoff_ProducesNoIndicator()
    {
        var host = new FakeMapHost();
        var vm = new DatasetsViewModel(new NoopLoader());
        using var controller = new DatasetExtentIndicatorController(
            host, vm, new StubMeasureOverlayAppearanceProvider(), NewSettings(),
            marshal: a => a());

        var entry = vm.Add("/a.000", "S-101");
        entry.IsLoaded = true;
        entry.MercatorExtent = new MRect(0, 0, 100, 100);
        // ContentMaxVisibleResolution left null → never disappears → no border.

        Assert.Equal(0, FeatureCount(host));
    }

    [Fact]
    public void HiddenEntry_ProducesNoIndicator()
    {
        var host = new FakeMapHost();
        var vm = new DatasetsViewModel(new NoopLoader());
        using var controller = new DatasetExtentIndicatorController(
            host, vm, new StubMeasureOverlayAppearanceProvider(), NewSettings(),
            marshal: a => a());

        var entry = LoadedOutOfScaleEntry(vm);
        entry.IsVisible = false;

        Assert.Equal(0, FeatureCount(host));
    }

    [Fact]
    public void TogglingSettingOff_ClearsIndicators()
    {
        var host = new FakeMapHost();
        var vm = new DatasetsViewModel(new NoopLoader());
        var settings = NewSettings(indicatorsOn: true);
        using var controller = new DatasetExtentIndicatorController(
            host, vm, new StubMeasureOverlayAppearanceProvider(), settings,
            marshal: a => a());

        LoadedOutOfScaleEntry(vm);
        Assert.Equal(1, FeatureCount(host));

        settings.ShowOutOfScaleExtentIndicators = false;
        Assert.Equal(0, FeatureCount(host));
    }

    [Fact]
    public void Dispose_RemovesOverlayLayer()
    {
        var host = new FakeMapHost();
        var vm = new DatasetsViewModel(new NoopLoader());
        var controller = new DatasetExtentIndicatorController(
            host, vm, new StubMeasureOverlayAppearanceProvider(), NewSettings(),
            marshal: a => a());

        controller.Dispose();

        Assert.DoesNotContain(host.OverlayLayers, l => l.Name == DatasetExtentIndicatorOverlayLayer.LayerName);
    }

    private sealed class StubAssetSource : IAssetSource
    {
        public Task<System.IO.Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream());
        public void Dispose() { }
    }

    private static ExchangeSets.BoundingBox Box(double w, double e, double s, double n) => new()
    {
        WestBoundLongitude = w,
        EastBoundLongitude = e,
        SouthBoundLatitude = s,
        NorthBoundLatitude = n,
    };

    private static DatasetEntry DeferredEntry(DatasetsViewModel vm)
        => vm.AddRangeFromExchangeSet(new List<ExchangeSetCellRegistration>
        {
            new(new StubAssetSource(), "a/US1.000", "S-57", GeographicBounds: Box(-123, -122, 37, 38)),
        })[0];

    [Fact]
    public void DeferredVisibleEntry_ProducesOutline()
    {
        var host = new FakeMapHost();
        var vm = new DatasetsViewModel(new NoopLoader());
        using var controller = new DatasetExtentIndicatorController(
            host, vm, new StubMeasureOverlayAppearanceProvider(), NewSettings(),
            marshal: a => a());

        var entry = DeferredEntry(vm);

        Assert.True(entry.IsDeferred);
        Assert.True(entry.IsVisible);
        Assert.Equal(1, FeatureCount(host));
    }

    [Fact]
    public void HiddenDeferredEntry_ProducesNoOutline()
    {
        var host = new FakeMapHost();
        var vm = new DatasetsViewModel(new NoopLoader());
        using var controller = new DatasetExtentIndicatorController(
            host, vm, new StubMeasureOverlayAppearanceProvider(), NewSettings(),
            marshal: a => a());

        var entry = DeferredEntry(vm);
        Assert.Equal(1, FeatureCount(host));

        // Hiding a deferred cell must remove its outline too, consistent with
        // hiding a loaded dataset (issue #458).
        entry.IsVisible = false;
        Assert.Equal(0, FeatureCount(host));
    }
}
