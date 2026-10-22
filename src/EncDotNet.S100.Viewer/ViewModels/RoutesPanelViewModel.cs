using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View-model for the Routes activity-bar panel. Presents the editable
/// <see cref="RouteCollection"/> owned by <see cref="RoutesService"/>: the
/// list of routes (with add / rename / reverse / remove / select), and the
/// active route's waypoint-and-leg timeline (numbered waypoints interleaved
/// with the legs that join them, with insert / delete / leg-geometry toggle,
/// and editable waypoint names and positions). The toolbar under the route
/// name has undo / redo / zoom to route / reverse, and the footer has the Edit
/// and Check passage toggles. The Passage card (<see cref="Passage"/>) holds
/// the route-level values legs inherit, and clicking a leg row opens its inline
/// detail (<see cref="LegDetail"/>) to override them. All mutations flow back into the shared
/// <see cref="RoutesService"/> through the route's
/// <see cref="Route.History"/>, so the map overlay and the
/// <c>RouteEditTool</c> stay in lock-step with the panel and every edit can be
/// undone.
/// </summary>
internal sealed class RoutesPanelViewModel : ViewModelBase
{
    /// <summary>Half-size of the box zoomed to for a one-waypoint route, in metres.</summary>
    private const double SinglePointZoomHalfMetres = 2000.0;

    private readonly RoutesService _routes;
    private readonly ICapabilityAccessor<IMapViewportController>? _viewport;
    private readonly IMarinerSettingsProvider? _marinerSettings;
    private readonly ITimeFormatProvider? _timeFormat;
    private readonly PassageCheckService? _check;
    private readonly RouteExchangeService? _exchange;
    private readonly IRouteDialogs? _dialogs;
    private bool _suppressSync;
    private Route? _shownRoute;
    private int? _openLegIndex;

