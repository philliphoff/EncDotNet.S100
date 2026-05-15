using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
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
    private static PickService CreatePickService(IDatasetLoaderService loader, MainViewModel viewModel)
    {
        // Pull MVM's status presenter (set via ctor) so that writes by
        // the pick service flow through to the MVM's StatusText forward.
        var presenter = (IStatusPresenter)typeof(MainViewModel)
            .GetField("_statusPresenter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(viewModel)!;
        return new PickService(loader, viewModel.PickReport, presenter);
    }

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
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(System.DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
    }

    private static MainViewModel CreateMainViewModel()
        => CreateMainViewModel(out _);

    private static MainViewModel CreateMainViewModel(out IStatusPresenter statusPresenter)
    {
        var settings = new ViewerSettings();
        var catalogues = new PortrayalCatalogueManager();
        statusPresenter = new StatusPresenter();
        var datasets = new DatasetsViewModel(new StubLoader());
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            catalogPanel: new CatalogPanelViewModel(new EmptyCatalogSource()),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: new PickReportViewModel(),
            timeline: new TimelineViewModel(new GlobalTimeService()),
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            textToolbar: new TextGroupToolbarViewModel(new EcdisDisplayState(), catalogues, datasets),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, datasets),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            measureAppearance: new StubMeasureOverlayAppearanceProvider(),
            toasts: new StubToastService(),
            statusPresenter: statusPresenter);
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

        var service = CreatePickService(new StubLoader(), viewModel);
        service.HandlePick(null);

        Assert.False(viewModel.PickReport.HasPick);
    }

    [Fact]
    public void HandlePick_NullMapInfo_NoPriorPick_StaysCleared()
    {
        var viewModel = CreateMainViewModel();
        var service = CreatePickService(new StubLoader(), viewModel);

        service.HandlePick(null);

        Assert.False(viewModel.PickReport.HasPick);
    }

    private sealed class StubProcessor : IDatasetProcessor
    {
        private readonly Dictionary<string, FeatureInfo> _features;

        public StubProcessor(string spec, params FeatureInfo[] features)
        {
            ProductSpec = spec;
            Spec = new SpecRef(spec, default);
            _features = new Dictionary<string, FeatureInfo>();
            foreach (var f in features)
                _features[f.FeatureRef] = f;
        }

        public string ProductSpec { get; }
        public SpecRef Spec { get; }
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
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public event Action<string?>? StatusChanged { add { } remove { } }
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

        var service = CreatePickService(loader, viewModel);
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

        var service = CreatePickService(loader, viewModel);
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

        var service = CreatePickService(loader, viewModel);
        service.HandlePick(BuildMapInfo(new[] { MakeRecord(orphanLayer, "1") }));

        Assert.False(viewModel.PickReport.HasPick);
    }

    [Fact]
    public void NavigateToReference_NoSelectedHit_ReturnsFalse()
    {
        var viewModel = CreateMainViewModel();
        var service = CreatePickService(new StubLoader(), viewModel);

        var ok = service.NavigateToReference(new FeatureReference { Role = "r", TargetRef = "x" });

        Assert.False(ok);
    }

    [Fact]
    public void NavigateToReference_TargetExists_ReplacesHitWithTarget()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-125");
        var processor = new StubProcessor(
            "S-125",
            new FeatureInfo
            {
                FeatureRef = "L1", FeatureType = "LightLateral", FeatureTypeName = "Lateral Light",
                Attributes = Array.Empty<PickAttribute>(),
                References = new[] { new FeatureReference { Role = "AtonStatus", TargetRef = "S1" } },
            },
            new FeatureInfo
            {
                FeatureRef = "S1", FeatureType = "AtonStatus", FeatureTypeName = "AtoN Status",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel);
        service.HandlePick(BuildMapInfo(new[] { MakeRecord(layer, "L1") }));
        Assert.Equal("L1", viewModel.PickReport.FeatureRef);
        Assert.True(viewModel.PickReport.HasReferences);

        var ok = service.NavigateToReference(viewModel.PickReport.References[0]);

        Assert.True(ok);
        Assert.Single(viewModel.PickReport.Hits);
        Assert.Equal("S1", viewModel.PickReport.FeatureRef);
        Assert.Equal("AtonStatus", viewModel.PickReport.FeatureType);
    }

    [Fact]
    public void NavigateToReference_TargetMissing_ReturnsFalseAndKeepsCurrentHit()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-125");
        var processor = new StubProcessor(
            "S-125",
            new FeatureInfo
            {
                FeatureRef = "L1", FeatureType = "LightLateral", FeatureTypeName = "Lateral Light",
                Attributes = Array.Empty<PickAttribute>(),
                References = new[] { new FeatureReference { Role = "Missing", TargetRef = "ghost" } },
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel);
        service.HandlePick(BuildMapInfo(new[] { MakeRecord(layer, "L1") }));

        var ok = service.NavigateToReference(viewModel.PickReport.References[0]);

        Assert.False(ok);
        Assert.Equal("L1", viewModel.PickReport.FeatureRef);
    }

    [Fact]
    public void NavigateCommand_OnViewModel_DrivesPickServiceNavigation()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-421");
        var processor = new StubProcessor(
            "S-421",
            new FeatureInfo
            {
                FeatureRef = "RTE", FeatureType = "Route", FeatureTypeName = "Route",
                Attributes = Array.Empty<PickAttribute>(),
                References = new[] { new FeatureReference { Role = "routeWaypoints", TargetRef = "RTE.WPTS" } },
            },
            new FeatureInfo
            {
                FeatureRef = "RTE.WPTS", FeatureType = "RouteWaypoints", FeatureTypeName = "Route Waypoints",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel);
        service.HandlePick(BuildMapInfo(new[] { MakeRecord(layer, "RTE") }));
        var reference = viewModel.PickReport.References[0];

        viewModel.PickReport.NavigateCommand.Execute(reference);

        Assert.Equal("RTE.WPTS", viewModel.PickReport.FeatureRef);
        Assert.Equal("Route Waypoints", viewModel.PickReport.FeatureTypeName);
    }

    [Fact]
    public void NavigateCommand_MissingTarget_SetsStatusText()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-125");
        var processor = new StubProcessor(
            "S-125",
            new FeatureInfo
            {
                FeatureRef = "L1", FeatureType = "LightLateral", FeatureTypeName = "Lateral Light",
                Attributes = Array.Empty<PickAttribute>(),
                References = new[] { new FeatureReference { Role = "X", TargetRef = "ghost" } },
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel);
        service.HandlePick(BuildMapInfo(new[] { MakeRecord(layer, "L1") }));

        viewModel.PickReport.NavigateCommand.Execute(viewModel.PickReport.References[0]);

        Assert.Contains("ghost", viewModel.StatusText ?? string.Empty);
    }
}
