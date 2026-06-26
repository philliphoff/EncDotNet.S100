using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View-model for the Routes activity-bar panel. Presents the editable
/// <see cref="RouteCollection"/> owned by <see cref="RoutesService"/>: the
/// list of routes (with add / rename / reverse / remove / select), and the
/// active route's waypoint-and-leg timeline (numbered waypoints interleaved
/// with the legs that join them, with insert / delete / leg-geometry toggle).
/// All mutations flow back into the shared <see cref="RoutesService"/>, so the
/// map overlay and the <c>RouteEditTool</c> stay in lock-step with the panel.
/// </summary>
internal sealed class RoutesPanelViewModel : ViewModelBase
{
    private readonly RoutesService _routes;
    private bool _suppressSync;

    public RoutesPanelViewModel(RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes;

        Routes = new ObservableCollection<RouteRowViewModel>();
        Timeline = new ObservableCollection<RouteTimelineRowViewModel>();

        AddRouteCommand = new RelayCommand(AddRoute);
        RemoveRouteCommand = new RelayCommand<RouteRowViewModel>(RemoveRoute);
        RenameRouteCommand = new RelayCommand<RouteRowViewModel>(RenameRoute);
        ReverseRouteCommand = new RelayCommand<RouteRowViewModel>(ReverseRoute);

        ReverseActiveRouteCommand = new RelayCommand(ReverseActiveRoute, () => HasActiveRoute);
        InsertAfterSelectedCommand = new RelayCommand(
            InsertAfterSelected,
            () => _routes.SelectedWaypointIndex is not null);

        BeginRenameCommand = new RelayCommand(BeginRename, () => HasActiveRoute);
        CommitRenameCommand = new RelayCommand(CommitRename);
        CancelRenameCommand = new RelayCommand(CancelRename);

        SelectWaypointCommand = new RelayCommand<RouteWaypointRowViewModel>(SelectWaypoint);
        InsertWaypointAfterCommand = new RelayCommand<RouteWaypointRowViewModel>(InsertWaypointAfter);
        DeleteWaypointAtCommand = new RelayCommand<RouteWaypointRowViewModel>(DeleteWaypointAt);
        ToggleLegGeometryAtCommand = new RelayCommand<RouteLegRowViewModel>(ToggleLegGeometryAt);

        DeleteWaypointCommand = new RelayCommand(
            DeleteSelectedWaypoint,
            () => _routes.SelectedWaypointIndex is not null);

        _routes.Changed += (_, _) => Rebuild();
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
    public ICommand InsertAfterSelectedCommand { get; }
    public ICommand BeginRenameCommand { get; }
    public ICommand CommitRenameCommand { get; }
    public ICommand CancelRenameCommand { get; }
    public ICommand SelectWaypointCommand { get; }
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

    private void ReverseRoute(RouteRowViewModel? row)
    {
        row?.Route.Reverse();
    }

    private void ReverseActiveRoute()
    {
        _routes.Routes.ActiveRoute?.Reverse();
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
        if (_routes.Routes.ActiveRoute is { } route)
            route.Name = string.IsNullOrWhiteSpace(RenameText) ? route.Name : RenameText;
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

    private void InsertAfterSelected()
    {
        if (_routes.SelectedWaypointIndex is { } index)
            InsertAfter(index);
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

        route.InsertWaypoint(index + 1, position);
        _routes.SelectedWaypointIndex = index + 1;
    }

    private void DeleteSelectedWaypoint()
    {
        if (_routes.SelectedWaypointIndex is { } index)
            DeleteWaypointIndex(index);
    }

    private void DeleteWaypointAt(RouteWaypointRowViewModel? row)
    {
        if (row is not null)
            DeleteWaypointIndex(row.Index);
    }

    private void DeleteWaypointIndex(int index)
    {
        if (_routes.Routes.ActiveRoute is { } route &&
            index >= 0 && index < route.Waypoints.Count)
        {
            route.RemoveWaypoint(index);
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
        leg.GeometryType = leg.GeometryType == RouteLegGeometryType.Geodesic
            ? RouteLegGeometryType.Loxodrome
            : RouteLegGeometryType.Geodesic;
        route.NotifyChanged();
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
        }
        finally
        {
            _suppressSync = false;
        }

        OnPropertyChanged(nameof(HasRoutes));
        OnPropertyChanged(nameof(HasActiveRoute));
        ((RelayCommand)ReverseActiveRouteCommand).NotifyCanExecuteChanged();
        ((RelayCommand)InsertAfterSelectedCommand).NotifyCanExecuteChanged();
        ((RelayCommand)DeleteWaypointCommand).NotifyCanExecuteChanged();
        ((RelayCommand)BeginRenameCommand).NotifyCanExecuteChanged();
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
            var label = string.IsNullOrWhiteSpace(wp.Name)
                ? string.Format(CultureInfo.CurrentCulture, Strings.Routes_WaypointNameFormat, i + 1)
                : wp.Name!;
            var coords = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Routes_WaypointCoordFormat,
                wp.Position.Latitude,
                wp.Position.Longitude);
            Timeline.Add(new RouteWaypointRowViewModel(i, i + 1, label, coords, InsertWaypointAfterCommand, DeleteWaypointAtCommand)
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
                Timeline.Add(new RouteLegRowViewModel(i, legText, geometryText, isGeodesic));
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

/// <summary>A numbered waypoint row in the timeline.</summary>
internal sealed class RouteWaypointRowViewModel : RouteTimelineRowViewModel
{
    public RouteWaypointRowViewModel(
        int index,
        int number,
        string label,
        string coords,
        ICommand insertAfterCommand,
        ICommand deleteCommand)
    {
        Index = index;
        Number = number;
        Label = label;
        Coords = coords;
        InsertAfterCommand = insertAfterCommand;
        DeleteCommand = deleteCommand;
    }

    /// <summary>
    /// Insert-a-waypoint-after command, surfaced on the row so the overflow
    /// <c>MenuFlyout</c> can bind to its own <see cref="ViewModelBase"/>
    /// DataContext. Ancestor bindings (e.g. <c>$parent[ItemsControl]</c>) do
    /// not resolve from inside a flyout popup, but the inherited DataContext
    /// does, so command access must live on the row item itself.
    /// </summary>
    public ICommand InsertAfterCommand { get; }

    /// <summary>
    /// Delete-this-waypoint command, surfaced on the row for the same
    /// flyout-binding reason as <see cref="InsertAfterCommand"/>.
    /// </summary>
    public ICommand DeleteCommand { get; }

    /// <summary>Zero-based waypoint index within the active route.</summary>
    public int Index { get; }

    /// <summary>One-based display number shown in the timeline circle.</summary>
    public int Number { get; }

    /// <summary>Waypoint name or a "Waypoint N" fallback.</summary>
    public string Label { get; }

    /// <summary>Latitude/longitude, pre-formatted for the muted subtitle.</summary>
    public string Coords { get; }

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
}

/// <summary>A leg row sitting between two waypoint rows in the timeline.</summary>
internal sealed class RouteLegRowViewModel : RouteTimelineRowViewModel
{
    public RouteLegRowViewModel(int legIndex, string legText, string geometryText, bool isGeodesic)
    {
        LegIndex = legIndex;
        LegText = legText;
        GeometryText = geometryText;
        IsGeodesic = isGeodesic;
    }

    /// <summary>Zero-based leg index within the active route.</summary>
    public int LegIndex { get; }

    /// <summary>Distance/bearing of the leg, pre-formatted (e.g. "1.6 NM · 070°T").</summary>
    public string LegText { get; }

    /// <summary>Geometry chip label ("Rhumb" / "Great circle").</summary>
    public string GeometryText { get; }

    /// <summary>True when the leg uses great-circle geometry.</summary>
    public bool IsGeodesic { get; }
}