    public RoutesPanelViewModel(
        RoutesService routes,
        ICapabilityAccessor<IMapViewportController>? viewport = null,
        IMarinerSettingsProvider? marinerSettings = null,
        ITimeFormatProvider? timeFormat = null,
        PassageCheckService? check = null,
        RouteFindingsService? findings = null,
        RouteExchangeService? exchange = null,
        IRouteDialogs? dialogs = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes;
        _exchange = exchange;
        _dialogs = dialogs;
        _viewport = viewport;
        _marinerSettings = marinerSettings;
        _timeFormat = timeFormat;
        _check = check;
        Passage = new RoutePassageCardViewModel(() => _routes.Routes.ActiveRoute, () => DepthUnit, () => TimeFormat);
        if (check is not null)
            Check = new PassageCheckViewModel(check, routes, () => DepthUnit, () => TimeFormat, findings, Passage.FocusDraft);

        LegDetail = new RouteLegDetailViewModel(() => _routes.Routes.ActiveRoute, () => DepthUnit);

        Routes = new ObservableCollection<RouteRowViewModel>();
        Timeline = new ObservableCollection<RouteTimelineRowViewModel>();

        AddRouteCommand = new RelayCommand(AddRoute);
        RemoveRouteCommand = new RelayCommand<RouteRowViewModel>(RemoveRoute);
        RenameRouteCommand = new RelayCommand<RouteRowViewModel>(RenameRoute);
        ReverseRouteCommand = new RelayCommand<RouteRowViewModel>(row => Reverse(row?.Route));

        ReverseActiveRouteCommand = new RelayCommand(
            () => Reverse(_routes.Routes.ActiveRoute),
            () => _routes.Routes.ActiveRoute is { Waypoints.Count: >= 2, IsReadOnly: false });
        UndoCommand = new RelayCommand(() => _routes.UndoActiveRoute(), () => CanUndo);
        RedoCommand = new RelayCommand(() => _routes.RedoActiveRoute(), () => CanRedo);
        ZoomToRouteCommand = new RelayCommand(
            ZoomToActiveRoute,
            () => _routes.Routes.ActiveRoute is { Waypoints.Count: > 0 });

        BeginRenameCommand = new RelayCommand(BeginRename, () => _routes.Routes.ActiveRoute is { IsReadOnly: false });
        CommitRenameCommand = new RelayCommand(CommitRename);
        CancelRenameCommand = new RelayCommand(CancelRename);

        SelectWaypointCommand = new RelayCommand<RouteWaypointRowViewModel>(SelectWaypoint);
        SelectLegCommand = new RelayCommand<RouteLegRowViewModel>(row =>
        {
            if (row is not null)
                _routes.SelectedLegIndex = row.LegIndex;
        });
        InsertWaypointAfterCommand = new RelayCommand<RouteWaypointRowViewModel>(InsertWaypointAfter);
        DeleteWaypointAtCommand = new RelayCommand<RouteWaypointRowViewModel>(DeleteWaypointAt);
        ToggleLegGeometryAtCommand = new RelayCommand<RouteLegRowViewModel>(ToggleLegGeometryAt);
        ToggleLegDetailCommand = new RelayCommand<RouteLegRowViewModel>(ToggleLegDetail);

        // #960: Import route…, Copy to edit and Export….
        ImportRouteCommand = new AsyncRelayCommand(ImportRouteAsync, () => _exchange is not null && _dialogs is not null);
        CopyToEditCommand = new RelayCommand<RouteRowViewModel>(
            row => CopyToEdit(row?.Route ?? _routes.Routes.ActiveRoute),
            row => _exchange is not null && (row?.Route ?? _routes.Routes.ActiveRoute) is { IsReadOnly: true });
        ExportRouteCommand = new RelayCommand<RouteRowViewModel>(
            row => Export(row?.Route ?? _routes.Routes.ActiveRoute),
            row => _exchange is not null && (row?.Route ?? _routes.Routes.ActiveRoute) is { Waypoints.Count: >= 2 });
        DropStaleSchedulesCommand = new RelayCommand(() => _routes.Routes.ActiveRoute?.DropStalePreservedSchedules());
        KeepStaleSchedulesCommand = new RelayCommand(() => _routes.Routes.ActiveRoute?.SetKeepStalePreservedSchedules(true));
        _routes.CopyToEditRequested += (_, _) => CopyToEdit(_routes.Routes.ActiveRoute);

        DeleteWaypointCommand = new RelayCommand(
            () => _routes.DeleteSelectedWaypoint(),
            () => _routes.SelectedWaypointIndex is not null);

        _routes.Routes.Changed += (_, _) => Rebuild();
        _routes.Routes.HistoryChanged += (_, _) => RefreshHistory();
        _routes.SelectionChanged += (_, _) => RefreshSelection();
        _routes.EditModeChanged += (_, _) => OnEditModeChanged();
        _routes.CheckModeChanged += (_, _) => OnCheckModeChanged();
        _routes.ViewTime.Changed += (_, _) => OnViewTimeChanged();

        // The card's expanded state is mirrored on RoutesService so MCP tools
        // can read and set it (set_passage_card, list_routes).
        Passage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RoutePassageCardViewModel.IsExpanded))
                _routes.IsPassageCardExpanded = Passage.IsExpanded;
        };
        _routes.PassageCardChanged += (_, _) => Passage.IsExpanded = _routes.IsPassageCardExpanded;
        if (_marinerSettings is not null)
            _marinerSettings.Changed += _ => Rebuild();
        if (_timeFormat is not null)
            _timeFormat.TimeFormatChanged += _ => Rebuild();
        Rebuild();
    }

    /// <summary>The routes in the collection, in creation order.</summary>
    public ObservableCollection<RouteRowViewModel> Routes { get; }

    /// <summary>
    /// Ordered timeline rows for the active route: a
    /// <see cref="RouteWaypointRowViewModel"/> for each waypoint, interleaved
    /// with a <see cref="RouteLegRowViewModel"/> for each leg joining two
    /// consecutive waypoints.
    /// </summary>
    public ObservableCollection<RouteTimelineRowViewModel> Timeline { get; }

    private RouteRowViewModel? _selectedRoute;
    /// <summary>
    /// The currently selected route row. Setting it makes the underlying
    /// route the collection's active route.
    /// </summary>
    public RouteRowViewModel? SelectedRoute
    {
        get => _selectedRoute;
        set
        {
            if (!SetProperty(ref _selectedRoute, value))
                return;
            if (!_suppressSync)
                _routes.Routes.SetActiveRoute(value?.Route);
        }
    }

    /// <summary>True when the collection has at least one route.</summary>
    public bool HasRoutes => Routes.Count > 0;

    /// <summary>True when there is an active route to inspect.</summary>
    public bool HasActiveRoute => _routes.Routes.ActiveRoute is not null;

    /// <summary>Placeholder shown when no routes exist.</summary>
    public string EmptyText => Strings.Routes_NoRoutes;

    /// <summary>Placeholder shown when routes exist but none is active.</summary>
    public string NoActiveRouteText => Strings.Routes_NoActiveRoute;

    /// <summary>
    /// Whether the route editor is on. Bound two-way to the footer's Edit
    /// toggle; turning it off is "done": it commits an open rename and leaves
    /// edit mode.
    /// </summary>
    public bool IsEditing
    {
        get => _routes.IsEditModeActive;
        set
        {
            if (!value)
                CommitRename();
            _routes.IsEditModeActive = value;
        }
    }

    /// <summary>
    /// The Passage card: the active route's draft, speed, departure, required
    /// UKC, vessel and cross-track, which its legs inherit.
    /// </summary>
    public RoutePassageCardViewModel Passage { get; }

    /// <summary>The inline detail of the open leg, shown under its leg row.</summary>
    public RouteLegDetailViewModel LegDetail { get; }

    /// <summary>
    /// Whether the active route has a draft, which the clearance check needs:
    /// on the passage, or overridden on every leg. Without one Check passage
    /// runs the route checks only (#953).
    /// </summary>
    public bool HasDraft => _routes.Routes.ActiveRoute is { } route
        && (route.Passage.DraftMeters is not null
            || (route.Legs.Count > 0 && route.Legs.All(l => l.DraftMeters is not null)));

    /// <summary>
    /// Whether Check passage can be turned on: the active route needs at least
    /// two waypoints (a draft is only needed for clearance, #953), and the
    /// panel needs the check service (absent in some tests).
    /// </summary>
    public bool CanCheckPassage => _routes.Routes.ActiveRoute is { Legs.Count: > 0 } && _check is not null;

    /// <summary>
    /// Whether Check passage is on (#901). Bound two-way to the footer's
    /// Check passage toggle; it stays off without two waypoints.
    /// </summary>
    public bool IsCheckingPassage
    {
        get => _routes.IsCheckModeActive;
        set
        {
            if (value && !CanCheckPassage)
                value = false;
            _routes.IsCheckModeActive = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The verdict card, Legs / Findings switch and check-mode leg rows; <c>null</c> without the check service.</summary>
    public PassageCheckViewModel? Check { get; }

    /// <summary>Whether to show the check-mode panel content instead of the waypoint timeline.</summary>
    public bool ShowCheckPanel => IsCheckingPassage && Check is not null;

    /// <summary>Tooltip on the Check route toggle: what's missing, or what it does.</summary>
    public string CheckPassageTooltip =>
        _routes.Routes.ActiveRoute is not { Legs.Count: > 0 } ? Strings.Tooltip_CheckRouteNoLegs
        : HasDraft ? Strings.Tooltip_CheckRoute
        : Strings.Tooltip_CheckRouteNoDraft;

    private DepthUnit DepthUnit => _marinerSettings?.Current.DepthUnit ?? DepthUnit.Metres;

    private TimeFormat TimeFormat => _timeFormat?.Current ?? TimeFormat.Local;

    /// <summary>Whether the active route is a received, read-only route (#960, A6).</summary>
    public bool IsActiveReadOnly => _routes.Routes.ActiveRoute is { IsReadOnly: true };

    /// <summary>Whether the active route can be edited (the footer's Edit toggle).</summary>
    public bool CanEditActiveRoute => _routes.Routes.ActiveRoute is { IsReadOnly: false };

    /// <summary>The "Received · read-only" badge after a received route's name.</summary>
    public string ReceivedBadgeText => Strings.Routes_ReceivedBadge;

    /// <summary>
    /// A received route's source on the header sub-line, e.g.
    /// "S-421 · 421ZZ00WARN ed. 1"; <c>null</c> for an owned route.
    /// </summary>
    public string? ActiveSourceText => _routes.Routes.ActiveRoute?.Source is { } source ? SourceText(source) : null;

    /// <summary>Whether to show the Native / Planning control: the active route came from an S-421 dataset (A7).</summary>
    public bool ShowDisplayMode => _routes.Routes.ActiveRoute?.Source?.Format == RouteSourceFormats.S421;

    /// <summary>
    /// Whether the Native / Planning control can change the mode: not while
    /// Check route is on, which draws the route in Planning mode.
    /// </summary>
    public bool CanChangeDisplayMode => !IsCheckingPassage;

    /// <summary>
    /// Whether the active route shows as Native. Bound two-way to the
    /// Native segment; while Check route is on it reads false (Planning).
    /// </summary>
    public bool IsNativeMode
    {
        get => !IsCheckingPassage && _routes.Routes.ActiveRoute is { DisplayMode: RouteDisplayMode.Native };
        set
        {
            if (value)
                SetDisplayMode(RouteDisplayMode.Native);
        }
    }

    /// <summary>Whether the active route shows as Planning. Bound two-way to the Planning segment.</summary>
    public bool IsPlanningMode
    {
        get => !IsNativeMode;
        set
        {
            if (value)
                SetDisplayMode(RouteDisplayMode.Planning);
        }
    }

    /// <summary>The Native / Planning control's tooltip.</summary>
    public string DisplayModeTooltip => IsCheckingPassage ? Strings.Tooltip_DisplayModeChecking : Strings.Tooltip_DisplayModeNative;

    /// <summary>The footer Edit toggle's tooltip: why a received route can't be edited, else the shortcut.</summary>
    public string EditToggleTooltipText => IsActiveReadOnly ? Strings.Tooltip_EditReceivedRoute : EditToggleTooltip;

    private void SetDisplayMode(RouteDisplayMode mode)
    {
        if (!IsCheckingPassage && _routes.Routes.ActiveRoute is { Source: not null } route)
            route.SetDisplayMode(mode);
        RaiseReceivedState();
    }

    /// <summary>A received route's source text, e.g. "S-421 · 421ZZ00WARN ed. 1".</summary>
    internal static string SourceText(RouteSource source)
    {
        var name = source.RouteId ?? source.FileName;
        return source.Edition is { } edition
            ? string.Format(CultureInfo.CurrentCulture, Strings.Routes_ReceivedSourceFormat, source.Format, name, edition)
            : string.Format(CultureInfo.CurrentCulture, Strings.Routes_ReceivedSourceNoEditionFormat, source.Format, name);
    }

    private void RaiseReceivedState()
    {
        OnPropertyChanged(nameof(IsActiveReadOnly));
        OnPropertyChanged(nameof(CanEditActiveRoute));
        OnPropertyChanged(nameof(ActiveSourceText));
        OnPropertyChanged(nameof(ShowDisplayMode));
        OnPropertyChanged(nameof(CanChangeDisplayMode));
        OnPropertyChanged(nameof(IsNativeMode));
        OnPropertyChanged(nameof(IsPlanningMode));
        OnPropertyChanged(nameof(DisplayModeTooltip));
        OnPropertyChanged(nameof(EditToggleTooltipText));
        OnPropertyChanged(nameof(ShowCopyToEdit));
        OnPropertyChanged(nameof(NewerEditionText));
        OnPropertyChanged(nameof(HasNewerEdition));
        OnPropertyChanged(nameof(StaleSchedulesText));
        OnPropertyChanged(nameof(HasStaleSchedules));
        OnPropertyChanged(nameof(IsKeepingStaleSchedules));
        ((RelayCommand<RouteRowViewModel>)CopyToEditCommand).NotifyCanExecuteChanged();
        ((RelayCommand<RouteRowViewModel>)ExportRouteCommand).NotifyCanExecuteChanged();
    }

    /// <summary>The toolbar hint, e.g. "Ctrl+E edit · Esc done".</summary>
    public string EditHint => string.Format(
        CultureInfo.CurrentCulture, Strings.Routes_EditHintFormat, ShortcutText.Command("E"));

    /// <summary>Tooltip on the footer's Edit toggle, naming its shortcut.</summary>
    public string EditToggleTooltip => string.Format(
        CultureInfo.CurrentCulture, Strings.Tooltip_EditRouteToggle, ShortcutText.Command("E"));

    /// <summary>Tooltip on the Undo button, naming its shortcut.</summary>
    public string UndoTooltip => string.Format(
        CultureInfo.CurrentCulture, Strings.Tooltip_UndoRouteEdit, ShortcutText.Command("Z"));

    /// <summary>Tooltip on the Redo button, naming its shortcut.</summary>
    public string RedoTooltip => string.Format(
        CultureInfo.CurrentCulture, Strings.Tooltip_RedoRouteEdit, ShortcutText.Command("Z", shift: true));

    /// <summary>True when the active route has an edit to undo.</summary>
    public bool CanUndo => _routes.Routes.ActiveRoute?.History.CanUndo ?? false;

    /// <summary>True when the active route has an edit to redo.</summary>
    public bool CanRedo => _routes.Routes.ActiveRoute?.History.CanRedo ?? false;

    private string? _activeRouteName;
    /// <summary>Display name of the active route (null when none).</summary>
    public string? ActiveRouteName
    {
        get => _activeRouteName;
        private set => SetProperty(ref _activeRouteName, value);
    }

    private string? _activeRouteDetailMeta;
    /// <summary>
    /// Waypoint-count, total-distance and predominant-geometry summary for the
    /// active route (e.g. "4 WP · 4.1 NM · Rhumb").
    /// </summary>
    public string? ActiveRouteDetailMeta
    {
        get => _activeRouteDetailMeta;
        private set => SetProperty(ref _activeRouteDetailMeta, value);
    }

    private bool _isRenamingActiveRoute;
    /// <summary>True while the active route name is being edited inline.</summary>
    public bool IsRenamingActiveRoute
    {
        get => _isRenamingActiveRoute;
        private set => SetProperty(ref _isRenamingActiveRoute, value);
    }

    private string? _renameText;
    /// <summary>Working copy of the route name bound to the inline editor.</summary>
    public string? RenameText
    {
        get => _renameText;
        set => SetProperty(ref _renameText, value);
    }

    public ICommand AddRouteCommand { get; }
    public ICommand RemoveRouteCommand { get; }
    public ICommand RenameRouteCommand { get; }
    public ICommand ReverseRouteCommand { get; }
    public ICommand ReverseActiveRouteCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand ZoomToRouteCommand { get; }
    public ICommand BeginRenameCommand { get; }
    public ICommand CommitRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand SelectWaypointCommand { get; }

    /// <summary>Selects a leg: the map haloes it and always shows its label.</summary>
    public ICommand SelectLegCommand { get; }
    public ICommand InsertWaypointAfterCommand { get; }
    public ICommand DeleteWaypointAtCommand { get; }
    public ICommand ToggleLegGeometryAtCommand { get; }

    /// <summary>Opens a leg's inline detail, closing any other; on the open leg, closes it.</summary>
    public ICommand ToggleLegDetailCommand { get; }
    public ICommand DeleteWaypointCommand { get; }

    /// <summary>Import route…: picks a file and opens it as a received route (F2).</summary>
    public IAsyncRelayCommand ImportRouteCommand { get; }

    /// <summary>Copy to edit: the footer, the ⋯ menu, Ctrl+E and the waypoint menu (F5, A8).</summary>
    public ICommand CopyToEditCommand { get; }

    /// <summary>Export…: the route ⋯ menu (F7).</summary>
    public ICommand ExportRouteCommand { get; }

    /// <summary>"Drop them": drops preserved schedules that no longer match (F6).</summary>
    public ICommand DropStaleSchedulesCommand { get; }

    /// <summary>"Keep and export as they are" (F6).</summary>
    public ICommand KeepStaleSchedulesCommand { get; }

    /// <summary>Whether the panel can import, copy and export (absent in some tests).</summary>
    public bool CanExchange => _exchange is not null;

    /// <summary>Whether the footer shows Copy to edit in Edit's place (A8).</summary>
    public bool ShowCopyToEdit => IsActiveReadOnly && _exchange is not null;

    /// <summary>"Source has a newer edition (ed. 4)" on a copy whose source has one open (F4), else <c>null</c>.</summary>
    public string? NewerEditionText
        => _exchange is not null && _routes.Routes.ActiveRoute is { IsReadOnly: false } route && _exchange.NewerSourceEdition(route) is { } edition
            ? string.Format(CultureInfo.CurrentCulture, Strings.Routes_NewerEditionFormat, edition)
            : null;

    /// <summary>Whether <see cref="NewerEditionText"/> shows.</summary>
    public bool HasNewerEdition => NewerEditionText is not null;

    /// <summary>"2 schedules no longer match the route" (F6), else <c>null</c>.</summary>
    public string? StaleSchedulesText
        => _routes.Routes.ActiveRoute is { HasStalePreservedSchedules: true, Preserved: { } preserved }
            ? preserved.ScheduleCount == 1
                ? Strings.Routes_StaleSchedule
                : string.Format(CultureInfo.CurrentCulture, Strings.Routes_StaleSchedulesFormat, preserved.ScheduleCount)
            : null;

    /// <summary>Whether the stale-schedules notice shows.</summary>
    public bool HasStaleSchedules => StaleSchedulesText is not null;

    /// <summary>Whether the mariner chose to keep the stale schedules.</summary>
    public bool IsKeepingStaleSchedules => _routes.Routes.ActiveRoute?.KeepStalePreservedSchedules ?? false;

    private async Task ImportRouteAsync()
    {
        if (_exchange is null || _dialogs is null)
            return;
        if (await _dialogs.PickImportFileAsync(_exchange.Formats.FilePatterns) is not { } path)
            return;
        var result = await _exchange.ImportAsync(path);
        if (result.Route is null)
        {
            ShowError(Strings.ImportError_Title, result.Error ?? Strings.Import_NotARoute);
            return;
        }
        ZoomToActiveRoute();
    }

    private void CopyToEdit(Route? route)
    {
        if (_exchange is null || route is not { IsReadOnly: true })
            return;
        var preview = _exchange.PreviewCopy(route);
        if (_dialogs is null)
        {
            _exchange.Copy(route);
            return;
        }
        _dialogs.Show(new RouteCopyDialogViewModel(preview, () => _exchange.Copy(route), d => _dialogs.Close(d)), 520);
    }

    private void Export(Route? route)
    {
        if (_exchange is null || route is not { Waypoints.Count: >= 2 } || _dialogs is null)
            return;
        _dialogs.Show(new RouteExportDialogViewModel(_exchange, route, _dialogs, d => _dialogs.Close(d)), 500);
    }

    private void ShowError(string title, string message)
        => _dialogs?.Show(new RouteImportErrorDialogViewModel(title, message, d => _dialogs.Close(d)), 420);

    private void AddRoute()
    {
        var name = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Routes_NewRouteNameFormat,
            _routes.Routes.Routes.Count + 1);
        _routes.Routes.CreateRoute(name);
    }

    private void RemoveRoute(RouteRowViewModel? row)
    {
        var route = row?.Route ?? SelectedRoute?.Route;
        if (route is not null)
            _routes.Routes.Remove(route);
    }

    private void RenameRoute(RouteRowViewModel? row)
    {
        if (row?.Route is { IsReadOnly: false } route)
        {
            _routes.Routes.SetActiveRoute(route);
            BeginRename();
        }
    }

    private static void Reverse(Route? route)
    {
        if (route is { IsReadOnly: false })
            route.History.Execute(RouteEditLabels.Reverse, route.Reverse);
    }

    private void ZoomToActiveRoute()
    {
        if (_viewport?.Current is not { } viewport ||
            _routes.Routes.ActiveRoute is not { Waypoints.Count: > 0 } route)
        {
            return;
        }

        if (ResolveZoomExtent(route) is { } extent)
            viewport.ZoomToExtent(extent);
    }

    /// <summary>
    /// The EPSG:3857 extent <see cref="ZoomToRouteCommand"/> zooms to: the
    /// route's waypoints, or a small box around a single waypoint.
    /// </summary>
    internal static MRect? ResolveZoomExtent(Route route)
    {
        if (route.Waypoints.Count == 0)
            return null;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var waypoint in route.Waypoints)
        {
            var (x, y) = SphericalMercator.FromLonLat(waypoint.Position.Longitude, waypoint.Position.Latitude);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (maxX - minX < 1.0 && maxY - minY < 1.0)
        {
            var (cx, cy) = ((minX + maxX) / 2.0, (minY + maxY) / 2.0);
            return new MRect(
                cx - SinglePointZoomHalfMetres, cy - SinglePointZoomHalfMetres,
                cx + SinglePointZoomHalfMetres, cy + SinglePointZoomHalfMetres);
        }
        return new MRect(minX, minY, maxX, maxY);
    }

    // The route being renamed: the name commits to it even after another
    // route becomes active.
    private Route? _renaming;

    private void BeginRename()
    {
        if (_routes.Routes.ActiveRoute is not { } route)
            return;
        _renaming = route;
        RenameText = route.Name;
        IsRenamingActiveRoute = true;
    }

    private void CommitRename()
    {
        if (!IsRenamingActiveRoute)
            return;
        IsRenamingActiveRoute = false;
        var route = _renaming;
        _renaming = null;
        if (route is not null && _routes.Routes.Routes.Contains(route) && !string.IsNullOrWhiteSpace(RenameText))
        {
            var name = RenameText;
            route.History.Execute(RouteEditLabels.Rename, () => route.Name = name);
        }
    }

    private void CancelRename()
    {
        IsRenamingActiveRoute = false;
        _renaming = null;
    }

    private void SelectWaypoint(RouteWaypointRowViewModel? row)
    {
        if (row is null)
            return;
        _routes.SelectedWaypointIndex = row.Index;
    }

    private void InsertWaypointAfter(RouteWaypointRowViewModel? row)
    {
        if (row is not null)
            InsertAfter(row.Index);
    }

    private void InsertAfter(int index)
    {
        if (_routes.Routes.ActiveRoute is { IsReadOnly: true })
            return;
        if (_routes.Routes.ActiveRoute is not { } route)
            return;
        if (index < 0 || index >= route.Waypoints.Count)
            return;

        GeoPosition position;
        if (index + 1 < route.Waypoints.Count)
        {
            // Midpoint of the leg leaving the selected waypoint.
            var a = route.Waypoints[index].Position;
            var b = route.Waypoints[index + 1].Position;
            position = new GeoPosition(
                (a.Latitude + b.Latitude) / 2.0,
                (a.Longitude + b.Longitude) / 2.0);
        }
        else
        {
            // Selected waypoint is the last one: extend along the inbound
            // bearing by mirroring the previous waypoint about it, or just
            // nudge east when the route has a single waypoint.
            var last = route.Waypoints[index].Position;
            if (index > 0)
            {
                var prev = route.Waypoints[index - 1].Position;
                position = new GeoPosition(
                    last.Latitude + (last.Latitude - prev.Latitude),
                    last.Longitude + (last.Longitude - prev.Longitude));
            }
            else
            {
                position = new GeoPosition(last.Latitude, last.Longitude + 0.01);
            }
        }

        route.History.Execute(RouteEditLabels.InsertWaypoint, () => route.InsertWaypoint(index + 1, position));
        _routes.SelectedWaypointIndex = index + 1;
    }

    private void DeleteWaypointAt(RouteWaypointRowViewModel? row)
    {
        if (_routes.Routes.ActiveRoute is { IsReadOnly: true })
            return;
        if (row is not null &&
            _routes.Routes.ActiveRoute is { } route &&
            row.Index >= 0 && row.Index < route.Waypoints.Count)
        {
            route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(row.Index));
            _routes.SelectedWaypointIndex = null;
        }
    }

    private void ToggleLegGeometryAt(RouteLegRowViewModel? row)
    {
        if (_routes.Routes.ActiveRoute is { IsReadOnly: true })
            return;
        if (_routes.Routes.ActiveRoute is not { } route ||
            row is null ||
            row.LegIndex < 0 || row.LegIndex >= route.Legs.Count)
        {
            return;
        }

        var leg = route.Legs[row.LegIndex];
        route.History.Execute(RouteEditLabels.EditLeg, () =>
        {
            leg.GeometryType = leg.GeometryType == RouteLegGeometryType.Geodesic
                ? RouteLegGeometryType.Loxodrome
                : RouteLegGeometryType.Geodesic;
            route.NotifyChanged();
        });
    }

    private void ToggleLegDetail(RouteLegRowViewModel? row)
    {
        if (row is null)
            return;
        // Opening a leg also selects it, so the map haloes the leg being edited.
        _routes.SelectedLegIndex = row.LegIndex;
        _openLegIndex = _openLegIndex == row.LegIndex ? null : row.LegIndex;
        RebuildTimeline();
    }

    /// <summary>
    /// Renames the waypoint at <paramref name="index"/>; an empty or blank
    /// name clears it so the row falls back to "Waypoint N".
    /// </summary>
    internal void CommitWaypointName(int index, string? text)
    {
        if (_routes.Routes.ActiveRoute is { IsReadOnly: true })
            return;
        if (_routes.Routes.ActiveRoute is not { } route || index < 0 || index >= route.Waypoints.Count)
            return;

        var waypoint = route.Waypoints[index];
        var name = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (name == waypoint.Name)
            return;
        route.History.Execute(RouteEditLabels.EditWaypoint, () =>
        {
            waypoint.Name = name;
            route.NotifyChanged();
        });
    }

    /// <summary>
    /// Moves the waypoint at <paramref name="index"/> to the position typed or
    /// pasted as <paramref name="text"/>, in the app's degrees-minutes format
    /// or any form <see cref="LatLonFormatter.TryParse"/> accepts.
    /// </summary>
    /// <returns><c>false</c>, leaving the waypoint where it is, when the text
    /// is not a valid position.</returns>
    internal bool CommitWaypointPosition(int index, string? text)
    {
        if (_routes.Routes.ActiveRoute is { IsReadOnly: true })
            return false;
        if (!LatLonFormatter.TryParse(text, out var latitude, out var longitude))
            return false;
        if (_routes.Routes.ActiveRoute is not { } route || index < 0 || index >= route.Waypoints.Count)
            return true;

        var position = new GeoPosition(latitude, longitude);
        route.History.Execute(RouteEditLabels.MoveWaypoint, () => route.MoveWaypoint(index, position));
        return true;
    }

    private void OnEditModeChanged()
    {
        if (!_routes.IsEditModeActive)
            CommitRename();
        OnPropertyChanged(nameof(IsEditing));
    }

    private void OnCheckModeChanged()
    {
        OnPropertyChanged(nameof(IsCheckingPassage));
        OnPropertyChanged(nameof(ShowCheckPanel));
        RaiseReceivedState();
        Check?.Refresh();
    }

    private void RefreshHistory()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        ((RelayCommand)UndoCommand).NotifyCanExecuteChanged();
        ((RelayCommand)RedoCommand).NotifyCanExecuteChanged();
    }

    private void RefreshSelection()
    {
        // Selection changes only move the highlight: rebuilding the rows would
        // drop the focus of a waypoint name or position being typed.
        var selected = _routes.SelectedWaypointIndex;
        foreach (var row in Timeline.OfType<RouteWaypointRowViewModel>())
            row.IsSelected = row.Index == selected;
        var selectedLeg = _routes.SelectedLegIndex;
        foreach (var row in Timeline.OfType<RouteLegRowViewModel>())
            row.IsSelected = row.LegIndex == selectedLeg;
        ((RelayCommand)DeleteWaypointCommand).NotifyCanExecuteChanged();
    }

    private void Rebuild()
    {
        // Choosing another route ends the rename, keeping the name typed so far.
        if (IsRenamingActiveRoute && !ReferenceEquals(_renaming, _routes.Routes.ActiveRoute))
            CommitRename();

        _suppressSync = true;
        try
        {
            Routes.Clear();
            RouteRowViewModel? activeRow = null;
            foreach (var route in _routes.Routes.Routes)
            {
                var isActive = ReferenceEquals(route, _routes.Routes.ActiveRoute);
                var row = new RouteRowViewModel(route, isActive, RouteMeta(route));
                Routes.Add(row);
                if (isActive)
                    activeRow = row;
            }

            SelectedRoute = activeRow;
            RebuildTimeline();
        }
        finally
        {
            _suppressSync = false;
        }

        OnPropertyChanged(nameof(HasRoutes));
        OnPropertyChanged(nameof(HasActiveRoute));
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(CanCheckPassage));
        OnPropertyChanged(nameof(CheckPassageTooltip));
        RaiseReceivedState();

        // Check passage needs a route with legs: losing them turns the mode off.
        if (_routes.IsCheckModeActive && !CanCheckPassage)
            _routes.IsCheckModeActive = false;
        ((RelayCommand)ReverseActiveRouteCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ZoomToRouteCommand).NotifyCanExecuteChanged();
        ((RelayCommand)DeleteWaypointCommand).NotifyCanExecuteChanged();
        ((RelayCommand)BeginRenameCommand).NotifyCanExecuteChanged();
        RefreshHistory();
    }

    private void RebuildTimeline()
    {
        var route = _routes.Routes.ActiveRoute;
        if (!ReferenceEquals(route, _shownRoute))
        {
            // A different route: close any open leg, and open the Passage
            // card only when the route already has passage values.
            _shownRoute = route;
            _openLegIndex = null;
            Passage.Refresh();
            Passage.IsExpanded = !Passage.IsEmpty;
        }
        else
        {
            Passage.Refresh();
        }

        if (route is null)
        {
            Timeline.Clear();
            ActiveRouteName = null;
            ActiveRouteDetailMeta = null;
            return;
        }

        ActiveRouteName = route.Name;
        if (_openLegIndex >= route.Legs.Count)
            _openLegIndex = null;

        // Rows are updated in place when the route keeps its shape, so an
        // open leg detail (and the field being tabbed to) survives an edit;
        // waypoint rows are replaced, as their fields commit on focus loss.
        var rowCount = Math.Max(0, (route.Waypoints.Count * 2) - 1);
        var reuse = Timeline.Count == rowCount;
        if (!reuse)
            Timeline.Clear();

        var metrics = route.ComputeAllLegMetrics();
        var etas = RouteEtas.Compute(route, _routes.ViewTime.Current);
        var selected = _routes.SelectedWaypointIndex;
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            var wp = route.Waypoints[i];
            var fallbackName = string.Format(CultureInfo.CurrentCulture, Strings.Routes_WaypointNameFormat, i + 1);
            var coords = LatLonFormatter.Format(wp.Position.Latitude, wp.Position.Longitude);
            var waypointRow = new RouteWaypointRowViewModel(this, i, wp.Name, fallbackName, coords)
            {
                IsSelected = selected == i,
                IsReadOnly = route.IsReadOnly,
                IsFirst = i == 0,
                IsLast = i == route.Waypoints.Count - 1,
            };
            ShowEta(waypointRow, etas);
            SetRow(i * 2, waypointRow, reuse);

            if (i < metrics.Count)
            {
                var legRow = reuse && Timeline[(i * 2) + 1] is RouteLegRowViewModel existing
                    ? existing
                    : new RouteLegRowViewModel(i);
                UpdateLegRow(route, legRow, metrics[i], etas.LegDuration(i));
                SetRow((i * 2) + 1, legRow, reuse);
            }
        }

        ActiveRouteDetailMeta = DetailMeta(route, etas);
    }

    /// <summary>
    /// The route header's sub-line (B6): <c>9 WP · 24,1 NM · Rhumb</c>, or with
    /// a speed <c>9 WP · 24,1 NM · 2 h 10 min · arrives 09:21</c>, saying when
    /// the arrival runs from the view time and where ETAs stop for want of a speed.
    /// </summary>
    private string DetailMeta(Route route, RouteEtas etas)
    {
        var total = route.TotalDistanceNm();
        if (etas.FirstLegWithoutSpeed is { } stop)
        {
            return string.Format(
                CultureInfo.CurrentCulture, Strings.Routes_DetailEtasStopFormat,
                route.Waypoints.Count, total, stop + 1, stop + 2);
        }
        if (etas is { Arrival: { } arrival, Duration: { } duration })
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                etas.IsFromViewTime ? Strings.Routes_DetailArrivalFromViewTimeFormat : Strings.Routes_DetailArrivalFormat,
                route.Waypoints.Count,
                total,
                PassageFormatting.FormatDuration(duration),
                PassageFormatting.FormatEta(arrival, etas.Departure, TimeFormat));
        }
        return route.Legs.Count == 0
            ? string.Format(CultureInfo.CurrentCulture, Strings.Routes_DetailMetaFormat, route.Waypoints.Count, total)
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.Routes_DetailMetaWithGeometryFormat,
                route.Waypoints.Count,
                total,
                PredominantGeometry(route));
    }

    private void ShowEta(RouteWaypointRowViewModel row, RouteEtas etas)
    {
        if (etas.WaypointEta(row.Index) is { } eta)
        {
            row.Eta = PassageFormatting.FormatEta(eta, etas.Departure, TimeFormat);
            row.EtaTooltip = string.Format(
                CultureInfo.CurrentCulture,
                etas.IsFromViewTime ? Strings.Tooltip_WaypointEtaFromViewTime : Strings.Tooltip_WaypointEta,
                TimeFormatting.Format(eta, TimeFormat));
        }
        else
        {
            row.Eta = null;
            row.EtaTooltip = null;
        }
    }

    /// <summary>
    /// Moves the ETAs with the view time when the active route has no
    /// departure, updating the rows in place: the view time moves often (Live),
    /// and a rebuild would take focus from a field being typed in.
    /// </summary>
    private void OnViewTimeChanged()
    {
        if (_routes.Routes.ActiveRoute is not { Passage.Departure: null, Legs.Count: > 0 } route)
            return;
        var etas = RouteEtas.Compute(route, _routes.ViewTime.Current);
        if (!etas.HasEtas)
            return;
        foreach (var row in Timeline.OfType<RouteWaypointRowViewModel>())
            ShowEta(row, etas);
        ActiveRouteDetailMeta = DetailMeta(route, etas);
    }

    private void SetRow(int index, RouteTimelineRowViewModel row, bool reuse)
    {
        if (!reuse)
            Timeline.Add(row);
        else if (!ReferenceEquals(Timeline[index], row))
            Timeline[index] = row;
    }

    private void UpdateLegRow(Route route, RouteLegRowViewModel row, RouteLegMetrics metrics, TimeSpan? duration)
    {
        var i = metrics.LegIndex;
        var legText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Routes_LegMetaFormat,
            metrics.DistanceNm,
            metrics.InitialBearingDegrees);
        if (duration is { } d)
            legText += " · " + PassageFormatting.FormatDuration(d);
        var isGeodesic = route.Legs[i].GeometryType == RouteLegGeometryType.Geodesic;
        var geometryText = isGeodesic ? Strings.Routes_GeometryGeodesic : Strings.Routes_GeometryLoxodrome;

        var isOpen = _openLegIndex == i;
        if (isOpen)
        {
            var title = string.Format(CultureInfo.CurrentCulture, Strings.Routes_LegDetailTitleFormat, i + 1, i + 2);
            LegDetail.Show(i, title, legText);
        }
        row.Update(legText, geometryText, isGeodesic, OverrideText(route, i), isOpen ? LegDetail : null);
        row.IsSelected = _routes.SelectedLegIndex == i;
    }

    /// <summary>
    /// The leg row's override summary, e.g. "8,0 kn · 10,0 m", or <c>null</c>
    /// when the leg inherits every passage value.
    /// </summary>
    private string? OverrideText(Route route, int legIndex)
    {
        var leg = route.Legs[legIndex];
        var unit = DepthUnit;
        var parts = new List<string>();
        if (leg.PlannedSpeedKnots is { } speed)
            parts.Add(PassageFormatting.Format(speed, PassageQuantity.Speed, unit));
        if (leg.DraftMeters is { } draft)
            parts.Add(PassageFormatting.Format(draft, PassageQuantity.Depth, unit));
        if (leg.PortCrossTrackDistanceLimitMeters is not null || leg.StarboardCrossTrackDistanceLimitMeters is not null)
        {
            parts.Add(RoutePassageCardViewModel.FormatCrossTrack(
                route.GetEffectiveLegValue(legIndex, RoutePassageField.PortCrossTrack),
                route.GetEffectiveLegValue(legIndex, RoutePassageField.StarboardCrossTrack))!);
        }
        if (leg.StaticUnderKeelClearanceMeters is { } ukc)
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Routes_SummaryUkcFormat,
                PassageFormatting.Format(ukc, PassageQuantity.Depth, unit)));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string RouteMeta(Route route)
        => string.Format(
            CultureInfo.CurrentCulture,
            route.IsReadOnly ? Strings.Routes_RouteMetaReceivedFormat : Strings.Routes_RouteMetaFormat,
            route.Waypoints.Count,
            route.TotalDistanceNm());

    private static string PredominantGeometry(Route route)
    {
        var geodesic = 0;
        var loxodrome = 0;
        foreach (var leg in route.Legs)
        {
            if (leg.GeometryType == RouteLegGeometryType.Geodesic)
                geodesic++;
            else
                loxodrome++;
        }

        if (geodesic > 0 && loxodrome > 0)
            return Strings.Routes_GeometryMixed;
        return geodesic > 0 ? Strings.Routes_GeometryGeodesic : Strings.Routes_GeometryLoxodrome;
    }
}

