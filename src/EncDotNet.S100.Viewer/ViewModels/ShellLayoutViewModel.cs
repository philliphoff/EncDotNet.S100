using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels.Activities;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The window's docks and activity tabs: which tab each dock shows, whether
/// it is open, the panel sizes, and the status bar and Timeline toggles. All
/// of it is kept in <see cref="ViewerSettings"/>.
/// </summary>
internal sealed class ShellLayoutViewModel : ViewModelBase
{
    private readonly ViewerSettings _settings;
    private readonly TimelineViewModel _timeline;

    /// <summary>Raised when the user closes the right dock, with the tab it was showing.</summary>
    public event Action<IActivityTab?>? RightDockClosed;

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
                // Remember the side panel a full-content tab (Settings) covers,
                // so closing it returns there rather than to a closed dock.
                if (newValue is { FillsContent: true } && !IsContentPanelOpen)
                {
                    _coveredLeftTab = _selectedLeftTab;
                    _coveredLeftDockOpen = _isLeftDockOpen;
                }
                else if (newValue is not { FillsContent: true })
                {
                    _coveredLeftTab = null;
                }

                _selectedLeftTab = newValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedLeftTabId));
                OnPropertyChanged(nameof(LeftDockTitle));
                OnContentPanelChanged();

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
                OnPropertyChanged(nameof(RightDockEdgeTooltip));
                OnPropertyChanged(nameof(IsLegendTabShown));
                UpdateRightTabsShown();

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
                OnPropertyChanged(nameof(IsTimelineTabSelected));
                OnPropertyChanged(nameof(IsClearanceTabSelected));
                OnPropertyChanged(nameof(IsTimeHudVisible));
                OnPropertyChanged(nameof(IsMapStampVisible));
                UpdateBottomTabsShown();

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
            // Closing a full-content tab (Settings) returns to the side panel
            // it covered, open or closed as it was.
            if (!value && IsContentPanelOpen && _coveredLeftTab is { } covered)
            {
                _coveredLeftTab = null;
                SetLeftSelectionSystem(covered);
                value = _coveredLeftDockOpen;
            }

            if (SetProperty(ref _isLeftDockOpen, value) && _settingsInitialized)
            {
                // A full-content tab is not a side panel: persist the state of
                // the panel it covers, so a restart doesn't open the dock.
                _settings.IsLeftDockOpen = IsContentPanelOpen ? _coveredLeftTab is not null && _coveredLeftDockOpen : value;
                _settings.Save();
            }

            OnContentPanelChanged();
        }
    }

    private IActivityTab? _coveredLeftTab;
    private bool _coveredLeftDockOpen;

    /// <summary>
    /// True while a <see cref="IActivityTab.FillsContent"/> tab (Settings) is
    /// selected and open: it is shown across the content area, not in the
    /// left dock's column.
    /// </summary>
    public bool IsContentPanelOpen => _isLeftDockOpen && _selectedLeftTab is { FillsContent: true };

    /// <summary>True when the left dock's column is shown (open, and not replaced by a full-content tab).</summary>
    public bool IsLeftDockShown => _isLeftDockOpen && !IsContentPanelOpen;

    /// <summary>The tab shown in the left dock's column, or <c>null</c> while a full-content tab is open.</summary>
    public IActivityTab? LeftDockContent => IsContentPanelOpen ? null : _selectedLeftTab;

    /// <summary>The full-content tab shown across the content area, or <c>null</c>.</summary>
    public IActivityTab? ContentPanelTab => IsContentPanelOpen ? _selectedLeftTab : null;

    private void OnContentPanelChanged()
    {
        OnPropertyChanged(nameof(IsContentPanelOpen));
        OnPropertyChanged(nameof(IsLeftDockShown));
        OnPropertyChanged(nameof(LeftDockContent));
        OnPropertyChanged(nameof(ContentPanelTab));
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
            if (!SetProperty(ref _isRightDockOpen, value))
                return;
            OnRightDockShownChanged();
            if (_settingsInitialized)
            {
                _settings.IsRightDockOpen = value;
                _settings.Save();
            }
        }
    }

    private bool _isRightDockFolded;
    /// <summary>
    /// True while Check passage has folded the Object Information dock to its
    /// 26 px edge tab (#901, handoff C1). Not persisted: turning the mode off,
    /// or clicking the tab, restores the dock.
    /// </summary>
    public bool IsRightDockFolded
    {
        get => _isRightDockFolded;
        set
        {
            if (!SetProperty(ref _isRightDockFolded, value))
                return;
            OnRightDockShownChanged();
        }
    }

    private void OnRightDockShownChanged()
    {
        OnPropertyChanged(nameof(IsRightDockShown));
        OnPropertyChanged(nameof(IsRightDockEdgeTabVisible));
        OnPropertyChanged(nameof(IsLegendTabShown));
        UpdateRightTabsShown();
    }

    /// <summary>Whether the right dock's panel is shown: open and not folded.</summary>
    public bool IsRightDockShown => IsRightDockOpen && !IsRightDockFolded;

    /// <summary>Whether the folded dock's edge tab is shown.</summary>
    public bool IsRightDockEdgeTabVisible => IsRightDockOpen && IsRightDockFolded;

    /// <summary>The folded dock's edge tab tooltip and name: "Show Object information", "Show Legend".</summary>
    public string RightDockEdgeTooltip => string.Format(
        System.Globalization.CultureInfo.CurrentCulture, Resources.Strings.Tooltip_ShowDockTabFormat, _selectedRightTab?.Name ?? string.Empty);

    /// <summary>Unfolds the right dock from its edge tab.</summary>
    public ICommand UnfoldRightDockCommand => _unfoldRightDockCommand ??= new RelayCommand(() => IsRightDockFolded = false);

    private ICommand? _unfoldRightDockCommand;


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
            if (SetProperty(ref _isBottomDockOpen, value))
            {
                RaiseTimeSurfaces();
                if (_settingsInitialized)
                {
                    _settings.IsBottomDockOpen = value;
                    _settings.Save();
                }
            }
        }
    }

    /// <summary>Below this window width the Timeline dock is unavailable and the Time HUD is the only control (#712, handoff F5).</summary>
    internal const double DockMinimumWindowWidth = 900;

    private double _windowWidth = double.PositiveInfinity;
    private double _mapWidth = double.PositiveInfinity;

    /// <summary>The window's width, set as it lays out (#712 F5).</summary>
    public double WindowWidth
    {
        get => _windowWidth;
        set
        {
            var wasNarrow = IsWindowNarrow;
            if (SetProperty(ref _windowWidth, value) && wasNarrow != IsWindowNarrow)
                RaiseTimeSurfaces();
        }
    }

    /// <summary>The map's width, which the Time HUD fits (it drops the offset, then shortens Go live to an icon).</summary>
    public double MapWidth
    {
        get => _mapWidth;
        set => SetProperty(ref _mapWidth, value);
    }

    /// <summary>True when the window is too narrow for the bottom dock (#712 F5).</summary>
    public bool IsWindowNarrow => _windowWidth < DockMinimumWindowWidth;

    /// <summary>
    /// True when the bottom dock is shown: open, and the window wide enough
    /// for it. A narrow window hides it without forgetting that it was open.
    /// </summary>
    public bool IsBottomDockShown => IsBottomDockOpen && !IsWindowNarrow;

    /// <summary>
    /// True when the Time HUD is on the map (#712, handoff F5): something is
    /// time-aware and the Timeline dock is not shown (closed, or the window too narrow).
    /// </summary>
    public bool IsTimeHudVisible => _timeline.IsActive && !(IsBottomDockShown && IsTimelineTabSelected);

    /// <summary>
    /// True when the map stamp is on the map: while the Timeline tab is shown
    /// in the dock (#713 B4). It's the exact complement of
    /// <see cref="IsTimeHudVisible"/>, so the map has one time surface at a
    /// time, and whichever is shown says "Display Not Real Time" (#993).
    /// </summary>
    public bool IsMapStampVisible => _timeline.IsActive && IsBottomDockShown && IsTimelineTabSelected;

    /// <summary>Opens or closes the Timeline dock from the Time HUD; unavailable in a narrow window.</summary>
    public ICommand ToggleTimelineDockCommand { get; private set; } = null!;

    private void RaiseTimeSurfaces()
    {
        OnPropertyChanged(nameof(IsWindowNarrow));
        OnPropertyChanged(nameof(IsBottomDockShown));
        OnPropertyChanged(nameof(IsTimeHudVisible));
        OnPropertyChanged(nameof(IsMapStampVisible));
        UpdateBottomTabsShown();
        (ToggleTimelineDockCommand as RelayCommand)?.NotifyCanExecuteChanged();
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
    /// <summary>
    /// Persisted fraction <c>[0,1]</c> of the Library tab's collections/datasets
    /// splitter. Keeps its original Catalog-tab name so existing settings carry over.
    /// </summary>
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

    private double? _routesListHeight;
    /// <summary>Persisted height of the Routes-tab route list once its splitter is dragged; <c>null</c> sizes it to its routes.</summary>
    public double? RoutesListHeight
    {
        get => _routesListHeight;
        set
        {
            if (SetProperty(ref _routesListHeight, value) && _settingsInitialized)
            {
                _settings.Panels.RoutesListHeight = value;
                _sizeSaver.RequestSave();
            }
        }
    }

    private double? _logsDetailsWidth;
    /// <summary>Persisted width of the Logs-tab details pane once its splitter is dragged; <c>null</c> is the default (#1174).</summary>
    public double? LogsDetailsWidth
    {
        get => _logsDetailsWidth;
        set
        {
            if (SetProperty(ref _logsDetailsWidth, value) && _settingsInitialized)
            {
                _settings.Panels.LogsDetailsWidth = value;
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
        if (tab.Dock == TabDock.Bottom)
            OnBottomTabVisibilityChanged(tab);
        if (tab.Dock == TabDock.Right)
        {
            OnPropertyChanged(nameof(VisibleRightTabs));
            OnPropertyChanged(nameof(HasRightDockTabs));
        }

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

    /// <summary>Id of the Timeline tab in the bottom dock.</summary>
    internal const string TimelineTabId = "Timeline";

    /// <summary>Id of the Clearance tab in the bottom dock (#902), shown while Check passage is on.</summary>
    internal const string ClearanceTabId = "Clearance";

    /// <summary>Id of the Logs tab in the bottom dock (#1080).</summary>
    internal const string LogsTabId = "Logs";

    /// <summary>
    /// Tells the bottom-dock tabs that care (<see cref="IActivityTabShown"/>)
    /// whether the user can see them now.
    /// </summary>
    private void UpdateBottomTabsShown()
    {
        foreach (var tab in BottomTabs)
        {
            if (tab.ViewModel is IActivityTabShown shown)
                shown.IsTabShown = IsBottomDockShown && ReferenceEquals(_selectedBottomTab, tab);
        }
    }

    /// <summary>Id of the Object information tab in the right dock.</summary>
    internal const string PickReportTabId = "PickReport";

    /// <summary>Id of the Legend tab in the right dock (#1404, legends design note D2).</summary>
    internal const string LegendTabId = "Legend";

    /// <summary>
    /// Tells the right-dock tabs that care (<see cref="IActivityTabShown"/>)
    /// whether the user can see them now: the Legend tab tells
    /// <see cref="Services.Legends.LegendService.IsTabShown"/>.
    /// </summary>
    private void UpdateRightTabsShown()
    {
        foreach (var tab in RightTabs)
        {
            if (tab.ViewModel is IActivityTabShown shown)
                shown.IsTabShown = IsRightDockShown && ReferenceEquals(_selectedRightTab, tab);
        }
    }

    /// <summary>The right-dock tabs that can be shown now, for the dock's tab strip (D2).</summary>
    public IReadOnlyList<IActivityTab> VisibleRightTabs => RightTabs.Where(t => t.IsVisible).ToArray();

    /// <summary>Whether the right dock shows a tab strip instead of a title: more than one tab is available.</summary>
    public bool HasRightDockTabs => RightTabs.Count(t => t.IsVisible) > 1;

    /// <summary>Whether the right dock is showing the Legend tab (View › Legend's check mark).</summary>
    public bool IsLegendTabShown => IsRightDockShown && SelectedRightTabId == LegendTabId;

    private IActivityTab? _rightTabBeforeLegend;
    private bool _rightDockOpenBeforeLegend;

    /// <summary>
    /// View › Legend (Ctrl+Shift+L): shows the Legend tab, unfolding the dock
    /// if Check passage folded it; pressed again, puts back the tab the dock
    /// showed before, or closes the dock if it was closed (D3, D15).
    /// </summary>
    public ICommand ToggleLegendCommand => _toggleLegendCommand ??= new RelayCommand(ToggleLegend);

    private ICommand? _toggleLegendCommand;

    private void ToggleLegend()
    {
        if (!_tabsById.TryGetValue(LegendTabId, out var legend) || !legend.IsVisible)
            return;

        if (IsLegendTabShown)
        {
            if (_rightDockOpenBeforeLegend && _rightTabBeforeLegend is { IsVisible: true } previous && !ReferenceEquals(previous, legend))
            {
                SelectedRightTab = previous;
            }
            else
            {
                IsRightDockOpen = false;
                RightDockClosed?.Invoke(legend);
            }
            return;
        }

        ShowLegendTab(legend);
    }

    /// <summary>Shows the Legend tab, remembering what the dock showed so the toggle can put it back.</summary>
    private void ShowLegendTab(IActivityTab legend)
    {
        if (!IsLegendTabShown)
        {
            _rightDockOpenBeforeLegend = IsRightDockShown;
            _rightTabBeforeLegend = _selectedRightTab;
        }
        IsRightDockFolded = false;
        SelectedRightTab = legend;
        IsRightDockOpen = true;
    }

    /// <summary>Opens the bottom dock on the Logs tab (Help › Show logs, #1080).</summary>
    private void ShowLogs()
    {
        if (!_tabsById.TryGetValue(LogsTabId, out var logs))
            return;
        SelectedBottomTab = logs;
        IsBottomDockOpen = true;
    }

    private IActivityTab? _bottomTabBeforeCheck;
    private bool _bottomDockOpenBeforeCheck;

    /// <summary>The bottom-dock tabs that can be shown now, for the dock's tab strip (D1).</summary>
    public IReadOnlyList<IActivityTab> VisibleBottomTabs => BottomTabs.Where(t => t.IsVisible).ToArray();

    /// <summary>Whether the bottom dock shows a tab strip instead of a title: more than one tab is available.</summary>
    public bool HasBottomDockTabs => BottomTabs.Count(t => t.IsVisible) > 1;

    /// <summary>Whether the bottom dock is showing the Timeline.</summary>
    public bool IsTimelineTabSelected => SelectedBottomTabId == TimelineTabId;

    /// <summary>Whether the bottom dock is showing the Clearance profile.</summary>
    public bool IsClearanceTabSelected => SelectedBottomTabId == ClearanceTabId;

    /// <summary>The Clearance tab's view model, for its controls in the dock header.</summary>
    public ClearanceViewModel? Clearance =>
        _tabsById.TryGetValue(ClearanceTabId, out var tab) ? tab.ViewModel as ClearanceViewModel : null;

    /// <summary>
    /// Check passage opens the bottom dock on Clearance (handoff C1) and
    /// turning it off puts back the tab and open state it found.
    /// </summary>
    private void OnBottomTabVisibilityChanged(IActivityTab tab)
    {
        OnPropertyChanged(nameof(VisibleBottomTabs));
        OnPropertyChanged(nameof(HasBottomDockTabs));
        if (tab.Id != ClearanceTabId)
            return;

        if (tab.IsVisible)
        {
            _bottomTabBeforeCheck = _selectedBottomTab;
            _bottomDockOpenBeforeCheck = _isBottomDockOpen;
            SelectedBottomTab = tab;
        }
        else if (ReferenceEquals(_selectedBottomTab, tab))
        {
            SelectedBottomTab = _bottomTabBeforeCheck is { IsVisible: true } previous
                ? previous
                : BottomTabs.FirstOrDefault(t => t.IsVisible);
            IsBottomDockOpen = _bottomDockOpenBeforeCheck;
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
        OnContentPanelChanged();
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


    /// <summary>
    /// Toggles the right dock open/closed. Kept for the existing
    /// View menu binding; in PR-M4 the dock contains the Pick Report tab.
    /// </summary>
    public ICommand TogglePickPanelCommand { get; }

    /// <summary>Opens the bottom dock on the Logs tab (Help › Show logs, #1080).</summary>
    public ICommand ShowLogsCommand { get; }

    /// <summary>Creates the layout over the registered tabs.</summary>
    /// <param name="settings">Where the dock state, the selected tabs and the panel sizes are kept.</param>
    /// <param name="activityTabs">The activity tabs, in any order.</param>
    /// <param name="timeline">The Timeline, which decides between the Time HUD and the map stamp.</param>
    /// <param name="post">Posts the debounced panel-size save to the UI thread.</param>
    public ShellLayoutViewModel(
        ViewerSettings settings,
        IEnumerable<IActivityTab> activityTabs,
        TimelineViewModel timeline,
        Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(activityTabs);
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(post);
        _settings = settings;
        _timeline = timeline;

        // PR-M3: debounced settings writer used by the splitter-size
        // setters. 500 ms window coalesces rapid drag updates into one
        // disk write; MainWindow.Closed calls OnShutdown → Flush so the
        // last drag is never lost.
        _sizeSaver = new DebouncedSettingsSaver(
            save: () => { try { _settings.Save(); } catch { /* best-effort */ } },
            dispatch: post);

        timeline.CloseRequested += () => IsBottomDockOpen = false;
        timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TimelineViewModel.IsActive))
            {
                OnPropertyChanged(nameof(IsTimeHudVisible));
                OnPropertyChanged(nameof(IsMapStampVisible));
            }
        };
        // The dock remembers lanes vs strip (#710, handoff E6).
        timeline.IsCollapsedToStrip = settings.IsTimelineCollapsedToStrip;
        timeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(TimelineViewModel.IsCollapsedToStrip) || _settings.IsTimelineCollapsedToStrip == timeline.IsCollapsedToStrip)
                return;
            _settings.IsTimelineCollapsedToStrip = timeline.IsCollapsedToStrip;
            _settings.Save();
        };

        // Activity tab registry — partitioned by Dock, ordered by
        // IActivityTab.Order ascending. Left-dock tabs further split into
        // top/bottom groups by the legacy <1000 / >=1000 convention so
        // Settings stays pinned to the bottom of the activity bar.
        _tabs = activityTabs
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
                    RightDockClosed?.Invoke(_selectedRightTab);
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

        ToggleTimelineCommand = new RelayCommand(() =>
        {
            // View › Timeline and T open the Timeline, not the Logs tab the dock
            // last showed (#1080).
            if (!IsBottomDockOpen && SelectedBottomTabId == LogsTabId && _tabsById.TryGetValue(TimelineTabId, out var timeline))
            {
                SelectedBottomTab = timeline;
                IsBottomDockOpen = true;
                return;
            }
            IsBottomDockOpen = !IsBottomDockOpen;
        });
        ToggleTimelineDockCommand = new RelayCommand(
            () =>
            {
                // From the HUD while the dock shows another tab (Clearance): switch to the Timeline.
                if (IsBottomDockOpen && !IsTimelineTabSelected && _tabsById.TryGetValue(TimelineTabId, out var timelineTab))
                    SelectedBottomTab = timelineTab;
                else
                    IsBottomDockOpen = !IsBottomDockOpen;
            },
            () => !IsWindowNarrow);

        TogglePickPanelCommand = new RelayCommand(() =>
        {
            if (IsRightDockFolded)
                IsRightDockFolded = false;
            else
                IsRightDockOpen = !IsRightDockOpen;
        });

        ShowLogsCommand = new RelayCommand(ShowLogs);

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
        _routesListHeight = settings.Panels.RoutesListHeight;
        _logsDetailsWidth = settings.Panels.LogsDetailsWidth;

        // Enable persistence write-backs only after hydration is complete.
        // Setters check this flag to avoid writing during initial assignment.
        _settingsInitialized = true;
        UpdateBottomTabsShown();
        UpdateRightTabsShown();
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
                    case TabDock.Right when captured.Id == LegendTabId:
                        // LegendService.Show: a link opened the tab (D8).
                        ShowLegendTab(captured);
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
}
