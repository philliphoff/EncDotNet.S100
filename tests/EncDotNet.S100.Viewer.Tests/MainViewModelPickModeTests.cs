using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public class MainViewModelPickModeTests
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
        public void Initialize(IMapHost host, ViewerCommandSettings? options) { }
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
        var catalogSource = new EmptyCatalogSource();
        var datasets = new DatasetsViewModel(new StubDatasetLoaderService());
        return new MainViewModel(
            settings,
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            catalogPanel: new CatalogPanelViewModel(catalogSource),
            layerStack: new LayerStackViewModel(new StubDatasetLoaderService()),
            search: new FeatureSearchViewModel(new StubFeatureSearchService(), new StubPickService()),
            settingsViewModel: new SettingsViewModel(settings),
            pickReport: pickReport ?? new PickReportViewModel(),
            timeline: new TimelineViewModel(new GlobalTimeService()),
            displayToolbar: new DisplayToolbarViewModel(new EcdisDisplayState()),
            textToolbar: new TextGroupToolbarViewModel(new EcdisDisplayState(), catalogues, datasets),
            displayModeToolbar: new DisplayModeToolbarViewModel(new EcdisDisplayState(), new FakeDatasetLoaderService()),
            ecdisDisplayPanel: new EcdisDisplayPanelViewModel(new EcdisDisplayState(), catalogues, datasets),
            themeService: new StubThemeService(),
            recentFiles: new StubRecentFilesService(),
            measureAppearance: new StubMeasureOverlayAppearanceProvider(),
            notifications: Notifications.TestNotifications.Create(),
            activityTabs: activityTabs);
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
            autoOpenOnContentSignal: true);

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
        Assert.True(vm.IsRightDockOpen);

        vm.CloseDockCommand.Execute(TabDock.Right);

        Assert.False(vm.IsRightDockOpen);
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
        vm.CloseDockCommand.Execute(TabDock.Right);
        Assert.False(vm.IsRightDockOpen);

        pickReport.SetPick("FT2", "FT2 name", "ref-2", "ds.000", "S-101", NoAttrs);

        Assert.True(pickReport.HasPick);
        Assert.True(vm.IsRightDockOpen);
    }
}
