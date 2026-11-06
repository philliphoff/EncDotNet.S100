using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class PickServiceTests
{
    private static PickService CreatePickService(IDatasetLoaderService loader, MainViewModel viewModel)
        => CreatePickService(loader, viewModel, out _);

    private static PickService CreatePickService(
        IDatasetLoaderService loader,
        MainViewModel viewModel,
        out NotificationService notifications)
    {
        // The pick service surfaces its picks through the view-model's shared
        // PickReport; failed reference navigations surface as notifications.
        notifications = Notifications.TestNotifications.Create();
        return new PickService(loader, viewModel.PickReport, notifications);
    }


    private sealed class StubThemeService : IThemeService
    {
        public bool IsDarkTheme { get; private set; }
        public event System.EventHandler? ThemeChanged;
        public bool ToggleTheme() { IsDarkTheme = !IsDarkTheme; ThemeChanged?.Invoke(this, System.EventArgs.Empty); return IsDarkTheme; }
        public ChromeTheme Current => IsDarkTheme ? ChromeTheme.Dark : ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) { IsDarkTheme = ChromeThemes.IsDark(theme); ThemeChanged?.Invoke(this, System.EventArgs.Empty); }
    }

    private sealed class StubLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(System.DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }

    private static MainViewModel CreateMainViewModel()
    {
        var settings = new ViewerSettings();
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new StubLoader());
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            layerStack: new LayerStackViewModel(new StubLoader()),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: new PickReportViewModel(),
            timeline: new TimelineViewModel(new GlobalTimeService()),
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            textToolbar: new TextGroupToolbarViewModel(new EcdisDisplayState(), catalogues, datasets),
            displayModeToolbar: new DisplayModeToolbarViewModel(new EcdisDisplayState(), new FakeDatasetLoaderService()),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, datasets),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            measureAppearance: new StubMeasureOverlayAppearanceProvider(),
            notifications: Notifications.TestNotifications.Create());
    }

    [Fact]
    public async Task HandlePick_NullMapInfo_ClearsPickReport()
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
        await service.HandlePickAsync(null, 1.0);

        Assert.False(viewModel.PickReport.HasPick);
    }

    [Fact]
    public async Task HandlePick_NullMapInfo_NoPriorPick_StaysCleared()
    {
        var viewModel = CreateMainViewModel();
        var service = CreatePickService(new StubLoader(), viewModel);

        await service.HandlePickAsync(null, 1.0);

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
        public FeatureInfo? GetFeatureInfo(string featureRef)
            => _features.TryGetValue(featureRef, out var info) ? info : null;
    }

    private sealed class LoaderWithEntries : IDatasetLoaderService, IPickSource
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
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, System.Threading.CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
        public IReadOnlyList<S100Pick> Picks { get; set; } = [];
        public Task<IReadOnlyList<S100Pick>> PickAsync(GeographicPickQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Picks);
    }

    /// <summary>A loader whose engine pick returns <see cref="Picks"/>.</summary>
    private interface IPickSource
    {
        IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }

        IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }

        IReadOnlyList<S100Pick> Picks { get; set; }
    }

    /// <summary>A drawn feature under the click: the layer that draws it and its feature reference.</summary>
    private static (ILayer Layer, string FeatureRef) MakeRecord(ILayer layer, string featureRef) => (layer, featureRef);

    /// <summary>
    /// Clicks the map with the engine pick returning <paramref name="records"/>,
    /// topmost first, each as a pick of the dataset whose layer draws it.
    /// </summary>
    private static Task PickAsync(
        PickService service,
        IPickSource loader,
        IEnumerable<(ILayer Layer, string FeatureRef)> records)
    {
        var picks = new List<S100Pick>();
        foreach (var (layer, featureRef) in records)
        {
            var entry = loader.EntryLayers.FirstOrDefault(e => e.Value.Contains(layer)).Key;
            var id = entry?.Id ?? new MapDatasetId("unknown:" + layer.Name);
            var info = entry is not null && loader.Processors.TryGetValue(entry, out var processor)
                ? processor.GetFeatureInfo(featureRef)
                : null;
            picks.Add(new S100Pick
            {
                DatasetId = id,
                Info = info ?? new FeatureInfo { FeatureRef = featureRef, FeatureType = "Unknown", Attributes = [] },
                IsCoverage = false,
                Inside = true,
                DistanceMeters = 0,
            });
        }

        loader.Picks = picks;
        return service.HandlePickAsync(new MPoint(0, 0), 1.0);
    }

    [Fact]
    public async Task HandlePick_MultipleRecords_PopulatesAllHitsInOrder()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-101");
        var processor = new StubProcessor(
            "S-101",
            new FeatureInfo
            {
                FeatureRef = "1",
                FeatureType = "DepthArea",
                FeatureTypeName = "Depth Area",
                Attributes = new[] { new PickAttribute { Code = "DRVAL1", RawValue = "10", Children = [] } },
            },
            new FeatureInfo
            {
                FeatureRef = "2",
                FeatureType = "LandArea",
                FeatureTypeName = "Land Area",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel);
        await PickAsync(service, loader, new[]
        {
            MakeRecord(layer, "1"),
            MakeRecord(layer, "2"),
        });

        Assert.True(viewModel.PickReport.HasPick);
        Assert.True(viewModel.PickReport.HasMultipleHits);
        Assert.Equal(2, viewModel.PickReport.Hits.Count);
        Assert.Equal("1", viewModel.PickReport.Hits[0].FeatureRef);
        Assert.Equal("2", viewModel.PickReport.Hits[1].FeatureRef);
        Assert.Equal("DepthArea", viewModel.PickReport.FeatureType);
    }

    [Fact]
    public async Task HandlePick_DuplicateFeatureAcrossSubLayers_DedupesByRef()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-101");
        var processor = new StubProcessor(
            "S-101",
            new FeatureInfo
            {
                FeatureRef = "1",
                FeatureType = "DepthArea",
                FeatureTypeName = "Depth Area",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var lineLayer = new MemoryLayer("lines");
        var labelLayer = new MemoryLayer("labels");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)lineLayer, labelLayer } });

        var service = CreatePickService(loader, viewModel);
        await PickAsync(service, loader, new[]
        {
            MakeRecord(lineLayer, "1"),
            MakeRecord(labelLayer, "1"),
        });

        Assert.Single(viewModel.PickReport.Hits);
        Assert.False(viewModel.PickReport.HasMultipleHits);
    }

    [Fact]
    public async Task HandlePick_RecordsWithUnknownLayer_AreSkipped()
    {
        var viewModel = CreateMainViewModel();
        var orphanLayer = new MemoryLayer("orphan");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor>(),
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>());

        var service = CreatePickService(loader, viewModel);
        await PickAsync(service, loader, new[] { MakeRecord(orphanLayer, "1") });

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
    public async Task NavigateToReference_TargetExists_ReplacesHitWithTarget()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-125");
        var processor = new StubProcessor(
            "S-125",
            new FeatureInfo
            {
                FeatureRef = "L1",
                FeatureType = "LightLateral",
                FeatureTypeName = "Lateral Light",
                Attributes = Array.Empty<PickAttribute>(),
                References = new[] { new FeatureReference { Role = "AtonStatus", TargetRef = "S1" } },
            },
            new FeatureInfo
            {
                FeatureRef = "S1",
                FeatureType = "AtonStatus",
                FeatureTypeName = "AtoN Status",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel);
        await PickAsync(service, loader, new[] { MakeRecord(layer, "L1") });
        Assert.Equal("L1", viewModel.PickReport.FeatureRef);
        Assert.True(viewModel.PickReport.HasReferences);

        var ok = service.NavigateToReference(viewModel.PickReport.References[0]);

        Assert.True(ok);
        Assert.Single(viewModel.PickReport.Hits);
        Assert.Equal("S1", viewModel.PickReport.FeatureRef);
        Assert.Equal("AtonStatus", viewModel.PickReport.FeatureType);
    }

    [Fact]
    public async Task NavigateToReference_TargetMissing_ReturnsFalseAndKeepsCurrentHit()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-125");
        var processor = new StubProcessor(
            "S-125",
            new FeatureInfo
            {
                FeatureRef = "L1",
                FeatureType = "LightLateral",
                FeatureTypeName = "Lateral Light",
                Attributes = Array.Empty<PickAttribute>(),
                References = new[] { new FeatureReference { Role = "Missing", TargetRef = "ghost" } },
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel);
        await PickAsync(service, loader, new[] { MakeRecord(layer, "L1") });

        var ok = service.NavigateToReference(viewModel.PickReport.References[0]);

        Assert.False(ok);
        Assert.Equal("L1", viewModel.PickReport.FeatureRef);
    }

    [Fact]
    public async Task NavigateCommand_OnViewModel_DrivesPickServiceNavigation()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-421");
        var processor = new StubProcessor(
            "S-421",
            new FeatureInfo
            {
                FeatureRef = "RTE",
                FeatureType = "Route",
                FeatureTypeName = "Route",
                Attributes = Array.Empty<PickAttribute>(),
                References = new[] { new FeatureReference { Role = "routeWaypoints", TargetRef = "RTE.WPTS" } },
            },
            new FeatureInfo
            {
                FeatureRef = "RTE.WPTS",
                FeatureType = "RouteWaypoints",
                FeatureTypeName = "Route Waypoints",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel);
        await PickAsync(service, loader, new[] { MakeRecord(layer, "RTE") });
        var reference = viewModel.PickReport.References[0];

        viewModel.PickReport.NavigateCommand.Execute(reference);

        Assert.Equal("RTE.WPTS", viewModel.PickReport.FeatureRef);
        Assert.Equal("Route Waypoints", viewModel.PickReport.FeatureTypeName);
    }

    [Fact]
    public async Task NavigateCommand_MissingTarget_ShowsNotification()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-125");
        var processor = new StubProcessor(
            "S-125",
            new FeatureInfo
            {
                FeatureRef = "L1",
                FeatureType = "LightLateral",
                FeatureTypeName = "Lateral Light",
                Attributes = Array.Empty<PickAttribute>(),
                References = new[] { new FeatureReference { Role = "X", TargetRef = "ghost" } },
            });
        var layer = new MemoryLayer("layer-a");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>> { [entry] = new[] { (ILayer)layer } });

        var service = CreatePickService(loader, viewModel, out var notifications);
        await PickAsync(service, loader, new[] { MakeRecord(layer, "L1") });

        viewModel.PickReport.NavigateCommand.Execute(viewModel.PickReport.References[0]);

        Assert.Contains(
            notifications.Active,
            n => (n.Message ?? string.Empty).Contains("ghost", StringComparison.Ordinal));
    }

    private sealed class StackAwareLoader : IDatasetLoaderService, IPickSource
    {
        public StackAwareLoader(
            IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> processors,
            IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> entryLayers,
            IReadOnlyList<ILayer> stackedLayers)
        {
            Processors = processors;
            EntryLayers = entryLayers;
            CurrentStackedLayers = stackedLayers;
        }

        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
        public IReadOnlyList<ILayer> CurrentStackedLayers { get; }
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
        public IReadOnlyList<S100Pick> Picks { get; set; } = [];
        public Task<IReadOnlyList<S100Pick>> PickAsync(GeographicPickQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Picks);
    }

    [Fact]
    public async Task HandlePick_KeepsTheEnginePicksTopFirstOrder()
    {
        // PR-L1 (S-98): the topmost-painted dataset's hit comes first,
        // matching the visible Z-order. The session's engine pick ranks
        // picks by the S-98 stack (map rendering slice 8); PickService keeps
        // that order.
        var viewModel = CreateMainViewModel();
        var entryA = new DatasetEntry("/tmp/a.gml", "S-101");
        var entryB = new DatasetEntry("/tmp/b.gml", "S-102");
        var entryC = new DatasetEntry("/tmp/c.gml", "S-124");

        var procA = new StubProcessor("S-101", new FeatureInfo
        {
            FeatureRef = "fa",
            FeatureType = "DepthArea",
            FeatureTypeName = "Depth Area",
            Attributes = Array.Empty<PickAttribute>(),
        });
        var procB = new StubProcessor("S-102", new FeatureInfo
        {
            FeatureRef = "fb",
            FeatureType = "BathyCell",
            FeatureTypeName = "Bathy",
            Attributes = Array.Empty<PickAttribute>(),
        });
        var procC = new StubProcessor("S-124", new FeatureInfo
        {
            FeatureRef = "fc",
            FeatureType = "Warning",
            FeatureTypeName = "Warning",
            Attributes = Array.Empty<PickAttribute>(),
        });

        var layerA = new MemoryLayer("a");
        var layerB = new MemoryLayer("b");
        var layerC = new MemoryLayer("c");

        // Stack is bottom-first; C ends up on top.
        var stack = new ILayer[] { layerA, layerB, layerC };

        var loader = new StackAwareLoader(
            new Dictionary<DatasetEntry, IDatasetProcessor>
            {
                [entryA] = procA,
                [entryB] = procB,
                [entryC] = procC,
            },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>
            {
                [entryA] = new[] { (ILayer)layerA },
                [entryB] = new[] { (ILayer)layerB },
                [entryC] = new[] { (ILayer)layerC },
            },
            stack);

        var service = CreatePickService(loader, viewModel);

        // The engine returns picks top-first: C, B, A.
        await PickAsync(service, loader, new[]
        {
            MakeRecord(layerC, "fc"),
            MakeRecord(layerB, "fb"),
            MakeRecord(layerA, "fa"),
        });

        Assert.Equal(3, viewModel.PickReport.Hits.Count);
        Assert.Equal("fc", viewModel.PickReport.Hits[0].FeatureRef);
        Assert.Equal("fb", viewModel.PickReport.Hits[1].FeatureRef);
        Assert.Equal("fa", viewModel.PickReport.Hits[2].FeatureRef);
    }

    [Fact]
    public async Task HandlePick_AfterR_101_102_B_Suppression_ReturnsS102Hit()
    {
        // PR-L2 (S-98): when R-101-102-B suppresses S-101 DepthArea
        // because an S-102 dataset is loaded, picking inside that area
        // must return the S-102 feature (the now-topmost-visible hit
        // at the pick location) — never the suppressed S-101 feature.
        // The session's engine pick leaves suppressed features out
        // (MapsuiDatasetLayerSession.PickAsync), so it returns only the
        // S-102 pick here.
        var viewModel = CreateMainViewModel();
        var s101 = new DatasetEntry("/tmp/s101.000", "S-101");
        var s102 = new DatasetEntry("/tmp/s102.h5", "S-102");

        var procS101 = new StubProcessor("S-101", new FeatureInfo
        {
            FeatureRef = "land-1",
            FeatureType = "LandArea",
            FeatureTypeName = "Land Area",
            Attributes = Array.Empty<PickAttribute>(),
        });
        var procS102 = new StubProcessor("S-102", new FeatureInfo
        {
            FeatureRef = "bathy-1",
            FeatureType = "BathymetryCoverage",
            FeatureTypeName = "Bathymetry",
            Attributes = Array.Empty<PickAttribute>(),
        });

        var s101AreasFiltered = new MemoryLayer("s101.areas");
        var s102Layer = new MemoryLayer("s102");

        // Post-suppression stack: filtered S-101 areas (DepthArea gone)
        // sits on plane BaseChartUnder; S-102 sits on plane Bathymetry
        // above it.
        var stack = new ILayer[] { s101AreasFiltered, s102Layer };

        var loader = new StackAwareLoader(
            new Dictionary<DatasetEntry, IDatasetProcessor>
            {
                [s101] = procS101,
                [s102] = procS102,
            },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>
            {
                [s101] = new[] { (ILayer)s101AreasFiltered },
                [s102] = new[] { (ILayer)s102Layer },
            },
            stack);

        var service = CreatePickService(loader, viewModel);

        // The engine pick returns only the S-102 pick: the DepthArea
        // feature is suppressed.
        await PickAsync(service, loader, new[]
        {
            MakeRecord(s102Layer, "bathy-1"),
        });

        Assert.Single(viewModel.PickReport.Hits);
        Assert.Equal("bathy-1", viewModel.PickReport.Hits[0].FeatureRef);
        Assert.Equal("BathymetryCoverage", viewModel.PickReport.FeatureType);
    }

    [Fact]
    public void PresentGeographicPick_ResolvesFeaturesByDisplayName_AndSetsLocation()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-101");
        var processor = new StubProcessor(
            "S-101",
            new FeatureInfo
            {
                FeatureRef = "1",
                FeatureType = "DepthArea",
                FeatureTypeName = "Depth Area",
                Attributes = Array.Empty<PickAttribute>(),
            });
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>());

        var service = CreatePickService(loader, viewModel);

        // DisplayName is the file name (Path.GetFileName) of the entry.
        service.PresentGeographicPick(
            47.6, -122.3,
            new[] { new GeographicPickFeature(entry.DisplayName, "1") });

        Assert.True(viewModel.PickReport.HasPick);
        Assert.Single(viewModel.PickReport.Hits);
        Assert.Equal("1", viewModel.PickReport.FeatureRef);
        Assert.True(viewModel.PickReport.HasLocation);
        Assert.Equal(47.6, viewModel.PickReport.Location!.Value.Latitude, 6);
        Assert.Equal(-122.3, viewModel.PickReport.Location!.Value.Longitude, 6);
    }

    [Fact]
    public void PresentGeographicPick_NoResolvableFeatures_ClearsPick()
    {
        var viewModel = CreateMainViewModel();
        var entry = new DatasetEntry("/tmp/test.gml", "S-101");
        var processor = new StubProcessor("S-101");
        var loader = new LoaderWithEntries(
            new Dictionary<DatasetEntry, IDatasetProcessor> { [entry] = processor },
            new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>());

        var service = CreatePickService(loader, viewModel);
        viewModel.PickReport.SetPick(
            featureType: "DepthArea", featureTypeName: null, featureRef: "old",
            datasetFileName: "test.gml", productSpec: "S-101",
            attributes: Array.Empty<PickAttribute>());
        Assert.True(viewModel.PickReport.HasPick);

        // Unknown feature ref → nothing resolves → pick cleared.
        service.PresentGeographicPick(
            47.6, -122.3,
            new[] { new GeographicPickFeature(entry.DisplayName, "missing") });

        Assert.False(viewModel.PickReport.HasPick);
    }
}