/// <summary>A single route row in the routes list.</summary>
internal sealed class RouteRowViewModel : ViewModelBase
{
    public RouteRowViewModel(Route route, bool isActive, string meta)
    {
        ArgumentNullException.ThrowIfNull(route);
        Route = route;
        _isActive = isActive;
        Meta = meta;
    }

    /// <summary>The underlying editable route.</summary>
    public Route Route { get; }

    /// <summary>Whether the route is a received, read-only route: its menu offers Close instead of Rename, Reverse and Remove.</summary>
    public bool IsReceived => Route.IsReadOnly;

    /// <summary>Whether the route is owned (editable).</summary>
    public bool IsOwned => !Route.IsReadOnly;

    /// <summary>Display name of the route.</summary>
    public string? Name => Route.Name;

    /// <summary>Waypoint-count + total-distance summary (e.g. "4 WP · 4.1 NM").</summary>
    public string Meta { get; }

    private bool _isActive;
    /// <summary>True when this is the collection's active route.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

/// <summary>
/// Base type for a row in the active route's waypoint/leg timeline. Concrete
/// rows are <see cref="RouteWaypointRowViewModel"/> and
/// <see cref="RouteLegRowViewModel"/>.
/// </summary>
internal abstract class RouteTimelineRowViewModel : ViewModelBase
{
}

/// <summary>
/// A numbered waypoint row in the timeline, with an editable name and an
/// editable, pasteable position.
/// </summary>
internal sealed class RouteWaypointRowViewModel : RouteTimelineRowViewModel
{
    private readonly RoutesPanelViewModel _panel;
    private readonly string? _name;
    private readonly string _coords;

