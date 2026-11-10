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
/// PR-M1: registry-shaped behaviour of <see cref="MainViewModel"/> driven
/// by <see cref="IActivityTab"/>. Exercises ordering, command dispatch,
/// pane-title binding, persistence semantics, and startup restoration.
/// </summary>
public sealed class ActivityTabRegistryTests : IDisposable
{
    private readonly string _tempSettingsPath = Path.Combine(
        Path.GetTempPath(), $"viewer-tests-{Guid.NewGuid():N}.json");

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
        public bool FillsContent { get; init; }

        public Control CreateIcon() => new ContentControl();
        public bool IsVisible { get; init; } = true;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private sealed class SignalingViewModel : IActivityTabContentSignal
    {
        public event EventHandler? ContentBecameAvailable;
        public void Raise() => ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
    }



    private sealed class NoopLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; }
            = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; }
            = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
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

    private MainViewModel CreateViewModel(IEnumerable<IActivityTab>? tabs, ViewerSettings? settings = null)
    {
        settings ??= new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new NoopLoader());
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), dispatch: EncDotNet.S100.Viewer.Services.UiThread.RunOrPost, refresher: null, progress: null, scope: null, library: null);
        var settingsVm = new SettingsViewModel(settings, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        var routeStore = new EncDotNet.S100.Viewer.Services.RoutesService(new EncDotNet.S100.Viewer.Services.ClockViewTime(TimeProvider.System));
        var shellLayout = new ShellLayoutViewModel(settings, tabs ?? [], timeline, EncDotNet.S100.Viewer.Services.UiThread.Post);
        return new MainViewModel(
            layout: shellLayout,
            mapTools: TestMapTools.Create(settingsVm, routeStore),
            statusBar: new StatusBarViewModel(mcpServerHost: null, Notifications.TestNotifications.Create(), EncDotNet.S100.Viewer.Services.UiThread.Post),
            appCommands: TestAppCommands.Create(),
            datasetOpening: new DatasetOpeningViewModel(new StubRecentFilesService(), Notifications.TestNotifications.Create(), datasets, shellLayout),
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            layerStack: new LayerStackViewModel(new NoopLoader(), dynamicSources: null, timeFormat: null, displayCombinations: null, datasets: null),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: settingsVm,
            pickReport: new PickReportViewModel(),
            timeline: timeline,
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            textToolbar: new TextGroupToolbarViewModel(new EcdisDisplayState(), catalogues, datasets),
            displayModeToolbar: new DisplayModeToolbarViewModel(new EcdisDisplayState(), new FakeDatasetLoaderService()),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, datasets, labelOverrides: new EcdisLabelOverrideProvider(), displayModeToolbar: null, displayCombinations: null, amlDisplay: null),
            routes: routeStore);
    }

    [Fact]
    public void Tabs_AreReturnedInOrderAscending()
    {
        var tabs = new IActivityTab[]
        {
            new FakeTab { Id = "C", Order = 30 },
            new FakeTab { Id = "A", Order = 10 },
            new FakeTab { Id = "B", Order = 20 },
            new FakeTab { Id = "Z", Order = 1000 }, // bottom group
        };

        var vm = CreateViewModel(tabs);

        Assert.Equal(new[] { "A", "B", "C", "Z" }, vm.Layout.Tabs.Select(t => t.Id));
        Assert.Equal(new[] { "A", "B", "C" }, vm.Layout.LeftDockTopTabs.Select(t => t.Id));
        Assert.Equal(new[] { "Z" }, vm.Layout.LeftDockBottomTabs.Select(t => t.Id));
    }

    [Fact]
    public void SelectTabCommand_SetsSelectedTabAndSelectedTabId()
    {
        var a = new FakeTab { Id = "A", Order = 10 };
        var b = new FakeTab { Id = "B", Order = 20 };
        var vm = CreateViewModel(new IActivityTab[] { a, b });

        vm.Layout.SelectTabCommand.Execute(b);

        Assert.Same(b, vm.Layout.SelectedLeftTab);
        Assert.Equal("B", vm.Layout.SelectedLeftTabId);
    }

    [Fact]
    public void PaneTitle_ReflectsSelectedTabTitle()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Title = "Default" };
        var a = new FakeTab { Id = "A", Order = 10, Title = "Alpha" };
        var b = new FakeTab { Id = "B", Order = 20, Title = "Beta" };
        var vm = CreateViewModel(new IActivityTab[] { datasets, a, b });

        // Startup defaults to Datasets.
        Assert.Equal("Default", vm.Layout.LeftDockTitle);

        vm.Layout.SelectTabCommand.Execute(a);
        Assert.Equal("Alpha", vm.Layout.LeftDockTitle);

        vm.Layout.SelectTabCommand.Execute(b);
        Assert.Equal("Beta", vm.Layout.LeftDockTitle);
    }

    [Fact]
    public void Selecting_PersistentTab_WritesIdToSettings()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var datasets = new FakeTab { Id = "Datasets", Order = 30, PersistAsLastSelected = true };
        var other = new FakeTab { Id = "Search", Order = 60, PersistAsLastSelected = true };
        var vm = CreateViewModel(new IActivityTab[] { datasets, other }, settings);

        vm.Layout.SelectTabCommand.Execute(other);

        Assert.Equal("Search", settings.LastSelectedActivity);
    }

    [Fact]
    public void Selecting_NonPersistentTab_DoesNotUpdateSettings()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedActivity = "Datasets",
        };
        var datasets = new FakeTab { Id = "Datasets", Order = 30, PersistAsLastSelected = true };
        var settingsTab = new FakeTab { Id = "Settings", Order = 1000, PersistAsLastSelected = false };
        var vm = CreateViewModel(new IActivityTab[] { datasets, settingsTab }, settings);

        vm.Layout.SelectTabCommand.Execute(settingsTab);

        Assert.Same(settingsTab, vm.Layout.SelectedLeftTab);
        Assert.Equal("Datasets", settings.LastSelectedActivity);
    }

    [Fact]
    public void Startup_RestoresValidLastSelectedActivity()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedActivity = "Search",
        };
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var search = new FakeTab { Id = "Search", Order = 60 };
        var vm = CreateViewModel(new IActivityTab[] { datasets, search }, settings);

        Assert.Same(search, vm.Layout.SelectedLeftTab);
        Assert.Equal("Search", settings.LastSelectedActivity);
    }

    [Fact]
    public void Startup_WithStaleLastSelectedActivity_FallsBackToDatasets()
    {
        var settings = new ViewerSettings
        {
            SettingsFilePath = _tempSettingsPath,
            LastSelectedActivity = "DoesNotExist",
        };
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var search = new FakeTab { Id = "Search", Order = 60 };
        var vm = CreateViewModel(new IActivityTab[] { datasets, search }, settings);

        Assert.Same(datasets, vm.Layout.SelectedLeftTab);
    }

    [Fact]
    public void Startup_WithNoLastSelectedActivity_DefaultsToDatasets()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var search = new FakeTab { Id = "Search", Order = 60 };
        var vm = CreateViewModel(new IActivityTab[] { datasets, search }, settings);

        Assert.Same(datasets, vm.Layout.SelectedLeftTab);
    }

    [Fact]
    public void SelectDefaultTab_SelectsDatasetsEvenIfNotFirstByOrder()
    {
        var search = new FakeTab { Id = "Search", Order = 10 }; // earliest order
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var vm = CreateViewModel(new IActivityTab[] { search, datasets });

        vm.Layout.SelectTabCommand.Execute(search);
        Assert.Same(search, vm.Layout.SelectedLeftTab);

        vm.Layout.SelectDefaultTab();

        Assert.Same(datasets, vm.Layout.SelectedLeftTab);
        Assert.Equal("Datasets", vm.Layout.SelectedLeftTabId);
    }

    [Fact]
    public void SelectDefaultTab_WithoutDatasets_FallsBackToFirstTab()
    {
        var alpha = new FakeTab { Id = "Alpha", Order = 10 };
        var beta = new FakeTab { Id = "Beta", Order = 20 };
        var vm = CreateViewModel(new IActivityTab[] { alpha, beta });

        // Startup falls back to first tab when Datasets is absent.
        Assert.Same(alpha, vm.Layout.SelectedLeftTab);

        vm.Layout.SelectTabCommand.Execute(beta);
        vm.Layout.SelectDefaultTab();

        Assert.Same(alpha, vm.Layout.SelectedLeftTab);
    }

    [Fact]
    public void A_full_content_tab_is_shown_across_the_content_area_not_in_the_dock()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var settingsTab = new FakeTab { Id = "Settings", Order = 1000, PersistAsLastSelected = false, FillsContent = true };
        var vm = CreateViewModel(new IActivityTab[] { datasets, settingsTab });
        Assert.Same(datasets, vm.Layout.LeftDockContent);

        vm.Layout.SelectTabCommand.Execute(settingsTab);

        Assert.True(vm.Layout.IsContentPanelOpen);
        Assert.False(vm.Layout.IsLeftDockShown);
        Assert.Same(settingsTab, vm.Layout.ContentPanelTab);
        Assert.Null(vm.Layout.LeftDockContent);
        Assert.Equal("Settings", vm.Layout.SelectedLeftTabId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Closing_a_full_content_tab_returns_to_the_panel_it_covered(bool dockWasOpen)
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var search = new FakeTab { Id = "Search", Order = 60 };
        var settingsTab = new FakeTab { Id = "Settings", Order = 1000, PersistAsLastSelected = false, FillsContent = true };
        var vm = CreateViewModel(new IActivityTab[] { datasets, search, settingsTab }, settings);
        vm.Layout.SelectTabCommand.Execute(search);
        vm.Layout.IsLeftDockOpen = dockWasOpen;

        vm.Layout.SelectTabCommand.Execute(settingsTab);
        // A restart while Settings is open restores the covered panel's state.
        Assert.Equal(dockWasOpen, settings.IsLeftDockOpen);
        vm.Layout.SelectTabCommand.Execute(settingsTab); // the activity bar toggles it closed

        Assert.False(vm.Layout.IsContentPanelOpen);
        Assert.Same(search, vm.Layout.SelectedLeftTab);
        Assert.Equal(dockWasOpen, vm.Layout.IsLeftDockOpen);
        Assert.Equal(dockWasOpen, vm.Layout.IsLeftDockShown);
        Assert.Equal(dockWasOpen, settings.IsLeftDockOpen);
    }

    [Fact]
    public void The_close_button_of_a_full_content_tab_returns_to_the_covered_panel()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var settingsTab = new FakeTab { Id = "Settings", Order = 1000, PersistAsLastSelected = false, FillsContent = true };
        var vm = CreateViewModel(new IActivityTab[] { datasets, settingsTab });
        vm.Layout.SelectTabCommand.Execute(settingsTab);

        vm.Layout.CloseDockCommand.Execute("Left");

        Assert.Same(datasets, vm.Layout.SelectedLeftTab);
        Assert.True(vm.Layout.IsLeftDockShown);
        Assert.Same(datasets, vm.Layout.LeftDockContent);
    }

    [Fact]
    public void Choosing_another_tab_from_a_full_content_tab_shows_it_in_the_dock()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30 };
        var search = new FakeTab { Id = "Search", Order = 60 };
        var settingsTab = new FakeTab { Id = "Settings", Order = 1000, PersistAsLastSelected = false, FillsContent = true };
        var vm = CreateViewModel(new IActivityTab[] { datasets, search, settingsTab });
        vm.Layout.SelectTabCommand.Execute(settingsTab);

        vm.Layout.SelectTabCommand.Execute(search);

        Assert.False(vm.Layout.IsContentPanelOpen);
        Assert.Same(search, vm.Layout.LeftDockContent);
        Assert.True(vm.Layout.IsLeftDockShown);
    }
}
