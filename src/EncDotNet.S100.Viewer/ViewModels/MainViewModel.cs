using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels.Activities;

namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class MainViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;
    private readonly IThemeService _theme;
    private readonly IRecentFilesService _recentFiles;
    private readonly IToastService _toasts;

    public FeatureCataloguesViewModel FeatureCatalogues { get; }
    public PortrayalCataloguesViewModel PortrayalCatalogues { get; }
    public DatasetsViewModel Datasets { get; }
    public CatalogPanelViewModel CatalogPanel { get; }
    public LayerStackViewModel LayerStack { get; }
    public FeatureSearchViewModel Search { get; }
    public SettingsViewModel Settings { get; }
    public PickReportViewModel PickReport { get; }
    public TimelineViewModel Timeline { get; }
    public DisplayToolbarViewModel DisplayToolbar { get; }
    public TextGroupToolbarViewModel TextToolbar { get; }
    public EcdisDisplayPanelViewModel EcdisDisplayPanel { get; }

    /// <summary>
    /// Identifier of the default tab selected on a fresh launch (and the
    /// fallback when a persisted <see cref="ViewerSettings.LastSelectedActivity"/>
    /// no longer resolves to a registered tab).
    /// </summary>
    internal const string DefaultTabId = "Datasets";

    private readonly IReadOnlyList<IActivityTab> _tabs;
    private readonly Dictionary<string, IActivityTab> _tabsById;

    /// <summary>All registered activity tabs, sorted by <see cref="IActivityTab.Order"/> ascending.</summary>
    public IReadOnlyList<IActivityTab> Tabs => _tabs;

    /// <summary>Tabs rendered in the top group of the activity bar (<see cref="IActivityTab.Order"/> &lt; 1000).</summary>
    public IReadOnlyList<IActivityTab> TopTabs { get; }

    /// <summary>Tabs pinned to the bottom of the activity bar (<see cref="IActivityTab.Order"/> &gt;= 1000).</summary>
    public IReadOnlyList<IActivityTab> BottomTabs { get; }

    private IActivityTab? _selectedTab;
    /// <summary>
    /// The active activity tab, or <c>null</c> when the pane is collapsed.
    /// Clicking the already-selected tab toggles it off (preserves the
    /// pre-refactor toggle behaviour).
    /// </summary>
    public IActivityTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            // Toggle: clicking the same tab again hides the pane.
            if (_selectedTab is { } current && ReferenceEquals(current, value))
            {
                value = null;
            }

            if (SetProperty(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(SelectedTabId));
                OnPropertyChanged(nameof(IsPaneVisible));
                OnPropertyChanged(nameof(PaneTitle));

                // Persist the last selected tab. Settings (and any future
                // transient tab) opts out via PersistAsLastSelected = false.
                if (value is null || value.PersistAsLastSelected)
                {
                    _settings.LastSelectedActivity = value?.Id;
                    _settings.Save();
                }
            }
        }
    }

    /// <summary>
    /// Id of the active tab, or <c>null</c> when no tab is selected.
    /// Bound by the activity-bar item template (via
    /// <see cref="ActiveTabConverter"/>) and used for persistence.
    /// </summary>
    public string? SelectedTabId => _selectedTab?.Id;

    public bool IsPaneVisible => _selectedTab is not null;

    public string PaneTitle => _selectedTab?.Title ?? string.Empty;

    /// <summary>
    /// Single, parameterised command bound by every activity-bar
    /// <c>ToggleButton</c> — the command parameter is the
    /// <see cref="IActivityTab"/> that owns the button.
    /// </summary>
    public ICommand SelectTabCommand { get; }

    public ICommand TogglePrimarySideBarCommand { get; }

    /// <summary>
    /// Selects the default tab (<see cref="DefaultTabId"/>), falling back
    /// to the first registered tab if the default isn't present. Used by
    /// <see cref="MainWindow"/> when a command (Open Dataset, Open Recent,
    /// Open Exchange Set, drag-drop) needs to force the Datasets pane open.
    /// </summary>
    public void SelectDefaultTab()
    {
        if (_tabsById.TryGetValue(DefaultTabId, out var defaultTab))
        {
            if (!ReferenceEquals(_selectedTab, defaultTab))
            {
                SelectedTab = defaultTab;
            }
            return;
        }

        if (_tabs.Count > 0 && !ReferenceEquals(_selectedTab, _tabs[0]))
        {
            SelectedTab = _tabs[0];
        }
    }

    /// <summary>
    /// Selects the tab with the given <see cref="IActivityTab.Id"/>, or
    /// no-ops if no such tab is registered. Convenience for tests and
    /// for callers that hold an id (e.g. settings restore) rather than
    /// the tab instance.
    /// </summary>
    public void SelectTab(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (_tabsById.TryGetValue(id, out var tab))
        {
            SelectedTab = tab;
        }
    }

    private string? _statusText;
    public string? StatusText
    {
        get => _statusPresenter?.StatusText ?? _statusText;
        set
        {
            if (_statusPresenter is { } presenter)
                presenter.StatusText = value;
            else
                SetProperty(ref _statusText, value);
        }
    }

    private bool _isStatusBarVisible;
    public bool IsStatusBarVisible
    {
        get => _isStatusBarVisible;
        set
        {
            if (SetProperty(ref _isStatusBarVisible, value))
            {
                _settings.IsStatusBarVisible = value;
                _settings.Save();
            }
        }
    }

    public ICommand ToggleStatusBarCommand { get; }

    private bool _isTimelineVisible;
    /// <summary>
    /// User preference for whether the bottom timeline panel is
    /// shown. When false, the timeline is hidden regardless of
    /// whether time-varying datasets are loaded. Persisted to
    /// <see cref="ViewerSettings"/>.
    /// </summary>
    public bool IsTimelineVisible
    {
        get => _isTimelineVisible;
        set
        {
            if (SetProperty(ref _isTimelineVisible, value))
            {
                _settings.IsTimelineVisible = value;
                _settings.Save();
            }
        }
    }

    public ICommand ToggleTimelineCommand { get; }

    private string _mouseLatLonText = LatLonFormatter.Placeholder;
    /// <summary>
    /// Lat/long of the mouse cursor when it is over the map, formatted in
    /// degrees-decimal-minutes (mariner-friendly). Set to
    /// <see cref="LatLonFormatter.Placeholder"/> when the cursor is not over
    /// the map.
    /// </summary>
    public string MouseLatLonText
    {
        get => _mouseLatLonText;
        set => SetProperty(ref _mouseLatLonText, value);
    }

    private bool _isPickPanelEnabled;
    /// <summary>
    /// User preference for whether the pick panel is allowed to auto-open
    /// when a feature is picked. Persisted to <see cref="ViewerSettings"/>.
    /// </summary>
    public bool IsPickPanelEnabled
    {
        get => _isPickPanelEnabled;
        set
        {
            if (SetProperty(ref _isPickPanelEnabled, value))
            {
                _settings.IsPickPanelVisible = value;
                _settings.Save();
                OnPropertyChanged(nameof(IsPickPanelVisible));
            }
        }
    }

    /// <summary>
    /// True when the pick panel should be displayed: a pick is active and
    /// the user hasn't disabled the panel via the View menu.
    /// </summary>
    public bool IsPickPanelVisible => _isPickPanelEnabled && PickReport.HasPick;

    public ICommand TogglePickPanelCommand { get; }

    private bool _isPickModeActive;
    /// <summary>
    /// True when the user has activated the ECDIS-style "Cursor Pick" tool.
    /// While active, single-taps on the map perform a pick and the
    /// double-tap-to-zoom gesture is suppressed. This is transient state
    /// and is not persisted between sessions.
    /// </summary>
    /// <remarks>
    /// Backed by <see cref="Tools"/>; reflects whether
    /// <see cref="MapToolController.ActiveToolId"/> is the pick tool's id.
    /// Setting this property activates / deactivates the pick tool in
    /// the controller (deactivating any other tool that was active).
    /// </remarks>
    public bool IsPickModeActive
    {
        get => _isPickModeActive;
        set
        {
            if (_isPickModeActive == value) return;
            // Drive the change through the controller so other tools are
            // properly deactivated; the controller's ActiveToolChanged
            // callback below will flip our backing flag.
            Tools.Activate(value ? PickTool.ToolId : null);
        }
    }

    public ICommand TogglePickModeCommand { get; }

    /// <summary>
    /// Convenience command that exits the active map tool (pick or measure).
    /// Wired to the <c>Esc</c> key on the main window.
    /// </summary>
    public ICommand ExitPickModeCommand { get; }

    /// <summary>
    /// True when the Measure Mode tool is active. Mirrors
    /// <see cref="MapToolController.ActiveToolId"/> just like
    /// <see cref="IsPickModeActive"/>.
    /// </summary>
    public bool IsMeasureModeActive => Tools.IsActive(MeasureTool.ToolId);

    public ICommand ToggleMeasureModeCommand { get; }

    /// <summary>
    /// Discrete tool actions wired to keyboard accelerators; current
    /// active tool decides what (if anything) to do.
    /// </summary>
    public ICommand ToolCommitCommand { get; }
    public ICommand ToolBackstepCommand { get; }

    /// <summary>
    /// Status-bar summary owned by the active tool (e.g. measure-leg /
    /// total). <c>null</c> when no tool is active or the tool has nothing
    /// to display.
    /// </summary>
    public string? MeasureSummary
    {
        get => _measureSummary;
        set => SetProperty(ref _measureSummary, value);
    }
    private string? _measureSummary;

    /// <summary>
    /// Map-tool registry / dispatcher. Tools (pick, measure) are
    /// registered from the constructor so view-model-level commands and
    /// tests work without the host having to register anything.
    /// <see cref="MapToolController.Initialize"/> is called by the view
    /// layer once the visual tree is up.
    /// </summary>
    public MapToolController Tools { get; } = new();

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set => SetProperty(ref _isDarkTheme, value);
    }

    public ICommand ToggleThemeCommand { get; }

    // ─── Exchange-set progress + banner (es3-progress) ────────────────────

    private bool _isExchangeSetLoading;
    private double _exchangeSetProgressFraction;
    private string? _exchangeSetCurrentDataset;
    private string? _exchangeSetCounter;
    private string? _exchangeSetSourceLabel;
    private bool _isExchangeSetBannerVisible;
    private string? _exchangeSetBannerMessage;
    private System.Threading.CancellationTokenSource? _exchangeSetCts;

    /// <summary>
    /// True while an exchange-set load is in flight. Bound to the
    /// progress overlay's <c>IsVisible</c>.
    /// </summary>
    public bool IsExchangeSetLoading
    {
        get => _isExchangeSetLoading;
        private set => SetProperty(ref _isExchangeSetLoading, value);
    }

    /// <summary>
    /// Progress fraction in <c>[0, 1]</c> — bound to a determinate
    /// <see cref="Avalonia.Controls.ProgressBar"/>.
    /// </summary>
    public double ExchangeSetProgressFraction
    {
        get => _exchangeSetProgressFraction;
        private set => SetProperty(ref _exchangeSetProgressFraction, value);
    }

    /// <summary>Catalogue-relative path of the dataset currently being routed.</summary>
    public string? ExchangeSetCurrentDataset
    {
        get => _exchangeSetCurrentDataset;
        private set => SetProperty(ref _exchangeSetCurrentDataset, value);
    }

    /// <summary>"3 of 12" counter shown in the overlay header.</summary>
    public string? ExchangeSetCounter
    {
        get => _exchangeSetCounter;
        private set => SetProperty(ref _exchangeSetCounter, value);
    }

    /// <summary>The folder or .zip path being opened — shown in the overlay.</summary>
    public string? ExchangeSetSourceLabel
    {
        get => _exchangeSetSourceLabel;
        private set => SetProperty(ref _exchangeSetSourceLabel, value);
    }

    /// <summary>Cancels the in-flight exchange-set load. No-op if idle.</summary>
    public ICommand CancelExchangeSetCommand { get; }

    /// <summary>True while a partial-failure / fatal-error banner is shown.</summary>
    public bool IsExchangeSetBannerVisible
    {
        get => _isExchangeSetBannerVisible;
        private set => SetProperty(ref _isExchangeSetBannerVisible, value);
    }

    /// <summary>Banner body text (already localised + formatted).</summary>
    public string? ExchangeSetBannerMessage
    {
        get => _exchangeSetBannerMessage;
        private set => SetProperty(ref _exchangeSetBannerMessage, value);
    }

    /// <summary>Hides the partial-failure banner.</summary>
    public ICommand DismissExchangeSetBannerCommand { get; }

    /// <summary>
    /// Called by <see cref="MainWindow"/> when an exchange-set load is
    /// about to start. Captures the cancellation source, resets progress
    /// state, and clears any leftover banner.
    /// </summary>
    internal System.Threading.CancellationToken BeginExchangeSetLoad(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        _exchangeSetCts?.Dispose();
        _exchangeSetCts = new System.Threading.CancellationTokenSource();

        IsExchangeSetBannerVisible = false;
        ExchangeSetBannerMessage = null;
        ExchangeSetSourceLabel = sourcePath;
        ExchangeSetCurrentDataset = null;
        ExchangeSetCounter = null;
        ExchangeSetProgressFraction = 0;
        IsExchangeSetLoading = true;
        return _exchangeSetCts.Token;
    }

    /// <summary>Pushes a single progress update from the service into the VM.</summary>
    internal void ReportExchangeSetProgress(ExchangeSetProgress progress)
    {
        ExchangeSetCurrentDataset = progress.CurrentDataset;
        ExchangeSetCounter = progress.Total > 0
            ? string.Format(Strings.Progress_ExchangeSetCounter, progress.Completed, progress.Total)
            : null;
        ExchangeSetProgressFraction = progress.Total > 0
            ? Math.Clamp((double)progress.Completed / progress.Total, 0.0, 1.0)
            : 0.0;
    }

    /// <summary>
    /// Called by <see cref="MainWindow"/> when an exchange-set load
    /// completes (or is cancelled / fatally fails). Hides the overlay
    /// and surfaces a banner if there is anything worth flagging.
    /// </summary>
    internal void EndExchangeSetLoad(ExchangeSetOpenResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        IsExchangeSetLoading = false;
        ExchangeSetCurrentDataset = null;

        if (result.FailureMessage is { } message && !result.CatalogueNotFound)
        {
            ExchangeSetBannerMessage = string.Format(
                Strings.Banner_ExchangeSetFailed, result.SourcePath, message);
            IsExchangeSetBannerVisible = true;
        }
        else if (result.SkippedUnsupported > 0)
        {
            ExchangeSetBannerMessage = string.Format(
                Strings.Banner_ExchangeSetPartial,
                result.SkippedUnsupported, result.Total, result.SourcePath, result.SkippedUnsupported);
            IsExchangeSetBannerVisible = true;
        }
    }

    /// <summary>
    /// Opens a dataset that the user has previously loaded. If the file is
    /// no longer at the recorded path the entry is dropped from the recent
    /// list and a status message is shown.
    /// </summary>
    public IAsyncRelayCommand<string> OpenRecentCommand { get; }

    private readonly IStatusPresenter? _statusPresenter;
    private readonly McpServerHost? _mcpServerHost;
    private EncDotNet.S100.Mcp.S100McpServer? _attachedMcpServer;

    /// <summary>True when the embedded MCP server is currently listening.</summary>
    public bool IsMcpRunning =>
        _mcpServerHost?.Server is { IsRunning: true };

    /// <summary>
    /// Status-bar text describing the MCP server. Empty when not running.
    /// </summary>
    public string McpStatusText
    {
        get
        {
            var server = _mcpServerHost?.Server;
            if (server is null || !server.IsRunning || server.Port is null)
                return string.Empty;
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Strings.Status_McpRunning,
                server.Port,
                server.ConnectionCount);
        }
    }

    /// <summary>Tooltip describing the MCP server endpoint.</summary>
    public string McpTooltipText
    {
        get
        {
            var server = _mcpServerHost?.Server;
            if (server?.Endpoint is null)
                return string.Empty;
            return string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Strings.Tooltip_McpEndpoint,
                server.Endpoint) + " " + Strings.Tooltip_McpLoopback;
        }
    }

    private void AttachToMcpServer()
    {
        if (_attachedMcpServer is { } prev)
        {
            prev.StateChanged -= OnMcpServerChanged;
            prev.ConnectionsChanged -= OnMcpServerChanged;
        }
        _attachedMcpServer = _mcpServerHost?.Server;
        if (_attachedMcpServer is { } next)
        {
            next.StateChanged += OnMcpServerChanged;
            next.ConnectionsChanged += OnMcpServerChanged;
        }
        OnMcpServerChanged(null, EventArgs.Empty);
    }

    private void OnMcpServerChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(IsMcpRunning));
            OnPropertyChanged(nameof(McpStatusText));
            OnPropertyChanged(nameof(McpTooltipText));
        });
    }

    private void OnMcpPortConflict(object? sender, McpPortConflictEventArgs e)
    {
        var conflictPort = e.Port;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _toasts.ShowError(
                title: Strings.Toast_McpPortConflictTitle,
                content: string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Strings.Toast_McpPortConflictBody,
                    conflictPort),
                actionLabel: Strings.Toast_McpPortFindAnother,
                action: () => _ = FindAnotherMcpPortAsync(),
                sticky: true);
        });
    }

    private async Task FindAnotherMcpPortAsync()
    {
        if (_mcpServerHost is not { } host) return;

        int? newPort;
        try
        {
            newPort = await host.ResetPortAsync().ConfigureAwait(false);
        }
        catch
        {
            newPort = null;
        }

        if (newPort is { } port)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _toasts.ShowInfo(
                    title: Strings.Toast_McpPortReassignedTitle,
                    content: string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Strings.Toast_McpPortReassignedBody,
                        port));
            });
        }
    }

    public MainViewModel(
        ViewerSettings settings,
        FeatureCataloguesViewModel featureCatalogues,
        PortrayalCataloguesViewModel portrayalCatalogues,
        DatasetsViewModel datasets,
        CatalogPanelViewModel catalogPanel,
        LayerStackViewModel layerStack,
        FeatureSearchViewModel search,
        SettingsViewModel settingsViewModel,
        PickReportViewModel pickReport,
        TimelineViewModel timeline,
        DisplayToolbarViewModel displayToolbar,
        TextGroupToolbarViewModel textToolbar,
        EcdisDisplayPanelViewModel ecdisDisplayPanel,
        IThemeService themeService,
        IRecentFilesService recentFiles,
        IMeasureOverlayAppearanceProvider measureAppearance,
        IToastService toasts,
        IEnumerable<IActivityTab>? activityTabs = null,
        McpServerHost? mcpServerHost = null,
        IStatusPresenter? statusPresenter = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(featureCatalogues);
        ArgumentNullException.ThrowIfNull(portrayalCatalogues);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(catalogPanel);
        ArgumentNullException.ThrowIfNull(layerStack);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        ArgumentNullException.ThrowIfNull(pickReport);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(displayToolbar);
        ArgumentNullException.ThrowIfNull(textToolbar);
        ArgumentNullException.ThrowIfNull(ecdisDisplayPanel);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(measureAppearance);
        ArgumentNullException.ThrowIfNull(toasts);

        _settings = settings;
        _theme = themeService;
        _recentFiles = recentFiles;
        _toasts = toasts;
        _isDarkTheme = themeService.IsDarkTheme;
        _statusPresenter = statusPresenter;
        _mcpServerHost = mcpServerHost;
        if (_mcpServerHost is { } host)
        {
            host.ServerChanged += (_, _) => AttachToMcpServer();
            host.McpPortConflict += OnMcpPortConflict;
            AttachToMcpServer();
        }
        if (_statusPresenter is { } presenter)
        {
            presenter.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(IStatusPresenter.StatusText))
                    OnPropertyChanged(nameof(StatusText));
            };
        }

        Tools.Register(new PickTool());
        Tools.Register(new MeasureTool(measureAppearance));

        FeatureCatalogues = featureCatalogues;
        PortrayalCatalogues = portrayalCatalogues;
        Datasets = datasets;
        CatalogPanel = catalogPanel;
        LayerStack = layerStack;
        Search = search;
        Settings = settingsViewModel;
        PickReport = pickReport;
        Timeline = timeline;
        DisplayToolbar = displayToolbar;
        TextToolbar = textToolbar;
        EcdisDisplayPanel = ecdisDisplayPanel;
        Timeline.CloseRequested += () => IsTimelineVisible = false;
        PickReport.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PickReportViewModel.HasPick))
                OnPropertyChanged(nameof(IsPickPanelVisible));
        };

        // Activity tab registry — ordered by IActivityTab.Order ascending.
        // Tabs with Order >= 1000 are pinned to the bottom of the activity
        // bar (currently only Settings) so the visual layout matches the
        // pre-refactor DockPanel arrangement.
        _tabs = (activityTabs ?? Array.Empty<IActivityTab>())
            .OrderBy(t => t.Order)
            .ToArray();
        _tabsById = _tabs.ToDictionary(t => t.Id, StringComparer.Ordinal);
        TopTabs = _tabs.Where(t => t.Order < 1000).ToArray();
        BottomTabs = _tabs.Where(t => t.Order >= 1000).ToArray();

        SelectTabCommand = new RelayCommand<IActivityTab>(tab =>
        {
            if (tab is not null) SelectedTab = tab;
        });

        TogglePrimarySideBarCommand = new RelayCommand(() =>
        {
            if (_selectedTab is not null)
            {
                SelectedTab = null;
                return;
            }

            // Re-open with the last persisted tab, falling back to the
            // default tab (Datasets) if the persisted id is no longer
            // registered.
            if (settings.LastSelectedActivity is { } last
                && _tabsById.TryGetValue(last, out var restored))
            {
                SelectedTab = restored;
            }
            else
            {
                SelectDefaultTab();
            }
        });

        _isStatusBarVisible = settings.IsStatusBarVisible;

        ToggleStatusBarCommand = new RelayCommand(() => IsStatusBarVisible = !IsStatusBarVisible);

        _isTimelineVisible = settings.IsTimelineVisible;
        ToggleTimelineCommand = new RelayCommand(() => IsTimelineVisible = !IsTimelineVisible);

        _isPickPanelEnabled = settings.IsPickPanelVisible;
        TogglePickPanelCommand = new RelayCommand(() => IsPickPanelEnabled = !IsPickPanelEnabled);

        TogglePickModeCommand = new RelayCommand(() => Tools.Toggle(PickTool.ToolId));
        ExitPickModeCommand = new RelayCommand(() => Tools.Activate(null));
        ToggleMeasureModeCommand = new RelayCommand(() => Tools.Toggle(MeasureTool.ToolId));
        ToolCommitCommand = new RelayCommand(
            () => Tools.OnAction(MapToolAction.Commit),
            () => Tools.ActiveTool != null);
        ToolBackstepCommand = new RelayCommand(
            () => Tools.OnAction(MapToolAction.Backstep),
            () => Tools.ActiveTool != null);

        // Mirror the controller's active-tool changes onto the legacy
        // IsPickModeActive flag (kept so existing XAML/cursor wiring
        // continues to function) and the IsMeasureModeActive property.
        // Also re-evaluate Commit/Backstep CanExecute so that the
        // window-level Enter/Backspace key bindings only consume those
        // keys while a tool is actually active — without this guard
        // typing Backspace in any TextBox is swallowed by the binding.
        Tools.ActiveToolChanged += _ =>
        {
            var newPick = Tools.IsActive(PickTool.ToolId);
            if (_isPickModeActive != newPick)
            {
                _isPickModeActive = newPick;
                OnPropertyChanged(nameof(IsPickModeActive));
            }
            OnPropertyChanged(nameof(IsMeasureModeActive));
            ((RelayCommand)ToolCommitCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ToolBackstepCommand).NotifyCanExecuteChanged();
        };

        ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = _theme.ToggleTheme());

        CancelExchangeSetCommand = new RelayCommand(
            () => _exchangeSetCts?.Cancel(),
            () => IsExchangeSetLoading);
        DismissExchangeSetBannerCommand = new RelayCommand(
            () => IsExchangeSetBannerVisible = false);

        // Re-evaluate Cancel command when the loading flag changes so the
        // overlay button enables/disables correctly.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsExchangeSetLoading))
                ((RelayCommand)CancelExchangeSetCommand).NotifyCanExecuteChanged();
        };

        OpenRecentCommand = new AsyncRelayCommand<string>(OpenRecentAsync);

        // Restore last selected tab. We assign _selectedTab directly so
        // restoration doesn't re-write settings. If the persisted id is
        // missing or stale, fall back to the default tab (Datasets), then
        // defensively to the first registered tab — matching the spec.
        if (settings.LastSelectedActivity is { } lastId
            && _tabsById.TryGetValue(lastId, out var restoredTab))
        {
            _selectedTab = restoredTab;
        }
        else if (_tabsById.TryGetValue(DefaultTabId, out var defaultTab))
        {
            _selectedTab = defaultTab;
        }
        else if (_tabs.Count > 0)
        {
            _selectedTab = _tabs[0];
        }
    }

    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!File.Exists(path))
        {
            var msg = string.Format(Strings.Status_FileNoLongerExists, path);
            StatusText = msg;
            _toasts.ShowWarning(Strings.Toast_Warning, msg);
            // Drop the missing entry so the menu reflects reality.
            _recentFiles.Remove(path);
            return;
        }

        SelectDefaultTab();
        await Datasets.LoadFromPathAsync(path);
    }
}
