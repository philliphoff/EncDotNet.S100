using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
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
/// and Check passage toggles. All mutations flow back into the shared
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
    private readonly IRouteDisplayUnits _units;
    private bool _suppressSync;

    public RoutesPanelViewModel(
        RoutesService routes,
        ICapabilityAccessor<IMapViewportController>? viewport = null,
        IRouteDisplayUnits? units = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes;
        _viewport = viewport;
        _units = units ?? DefaultRouteDisplayUnits.Instance;
        var clock = time ?? TimeProvider.System;
        PassageCard = new PassageCardViewModel(routes, _units, clock.GetUtcNow);
        CloseLegCommand = new RelayCommand(() => _routes.SelectedLegIndex = null);

        Routes = new ObservableCollection<RouteRowViewModel>();
        Timeline = new ObservableCollection<RouteTimelineRowViewModel>();

        AddRouteCommand = new RelayCommand(AddRoute);
        RemoveRouteCommand = new RelayCommand<RouteRowViewModel>(RemoveRoute);
        RenameRouteCommand = new RelayCommand<RouteRowViewModel>(RenameRoute);
        ReverseRouteCommand = new RelayCommand<RouteRowViewModel>(row => Reverse(row?.Route));

        ReverseActiveRouteCommand = new RelayCommand(
            () => Reverse(_routes.Routes.ActiveRoute),
            () => _routes.Routes.ActiveRoute is { Waypoints.Count: >= 2 });
        UndoCommand = new RelayCommand(() => _routes.UndoActiveRoute(), () => CanUndo);
        RedoCommand = new RelayCommand(() => _routes.RedoActiveRoute(), () => CanRedo);
        ZoomToRouteCommand = new RelayCommand(
            ZoomToActiveRoute,
            () => _routes.Routes.ActiveRoute is { Waypoints.Count: > 0 });

        BeginRenameCommand = new RelayCommand(BeginRename, () => HasActiveRoute);
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

        DeleteWaypointCommand = new RelayCommand(
            () => _routes.DeleteSelectedWaypoint(),
            () => _routes.SelectedWaypointIndex is not null);

        _routes.Routes.Changed += (_, _) => Rebuild();
        _routes.Routes.HistoryChanged += (_, _) => RefreshHistory();
        _routes.SelectionChanged += (_, _) => RefreshSelection();
        _routes.EditModeChanged += (_, _) => OnEditModeChanged();
        _units.Changed += (_, _) => Rebuild();
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

    /// <summary>The Passage card under the route header.</summary>
    public PassageCardViewModel PassageCard { get; }

    /// <summary>Closes the open leg's detail (clears the leg selection).</summary>
    public ICommand CloseLegCommand { get; }

    /// <summary>
    /// Whether Check passage can be turned on. It needs a draft (handoff B8);
    /// the mode itself arrives with #901, so until then it stays off.
    /// </summary>
    public bool CanCheckPassage => false;

    /// <summary>Whether Check passage is on. Always <c>false</c> until it can be enabled.</summary>
    public bool IsCheckingPassage => false;

    /// <summary>Tooltip on the Check passage toggle: "Add a draft to check clearance" while there is no draft.</summary>
    public string CheckPassageTooltip => PassageCard.HasDraft
        ? Strings.Button_CheckPassage
        : Strings.Tooltip_CheckPassageNoDraft;

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
    public ICommand DeleteWaypointCommand { get; }

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
        if (row?.Route is { } route)
        {
            _routes.Routes.SetActiveRoute(route);
            BeginRename();
        }
    }

    private static void Reverse(Route? route)
        => route?.History.Execute(RouteEditLabels.Reverse, route.Reverse);

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

    private void BeginRename()
    {
        if (_routes.Routes.ActiveRoute is not { } route)
            return;
        RenameText = route.Name;
        IsRenamingActiveRoute = true;
    }

    private void CommitRename()
    {
        if (!IsRenamingActiveRoute)
            return;
        IsRenamingActiveRoute = false;
        if (_routes.Routes.ActiveRoute is { } route && !string.IsNullOrWhiteSpace(RenameText))
        {
            var name = RenameText;
            route.History.Execute(RouteEditLabels.Rename, () => route.Name = name);
        }
    }

    private void CancelRename()
    {
        IsRenamingActiveRoute = false;
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

    /// <summary>
    /// Renames the waypoint at <paramref name="index"/>; an empty or blank
    /// name clears it so the row falls back to "Waypoint N".
    /// </summary>
    internal void CommitWaypointName(int index, string? text)
    {
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
            PassageCard.Refresh(_routes.Routes.ActiveRoute);
        }
        finally
        {
            _suppressSync = false;
        }

        OnPropertyChanged(nameof(HasRoutes));
        OnPropertyChanged(nameof(HasActiveRoute));
        OnPropertyChanged(nameof(CheckPassageTooltip));
        ((RelayCommand)ReverseActiveRouteCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ZoomToRouteCommand).NotifyCanExecuteChanged();
        ((RelayCommand)DeleteWaypointCommand).NotifyCanExecuteChanged();
        ((RelayCommand)BeginRenameCommand).NotifyCanExecuteChanged();
        RefreshHistory();
    }

    private void RebuildTimeline()
    {
        Timeline.Clear();
        if (_routes.Routes.ActiveRoute is not { } route)
        {
            ActiveRouteName = null;
            ActiveRouteDetailMeta = null;
            return;
        }

        ActiveRouteName = route.Name;

        var metrics = route.ComputeAllLegMetrics();
        var selected = _routes.SelectedWaypointIndex;
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            var wp = route.Waypoints[i];
            var fallbackName = string.Format(CultureInfo.CurrentCulture, Strings.Routes_WaypointNameFormat, i + 1);
            var coords = LatLonFormatter.Format(wp.Position.Latitude, wp.Position.Longitude);
            Timeline.Add(new RouteWaypointRowViewModel(this, i, wp.Name, fallbackName, coords)
            {
                IsSelected = selected == i,
                IsFirst = i == 0,
                IsLast = i == route.Waypoints.Count - 1,
            });

            if (i < metrics.Count)
            {
                var m = metrics[i];
                var legText = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Routes_LegMetaFormat,
                    m.DistanceNm,
                    m.InitialBearingDegrees);
                var isGeodesic = route.Legs[i].GeometryType == RouteLegGeometryType.Geodesic;
                var geometryText = isGeodesic
                    ? Strings.Routes_GeometryGeodesic
                    : Strings.Routes_GeometryLoxodrome;
                Timeline.Add(new RouteLegRowViewModel(route, i, legText, geometryText, isGeodesic, _units, CloseLegCommand)
                {
                    IsSelected = _routes.SelectedLegIndex == i,
                });
            }
        }

        var total = route.TotalDistanceNm();
        ActiveRouteDetailMeta = route.Legs.Count == 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.Routes_DetailMetaFormat,
                route.Waypoints.Count,
                total)
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.Routes_DetailMetaWithGeometryFormat,
                route.Waypoints.Count,
                total,
                PredominantGeometry(route));
    }

    private static string RouteMeta(Route route)
        => string.Format(
            CultureInfo.CurrentCulture,
            Strings.Routes_RouteMetaFormat,
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
/// A leg row sitting between two waypoint rows in the timeline. Its
/// overrides show after the distance and bearing ("● 8,0 kn", handoff B4).
/// The selected leg opens inline as a detail card (B5): its passage values
/// (overridden or inherited, with Reset), the Rhumb / Great circle control
/// and a note. One leg is open at a time, because only one is selected.
/// </summary>
internal sealed class RouteLegRowViewModel : RouteTimelineRowViewModel
{
    private readonly Route? _route;
    private readonly IRouteDisplayUnits _units;

    public RouteLegRowViewModel(int legIndex, string legText, string geometryText, bool isGeodesic)
        : this(null, legIndex, legText, geometryText, isGeodesic, DefaultRouteDisplayUnits.Instance, null)
    {
    }

    internal RouteLegRowViewModel(
        Route? route,
        int legIndex,
        string legText,
        string geometryText,
        bool isGeodesic,
        IRouteDisplayUnits units,
        ICommand? closeCommand)
    {
        _route = route;
        _units = units;
        LegIndex = legIndex;
        LegText = legText;
        GeometryText = geometryText;
        IsGeodesic = isGeodesic;
        CloseCommand = closeCommand ?? new RelayCommand(() => { });

        Header = string.Format(CultureInfo.CurrentCulture, Strings.Leg_HeaderFormat, legIndex + 1, legIndex + 2);
        Speed = Field("Routes.Leg.Speed", Strings.Passage_Speed, Strings.Passage_InvalidSpeed, CommitSpeed, RoutePassageField.Speed);
        Draft = Field("Routes.Leg.Draft", Strings.Leg_Draft, PassageCardViewModel.InvalidDepth, t => CommitDepth(RoutePassageField.Draft, t), RoutePassageField.Draft);
        CrossTrack = Field("Routes.Leg.CrossTrack", Strings.Leg_CrossTrack, Strings.Passage_InvalidCrossTrack, CommitCrossTrack, RoutePassageField.PortCrossTrack, RoutePassageField.StarboardCrossTrack);
        RequiredUkc = Field("Routes.Leg.RequiredUkc", Strings.Passage_RequiredUkc, PassageCardViewModel.InvalidDepth, t => CommitDepth(RoutePassageField.RequiredUkc, t), RoutePassageField.RequiredUkc);

        SetRhumbCommand = new RelayCommand(() => SetGeometry(RouteLegGeometryType.Loxodrome));
        SetGreatCircleCommand = new RelayCommand(() => SetGeometry(RouteLegGeometryType.Geodesic));
        AddNoteCommand = new RelayCommand(() => IsNoteVisible = true);
        CommitNoteCommand = new RelayCommand(CommitNote);

        Refresh();
    }

    /// <summary>Zero-based leg index within the active route.</summary>
    public int LegIndex { get; }

    /// <summary>Distance/bearing of the leg, pre-formatted (e.g. "1.6 NM · 070°T").</summary>
    public string LegText { get; }

    /// <summary>Geometry chip label ("Rhumb" / "Great circle").</summary>
    public string GeometryText { get; }

    /// <summary>True when the leg uses great-circle geometry.</summary>
    public bool IsGeodesic { get; }

    /// <summary>True when the leg uses rhumb-line geometry.</summary>
    public bool IsRhumb => !IsGeodesic;

    private bool _isSelected;
    /// <summary>True when this is the selected leg: haloed on the map and open here.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(IsCollapsed));
        }
    }

    /// <summary>True when the leg shows as a plain row (it isn't open).</summary>
    public bool IsCollapsed => !_isSelected;

    /// <summary>The detail card's title, e.g. "Leg 6 → 7".</summary>
    public string Header { get; }

    private string _overrideSummary = string.Empty;
    /// <summary>The leg's overridden values, e.g. "8,0 kn · 9,5 m"; empty when it inherits everything.</summary>
    public string OverrideSummary
    {
        get => _overrideSummary;
        private set
        {
            if (SetProperty(ref _overrideSummary, value))
                OnPropertyChanged(nameof(HasOverrides));
        }
    }

    /// <summary>Whether the leg overrides any passage value.</summary>
    public bool HasOverrides => _overrideSummary.Length > 0;

    /// <summary>Speed for this leg.</summary>
    public PassageFieldViewModel Speed { get; }

    /// <summary>Draft for this leg.</summary>
    public PassageFieldViewModel Draft { get; }

    /// <summary>Cross-track limit, port / starboard, for this leg.</summary>
    public PassageFieldViewModel CrossTrack { get; }

    /// <summary>Required UKC for this leg.</summary>
    public PassageFieldViewModel RequiredUkc { get; }

    private string _note = string.Empty;
    /// <summary>The leg's note, as typed.</summary>
    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    private bool _isNoteVisible;
    /// <summary>Whether the note box shows: the leg has a note, or "+ Note" was chosen.</summary>
    public bool IsNoteVisible
    {
        get => _isNoteVisible;
        private set
        {
            if (SetProperty(ref _isNoteVisible, value))
                OnPropertyChanged(nameof(IsNoteHidden));
        }
    }

    /// <summary>Whether "+ Note" shows.</summary>
    public bool IsNoteHidden => !_isNoteVisible;

    /// <summary>The detail card's footnote.</summary>
    public string Footnote => Strings.Leg_Footnote;

    /// <summary>Closes the detail (clears the leg selection).</summary>
    public ICommand CloseCommand { get; }

    /// <summary>Makes the leg a rhumb line.</summary>
    public ICommand SetRhumbCommand { get; }

    /// <summary>Makes the leg a great circle.</summary>
    public ICommand SetGreatCircleCommand { get; }

    /// <summary>Shows the note box.</summary>
    public ICommand AddNoteCommand { get; }

    /// <summary>Saves <see cref="Note"/> (Enter or focus loss).</summary>
    public ICommand CommitNoteCommand { get; }

    private PassageFieldViewModel Field(string id, string label, string error, Func<string, bool> commit, params RoutePassageField[] fields)
        => new(id, label, error, commit, () => Edit(r =>
        {
            foreach (var field in fields)
                r.ResetLegOverride(LegIndex, field);
        }));

    private void Refresh()
    {
        if (_route is null || LegIndex >= _route.Legs.Count)
            return;

        var unit = _units.DepthUnit;
        string Depth(double? v) => v is { } x ? PassageText.Depth(x, unit) : string.Empty;
        string Speed(double? v) => v is { } x ? PassageText.Speed(x) : string.Empty;

        Load(this.Speed, Strings.Passage_Speed, RoutePassageField.Speed, Speed);
        Load(Draft, Strings.Leg_Draft, RoutePassageField.Draft, Depth);
        Load(RequiredUkc, Strings.Passage_RequiredUkc, RoutePassageField.RequiredUkc, Depth);

        var portOverride = _route.IsLegOverridden(LegIndex, RoutePassageField.PortCrossTrack);
        var starboardOverride = _route.IsLegOverridden(LegIndex, RoutePassageField.StarboardCrossTrack);
        var crossTrackOverridden = portOverride || starboardOverride;
        var crossTrack = PassageText.CrossTrack(
            _route.GetEffectiveLegValue(LegIndex, RoutePassageField.PortCrossTrack),
            _route.GetEffectiveLegValue(LegIndex, RoutePassageField.StarboardCrossTrack));
        CrossTrack.Load(
            crossTrackOverridden ? crossTrack : string.Empty,
            crossTrackOverridden ? Strings.Passage_Add : Inherited(crossTrack),
            crossTrackOverridden,
            OverrideLabel(Strings.Leg_CrossTrack, crossTrackOverridden));

        var parts = new List<string>();
        foreach (var field in RoutePassageFields.LegOverridable)
        {
            if (!_route.IsLegOverridden(LegIndex, field) || field == RoutePassageField.StarboardCrossTrack && portOverride)
                continue;
            parts.Add(field switch
            {
                RoutePassageField.Speed => Speed(_route.Legs[LegIndex].GetOverride(field)),
                RoutePassageField.PortCrossTrack or RoutePassageField.StarboardCrossTrack => crossTrack,
                _ => Depth(_route.Legs[LegIndex].GetOverride(field)),
            });
        }
        OverrideSummary = string.Join(" · ", parts);

        _note = _route.Legs[LegIndex].Note ?? string.Empty;
        OnPropertyChanged(nameof(Note));
        IsNoteVisible = _note.Length > 0;
    }

    private void Load(PassageFieldViewModel field, string label, RoutePassageField passageField, Func<double?, string> format)
    {
        var overridden = _route!.IsLegOverridden(LegIndex, passageField);
        var inherited = format(_route.Passage.Get(passageField));
        field.Load(
            overridden ? format(_route.Legs[LegIndex].GetOverride(passageField)) : string.Empty,
            overridden ? Strings.Passage_Add : Inherited(inherited),
            overridden,
            OverrideLabel(label, overridden));
    }

    private static string Inherited(string value) => value.Length > 0 ? value : Strings.Passage_Add;

    private static string OverrideLabel(string label, bool overridden)
        => overridden ? string.Format(CultureInfo.CurrentCulture, Strings.Leg_OverrideFormat, label) : label;

    private bool Edit(Action<Route> edit, string label = RouteEditLabels.EditLeg)
    {
        if (_route is null || LegIndex >= _route.Legs.Count)
            return true;
        _route.History.Execute(label, () => edit(_route));
        return true;
    }

    private bool CommitSpeed(string text)
    {
        double? speed = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!PassageText.TryParseSpeed(text, out var parsed))
                return false;
            speed = parsed;
        }
        return Edit(r => r.SetLegOverride(LegIndex, RoutePassageField.Speed, speed));
    }

    private bool CommitDepth(RoutePassageField field, string text)
    {
        double? value = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!PassageText.TryParseDepth(text, _units.DepthUnit, out var parsed))
                return false;
            value = parsed;
        }
        return Edit(r => r.SetLegOverride(LegIndex, field, value));
    }

    private bool CommitCrossTrack(string text)
    {
        if (!PassageText.TryParseCrossTrack(text, out var port, out var starboard))
            return false;
        return Edit(r =>
        {
            r.SetLegOverride(LegIndex, RoutePassageField.PortCrossTrack, port);
            r.SetLegOverride(LegIndex, RoutePassageField.StarboardCrossTrack, starboard);
        });
    }

    private void SetGeometry(RouteLegGeometryType type)
        => Edit(r =>
        {
            if (r.Legs[LegIndex].GeometryType == type)
                return;
            r.Legs[LegIndex].GeometryType = type;
            r.NotifyChanged();
        });

    private void CommitNote()
    {
        if (_route is null || LegIndex >= _route.Legs.Count)
            return;
        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        if (note == _route.Legs[LegIndex].Note)
            return;
        Edit(r =>
        {
            r.Legs[LegIndex].Note = note;
            r.NotifyChanged();
        });
    }
}
