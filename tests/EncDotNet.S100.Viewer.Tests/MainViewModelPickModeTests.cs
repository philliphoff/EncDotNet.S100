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
        var settingsVm = new ViewerSettingsService(settings);
        var routeStore = new EncDotNet.S100.Viewer.Services.RoutesService(new EncDotNet.S100.Viewer.Services.ClockViewTime(TimeProvider.System));
        var shellLayout = new ShellLayoutViewModel(settings, activityTabs ?? [], timeline, EncDotNet.S100.Viewer.Services.UiThread.Post);
        return new MainViewModel(
            layout: shellLayout,
            mapTools: TestMapTools.Create(settingsVm, routeStore),
            statusBar: new StatusBarViewModel(mcpServerHost: null, Notifications.TestNotifications.Create(), EncDotNet.S100.Viewer.Services.UiThread.Post),
            appCommands: TestAppCommands.Create(),
            datasetOpening: new DatasetOpeningViewModel(new StubRecentFilesService(), Notifications.TestNotifications.Create(), datasets, shellLayout),
            mapChrome: TestMapChrome.Create(catalogues, datasets),
            featureCatalogues: new FeatureCataloguesViewModel(settings),
            portrayalCatalogues: new PortrayalCataloguesViewModel(settings, catalogues),
            datasets: datasets,
            palette: new PaletteMenuViewModel(settingsVm),
            pickReport: pickReport ?? new PickReportViewModel(),
            timeline: timeline,
            routes: routeStore);
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
