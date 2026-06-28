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
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels.Activities;
using ShadUI;
namespace EncDotNet.S100.Viewer.ViewModels;

internal sealed class MainViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;
    private readonly IThemeService _theme;
    private readonly IRecentFilesService _recentFiles;
    private readonly INotificationService _notifications;
    private readonly RoutesService _routes;
    private readonly MeasureTool _measureTool;
    private readonly RouteEditTool _routeEditTool;
    private readonly ShadUI.DialogManager _dialogManager;
    private readonly Func<FeedbackDialogViewModel>? _feedbackDialogFactory;
    private readonly Func<AboutDialogViewModel>? _aboutDialogFactory;

    /// <summary>
    /// The persistent MCP port-conflict notification, held so the
    /// "find another port" outcome updates it in place rather than
    /// stacking a second notification.
    /// </summary>
    private INotificationHandle? _mcpPortConflictNotification;

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

    /// <summary>All tabs that live in the left dock (the activity pane), sorted by <see cref="IActivityTab.Order"/>.</summary>
    public IReadOnlyList<IActivityTab> LeftTabs { get; }

    /// <summary>All tabs that live in the right dock (currently just Pick Report).</summary>
    public IReadOnlyList<IActivityTab> RightTabs { get; }

    /// <summary>All tabs that live in the bottom dock (currently just Timeline).</summary>
    public IReadOnlyList<IActivityTab> BottomTabs { get; }

    /// <summary>Left-dock tabs rendered in the top group of the activity bar (<see cref="IActivityTab.Order"/> &lt; 1000).</summary>
    public IReadOnlyList<IActivityTab> LeftDockTopTabs { get; }

    /// <summary>Left-dock tabs pinned to the bottom of the activity bar (<see cref="IActivityTab.Order"/> &gt;= 1000).</summary>
    public IReadOnlyList<IActivityTab> LeftDockBottomTabs { get; }

    private IActivityTab? _selectedLeftTab;
    /// <summary>
    /// The active tab in the left dock. Setting this to the currently
    /// selected tab toggles <see cref="IsLeftDockOpen"/> off (preserves
    /// the pre-PR-M4 toggle behaviour). Setting it to a different tab
    /// always opens the dock.
    /// </summary>
    public IActivityTab? SelectedLeftTab
    {
        get => _selectedLeftTab;
        set
        {
            // Toggle: clicking the already-selected tab while the dock is
            // open closes it without changing the selection (so re-opening
            // restores the same tab).
            if (value is not null
                && _isLeftDockOpen
                && _selectedLeftTab is { } current
                && ReferenceEquals(current, value))
            {
                IsLeftDockOpen = false;
                return;
            }

            var newValue = value ?? _selectedLeftTab;
            if (!ReferenceEquals(_selectedLeftTab, newValue))
            {
                _selectedLeftTab = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedLeftTabId));
                OnPropertyChanged(nameof(LeftDockTitle));

                if (newValue is not null && newValue.PersistAsLastSelected)
                {
                    _settings.LastSelectedActivity = newValue.Id;
                    _settings.Save();
                }
            }

            if (value is not null && !_isLeftDockOpen)
            {
                IsLeftDockOpen = true;
            }
        }
    }

    /// <summary>
    /// Id of the active left-dock tab, or <c>null</c> when no left-dock
    /// tab is registered. Bound by the activity-bar item template (via
    /// <see cref="ActiveTabConverter"/>) and used for persistence.
    /// </summary>
    public string? SelectedLeftTabId => _selectedLeftTab?.Id;

    private IActivityTab? _selectedRightTab;
    /// <summary>Active tab in the right dock; setting it opens the dock.</summary>
    public IActivityTab? SelectedRightTab
    {
        get => _selectedRightTab;
        set
        {
            if (!ReferenceEquals(_selectedRightTab, value))
            {
                _selectedRightTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedRightTabId));
                OnPropertyChanged(nameof(RightDockTitle));

                // PR-M3: persist the selected right-dock tab id so the
                // user's last choice survives restart. Both user clicks
                // and auto-open driven changes flow through this setter,
                // so both write to settings — matching the M2/M4 design.
                if (_settingsInitialized)
                {
                    _settings.LastSelectedRightTab = value?.Id;
                    _settings.Save();
                }
            }
            if (value is not null && !_isRightDockOpen) IsRightDockOpen = true;
        }
    }

    /// <summary>Id of the active right-dock tab, or <c>null</c> when none.</summary>
    public string? SelectedRightTabId => _selectedRightTab?.Id;

    private IActivityTab? _selectedBottomTab;
    /// <summary>Active tab in the bottom dock; setting it opens the dock.</summary>
    public IActivityTab? SelectedBottomTab
    {
        get => _selectedBottomTab;
        set
        {
            if (!ReferenceEquals(_selectedBottomTab, value))
            {
                _selectedBottomTab = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedBottomTabId));
                OnPropertyChanged(nameof(BottomDockTitle));

                if (_settingsInitialized)
                {
                    _settings.LastSelectedBottomTab = value?.Id;
                    _settings.Save();
                }
            }
            if (value is not null && !_isBottomDockOpen) IsBottomDockOpen = true;
        }
    }

    /// <summary>Id of the active bottom-dock tab, or <c>null</c> when none.</summary>
    public string? SelectedBottomTabId => _selectedBottomTab?.Id;

    private bool _isLeftDockOpen;
    /// <summary>
    /// True when the left dock (activity pane) is shown. Persisted in
    /// <see cref="ViewerSettings.IsLeftDockOpen"/> (PR-M3).
    /// </summary>
    public bool IsLeftDockOpen
    {
        get => _isLeftDockOpen;
        set
        {
            if (SetProperty(ref _isLeftDockOpen, value) && _settingsInitialized)
            {
                _settings.IsLeftDockOpen = value;
                _settings.Save();
            }
        }
    }

    private bool _isRightDockOpen;
    /// <summary>
    /// True when the right dock is shown. Persisted in
    /// <see cref="ViewerSettings.IsRightDockOpen"/> (PR-M3).
    /// </summary>
    public bool IsRightDockOpen
    {
        get => _isRightDockOpen;
        set
        {
            if (SetProperty(ref _isRightDockOpen, value) && _settingsInitialized)
            {
                _settings.IsRightDockOpen = value;
                _settings.Save();
            }
        }
    }

    private bool _isBottomDockOpen;
    /// <summary>
    /// True when the bottom dock is shown. Persisted in
    /// <see cref="ViewerSettings.IsBottomDockOpen"/> (PR-M3).
    /// </summary>
    public bool IsBottomDockOpen
    {
        get => _isBottomDockOpen;
        set
        {
            if (SetProperty(ref _isBottomDockOpen, value) && _settingsInitialized)
            {
                _settings.IsBottomDockOpen = value;
                _settings.Save();
            }
        }
    }

    /// <summary>Pane header text for the left dock chrome.</summary>
    public string LeftDockTitle => _selectedLeftTab?.Title ?? string.Empty;

    /// <summary>Pane header text for the right dock chrome.</summary>
    public string RightDockTitle => _selectedRightTab?.Title ?? string.Empty;

    /// <summary>Pane header text for the bottom dock chrome.</summary>
    public string BottomDockTitle => _selectedBottomTab?.Title ?? string.Empty;

    // ─── PR-M3: persisted panel sizes ─────────────────────────────────
    // All sizes are absolute pixels except the two inner-split values
    // which are fractions in [0, 1]. The properties write through a
    // shared DebouncedSettingsSaver so rapid splitter drags coalesce
    // into a single disk write 500 ms after the last move. Flush() is
    // called from MainWindow.Closed (see OnShutdown).

    private double? _leftDockSavedWidth;
    /// <summary>Persisted absolute pixel width of the left dock (PR-M3).</summary>
    public double? LeftDockSavedWidth
    {
        get => _leftDockSavedWidth;
        set
        {
            if (SetProperty(ref _leftDockSavedWidth, value) && _settingsInitialized)
            {
                _settings.Panels.LeftDockWidth = value;
                _sizeSaver.RequestSave();
            }
        }
    }

    private double? _rightDockSavedWidth;
    /// <summary>Persisted absolute pixel width of the right dock (PR-M3).</summary>
    public double? RightDockSavedWidth
    {
        get => _rightDockSavedWidth;
        set
        {
            if (SetProperty(ref _rightDockSavedWidth, value) && _settingsInitialized)
            {
                _settings.Panels.RightDockWidth = value;
                _sizeSaver.RequestSave();
            }
        }
    }

    private double? _bottomDockSavedHeight;
    /// <summary>Persisted absolute pixel height of the bottom dock (PR-M3).</summary>
    public double? BottomDockSavedHeight
    {
        get => _bottomDockSavedHeight;
        set
        {
            if (SetProperty(ref _bottomDockSavedHeight, value) && _settingsInitialized)
            {
                _settings.Panels.BottomDockHeight = value;
                _sizeSaver.RequestSave();
            }
        }
    }

    private double? _datasetsInnerSplit;
    /// <summary>Persisted fraction <c>[0,1]</c> of the Datasets-tab master/detail splitter (PR-M3).</summary>
    public double? DatasetsInnerSplit
    {
        get => _datasetsInnerSplit;
        set
        {
            if (SetProperty(ref _datasetsInnerSplit, value) && _settingsInitialized)
            {
                _settings.Panels.DatasetsInnerSplit = value;
                _sizeSaver.RequestSave();
            }
        }
    }

    private double? _catalogInnerSplit;
    /// <summary>Persisted fraction <c>[0,1]</c> of the Catalog-tab master/detail splitter (PR-M3).</summary>
    public double? CatalogInnerSplit
    {
        get => _catalogInnerSplit;
        set
        {
            if (SetProperty(ref _catalogInnerSplit, value) && _settingsInitialized)
            {
                _settings.Panels.CatalogInnerSplit = value;
                _sizeSaver.RequestSave();
            }
        }
    }

    private double? _vesselsInnerSplit;
    /// <summary>Persisted fraction <c>[0,1]</c> of the Vessels-tab master/detail splitter.</summary>
    public double? VesselsInnerSplit
    {
        get => _vesselsInnerSplit;
        set
        {
            if (SetProperty(ref _vesselsInnerSplit, value) && _settingsInitialized)
            {
                _settings.Panels.VesselsInnerSplit = value;
                _sizeSaver.RequestSave();
            }
        }
    }

    private readonly DebouncedSettingsSaver _sizeSaver;

    /// <summary>
    /// True once the constructor finishes hydrating from settings — used by
    /// every persisted property setter to suppress the write-back that would
    /// otherwise happen during initial assignment. Hosts call
    /// <see cref="OnShutdown"/> on application exit to flush any pending
    /// debounced size writes.
    /// </summary>
    private bool _settingsInitialized;

    /// <summary>
    /// Called by the host (MainWindow on Closed) so the debounced size
    /// saver flushes its last pending write before the process exits.
    /// </summary>
    public void OnShutdown()
    {
        _sizeSaver.Flush();
    }

    /// <summary>
    /// Single, parameterised command bound by every activity-bar
    /// <c>ToggleButton</c> — the command parameter is the
    /// <see cref="IActivityTab"/> that owns the button.
    /// </summary>
    /// <summary>
    /// Single, parameterised command bound by every activity-bar
    /// <c>ToggleButton</c> — the command parameter is the
    /// <see cref="IActivityTab"/> that owns the button. Selecting routes
    /// to the dock that owns the tab.
    /// </summary>
    public ICommand SelectTabCommand { get; }

    /// <summary>Closes the dock identified by the command parameter (a <see cref="TabDock"/>).</summary>
    public ICommand CloseDockCommand { get; }

    public ICommand TogglePrimarySideBarCommand { get; }

    /// <summary>
    /// Selects the default left-dock tab (<see cref="DefaultTabId"/>),
    /// falling back to the first registered left-dock tab if the default
    /// isn't present. Opens the left dock as a side-effect. Used by
    /// <see cref="MainWindow"/> when a command (Open Dataset, Open Recent,
    /// Open Exchange Set, drag-drop) needs to force the Datasets pane open.
    /// </summary>
    public void SelectDefaultTab()
    {
        if (_tabsById.TryGetValue(DefaultTabId, out var defaultTab)
            && defaultTab.Dock == TabDock.Left)
        {
            SelectedLeftTab = defaultTab;
            IsLeftDockOpen = true;
            return;
        }

        var firstLeft = LeftTabs.FirstOrDefault(t => t.IsVisible);
        if (firstLeft is not null)
        {
            SelectedLeftTab = firstLeft;
            IsLeftDockOpen = true;
        }
    }

    /// <summary>
    /// Selects the tab with the given <see cref="IActivityTab.Id"/>, or
    /// no-ops if no such tab is registered. Routes to whichever dock
    /// owns the tab.
    /// </summary>
    public void SelectTab(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (_tabsById.TryGetValue(id, out var tab))
        {
            RouteSelection(tab);
        }
    }

    private void RouteSelection(IActivityTab tab)
    {
        // Never route selection to a hidden tab (e.g. the Helm tab while
        // own-vessel tracking is disabled). The activity-bar button is
        // hidden by binding, but selection can also be driven
        // programmatically (restore, default, content auto-open).
        if (!tab.IsVisible) return;

        switch (tab.Dock)
        {
            case TabDock.Left: SelectedLeftTab = tab; break;
            case TabDock.Right: SelectedRightTab = tab; break;
            case TabDock.Bottom: SelectedBottomTab = tab; break;
        }
    }

    /// <summary>
    /// Reacts to a tab's <see cref="IActivityTab.IsVisible"/> toggling. When
    /// a now-hidden tab is the selected tab in its dock, moves selection to
    /// the first remaining visible tab in that dock, or clears the
    /// selection and closes the dock when none remain. System-driven, so it
    /// does not persist <see cref="ViewerSettings.LastSelectedActivity"/>.
    /// </summary>
    private void OnTabVisibilityChanged(IActivityTab tab)
    {
        if (tab.IsVisible) return;

        switch (tab.Dock)
        {
            case TabDock.Left when ReferenceEquals(_selectedLeftTab, tab):
            {
                var next = LeftTabs.FirstOrDefault(t => t.IsVisible);
                if (next is not null)
                {
                    SetLeftSelectionSystem(next);
                }
                else
                {
                    SetLeftSelectionSystem(null);
                    IsLeftDockOpen = false;
                }
                break;
            }
            case TabDock.Right when ReferenceEquals(_selectedRightTab, tab):
                SelectedRightTab = RightTabs.FirstOrDefault(t => t.IsVisible);
                break;
            case TabDock.Bottom when ReferenceEquals(_selectedBottomTab, tab):
                SelectedBottomTab = BottomTabs.FirstOrDefault(t => t.IsVisible);
                break;
        }
    }

    /// <summary>
    /// Sets the left-dock selection without persisting
    /// <see cref="ViewerSettings.LastSelectedActivity"/> and without forcing
    /// the dock open — used for system-driven fix-ups (a selected tab being
    /// hidden), not user clicks.
    /// </summary>
    private void SetLeftSelectionSystem(IActivityTab? tab)
    {
        if (ReferenceEquals(_selectedLeftTab, tab)) return;
        _selectedLeftTab = tab;
        OnPropertyChanged(nameof(SelectedLeftTab));
        OnPropertyChanged(nameof(SelectedLeftTabId));
        OnPropertyChanged(nameof(LeftDockTitle));
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

    /// <summary>
    /// Toggles the bottom dock open/closed. Kept for the existing
    /// View menu binding; in PR-M4 the dock contains the Timeline tab.
    /// </summary>
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

    /// <summary>
    /// Toggles the right dock open/closed. Kept for the existing
    /// View menu binding; in PR-M4 the dock contains the Pick Report tab.
    /// </summary>
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
    /// The application-scoped editable route store. Exposed so the host
    /// window can drive the persistent route overlay and the routes side
    /// panel can bind to it.
    /// </summary>
    public RoutesService RoutesService => _routes;

    /// <summary>
    /// True when the interactive route editor tool is active. Mirrors
    /// <see cref="MapToolController.ActiveToolId"/> like
    /// <see cref="IsMeasureModeActive"/>.
    /// </summary>
    public bool IsRouteEditModeActive => Tools.IsActive(RouteEditTool.ToolId);

    /// <summary>
    /// True when the tool-owned status summary should be shown — i.e. while
    /// either Measure Mode or Route Edit Mode is active.
    /// </summary>
    public bool IsToolSummaryVisible => IsMeasureModeActive || IsRouteEditModeActive;

    /// <summary>Toggles the interactive route editor tool on/off.</summary>
    public ICommand ToggleRouteEditModeCommand { get; }

    /// <summary>
    /// Activates the interactive route editor tool. Unlike
    /// <see cref="ToggleRouteEditModeCommand"/> this never turns the tool
    /// off, so it is safe to bind to an explicit "edit" affordance.
    /// </summary>
    public ICommand EnterRouteEditModeCommand { get; }

    /// <summary>
    /// Deactivates the interactive route editor tool when it is active.
    /// Bound to the Routes panel's "Done" action so finishing an edit
    /// returns the map to its default (no-tool) state.
    /// </summary>
    public ICommand ExitRouteEditModeCommand { get; }

    /// <summary>
    /// Promotes the current Measure Mode path into a new editable route and
    /// switches to the route editor. Enabled only when the measurement has
    /// at least two waypoints.
    /// </summary>
    public ICommand PromoteMeasurementToRouteCommand { get; }

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

    /// <summary>
    /// Copies the current Measure Mode path into a brand-new editable route,
    /// makes it the active route, and switches to the route editor tool so
    /// the user can immediately refine it.
    /// </summary>
    private void PromoteMeasurementToRoute()
    {
        var waypoints = _measureTool.State.Waypoints;
        if (waypoints.Count < 2)
            return;

        var name = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            Strings.Routes_NewRouteNameFormat,
            _routes.Routes.Routes.Count + 1);
        var route = _routes.Routes.CreateRoute(name);
        foreach (var (lat, lon) in waypoints)
            route.AppendWaypoint(new EncDotNet.S100.DataModel.GeoPosition(lat, lon));

        _routes.SelectedWaypointIndex = null;
        Tools.Activate(RouteEditTool.ToolId);
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
            _mcpPortConflictNotification = _notifications
                .Create(Strings.Toast_McpPortConflictTitle)
                .WithSeverity(NotificationSeverity.Error)
                .WithContent(string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Strings.Toast_McpPortConflictBody,
                    conflictPort))
                .WithAction(
                    Strings.Toast_McpPortFindAnother,
                    () => _ = FindAnotherMcpPortAsync(),
                    dismissOnInvoke: false)
                .Persistent()
                .Show();
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
            // Reuse the conflict notification: transition it in place to a
            // success message that auto-dismisses, rather than stacking a
            // second notification.
            var handle = _mcpPortConflictNotification;
            _mcpPortConflictNotification = null;
            if (handle is not null && !handle.IsDismissed)
            {
                handle.SetActions();
                handle.Update(
                    title: Strings.Toast_McpPortReassignedTitle,
                    message: string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Strings.Toast_McpPortReassignedBody,
                        port),
                    severity: NotificationSeverity.Info);
                handle.ScheduleAutoDismiss(
                    NotificationService.DefaultDelayFor(NotificationSeverity.Info));
            }
            else
            {
                _notifications.Create(Strings.Toast_McpPortReassignedTitle)
                    .WithSeverity(NotificationSeverity.Info)
                    .WithContent(string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        Strings.Toast_McpPortReassignedBody,
                        port))
                    .Show();
            }
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
        INotificationService notifications,
        ShadUI.DialogManager? dialogManager = null,
        Func<FeedbackDialogViewModel>? feedbackDialogFactory = null,
        Func<AboutDialogViewModel>? aboutDialogFactory = null,
        IEnumerable<IActivityTab>? activityTabs = null,
        McpServerHost? mcpServerHost = null,
        IStatusPresenter? statusPresenter = null,
        RoutesService? routes = null)
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
        ArgumentNullException.ThrowIfNull(notifications);

        _settings = settings;
        _theme = themeService;
        _recentFiles = recentFiles;
        _notifications = notifications;
        // The dialog manager is supplied by DI in the running app; tests
        // that construct the view-model directly omit it, so fall back to
        // an unhosted manager to keep the DialogManager property non-null.
        _dialogManager = dialogManager ?? new ShadUI.DialogManager();
        _feedbackDialogFactory = feedbackDialogFactory;
        _aboutDialogFactory = aboutDialogFactory;
        DialogManager = _dialogManager;
        _isDarkTheme = themeService.IsDarkTheme;
        _statusPresenter = statusPresenter;
        _mcpServerHost = mcpServerHost;
        _routes = routes ?? new RoutesService();

        // PR-M3: debounced settings writer used by the splitter-size
        // setters. 500 ms window coalesces rapid drag updates into one
        // disk write; MainWindow.Closed calls OnShutdown → Flush so the
        // last drag is never lost.
        _sizeSaver = new DebouncedSettingsSaver(
            save: () => { try { _settings.Save(); } catch { /* best-effort */ } },
            dispatch: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
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

        _measureTool = new MeasureTool(measureAppearance);
        _routeEditTool = new RouteEditTool(_routes);
        Tools.Register(new PickTool());
        Tools.Register(_measureTool);
        Tools.Register(_routeEditTool);

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
        Timeline.CloseRequested += () => IsBottomDockOpen = false;

        // Activity tab registry — partitioned by Dock, ordered by
        // IActivityTab.Order ascending. Left-dock tabs further split into
        // top/bottom groups by the legacy <1000 / >=1000 convention so
        // Settings stays pinned to the bottom of the activity bar.
        _tabs = (activityTabs ?? Array.Empty<IActivityTab>())
            .OrderBy(t => t.Order)
            .ToArray();
        _tabsById = _tabs.ToDictionary(t => t.Id, StringComparer.Ordinal);
        LeftTabs = _tabs.Where(t => t.Dock == TabDock.Left).ToArray();
        RightTabs = _tabs.Where(t => t.Dock == TabDock.Right).ToArray();
        BottomTabs = _tabs.Where(t => t.Dock == TabDock.Bottom).ToArray();
        LeftDockTopTabs = LeftTabs.Where(t => t.Order < 1000).ToArray();
        LeftDockBottomTabs = LeftTabs.Where(t => t.Order >= 1000).ToArray();

        // Watch for tabs whose visibility toggles dynamically (e.g. the
        // Helm tab, shown only while own-vessel tracking is enabled). When
        // the currently-selected tab becomes hidden we move selection to
        // another visible tab (or close the dock). ActivityTab marshals the
        // PropertyChanged onto the UI thread, so this handler is UI-safe.
        foreach (var tab in _tabs)
        {
            tab.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(IActivityTab.IsVisible) && sender is IActivityTab changed)
                {
                    OnTabVisibilityChanged(changed);
                }
            };
        }

        SelectTabCommand = new RelayCommand<IActivityTab>(tab =>
        {
            if (tab is not null) RouteSelection(tab);
        });

        CloseDockCommand = new RelayCommand<object?>(parameter =>
        {
            // Accept both TabDock enum values and their string names so
            // XAML callers can pass either form without a converter.
            TabDock? dock = parameter switch
            {
                TabDock d => d,
                string s when Enum.TryParse<TabDock>(s, ignoreCase: true, out var parsed) => parsed,
                _ => null,
            };
            switch (dock)
            {
                case TabDock.Left: IsLeftDockOpen = false; break;
                case TabDock.Right:
                    IsRightDockOpen = false;
                    // Dismissing the Pick Report should also drop the map
                    // highlight so the report and overlay stay in sync, and so
                    // a subsequent pick re-fires HasPick false→true and
                    // re-opens the dock. Only clear when the dock being closed
                    // is actually showing the Pick Report.
                    if (ReferenceEquals(_selectedRightTab?.ViewModel, PickReport))
                        PickReport.Clear();
                    break;
                case TabDock.Bottom: IsBottomDockOpen = false; break;
            }
        });

        TogglePrimarySideBarCommand = new RelayCommand(() =>
        {
            if (IsLeftDockOpen)
            {
                IsLeftDockOpen = false;
                return;
            }

            // Re-open with the persisted left-dock tab when available,
            // otherwise fall back to the default tab (Datasets).
            if (settings.LastSelectedActivity is { } last
                && _tabsById.TryGetValue(last, out var restored)
                && restored.Dock == TabDock.Left)
            {
                SelectedLeftTab = restored;
                IsLeftDockOpen = true;
            }
            else if (_selectedLeftTab is not null)
            {
                IsLeftDockOpen = true;
            }
            else
            {
                SelectDefaultTab();
            }
        });

        _isStatusBarVisible = settings.IsStatusBarVisible;

        ToggleStatusBarCommand = new RelayCommand(() => IsStatusBarVisible = !IsStatusBarVisible);

        ToggleTimelineCommand = new RelayCommand(() => IsBottomDockOpen = !IsBottomDockOpen);

        TogglePickPanelCommand = new RelayCommand(() => IsRightDockOpen = !IsRightDockOpen);

        TogglePickModeCommand = new RelayCommand(() => Tools.Toggle(PickTool.ToolId));
        ExitPickModeCommand = new RelayCommand(() => Tools.Activate(null));
        ToggleMeasureModeCommand = new RelayCommand(() => Tools.Toggle(MeasureTool.ToolId));
        ToggleRouteEditModeCommand = new RelayCommand(() => Tools.Toggle(RouteEditTool.ToolId));
        EnterRouteEditModeCommand = new RelayCommand(() => Tools.Activate(RouteEditTool.ToolId));
        ExitRouteEditModeCommand = new RelayCommand(
            () => Tools.Activate(null),
            () => Tools.IsActive(RouteEditTool.ToolId));
        PromoteMeasurementToRouteCommand = new RelayCommand(
            PromoteMeasurementToRoute,
            () => _measureTool.State.Waypoints.Count >= 2);
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
            OnPropertyChanged(nameof(IsRouteEditModeActive));
            OnPropertyChanged(nameof(IsToolSummaryVisible));
            ((RelayCommand)ToolCommitCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ToolBackstepCommand).NotifyCanExecuteChanged();
            ((RelayCommand)PromoteMeasurementToRouteCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ExitRouteEditModeCommand).NotifyCanExecuteChanged();
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

        // Restore last selected left-dock tab. We assign _selectedLeftTab
        // directly so restoration doesn't re-write settings. If the
        // persisted id is missing, stale, or points to a non-left dock,
        // fall back to the default tab (Datasets), then defensively to
        // the first registered left-dock tab.
        if (settings.LastSelectedActivity is { } lastId
            && _tabsById.TryGetValue(lastId, out var restoredTab)
            && restoredTab.Dock == TabDock.Left
            && restoredTab.IsVisible)
        {
            _selectedLeftTab = restoredTab;
        }
        else if (_tabsById.TryGetValue(DefaultTabId, out var defaultTab)
            && defaultTab.Dock == TabDock.Left
            && defaultTab.IsVisible)
        {
            _selectedLeftTab = defaultTab;
        }
        else
        {
            _selectedLeftTab = LeftTabs.FirstOrDefault(t => t.IsVisible);
        }

        // PR-M3: restore persisted dock visibility before wiring auto-open
        // subscriptions. Auto-open events still fire on content-becomes-
        // available signals but no-op when the dock is already open.
        // Left dock: persisted flag wins, but always close if no tab is
        // available to show.
        _isLeftDockOpen = settings.IsLeftDockOpen && _selectedLeftTab is not null;
        _isRightDockOpen = settings.IsRightDockOpen;
        _isBottomDockOpen = settings.IsBottomDockOpen;

        WireAutoOpenSubscriptions();

        // PR-M3: restore persisted right/bottom tab selections. Done after
        // WireAutoOpenSubscriptions so the defaults set there are
        // overridden when a persisted id resolves; falls back to the
        // first registered tab of that dock when the id is stale.
        if (settings.LastSelectedRightTab is { } rightId
            && _tabsById.TryGetValue(rightId, out var rightTab)
            && rightTab.Dock == TabDock.Right)
        {
            _selectedRightTab = rightTab;
        }
        if (settings.LastSelectedBottomTab is { } bottomId
            && _tabsById.TryGetValue(bottomId, out var bottomTab)
            && bottomTab.Dock == TabDock.Bottom)
        {
            _selectedBottomTab = bottomTab;
        }

        // PR-M3: hydrate persisted panel sizes.
        _leftDockSavedWidth = settings.Panels.LeftDockWidth;
        _rightDockSavedWidth = settings.Panels.RightDockWidth;
        _bottomDockSavedHeight = settings.Panels.BottomDockHeight;
        _datasetsInnerSplit = settings.Panels.DatasetsInnerSplit;
        _catalogInnerSplit = settings.Panels.CatalogInnerSplit;
        _vesselsInnerSplit = settings.Panels.VesselsInnerSplit;

        // Enable persistence write-backs only after hydration is complete.
        // Setters check this flag to avoid writing during initial assignment.
        _settingsInitialized = true;
    }

    private void WireAutoOpenSubscriptions()
    {
        foreach (var tab in _tabs)
        {
            if (!tab.AutoOpenOnContentSignal) continue;
            if (tab.ViewModel is not IActivityTabContentSignal signal) continue;

            var captured = tab;
            signal.ContentBecameAvailable += (_, _) =>
            {
                switch (captured.Dock)
                {
                    case TabDock.Left:
                        SelectedLeftTab = captured;
                        IsLeftDockOpen = true;
                        break;
                    case TabDock.Right:
                        SelectedRightTab = captured;
                        IsRightDockOpen = true;
                        break;
                    case TabDock.Bottom:
                        SelectedBottomTab = captured;
                        IsBottomDockOpen = true;
                        break;
                }
            };
        }

        // Pre-select the single right/bottom tab (when present) so the
        // chrome title and ContentControl have something to bind to even
        // before the dock first opens. Picking a default selection does
        // not open the dock — Open state remains controlled by user
        // toggles and ContentBecameAvailable.
        if (_selectedRightTab is null && RightTabs.Count > 0)
            _selectedRightTab = RightTabs[0];
        if (_selectedBottomTab is null && BottomTabs.Count > 0)
            _selectedBottomTab = BottomTabs[0];
    }

    private async Task OpenRecentAsync(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        if (!File.Exists(path))
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

        SelectDefaultTab();
        await Datasets.LoadFromPathAsync(path);
    }

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