    public RouteWaypointRowViewModel(
        RoutesPanelViewModel panel,
        int index,
        string? name,
        string fallbackName,
        string coords)
    {
        _panel = panel;
        Index = index;
        Number = index + 1;
        _name = name;
        Label = string.IsNullOrWhiteSpace(name) ? fallbackName : name;
        FallbackName = fallbackName;
        _coords = coords;
        _nameText = name ?? string.Empty;
        _coordsText = coords;

        CommitNameCommand = new RelayCommand(CommitName);
        RevertNameCommand = new RelayCommand(() => NameText = _name ?? string.Empty);
        CommitCoordsCommand = new RelayCommand(CommitCoords);
        RevertCoordsCommand = new RelayCommand(() =>
        {
            CoordsText = _coords;
            CoordsError = null;
        });
    }

    /// <summary>
    /// Insert-a-waypoint-after command, surfaced on the row so the overflow
    /// <c>MenuFlyout</c> can bind to its own <see cref="ViewModelBase"/>
    /// DataContext. Ancestor bindings (e.g. <c>$parent[ItemsControl]</c>) do
    /// not resolve from inside a flyout popup, but the inherited DataContext
    /// does, so command access must live on the row item itself.
    /// </summary>
    public ICommand InsertAfterCommand => _panel.InsertWaypointAfterCommand;

