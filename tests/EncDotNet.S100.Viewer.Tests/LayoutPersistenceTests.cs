using Avalonia.Controls;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// PR-M3: per-dock visibility / tab / panel-size persistence in
/// <see cref="ViewerSettings"/>. Verifies restore-immediately
/// precedence, stale-id fallback, and the Settings-tab exemption.
/// </summary>
public sealed class LayoutPersistenceTests : IDisposable
{
    private readonly string _tempSettingsPath = Path.Combine(
        Path.GetTempPath(), $"viewer-layout-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_tempSettingsPath)) File.Delete(_tempSettingsPath); }
        catch { /* best effort */ }
    }

    private sealed class FakeTab : IActivityTab
    {
        public required string Id { get; init; }
        public int Order { get; init; }
        public string Title { get; init; } = "T";
        public string Tooltip { get; init; } = "Tip";
        public object ViewModel { get; init; } = new object();
        public Control CreateView() => new ContentControl();
        public bool PersistAsLastSelected { get; init; } = true;
        public TabDock Dock { get; init; } = TabDock.Left;
        public bool AutoOpenOnContentSignal { get; init; }

        public Control CreateIcon() => new ContentControl();
        public bool IsVisible { get; init; } = true;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private sealed class SignalingVm : IActivityTabContentSignal
    {
        public event EventHandler? ContentBecameAvailable;
        public void Raise() => ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
    }


    private sealed class StubThemeService : IThemeService
    {
        public bool IsDarkTheme => false;
        public event System.EventHandler? ThemeChanged { add { } remove { } }
        public bool ToggleTheme() => false;
        public ChromeTheme Current => ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) { }
    }

    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public System.Threading.Tasks.Task LoadAsync(DatasetEntry entry, System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ReRenderAtTimeAsync(DateTime t, System.Threading.CancellationToken ct) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ReRenderAllAsync() => System.Threading.Tasks.Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }

