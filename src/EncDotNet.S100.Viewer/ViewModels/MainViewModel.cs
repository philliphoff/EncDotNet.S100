using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using ShadUI;
namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class MainViewModel : ViewModelBase
{
    private readonly IThemeService _theme;
    private readonly IRecentFilesService _recentFiles;
    private readonly INotificationService _notifications;
    private readonly RoutesService _routes;
    private readonly ShadUI.DialogManager _dialogManager;
    private readonly Func<FeedbackDialogViewModel>? _feedbackDialogFactory;
    private readonly Func<AboutDialogViewModel>? _aboutDialogFactory;

    /// <summary>
    /// The persistent MCP port-conflict notification, held so the
    /// "find another port" outcome updates it in place rather than
    /// stacking a second notification.
    /// </summary>

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

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

    public ICommand ToggleThemeCommand { get; }

    /// <summary>
    /// ShadUI dialog manager bound by the main window's
    /// <c>DialogHost</c>; used to present the feedback dialog and any
    /// future modal dialogs.
    /// </summary>
    public ShadUI.DialogManager DialogManager { get; }

    /// <summary>
    /// Opens the "Report Feedback" modal dialog. Triggered from the
    /// app-bar button and the Help menu.
    /// </summary>
    public ICommand ShowFeedbackCommand { get; }

    /// <summary>
    /// Opens the "About" modal dialog (version info + update check).
    /// Triggered from the Help menu.
    /// </summary>
    public ICommand ShowAboutCommand { get; }

    // ─── Exchange-set progress + banner (es3-progress) ────────────────────

    private bool _isExchangeSetLoading;
    private System.Threading.CancellationTokenSource? _exchangeSetCts;

    /// <summary>
    /// True while an exchange-set load is in flight. Gates the
    /// <see cref="CancelExchangeSetCommand"/>; progress itself is surfaced
    /// through the notification system, not a modal overlay.
    /// </summary>
    public bool IsExchangeSetLoading
    {
        get => _isExchangeSetLoading;
        private set => SetProperty(ref _isExchangeSetLoading, value);
    }

    /// <summary>Cancels the in-flight exchange-set load. No-op if idle.</summary>
    public ICommand CancelExchangeSetCommand { get; }

    /// <summary>
    /// Called by <see cref="MainWindow"/> when an exchange-set load is
    /// about to start. Captures the cancellation source and marks the
    /// load as in flight.
    /// </summary>
    internal System.Threading.CancellationToken BeginExchangeSetLoad(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        _exchangeSetCts?.Dispose();
        _exchangeSetCts = new System.Threading.CancellationTokenSource();

        IsExchangeSetLoading = true;
        return _exchangeSetCts.Token;
    }

    /// <summary>
    /// Called by <see cref="MainWindow"/> when an exchange-set load
    /// completes (or is cancelled / fatally fails). Clears the in-flight
    /// flag. Progress and outcome (success / partial / failure) are
    /// surfaced by <see cref="Services.IExchangeSetService"/> through the
    /// notification service.
    /// </summary>
    internal void EndExchangeSetLoad(ExchangeSetOpenResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        IsExchangeSetLoading = false;
    }

    /// <summary>
    /// Opens a dataset that the user has previously loaded. If the file is
    /// no longer at the recorded path the entry is dropped from the recent
    /// list and a status message is shown.
    /// </summary>
    public IAsyncRelayCommand<string> OpenRecentCommand { get; }

    public MainViewModel(
        ShellLayoutViewModel layout,
        MapToolsViewModel mapTools,
        StatusBarViewModel statusBar,
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
        IThemeService themeService,
        IRecentFilesService recentFiles,
        INotificationService notifications,
        ShadUI.DialogManager dialogManager,
        RoutesService routes,
        Func<FeedbackDialogViewModel>? feedbackDialogFactory = null,
        Func<AboutDialogViewModel>? aboutDialogFactory = null,
        NavigationalWarningsViewModel? navigationalWarnings = null,
        Services.NavigationalWarningDrawingService? navigationalWarningDrawing = null,
        DisplayCombinationsViewModel? displayCombinations = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(mapTools);
        ArgumentNullException.ThrowIfNull(statusBar);
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
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(dialogManager);
        ArgumentNullException.ThrowIfNull(routes);

        Layout = layout;
        MapTools = mapTools;
        StatusBar = statusBar;
        _theme = themeService;
        _recentFiles = recentFiles;
        _notifications = notifications;
        _dialogManager = dialogManager;
        _feedbackDialogFactory = feedbackDialogFactory;
        _aboutDialogFactory = aboutDialogFactory;
        DialogManager = _dialogManager;
        _isDarkTheme = themeService.IsDarkTheme;
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

        ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = _theme.ToggleTheme());

        ShowFeedbackCommand = new AsyncRelayCommand(ShowFeedbackAsync);
        ShowAboutCommand = new AsyncRelayCommand(ShowAboutAsync);

        // Keep IsDarkTheme in sync when the theme is changed via paths
        // other than ToggleThemeCommand (e.g. the SettingsView chrome
        // selector, which routes through IThemeService.SetTheme).
        _theme.ThemeChanged += (_, _) => IsDarkTheme = _theme.IsDarkTheme;

        CancelExchangeSetCommand = new RelayCommand(
            () => _exchangeSetCts?.Cancel(),
            () => IsExchangeSetLoading);

        // Re-evaluate Cancel command when the loading flag changes so the
        // overlay button enables/disables correctly.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsExchangeSetLoading))
                ((RelayCommand)CancelExchangeSetCommand).NotifyCanExecuteChanged();
        };

        OpenRecentCommand = new AsyncRelayCommand<string>(OpenRecentAsync);

    }

    /// <summary>
    /// Opens an exchange set (folder, ZIP or <c>CATALOG.031</c>) with the
    /// window's progress UI; used by Open Recent to replay an exchange set.
    /// Set by the main window. Issue #655.
    /// </summary>
    internal Func<string, Task>? ExchangeSetOpener { get; set; }

    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            _notifications.Create(Strings.Toast_Warning)
                .WithSeverity(NotificationSeverity.Warning)
                .WithContent(string.Format(
                    Strings.Status_FileNoLongerExists,
                    Services.Notifications.NotificationFormat.ShortenPath(path)))
                .Show();
            // Drop the missing entry so the menu reflects reality.
            _recentFiles.Remove(path);
            return;
        }

        Layout.SelectDefaultTab();
        if (ExchangeSetOpener is { } openExchangeSet && IsExchangeSetPath(path))
        {
            await openExchangeSet(path);
            return;
        }

        await Datasets.LoadFromPathAsync(path);
    }

    /// <summary>
    /// True for a path Open Recent should replay as an exchange set: a folder,
    /// an exchange-set ZIP, or a <c>CATALOG.031</c>.
    /// </summary>
    internal static bool IsExchangeSetPath(string path) =>
        Directory.Exists(path)
        || (ExchangeSetDetection.IsZipPath(path) && ExchangeSetDetection.LooksLikeExchangeSetZip(path))
        || ExchangeSetDetection.IsS57CataloguePath(path);

    /// <summary>
    /// Collects diagnostics + an optional screenshot, then presents the
    /// feedback dialog via the ShadUI dialog manager.
    /// </summary>
    private async Task ShowFeedbackAsync()
    {
        if (_feedbackDialogFactory is null)
        {
            return;
        }

        var dialog = _feedbackDialogFactory();
        await dialog.InitializeAsync();
        _dialogManager.CreateDialog(dialog)
            .Dismissible()
            .WithMaxWidth(620)
            .Show();
    }

    private async Task ShowAboutAsync()
    {
        if (_aboutDialogFactory is null)
        {
            return;
        }

        var dialog = _aboutDialogFactory();
        _dialogManager.CreateDialog(dialog)
            .Dismissible()
            .WithMaxWidth(480)
            .Show();

        // Kick off the (throttled) update check after the dialog is shown so
        // the UI appears immediately and the network call resolves into it.
        await dialog.InitializeAsync().ConfigureAwait(true);
    }
}