    /// <summary>
    /// Delete-this-waypoint command, surfaced on the row for the same
    /// flyout-binding reason as <see cref="InsertAfterCommand"/>.
    /// </summary>
    public ICommand DeleteCommand => _panel.DeleteWaypointAtCommand;

    /// <summary>Copy to edit, offered by a received route's waypoint menu (#960, A8).</summary>
    public ICommand CopyToEditCommand => _panel.CopyToEditCommand;

    /// <summary>Whether the route is a received, read-only route: the row can't be edited.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Commits <see cref="NameText"/> as the waypoint's name (Enter or focus loss).</summary>
    public ICommand CommitNameCommand { get; }

    /// <summary>Restores <see cref="NameText"/> to the waypoint's name (Esc).</summary>
    public ICommand RevertNameCommand { get; }

    /// <summary>
    /// Moves the waypoint to the position in <see cref="CoordsText"/> (Enter or
    /// focus loss), or shows <see cref="CoordsError"/> when it isn't one.
    /// </summary>
    public ICommand CommitCoordsCommand { get; }

    /// <summary>Restores <see cref="CoordsText"/> to the waypoint's position and clears the error (Esc).</summary>
    public ICommand RevertCoordsCommand { get; }

    /// <summary>Zero-based waypoint index within the active route.</summary>
    public int Index { get; }

