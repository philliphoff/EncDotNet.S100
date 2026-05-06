using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Styles;

namespace EncDotNet.S100.Viewer.Tests;

public class PickServiceTests
{
    private sealed class EmptyCatalogSource : IDatasetCatalogSource
    {
        public string Id => "test";
        public string DisplayName => "Test";
        public IReadOnlyList<DatasetCatalogEntry> Entries => Array.Empty<DatasetCatalogEntry>();
        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }

    private sealed class StubThemeService : IThemeService
    {
        public bool IsDarkTheme { get; private set; }
        public bool ToggleTheme() { IsDarkTheme = !IsDarkTheme; return IsDarkTheme; }
    }

    private sealed class StubLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(System.DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
    }

    private static MainViewModel CreateMainViewModel()
    {
        var settings = new ViewerSettings();
        var catalogues = new PortrayalCatalogueManager();
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: new DatasetsViewModel(new StubLoader()),
            catalogPanel: new CatalogPanelViewModel(new EmptyCatalogSource()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: new PickReportViewModel(),
            timeline: new TimelineViewModel(new GlobalTimeService()),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            measureAppearance: new StubMeasureOverlayAppearanceProvider());
    }

    [Fact]
    public void HandlePick_NullMapInfo_ClearsPickReport()
    {
        var viewModel = CreateMainViewModel();
        // Seed with an active pick so we can verify it gets cleared.
        viewModel.PickReport.SetPick(
            featureType: "DepthArea",
            featureTypeName: null,
            featureRef: "FRID#1",
            datasetFileName: "test.000",
            productSpec: "S-101",
            attributes: System.Array.Empty<EncDotNet.S100.Datasets.Pipelines.PickAttribute>());
        Assert.True(viewModel.PickReport.HasPick);

        var service = new PickService(new StubLoader(), viewModel);
        service.HandlePick(null);

        Assert.False(viewModel.PickReport.HasPick);
    }

    [Fact]
    public void HandlePick_NullMapInfo_NoPriorPick_StaysCleared()
    {
        var viewModel = CreateMainViewModel();
        var service = new PickService(new StubLoader(), viewModel);

        service.HandlePick(null);

        Assert.False(viewModel.PickReport.HasPick);
    }

    private sealed class StubProcessor : IDatasetProcessor
    {
        private readonly Dictionary<string, FeatureInfo> _features;

        public StubProcessor(string spec, params FeatureInfo[] features)
        {
            ProductSpec = spec;
            _features = new Dictionary<string, FeatureInfo>();
            foreach (var f in features)
                _features[f.FeatureRef] = f;
        }

        public string ProductSpec { get; }
        public DatasetResult Render(RenderContext? context = null)
            => throw new NotSupportedException();
        public FeatureInfo? GetFeatureInfo(string featureRef)
            => _features.TryGetValue(featureRef, out var info) ? info : null;
    }

    private sealed class LoaderWithEntries : IDatasetLoaderService
    {
        public LoaderWithEntries(
            IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> processors,
            IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> entryLayers)
        {
            Processors = processors;
            EntryLayers = entryLayers;
        }

        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
    }

    private static MapInfo BuildMapInfo(IEnumerable<MapInfoRecord> records)
        => new(new ScreenPosition(0, 0), new MPoint(0, 0), 1.0, records);

    private static MapInfoRecord MakeRecord(ILayer layer, string featureRef)
    {
        var feature = new PointFeature(0, 0);
        feature[MapsuiDisplayListRenderer.FeatureRefKey] = featureRef;
        return new MapInfoRecord(feature, new VectorStyle(), layer);
    }

    [Fact]
    public void HandlePick_MultipleRecords_PopulatesAllHitsInOrder()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-101");
        var processor = new StubProcessor(
            "S-101",
            new FeatureInfo
            {
                FeatureRef = "1", FeatureType = "DepthArea", FeatureTypeName = "Depth Area",
                Attributes = new[] { new PickAttribute { Code = "DRVAL1", RawValue = "10", Children = [] } },
            },
            new FeatureInfo
            {
                FeatureRef = "2", FeatureType = "LandArea", FeatureTypeName = "Land Area",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = new PickService(loader, viewModel);
        service.HandlePick(BuildMapInfo(new[]
        {
            MakeRecord(layer, "1"),
            MakeRecord(layer, "2"),
        }));

        Assert.True(viewModel.PickReport.HasPick);
        Assert.True(viewModel.PickReport.HasMultipleHits);
        Assert.Equal(2, viewModel.PickReport.Hits.Count);
        Assert.Equal("1", viewModel.PickReport.Hits[0].FeatureRef);
        Assert.Equal("2", viewModel.PickReport.Hits[1].FeatureRef);
        Assert.Equal("DepthArea", viewModel.PickReport.FeatureType);
    }

    [Fact]
    public void HandlePick_DuplicateFeatureAcrossSubLayers_DedupesByRef()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-101");
        var processor = new StubProcessor(
            "S-101",
            new FeatureInfo
            {
                FeatureRef = "1", FeatureType = "DepthArea", FeatureTypeName = "Depth Area",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var lineLayer = new MemoryLayer("lines");
        var labelLayer = new MemoryLayer("labels");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)lineLayer, labelLayer } });

        var service = new PickService(loader, viewModel);
        service.HandlePick(BuildMapInfo(new[]
        {
            MakeRecord(lineLayer, "1"),
            MakeRecord(labelLayer, "1"),
        }));

        Assert.Single(viewModel.PickReport.Hits);
        Assert.False(viewModel.PickReport.HasMultipleHits);
    }

    [Fact]
    public void HandlePick_RecordsWithUnknownLayer_AreSkipped()
    {
        var viewModel = CreateMainViewModel();
        var orphanLayer = new MemoryLayer("orphan");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor>(),
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>());

        var service = new PickService(loader, viewModel);
        service.HandlePick(BuildMapInfo(new[] { MakeRecord(orphanLayer, "1") }));

        Assert.False(viewModel.PickReport.HasPick);
    }
}
