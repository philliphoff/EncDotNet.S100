using EncDotNet.S100.Viewer.Services;
namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class MainViewModel : ViewModelBase
{
    private readonly RoutesService _routes;

    /// <summary>The docks, the activity tabs and the panel sizes.</summary>
    public ShellLayoutViewModel Layout { get; }

    public FeatureCataloguesViewModel FeatureCatalogues { get; }
    public PortrayalCataloguesViewModel PortrayalCatalogues { get; }
    public DatasetsViewModel Datasets { get; }
    /// <summary>The display toolbar's palette menu.</summary>
    public PaletteMenuViewModel Palette { get; }
    public PickReportViewModel PickReport { get; }
    public TimelineViewModel Timeline { get; }

    /// <summary>The display toolbars, the View pill's combinations and the navigational warnings' map controls.</summary>
    public MapChromeViewModel MapChrome { get; }

    /// <summary>Whether Check passage is on (the map legend and wash follow it).</summary>
    public bool IsCheckModeActive => _routes.IsCheckModeActive;

    /// <summary>
    /// Opens the Datasets panel on its flat list and selects the dataset with
    /// <paramref name="datasetId"/>, so the user sees its layer time.
    /// </summary>
    internal void ShowLayer(string datasetId)
    {
        if (Datasets.Entries.FirstOrDefault(e => string.Equals(e.Id.Value, datasetId, StringComparison.Ordinal)) is not { } entry)
            return;
        Layout.SelectTab("Datasets");
        Datasets.ActiveTabIndex = 1;
        Datasets.SelectedDataset = entry;
    }

    /// <summary>The status bar's readouts: cursor position, scale, overscale, depth basis and MCP status.</summary>
    public StatusBarViewModel StatusBar { get; }

    /// <summary>The map's interactive tools: pick, measure, range &amp; bearing and the route editor.</summary>
    public MapToolsViewModel MapTools { get; }

    /// <summary>
    /// The application-scoped editable route store. Exposed so the host
    /// window can drive the persistent route overlay and the routes side
    /// panel can bind to it.
    /// </summary>
    public RoutesService RoutesService => _routes;

    /// <summary>The theme toggle and the Report Feedback and About dialogs.</summary>
    public AppCommandsViewModel AppCommands { get; }

    /// <summary>Open Recent and the in-flight exchange-set load.</summary>
    public DatasetOpeningViewModel DatasetOpening { get; }

    public MainViewModel(
        ShellLayoutViewModel layout,
        MapToolsViewModel mapTools,
        StatusBarViewModel statusBar,
        AppCommandsViewModel appCommands,
        DatasetOpeningViewModel datasetOpening,
        MapChromeViewModel mapChrome,
        FeatureCataloguesViewModel featureCatalogues,
        PortrayalCataloguesViewModel portrayalCatalogues,
        DatasetsViewModel datasets,
        PaletteMenuViewModel palette,
        PickReportViewModel pickReport,
        TimelineViewModel timeline,
        RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(mapTools);
        ArgumentNullException.ThrowIfNull(statusBar);
        ArgumentNullException.ThrowIfNull(appCommands);
        ArgumentNullException.ThrowIfNull(datasetOpening);
        ArgumentNullException.ThrowIfNull(mapChrome);
        ArgumentNullException.ThrowIfNull(featureCatalogues);
        ArgumentNullException.ThrowIfNull(portrayalCatalogues);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(pickReport);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(routes);

        Layout = layout;
        MapTools = mapTools;
        StatusBar = statusBar;
        AppCommands = appCommands;
        DatasetOpening = datasetOpening;
        MapChrome = mapChrome;
        _routes = routes;

        FeatureCatalogues = featureCatalogues;
        PortrayalCatalogues = portrayalCatalogues;
        Datasets = datasets;
        Palette = palette;
        PickReport = pickReport;
        // Dismissing the Pick Report also drops the map highlight, so the
        // report and overlay stay in sync and a later pick re-opens the dock.
        layout.RightDockClosed += tab =>
        {
            if (ReferenceEquals(tab?.ViewModel, PickReport))
                PickReport.Clear();
        };
        Timeline = timeline;
        // "No data … for N of M layers" opens the dataset list on that layer (#709, handoff D4).
        Timeline.ShowLayerRequested += ShowLayer;

        // Check passage folds the Object Information dock to its edge tab
        // while it's on, and restores it when it's turned off (C1).
        _routes.CheckModeChanged += (_, _) =>
        {
            Layout.IsRightDockFolded = _routes.IsCheckModeActive;
            OnPropertyChanged(nameof(IsCheckModeActive));
        };
    }
}