    /// <summary>One-based display number shown in the number disc.</summary>
    public int Number { get; }

    /// <summary>Waypoint name or a "Waypoint N" fallback.</summary>
    public string Label { get; }

    /// <summary>The "Waypoint N" name shown as the name field's placeholder.</summary>
    public string FallbackName { get; }

    private string _nameText;
    /// <summary>The name being edited; empty clears the name.</summary>
    public string NameText
    {
        get => _nameText;
        set => SetProperty(ref _nameText, value);
    }

    private string _coordsText;
    /// <summary>The position being edited, in the app's degrees-minutes format.</summary>
    public string CoordsText
    {
        get => _coordsText;
        set
        {
            if (SetProperty(ref _coordsText, value))
                CoordsError = null;
        }
    }

    private string? _coordsError;
    /// <summary>The inline error under the position, or <c>null</c> when it is valid.</summary>
    public string? CoordsError
    {
        get => _coordsError;
        private set
        {
            if (SetProperty(ref _coordsError, value))
                OnPropertyChanged(nameof(HasCoordsError));
        }
    }

    /// <summary>True when <see cref="CoordsError"/> is shown.</summary>
    public bool HasCoordsError => _coordsError is not null;

    private bool _isSelected;
    /// <summary>True when this is the highlighted waypoint.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>True for the first waypoint (no inbound connector).</summary>
    public bool IsFirst { get; init; }

