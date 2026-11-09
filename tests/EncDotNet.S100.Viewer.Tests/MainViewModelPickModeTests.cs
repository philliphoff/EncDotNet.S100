using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class MainViewModelPickModeTests
{

    private sealed class StubThemeService : IThemeService
    {
        public bool IsDarkTheme { get; private set; }
        public event System.EventHandler? ThemeChanged;
        public bool ToggleTheme() { IsDarkTheme = !IsDarkTheme; ThemeChanged?.Invoke(this, System.EventArgs.Empty); return IsDarkTheme; }
        public ChromeTheme Current => IsDarkTheme ? ChromeTheme.Dark : ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) { IsDarkTheme = ChromeThemes.IsDark(theme); ThemeChanged?.Invoke(this, System.EventArgs.Empty); }
    }

    private sealed class StubDatasetLoaderService : IDatasetLoaderService
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

    private static MainViewModel CreateViewModel(
        PickReportViewModel? pickReport = null,
        IEnumerable<IActivityTab>? activityTabs = null)
    {
        // Construct in-memory settings (without invoking Save()) and a
        // throwaway catalogue manager. MainViewModel only touches the
        // settings file when Save() is called via a setter, which the pick
        // mode commands never do.
        var settings = new ViewerSettings();
        var catalogues = new PortrayalCatalogueManager();
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());
        var timeline = new TimelineViewModel(new GlobalTimeService(), null, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), dispatch: EncDotNet.S100.Viewer.Services.UiThread.RunOrPost, refresher: null, progress: null, scope: null, library: null);
        return new MainViewModel(
            layout: new ShellLayoutViewModel(settings, activityTabs ?? [], timeline, EncDotNet.S100.Viewer.Services.UiThread.Post),
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            layerStack: new LayerStackViewModel(new StubDatasetLoaderService(), dynamicSources: null, timeFormat: null, displayCombinations: null, datasets: null),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: new SettingsViewModel(settings, dataMaintenance: null, applicationControl: null, dialogManager: null, keysAndCertificates: null),
            pickReport: pickReport ?? new PickReportViewModel(),
            timeline: timeline,
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            textToolbar: new TextGroupToolbarViewModel(new EcdisDisplayState(), catalogues, datasets),
            displayModeToolbar: new DisplayModeToolbarViewModel(new EcdisDisplayState(), new FakeDatasetLoaderService()),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, datasets, labelOverrides: new EcdisLabelOverrideProvider(), displayModeToolbar: null, displayCombinations: null),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            measureAppearance: new StubMeasureOverlayAppearanceProvider(),
            notifications: Notifications.TestNotifications.Create(),
            dialogManager: new ShadUI.DialogManager(),
            routes: new EncDotNet.S100.Viewer.Services.RoutesService(new EncDotNet.S100.Viewer.Services.ClockViewTime(TimeProvider.System)),
            measure: new EncDotNet.S100.Viewer.Services.MeasureSession(settings: null));
    }

    /// <summary>
    /// Builds a Right-dock activity tab hosting the supplied pick report,
    /// mirroring the real registration in <c>App</c> (auto-open on content
    /// signal). The icon factory is never invoked by these tests.
    /// </summary>
    private static IActivityTab CreatePickReportTab(PickReportViewModel pickReport) =>
        new ActivityTab<PickReportViewModel, PickReportView>(
            id: "PickReport",
            order: 10,
            title: "Pick",
            tooltip: "Pick",
            iconFactory: static () => new Avalonia.Controls.Border(),
            viewModel: pickReport,
            persistAsLastSelected: false,
            dock: TabDock.Right,
            autoOpenOnContentSignal: true,
            visibility: null);

    private static readonly System.Collections.Generic.IReadOnlyList<PickAttribute> NoAttrs =
        Array.Empty<PickAttribute>();

    [Fact]
    public void IsPickModeActive_DefaultsToFalse()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void TogglePickModeCommand_FlipsState()
    {
        var vm = CreateViewModel();

        vm.TogglePickModeCommand.Execute(null);
        Assert.True(vm.IsPickModeActive);

        vm.TogglePickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void ExitPickModeCommand_TurnsOffAndIsIdempotent()
    {
        var vm = CreateViewModel();
        vm.IsPickModeActive = true;

        vm.ExitPickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);

        vm.ExitPickModeCommand.Execute(null);
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void IsPickModeActive_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var fired = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPickModeActive))
                fired++;
        };

        vm.TogglePickModeCommand.Execute(null);
        vm.TogglePickModeCommand.Execute(null);

        Assert.Equal(2, fired);
    }

    [Fact]
    public void ToggleRouteEditModeCommand_FlipsState()
    {
        var vm = CreateViewModel();

        vm.ToggleRouteEditModeCommand.Execute(null);
        Assert.True(vm.IsRouteEditModeActive);

        vm.ToggleRouteEditModeCommand.Execute(null);
        Assert.False(vm.IsRouteEditModeActive);
    }

    [Fact]
    public void RouteEditMode_AndPickMode_AreMutuallyExclusive()
    {
        var vm = CreateViewModel();

        vm.IsPickModeActive = true;
        vm.ToggleRouteEditModeCommand.Execute(null);

        Assert.True(vm.IsRouteEditModeActive);
        Assert.False(vm.IsPickModeActive);
    }

    [Fact]
    public void PromoteMeasurementToRouteCommand_DisabledWithoutMeasurement()
    {
        var vm = CreateViewModel();
        Assert.False(vm.PromoteMeasurementToRouteCommand.CanExecute(null));
    }

    [Fact]
    public void PromoteMeasurement_KeepsGreatCircleGeometry()
    {
        var vm = CreateViewModel();
        vm.ToggleMeasureModeCommand.Execute(null);
        vm.MeasureSession.SetGeometry(EncDotNet.S100.Navigation.LegGeometry.GreatCircle);
        vm.MeasureSession.State.SetWaypoints([new(0, 0), new(10, 10), new(20, 0)]);
        vm.MeasureSession.NotifyChanged();
        Assert.True(vm.PromoteMeasurementToRouteCommand.CanExecute(null));

        vm.PromoteMeasurementToRouteCommand.Execute(null);

        var route = vm.RoutesService.Routes.Routes[^1];
        Assert.Equal(2, route.Legs.Count);
        Assert.All(route.Legs, l => Assert.Equal(Routing.RouteLegGeometryType.Geodesic, l.GeometryType));
        Assert.True(vm.IsRouteEditModeActive);
    }

    [Fact]
    public void MeasureCard_Close_AndActivationRequest_DriveTheTool()
    {
        var vm = CreateViewModel();

        vm.MeasureSession.RequestActivation();
        Assert.True(vm.IsMeasureModeActive);

        vm.MeasureCard.CloseCommand.Execute(null);
        Assert.False(vm.IsMeasureModeActive);
    }

    [Fact]
    public void IsToolSummaryVisible_TrueWhileRouteEditing()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsToolSummaryVisible);

        vm.ToggleRouteEditModeCommand.Execute(null);
        Assert.True(vm.IsToolSummaryVisible);
    }

    [Fact]
    public void CloseDockCommand_Right_ClearsPickWhenPickReportShown()
    {
        // issue #374: dismissing the Pick Report must also drop the pick (and
        // its map highlight) so the report and overlay stay in sync.
        var pickReport = new PickReportViewModel();
        var vm = CreateViewModel(pickReport, new[] { CreatePickReportTab(pickReport) });

        pickReport.SetPick("FT", "FT name", "ref-1", "ds.000", "S-101", NoAttrs);
        Assert.True(pickReport.HasPick);
        Assert.True(vm.Layout.IsRightDockOpen);

        vm.Layout.CloseDockCommand.Execute(TabDock.Right);

        Assert.False(vm.Layout.IsRightDockOpen);
        Assert.False(pickReport.HasPick);
    }

    [Fact]
    public void CloseDockCommand_Right_ThenNewPick_ReopensDock()
    {
        // issue #374: after a manual close clears the pick, a fresh pick is a
        // genuine HasPick false→true transition and re-opens the dock.
        var pickReport = new PickReportViewModel();
        var vm = CreateViewModel(pickReport, new[] { CreatePickReportTab(pickReport) });

        pickReport.SetPick("FT", "FT name", "ref-1", "ds.000", "S-101", NoAttrs);
        vm.Layout.CloseDockCommand.Execute(TabDock.Right);
        Assert.False(vm.Layout.IsRightDockOpen);

        pickReport.SetPick("FT2", "FT2 name", "ref-2", "ds.000", "S-101", NoAttrs);

        Assert.True(pickReport.HasPick);
        Assert.True(vm.Layout.IsRightDockOpen);
    }
}
