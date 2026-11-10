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
/// PR-M4: multi-dock activity-tab registry. Exercises the per-dock
/// partitioning, dock-aware selection, close commands, and
/// auto-open-on-content-signal behaviour.
/// </summary>
public sealed class MultiDockActivityTabTests : IDisposable
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

        public Control CreateIcon() => new ContentControl();
        public bool IsVisible { get; init; } = true;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private sealed class SignalingVm : IActivityTabContentSignal
    {
        public event EventHandler? ContentBecameAvailable;
        public void Raise() => ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
    }

    private sealed class MutableTab : IActivityTab
    {
        private bool _isVisible = true;

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

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    [Fact]
    public void HidingSelectedLeftTab_MovesSelectionToFirstVisibleTab()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var helm = new MutableTab { Id = "Helm", Order = 67, Dock = TabDock.Left, PersistAsLastSelected = false };

        var vm = CreateViewModel(new IActivityTab[] { datasets, helm });
        vm.Layout.SelectTabCommand.Execute(helm);
        Assert.Same(helm, vm.Layout.SelectedLeftTab);

        helm.IsVisible = false;

        Assert.Same(datasets, vm.Layout.SelectedLeftTab);
        Assert.True(vm.Layout.IsLeftDockOpen);
    }

    [Fact]
    public void HidingSelectedLeftTab_WithNoOtherVisibleTab_ClosesDock()
    {
        var helm = new MutableTab { Id = "Helm", Order = 67, Dock = TabDock.Left, PersistAsLastSelected = false };

        var vm = CreateViewModel(new IActivityTab[] { helm });
        vm.Layout.SelectTabCommand.Execute(helm);
        Assert.Same(helm, vm.Layout.SelectedLeftTab);

        helm.IsVisible = false;

        Assert.Null(vm.Layout.SelectedLeftTab);
        Assert.False(vm.Layout.IsLeftDockOpen);
    }

    [Fact]
    public void SelectTabCommand_IgnoresHiddenTab()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var helm = new MutableTab { Id = "Helm", Order = 67, Dock = TabDock.Left, PersistAsLastSelected = false, IsVisible = false };

        var vm = CreateViewModel(new IActivityTab[] { datasets, helm });

        vm.Layout.SelectTabCommand.Execute(helm);

        Assert.NotSame(helm, vm.Layout.SelectedLeftTab);
    }

    [Fact]
    public void HiddenTab_IsNotRestoredAtStartup()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath, LastSelectedActivity = "Helm" };
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var helm = new MutableTab { Id = "Helm", Order = 67, Dock = TabDock.Left, PersistAsLastSelected = false, IsVisible = false };

        var vm = CreateViewModel(new IActivityTab[] { datasets, helm }, settings);

        Assert.Same(datasets, vm.Layout.SelectedLeftTab);
    }

    [Fact]
    public void SystemFallback_AfterHide_DoesNotPersistLastSelected()
    {
        var settings = new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var helm = new MutableTab { Id = "Helm", Order = 67, Dock = TabDock.Left, PersistAsLastSelected = false };

        var vm = CreateViewModel(new IActivityTab[] { datasets, helm }, settings);
        vm.Layout.SelectTabCommand.Execute(helm);

        helm.IsVisible = false;

        // Falling back to Datasets is system-driven and must not overwrite
        // the persisted last-selected activity.
        Assert.Same(datasets, vm.Layout.SelectedLeftTab);
        Assert.Null(settings.LastSelectedActivity);
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

    private MainViewModel CreateViewModel(IEnumerable<IActivityTab>? tabs, ViewerSettings? settings = null, TimelineViewModel? timeline = null)
    {
        settings ??= new ViewerSettings { SettingsFilePath = _tempSettingsPath };
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new NoopLoader());
        var timelineVm = timeline ?? new TimelineViewModel(new GlobalTimeService(), null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), dispatch: EncDotNet.S100.Viewer.Services.UiThread.RunOrPost, refresher: null, progress: null, scope: null, library: null);
        var settingsVm = new SettingsViewModel(settings, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null);
        var routeStore = new EncDotNet.S100.Viewer.Services.RoutesService(new EncDotNet.S100.Viewer.Services.ClockViewTime(TimeProvider.System));
        return new MainViewModel(
            layout: new ShellLayoutViewModel(settings, tabs ?? [], timelineVm, EncDotNet.S100.Viewer.Services.UiThread.Post),
            mapTools: TestMapTools.Create(settingsVm, routeStore),
            statusBar: new StatusBarViewModel(mcpServerHost: null, Notifications.TestNotifications.Create(), EncDotNet.S100.Viewer.Services.UiThread.Post),
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            layerStack: new LayerStackViewModel(new NoopLoader(), dynamicSources: null, timeFormat: null, displayCombinations: null, datasets: null),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: settingsVm,
            pickReport: new PickReportViewModel(),
            timeline: timelineVm,
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            textToolbar: new TextGroupToolbarViewModel(new EcdisDisplayState(), catalogues, datasets),
            displayModeToolbar: new DisplayModeToolbarViewModel(new EcdisDisplayState(), new FakeDatasetLoaderService()),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, datasets, labelOverrides: new EcdisLabelOverrideProvider(), displayModeToolbar: null, displayCombinations: null),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            notifications: Notifications.TestNotifications.Create(),
            dialogManager: new ShadUI.DialogManager(),
            routes: routeStore);
    }

    [Fact]
    public void Tabs_PartitionByDock()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var timeline = new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick, timeline });

        Assert.Equal(new[] { "Datasets" }, vm.Layout.LeftTabs.Select(t => t.Id));
        Assert.Equal(new[] { "Pick" }, vm.Layout.RightTabs.Select(t => t.Id));
        Assert.Equal(new[] { "Timeline" }, vm.Layout.BottomTabs.Select(t => t.Id));
    }

    [Fact]
    public void SelectingLeftTab_DoesNotChangeRightOrBottomSelection()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var search = new FakeTab { Id = "Search", Order = 60, Dock = TabDock.Left };
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var timeline = new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom };

        var vm = CreateViewModel(new IActivityTab[] { datasets, search, pick, timeline });

        var rightBefore = vm.Layout.SelectedRightTab;
        var bottomBefore = vm.Layout.SelectedBottomTab;

        vm.Layout.SelectTabCommand.Execute(search);

        Assert.Same(search, vm.Layout.SelectedLeftTab);
        Assert.Same(rightBefore, vm.Layout.SelectedRightTab);
        Assert.Same(bottomBefore, vm.Layout.SelectedBottomTab);
    }

    [Fact]
    public void TimeHud_ShowsWhileTheTimelineDockIsClosedOrTheMapTooNarrow()
    {
        // #712 F5: the dock no longer opens by itself; the HUD stands in for it.
        var time = new GlobalTimeService();
        var timeline = new TimelineViewModel(time, null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), action => action(), refresher: null, progress: null, scope: null, library: null);
        var vm = CreateViewModel(new IActivityTab[] { new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom } }, timeline: timeline);
        vm.Layout.WindowWidth = 1200;
        Assert.False(vm.Layout.IsTimeHudVisible);

        var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        time.ApplySnapshot(new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSnapshot
        {
            Minimum = t0,
            Maximum = t0.AddHours(2),
            Current = t0,
            Samples = [t0, t0.AddHours(1), t0.AddHours(2)],
            CoverageSegments = [new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSegment(t0, t0.AddHours(2))],
            Datasets = [new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimedDataset("x", t0, t0.AddHours(2))],
        });
        Assert.True(vm.Layout.IsTimeHudVisible);

        vm.Layout.ToggleTimelineDockCommand.Execute(null);
        Assert.True(vm.Layout.IsBottomDockShown);
        Assert.False(vm.Layout.IsTimeHudVisible);

        // Under 900 px the dock is hidden (but remembered) and the HUD is the only control.
        vm.Layout.WindowWidth = 850;
        Assert.True(vm.Layout.IsBottomDockOpen);
        Assert.False(vm.Layout.IsBottomDockShown);
        Assert.True(vm.Layout.IsTimeHudVisible);
        Assert.False(vm.Layout.ToggleTimelineDockCommand.CanExecute(null));

        vm.Layout.WindowWidth = 1000;
        Assert.True(vm.Layout.IsBottomDockShown);
    }

    [Fact]
    public void TheMapHasOneTimeSurfaceAtATime()
    {
        // Design note time-overlays D1: the stamp while the Timeline tab is
        // shown, the HUD otherwise, never both (the Logs tab used to show both).
        var time = new GlobalTimeService();
        var timeline = new TimelineViewModel(time, null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), action => action(), refresher: null, progress: null, scope: null, library: null);
        var timelineTab = new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom };
        var logsTab = new FakeTab { Id = "Logs", Order = 20, Dock = TabDock.Bottom };
        var vm = CreateViewModel(new IActivityTab[] { timelineTab, logsTab }, timeline: timeline);
        vm.Layout.WindowWidth = 1200;
        var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        time.ApplySnapshot(new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSnapshot
        {
            Minimum = t0,
            Maximum = t0.AddHours(2),
            Current = t0,
            Samples = [t0, t0.AddHours(1), t0.AddHours(2)],
            CoverageSegments = [new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimeSegment(t0, t0.AddHours(2))],
            Datasets = [new EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimedDataset("x", t0, t0.AddHours(2))],
        });
        void AssertOne(bool stamp)
        {
            Assert.Equal(stamp, vm.Layout.IsMapStampVisible);
            Assert.Equal(!stamp, vm.Layout.IsTimeHudVisible);
        }

        AssertOne(stamp: false);
        vm.Layout.ToggleTimelineDockCommand.Execute(null);
        Assert.True(vm.Layout.IsTimelineTabSelected);
        AssertOne(stamp: true);

        var raised = new List<string?>();
        vm.Layout.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Layout.SelectTabCommand.Execute(logsTab);
        Assert.Contains(nameof(ShellLayoutViewModel.IsMapStampVisible), raised);
        AssertOne(stamp: false);

        // Pinned with date-dependent data: the HUD says Display Not Real Time, so the stamp stays away.
        timeline.HasDateDependentData = true;
        timeline.NextStepCommand.Execute(null);
        Assert.True(timeline.IsNotRealTime);
        AssertOne(stamp: false);

        vm.Layout.SelectTabCommand.Execute(timelineTab);
        AssertOne(stamp: true);
        vm.Layout.WindowWidth = 850;
        AssertOne(stamp: false);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task PanelState_ReportsBottomPanelNotShowingWhileTheWindowIsTooNarrow()
    {
        // list_panels said the Logs tab was showing while a narrow window hid the dock.
        var logs = new FakeTab { Id = "Logs", Order = 30, Dock = TabDock.Bottom };
        var vm = CreateViewModel(new IActivityTab[] { new FakeTab { Id = "Datasets", Dock = TabDock.Left }, logs });
        vm.Layout.WindowWidth = 1200;
        var controller = new ViewerUiController(vm.Layout);

        var shown = await controller.SetPanelVisibilityAsync("Logs", visible: true);
        Assert.True(shown.State!.Value.Showing);

        vm.Layout.WindowWidth = 850;
        var narrow = (await controller.GetPanelsAsync()).Single(p => p.Id == "Logs");
        Assert.True(narrow.DockOpen);
        Assert.False(narrow.Showing);

        vm.Layout.WindowWidth = 1000;
        Assert.True((await controller.GetPanelsAsync()).Single(p => p.Id == "Logs").Showing);
    }

    [Fact]
    public void CloseDockCommand_ClosesTheTargetedDock()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var pick = new FakeTab { Id = "Pick", Order = 10, Dock = TabDock.Right };
        var timeline = new FakeTab { Id = "Timeline", Order = 10, Dock = TabDock.Bottom };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick, timeline });

        vm.Layout.IsRightDockOpen = true;
        vm.Layout.IsBottomDockOpen = true;

        vm.Layout.CloseDockCommand.Execute(TabDock.Right);

        Assert.False(vm.Layout.IsRightDockOpen);
        Assert.True(vm.Layout.IsBottomDockOpen);
        Assert.True(vm.Layout.IsLeftDockOpen);
    }

    [Fact]
    public void AutoOpen_RaisingContentBecameAvailable_OpensDockAndSelectsTab()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var signal = new SignalingVm();
        var pick = new FakeTab
        {
            Id = "Pick",
            Order = 10,
            Dock = TabDock.Right,
            AutoOpenOnContentSignal = true,
            ViewModel = signal,
        };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick });

        Assert.False(vm.Layout.IsRightDockOpen);

        signal.Raise();

        Assert.True(vm.Layout.IsRightDockOpen);
        Assert.Same(pick, vm.Layout.SelectedRightTab);
    }

    [Fact]
    public void AutoOpen_DoesNotFire_WhenAutoOpenOnContentSignalIsFalse()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var signal = new SignalingVm();
        var pick = new FakeTab
        {
            Id = "Pick",
            Order = 10,
            Dock = TabDock.Right,
            AutoOpenOnContentSignal = false,
            ViewModel = signal,
        };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick });

        signal.Raise();

        Assert.False(vm.Layout.IsRightDockOpen);
    }

    [Fact]
    public void AutoOpen_AfterUserClose_SubsequentSignalReopensDock()
    {
        var datasets = new FakeTab { Id = "Datasets", Order = 30, Dock = TabDock.Left };
        var signal = new SignalingVm();
        var pick = new FakeTab
        {
            Id = "Pick",
            Order = 10,
            Dock = TabDock.Right,
            AutoOpenOnContentSignal = true,
            ViewModel = signal,
        };

        var vm = CreateViewModel(new IActivityTab[] { datasets, pick });

        signal.Raise();
        Assert.True(vm.Layout.IsRightDockOpen);

        vm.Layout.CloseDockCommand.Execute(TabDock.Right);
        Assert.False(vm.Layout.IsRightDockOpen);

        signal.Raise();
        Assert.True(vm.Layout.IsRightDockOpen);
    }

    // ── Logs tab (#1080) ────────────────────────────────────────────────

    [Fact]
    public void Show_logs_opens_the_bottom_dock_on_the_logs_tab_and_tells_it_it_is_shown()
    {
        using var buffer = new EncDotNet.S100.Viewer.Diagnostics.LogBuffer(timeProvider: TimeProvider.System);
        using var logs = new LogsViewModel(buffer, post: action => action(), showFeedback: null, timeFormat: null, time: TimeProvider.System);
        var timeline = new FakeTab { Id = ShellLayoutViewModel.TimelineTabId, Order = 10, Dock = TabDock.Bottom };
        var logsTab = new ActivityTab<LogsViewModel, ContentControl>(
            ShellLayoutViewModel.LogsTabId, 30, "Logs", "Logs", () => new ContentControl(), logs, false, visibility: null, TabDock.Bottom);
        var vm = CreateViewModel(new IActivityTab[] { timeline, logsTab });
        vm.Layout.WindowWidth = 1200;
        Assert.False(vm.Layout.IsBottomDockOpen);
        Assert.Same(timeline, vm.Layout.SelectedBottomTab);
        Assert.False(logs.IsTabShown);

        vm.Layout.ShowLogsCommand.Execute(null);

        Assert.True(vm.Layout.IsBottomDockOpen);
        Assert.Same(logsTab, vm.Layout.SelectedBottomTab);
        Assert.True(logs.IsTabShown);
        Assert.True(vm.Layout.HasBottomDockTabs);

        vm.Layout.SelectTabCommand.Execute(timeline);
        Assert.False(logs.IsTabShown);
        vm.Layout.SelectTabCommand.Execute(logsTab);
        Assert.True(logs.IsTabShown);
        vm.Layout.CloseDockCommand.Execute("Bottom");
        Assert.False(logs.IsTabShown);

        // View › Timeline opens the Timeline, not the Logs tab the dock last showed.
        vm.Layout.ToggleTimelineCommand.Execute(null);
        Assert.True(vm.Layout.IsBottomDockOpen);
        Assert.True(vm.Layout.IsTimelineTabSelected);
    }

    [Fact]
    public void The_logs_badge_reaches_its_bottom_dock_tab()
    {
        using var buffer = new EncDotNet.S100.Viewer.Diagnostics.LogBuffer(timeProvider: TimeProvider.System);
        using var logs = new LogsViewModel(buffer, post: action => action(), showFeedback: null, timeFormat: null, time: TimeProvider.System);
        using var logsTab = new ActivityTab<LogsViewModel, ContentControl>(
            ShellLayoutViewModel.LogsTabId, 30, "Logs", "Logs", () => new ContentControl(), logs, false, visibility: null, TabDock.Bottom);

        buffer.Add(Microsoft.Extensions.Logging.LogLevel.Error, "EncDotNet.S100.Viewer.App", "boom");
        buffer.Flush();

        Assert.True(logsTab.HasBadge);
        Assert.Equal(1, logsTab.BadgeCount);
    }
}