    /// <summary>True for the last waypoint (no outbound connector).</summary>
    public bool IsLast { get; init; }

    private string? _eta;
    /// <summary>The ETA at this waypoint (A8), e.g. "09:21", or <c>null</c> when unknown.</summary>
    public string? Eta
    {
        get => _eta;
        set
        {
            if (SetProperty(ref _eta, value))
                OnPropertyChanged(nameof(HasEta));
        }
    }

    /// <summary>True when <see cref="Eta"/> is shown.</summary>
    public bool HasEta => _eta is not null;

    private string? _etaTooltip;
    /// <summary>The ETA's full date and time, and whether it runs from the view time.</summary>
    public string? EtaTooltip
    {
        get => _etaTooltip;
        set => SetProperty(ref _etaTooltip, value);
    }

    private void CommitName()
    {
        if (NameText != (_name ?? string.Empty))
            _panel.CommitWaypointName(Index, NameText);
    }

    private void CommitCoords()
    {
        if (CoordsText == _coords)
        {
            CoordsError = null;
            return;
        }
        if (!_panel.CommitWaypointPosition(Index, CoordsText))
            CoordsError = Strings.Routes_CoordinateError;
    }
}

/// <summary>
/// A leg row sitting between two waypoint rows in the timeline. It is updated
/// in place across edits, so its open detail keeps focus.
/// </summary>
internal sealed class RouteLegRowViewModel : RouteTimelineRowViewModel
{
    public RouteLegRowViewModel(int legIndex)
    {
        LegIndex = legIndex;
    }

