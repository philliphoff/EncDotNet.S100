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
    public LayerStackViewModel LayerStack { get; }
    public FeatureSearchViewModel Search { get; }
    public SettingsViewModel Settings { get; }
    public PickReportViewModel PickReport { get; }
    public TimelineViewModel Timeline { get; }
    public DisplayToolbarViewModel DisplayToolbar { get; }
    public TextGroupToolbarViewModel TextToolbar { get; }
    public DisplayModeToolbarViewModel DisplayModeToolbar { get; }
    public EcdisDisplayPanelViewModel EcdisDisplayPanel { get; }

    /// <summary>
    /// The Navigational warnings panel (#932), whose in-map note the window
    /// shows over the map; <c>null</c> in hosts without it.
    /// </summary>
    public NavigationalWarningsViewModel? NavigationalWarnings { get; }

    /// <summary>Which S-124 warnings the map draws, and the "Show warnings not yet in force" setting (#993).</summary>
    public Services.NavigationalWarningDrawingService? NavigationalWarningDrawing { get; }

    /// <summary>
    /// The display combinations behind the map's <b>View ▾</b> pill, or
    /// <see langword="null"/> where they aren't wired (tests).
    /// </summary>
    public DisplayCombinationsViewModel? DisplayCombinations { get; }

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
        FeatureCataloguesViewModel featureCatalogues,
        PortrayalCataloguesViewModel portrayalCatalogues,
        DatasetsViewModel datasets,
        LayerStackViewModel layerStack,
        FeatureSearchViewModel search,
        SettingsViewModel settingsViewModel,
        PickReportViewModel pickReport,
        TimelineViewModel timeline,
        DisplayToolbarViewModel displayToolbar,
        TextGroupToolbarViewModel textToolbar,
        DisplayModeToolbarViewModel displayModeToolbar,
        EcdisDisplayPanelViewModel ecdisDisplayPanel,
        RoutesService routes,
        NavigationalWarningsViewModel? navigationalWarnings = null,
        Services.NavigationalWarningDrawingService? navigationalWarningDrawing = null,
        DisplayCombinationsViewModel? displayCombinations = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(mapTools);
        ArgumentNullException.ThrowIfNull(statusBar);
        ArgumentNullException.ThrowIfNull(appCommands);
        ArgumentNullException.ThrowIfNull(datasetOpening);
        ArgumentNullException.ThrowIfNull(featureCatalogues);
        ArgumentNullException.ThrowIfNull(portrayalCatalogues);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(layerStack);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        ArgumentNullException.ThrowIfNull(pickReport);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(displayToolbar);
        ArgumentNullException.ThrowIfNull(textToolbar);
        ArgumentNullException.ThrowIfNull(displayModeToolbar);
        ArgumentNullException.ThrowIfNull(ecdisDisplayPanel);
        ArgumentNullException.ThrowIfNull(routes);

        Layout = layout;
        MapTools = mapTools;
        StatusBar = statusBar;
        AppCommands = appCommands;
        DatasetOpening = datasetOpening;
        _routes = routes;

        FeatureCatalogues = featureCatalogues;
        PortrayalCatalogues = portrayalCatalogues;
        Datasets = datasets;
        LayerStack = layerStack;
        Search = search;
        Settings = settingsViewModel;
        PickReport = pickReport;
        // Dismissing the Pick Report also drops the map highlight, so the
        // report and overlay stay in sync and a later pick re-opens the dock.
        layout.RightDockClosed += tab =>
        {
            if (ReferenceEquals(tab?.ViewModel, PickReport))
                PickReport.Clear();
        };
        Timeline = timeline;
        NavigationalWarnings = navigationalWarnings;
        NavigationalWarningDrawing = navigationalWarningDrawing;
        DisplayCombinations = displayCombinations;
        DisplayToolbar = displayToolbar;
        TextToolbar = textToolbar;
        DisplayModeToolbar = displayModeToolbar;
        EcdisDisplayPanel = ecdisDisplayPanel;
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