    private MainViewModel CreateViewModel(IEnumerable<IActivityTab>? tabs, ViewerSettings? settings = null, RoutesService? routes = null)
    {
        settings ??= new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new NoopLoader());
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            layerStack: new LayerStackViewModel(new NoopLoader()),
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
            notifications: Notifications.TestNotifications.Create(),
            activityTabs: tabs,
            routes: routes);
    }

    private static IActivityTab[] StandardTabs() => new IActivityTab[]
    {
        new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left },
        new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right },
        new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom },
    };

    [Fact]
    public void Check_passage_folds_object_information_to_its_edge_tab_and_restores_it()
    {
        var routes = new RoutesService();
        var vm = CreateViewModel(StandardTabs(), routes: routes);
        vm.IsRightDockOpen = true;
        Assert.True(vm.IsRightDockShown);
        Assert.False(vm.IsRightDockEdgeTabVisible);

        routes.IsCheckModeActive = true;

        Assert.True(vm.IsCheckModeActive);
        Assert.True(vm.IsRightDockOpen);
        Assert.False(vm.IsRightDockShown);
        Assert.True(vm.IsRightDockEdgeTabVisible);

        // The edge tab unfolds the dock while the mode stays on.
        vm.UnfoldRightDockCommand.Execute(null);
        Assert.True(vm.IsRightDockShown);
        routes.IsCheckModeActive = false;
        routes.IsCheckModeActive = true;
        Assert.False(vm.IsRightDockShown);

        routes.IsCheckModeActive = false;

        Assert.False(vm.IsCheckModeActive);
        Assert.True(vm.IsRightDockShown);
        Assert.False(vm.IsRightDockEdgeTabVisible);
    }

    private static IActivityTab[] TabsWithClearance(RoutesService routes) =>
    [
        new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left },
        new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right },
        new ActivityTab<object, ContentControl>(
            MainViewModel.TimelineTabId, 10, "Timeline", "Timeline", () => new ContentControl(), new object(), false, TabDock.Bottom),
        new ActivityTab<object, ContentControl>(
            MainViewModel.ClearanceTabId, 20, "Clearance", "Clearance", () => new ContentControl(), new object(), false, TabDock.Bottom,
            visibility: new CheckModeVisibilitySource(routes)),
    ];

    [Fact]
    public void Check_passage_opens_the_bottom_dock_on_clearance_and_restores_it()
    {
        var routes = new RoutesService();
        var vm = CreateViewModel(TabsWithClearance(routes), routes: routes);
        vm.WindowWidth = 1200;
        Assert.False(vm.IsBottomDockOpen);
        Assert.False(vm.HasBottomDockTabs);
        Assert.Equal([MainViewModel.TimelineTabId], vm.VisibleBottomTabs.Select(t => t.Id));

        routes.IsCheckModeActive = true;

        Assert.True(vm.IsBottomDockOpen);
        Assert.True(vm.IsClearanceTabSelected);
        Assert.True(vm.HasBottomDockTabs);
        Assert.Equal([MainViewModel.TimelineTabId, MainViewModel.ClearanceTabId], vm.VisibleBottomTabs.Select(t => t.Id));

        // The dock's tabs switch between the two.
        vm.SelectTabCommand.Execute(vm.VisibleBottomTabs[0]);
        Assert.True(vm.IsTimelineTabSelected);
        vm.SelectTabCommand.Execute(vm.VisibleBottomTabs[1]);
        Assert.True(vm.IsClearanceTabSelected);

        routes.IsCheckModeActive = false;

        Assert.False(vm.IsBottomDockOpen);
        Assert.True(vm.IsTimelineTabSelected);
        Assert.False(vm.HasBottomDockTabs);
    }

    [Fact]
    public void Check_passage_leaves_an_open_timeline_dock_open_on_the_timeline()
    {
        var routes = new RoutesService();
        var vm = CreateViewModel(TabsWithClearance(routes), routes: routes);
        vm.WindowWidth = 1200;
        vm.SelectTabCommand.Execute(vm.VisibleBottomTabs[0]);
        Assert.True(vm.IsBottomDockOpen);

        routes.IsCheckModeActive = true;
        Assert.True(vm.IsClearanceTabSelected);
        routes.IsCheckModeActive = false;

        Assert.True(vm.IsBottomDockOpen);
        Assert.True(vm.IsTimelineTabSelected);
    }

    [Fact]
    public void Check_passage_leaves_a_closed_object_information_dock_closed()
    {
        var routes = new RoutesService();
        var vm = CreateViewModel(StandardTabs(), routes: routes);
        Assert.False(vm.IsRightDockOpen);

        routes.IsCheckModeActive = true;

        Assert.False(vm.IsRightDockShown);
        Assert.False(vm.IsRightDockEdgeTabVisible);
        routes.IsCheckModeActive = false;
        Assert.False(vm.IsRightDockShown);
    }

    [Fact]
    public void IsLeftDockOpen_DefaultsToTrue_WhenSettingsAbsent()
    {
        var vm = CreateViewModel(StandardTabs());
        Assert.True(vm.IsLeftDockOpen);
    }

    [Fact]
    public void IsRightDockOpen_DefaultsToFalse_WhenSettingsAbsent()
    {
        var vm = CreateViewModel(StandardTabs());
        Assert.False(vm.IsRightDockOpen);
    }

    [Fact]
    public void IsBottomDockOpen_DefaultsToFalse_WhenSettingsAbsent()
    {
        var vm = CreateViewModel(StandardTabs());
        Assert.False(vm.IsBottomDockOpen);
    }

    [Fact]
    public void IsRightDockOpen_RestoredFromSettings()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            IsRightDockOpen = true,
        };
        var vm = CreateViewModel(StandardTabs(), settings);
        Assert.True(vm.IsRightDockOpen);
    }

    [Fact]
    public void IsBottomDockOpen_RestoredFromSettings()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            IsBottomDockOpen = true,
        };
        var vm = CreateViewModel(StandardTabs(), settings);
        Assert.True(vm.IsBottomDockOpen);
    }

    [Fact]
    public void IsLeftDockOpen_RestoredFromSettings_FalseValue()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            IsLeftDockOpen = false,
        };
        var vm = CreateViewModel(StandardTabs(), settings);
        Assert.False(vm.IsLeftDockOpen);
    }

    [Fact]
    public void TogglingIsRightDockOpen_WritesToSettings()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var vm = CreateViewModel(StandardTabs(), settings);

        vm.IsRightDockOpen = true;

        Assert.True(settings.IsRightDockOpen);
        Assert.True(File.Exists(_tempSettingsPath));
    }

    [Fact]
    public void LastSelectedRightTab_RestoredOnStartup_WhenValid()
    {
        var alt = new FakeTab { Id = "Alt", Order = 20, Dock = TabDock.Right };
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedRightTab = "Alt",
        };
        var vm = CreateViewModel(new IActivityTab[] { pick, alt }, settings);

        Assert.NotNull(vm.SelectedRightTab);
        Assert.Equal("Alt", vm.SelectedRightTab!.Id);
    }

    [Fact]
    public void LastSelectedRightTab_FallsBackToFirstTab_WhenStale()
    {
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedRightTab = "DoesNotExist",
        };
        var vm = CreateViewModel(new IActivityTab[] { pick }, settings);

        Assert.NotNull(vm.SelectedRightTab);
        Assert.Equal("Pick", vm.SelectedRightTab!.Id);
    }

    [Fact]
    public void LastSelectedBottomTab_RestoredOnStartup_WhenValid()
    {
        var alt = new FakeTab { Id = "AltB", Order = 20, Dock = TabDock.Bottom };
        var timeline = new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom };
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedBottomTab = "AltB",
        };
        var vm = CreateViewModel(new IActivityTab[] { timeline, alt }, settings);

        Assert.NotNull(vm.SelectedBottomTab);
        Assert.Equal("AltB", vm.SelectedBottomTab!.Id);
    }

    [Fact]
    public void UserSelectingRightTab_WritesIdToSettings()
    {
        var alt = new FakeTab { Id = "Alt", Order = 20, Dock = TabDock.Right };
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var vm = CreateViewModel(new IActivityTab[] { pick, alt }, settings);

        vm.SelectedRightTab = alt;

        Assert.Equal("Alt", settings.LastSelectedRightTab);
    }

    [Fact]
    public void RestoreImmediatelyPrecedence_OpensRightDockBeforeAutoOpenFires()
    {
        var signal = new SignalingVm();
        var pick = new FakeTab
        {
            Id = "Pick",
            Order = 10,
            Dock = TabDock.Right,
            AutoOpenOnContentSignal = true,
            ViewModel = signal,
        };
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            IsRightDockOpen = true,
        };
        var vm = CreateViewModel(new IActivityTab[] { pick }, settings);

        // Dock should already be open from settings, before any signal fires.
        Assert.True(vm.IsRightDockOpen);

        // Subsequent auto-open is a no-op (dock stays open).
        signal.Raise();
        Assert.True(vm.IsRightDockOpen);
    }

    [Fact]
    public void PanelSizes_HydratedFromSettings()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            Panels = new PanelSizes
            {
                LeftDockWidth = 250,
                RightDockWidth = 320,
                BottomDockHeight = 180,
                DatasetsInnerSplit = 0.4,
                CatalogInnerSplit = 0.6,
            },
        };
        var vm = CreateViewModel(StandardTabs(), settings);

        Assert.Equal(250, vm.LeftDockSavedWidth);
        Assert.Equal(320, vm.RightDockSavedWidth);
        Assert.Equal(180, vm.BottomDockSavedHeight);
        Assert.Equal(0.4, vm.DatasetsInnerSplit);
        Assert.Equal(0.6, vm.CatalogInnerSplit);
    }

    [Fact]
    public void SettingDatasetsInnerSplit_PersistsToSettingsObject()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var vm = CreateViewModel(StandardTabs(), settings);

        vm.DatasetsInnerSplit = 0.42;

        Assert.Equal(0.42, settings.Panels.DatasetsInnerSplit);
    }

    [Fact]
    public void SettingCatalogInnerSplit_PersistsToSettingsObject()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var vm = CreateViewModel(StandardTabs(), settings);

        vm.CatalogInnerSplit = 0.33;

        Assert.Equal(0.33, settings.Panels.CatalogInnerSplit);
    }

    [Fact]
    public void OnShutdown_DoesNotThrow()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var vm = CreateViewModel(StandardTabs(), settings);

        vm.CatalogInnerSplit = 0.33;
        var ex = Record.Exception(() => vm.OnShutdown());

        Assert.Null(ex);
    }
}