    /// <summary>Zero-based leg index within the active route.</summary>
    public int LegIndex { get; }

    private string _legText = string.Empty;
    /// <summary>Distance/bearing of the leg, pre-formatted (e.g. "1.6 NM · 070°T").</summary>
    public string LegText
    {
        get => _legText;
        private set => SetProperty(ref _legText, value);
    }

    private string _geometryText = string.Empty;
    /// <summary>Geometry label ("Rhumb" / "Great circle").</summary>
    public string GeometryText
    {
        get => _geometryText;
        private set => SetProperty(ref _geometryText, value);
    }

    private bool _isGeodesic;
    /// <summary>True when the leg uses great-circle geometry.</summary>
    public bool IsGeodesic
    {
        get => _isGeodesic;
        private set => SetProperty(ref _isGeodesic, value);
    }

    private string? _overrideText;
    /// <summary>The leg's overridden passage values, e.g. "8,0 kn", or <c>null</c>.</summary>
    public string? OverrideText
    {
        get => _overrideText;
        private set
        {
            if (SetProperty(ref _overrideText, value))
                OnPropertyChanged(nameof(HasOverride));
        }
    }

    /// <summary>True when the leg overrides a passage value (the row shows a blue dot).</summary>
    public bool HasOverride => _overrideText is not null;

    private RouteLegDetailViewModel? _detail;
    /// <summary>The inline detail when this leg is open, else <c>null</c>.</summary>
    public RouteLegDetailViewModel? Detail
    {
        get => _detail;
        private set
        {
            if (SetProperty(ref _detail, value))
                OnPropertyChanged(nameof(IsOpen));
        }
    }

    /// <summary>True when this leg's detail is open.</summary>
    public bool IsOpen => _detail is not null;

    /// <summary>Updates the row from the route.</summary>
    public void Update(string legText, string geometryText, bool isGeodesic, string? overrideText, RouteLegDetailViewModel? detail)
    {
        LegText = legText;
        GeometryText = geometryText;
        IsGeodesic = isGeodesic;
        OverrideText = overrideText;
        Detail = detail;
    }

    private bool _isSelected;
    /// <summary>True when this is the selected leg (haloed on the map).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
