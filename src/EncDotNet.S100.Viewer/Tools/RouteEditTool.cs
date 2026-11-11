using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Interactive editor for the active <see cref="Route"/> in the shared
/// <see cref="RoutesService"/>. Click on empty water appends a waypoint (or
/// inserts one when the click lands on an existing leg); drag a waypoint to
/// move it; click or drag a leg's insert handle (the ⊕ at its midpoint) to
/// insert a waypoint there; click a waypoint to select it; right-click removes the waypoint
/// under the pointer; Backspace removes the last. The Delete key (bound by the
/// host, see <c>RouteEditorKeys</c>) removes the selected waypoint.
/// </summary>
/// <remarks>
/// Unlike <see cref="MeasureTool"/>, this tool owns no overlay layer: the
/// route overlay is host-managed and persistent, and redraws reactively
/// when the tool mutates the model (via <see cref="RoutesService"/>). The
/// tool only translates pointer gestures into model edits and publishes a
/// status summary. Drag detection mirrors the measure tool — a press/move
/// beyond <see cref="DragThresholdPx"/> off a waypoint falls through to
/// Mapsui as a pan.
/// <para>
/// Every edit runs through the route's <see cref="Route.History"/>, so it can
/// be undone; a drag is one step however many moves it makes.
/// </para>
/// </remarks>
internal sealed class RouteEditTool : IMapTool
{
    public const string ToolId = "route-edit";

    /// <summary>Click vs. drag threshold (DIPs).</summary>
    private const double DragThresholdPx = 3.0;

    /// <summary>Pointer-to-waypoint hit radius (DIPs).</summary>
    private const double WaypointHitRadiusPx = 10.0;

    /// <summary>Pointer-to-segment hit tolerance for insert (DIPs).</summary>
    private const double SegmentHitTolerancePx = 6.0;

    private readonly RoutesService _routes;
    private MapToolContext? _context;
    private Point? _pressPosition;
    private bool _pressIsLeftButton;
    private int? _dragWaypointIndex;
    private int? _pressHandleLeg;
    private bool _dragActive;
    private IDisposable? _dragGesture;

    public RouteEditTool(RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        _routes = routes;
    }

    public string Id => ToolId;

    private Cursor? _cursor;
    /// <inheritdoc />
    public Cursor? Cursor => _cursor ??= new Cursor(StandardCursorType.Cross);

    public void OnActivated(MapToolContext context)
    {
        _context = context;
        // Ensure there is a route to edit so the first click has a target.
        if (_routes.Routes.ActiveRoute is null)
            CreateRoute();
        PushSummary();
    }

    public void OnDeactivated()
    {
        EndDragGesture();
        _context?.SetStatusSummary(null);
        _context = null;
        _pressPosition = null;
        _pressIsLeftButton = false;
        _dragWaypointIndex = null;
        _pressHandleLeg = null;
        _dragActive = false;
    }

    public bool OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_context is null) return false;
        var props = e.GetCurrentPoint(_context.MapControl).Properties;
        var pos = e.GetPosition(_context.MapControl);

        if (props.IsRightButtonPressed)
        {
            // Right-click deletes the waypoint under the cursor, if any.
            var hit = HitTestWaypoint(pos);
            if (hit is { } index && _routes.Routes.ActiveRoute is { } route)
            {
                route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(index));
                _routes.SelectedWaypointIndex = null;
                PushSummary();
            }
            return true; // always consume so no context menu pops up
        }

        if (props.IsLeftButtonPressed)
        {
            // Clicking the map ends any typing in the Routes panel (committing
            // it), so the route shortcuts apply to the map again.
            if (!_context.MapControl.Focus())
                TopLevel.GetTopLevel(_context.MapControl)?.Focus();

            _pressPosition = pos;
            _pressIsLeftButton = true;
            _dragActive = false;
            _dragWaypointIndex = HitTestWaypoint(pos);
            _pressHandleLeg = _dragWaypointIndex is null ? HitTestHandle(pos) : null;

            // Pressing on a waypoint or an insert handle begins a potential
            // drag — consume so Mapsui doesn't pan. Pressing empty water lets
            // pan win until we know (on release) that it was a click.
            return _dragWaypointIndex is not null || _pressHandleLeg is not null;
        }

        return false;
    }

    public bool OnPointerMoved(PointerEventArgs e)
    {
        if (_context is null || !_pressIsLeftButton || (_dragWaypointIndex is null && _pressHandleLeg is null))
            return false;

        var pos = e.GetPosition(_context.MapControl);
        if (!_dragActive && _pressPosition is { } press)
        {
            var dx = pos.X - press.X;
            var dy = pos.Y - press.Y;
            if ((dx * dx + dy * dy) <= DragThresholdPx * DragThresholdPx)
                return true; // not yet a drag, but keep consuming
            _dragActive = true;
        }

        // Dragging an insert handle inserts a waypoint at the leg's midpoint
        // and drags it, as one undoable step.
        if (_pressHandleLeg is { } leg && _routes.Routes.ActiveRoute is { } target)
        {
            _pressHandleLeg = null;
            _dragGesture ??= target.History.BeginGesture(RouteEditLabels.InsertWaypoint);
            _dragWaypointIndex = InsertAtHandle(target, leg);
        }

        if (_dragWaypointIndex is not { } index)
            return true;

        var world = _context.ScreenToLatLon(pos);
        if (world is { } w && _routes.Routes.ActiveRoute is { } route && index < route.Waypoints.Count)
        {
            _dragGesture ??= route.History.BeginGesture(RouteEditLabels.MoveWaypoint);
            route.MoveWaypoint(index, new GeoPosition(w.Latitude, w.Longitude));
            _routes.SelectedWaypointIndex = index;
            PushSummary();
        }
        return true;
    }

    public bool OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_context is null || !_pressIsLeftButton || _pressPosition is not { } press)
            return false;

        _pressIsLeftButton = false;
        EndDragGesture();
        var release = e.GetPosition(_context.MapControl);
        var draggedIndex = _dragWaypointIndex;
        var handleLeg = _pressHandleLeg;
        var wasDragActive = _dragActive;
        _pressPosition = null;
        _dragWaypointIndex = null;
        _pressHandleLeg = null;
        _dragActive = false;

        if (handleLeg is { } clickedLeg && _routes.Routes.ActiveRoute is { } handleRoute)
        {
            // A click on an insert handle inserts a waypoint at the leg's midpoint.
            handleRoute.History.Execute(RouteEditLabels.InsertWaypoint, () => InsertAtHandle(handleRoute, clickedLeg));
            PushSummary();
            return true;
        }

        var dx = release.X - press.X;
        var dy = release.Y - press.Y;
        var isClick = (dx * dx + dy * dy) <= DragThresholdPx * DragThresholdPx;

        if (draggedIndex is { } index)
        {
            // Pressed on a waypoint: a click (no drag) selects it; a drag was
            // already applied live in OnPointerMoved.
            if (!wasDragActive)
                _routes.SelectedWaypointIndex = index;
            PushSummary();
            return true;
        }

        if (!isClick)
            return false; // empty-water drag → Mapsui handled the pan

        var world = _context.ScreenToLatLon(release);
        if (world is not { } w)
            return false;

        var active = _routes.Routes.ActiveRoute ?? CreateRoute();
        var position = new GeoPosition(w.Latitude, w.Longitude);

        // If the click lands on an existing leg, insert a waypoint that
        // splits it; otherwise append to the end of the route.
        var legIndex = HitTestLeg(release);
        if (legIndex is { } li)
        {
            active.History.Execute(RouteEditLabels.InsertWaypoint, () => active.InsertWaypoint(li + 1, position));
            _routes.SelectedWaypointIndex = li + 1;
        }
        else
        {
            active.History.Execute(RouteEditLabels.AppendWaypoint, () => active.AppendWaypoint(position));
            _routes.SelectedWaypointIndex = active.Waypoints.Count - 1;
        }

        PushSummary();
        return true;
    }

    public bool OnDoubleTapped(TappedEventArgs e) => true; // suppress zoom-on-double-tap

    public bool OnAction(MapToolAction action)
    {
        if (_routes.Routes.ActiveRoute is not { } route || route.Waypoints.Count == 0)
            return false;

        switch (action)
        {
            case MapToolAction.Backstep:
                route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(route.Waypoints.Count - 1));
                _routes.SelectedWaypointIndex = null;
                PushSummary();
                return true;

            case MapToolAction.Discard:
                var target = _routes.SelectedWaypointIndex ?? route.Waypoints.Count - 1;
                if (target >= 0 && target < route.Waypoints.Count)
                {
                    route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(target));
                    _routes.SelectedWaypointIndex = null;
                    PushSummary();
                    return true;
                }
                return false;

            case MapToolAction.Commit:
                if (_routes.SelectedWaypointIndex is null)
                    return false;
                _routes.SelectedWaypointIndex = null;
                return true;

            default:
                return false;
        }
    }

    private void EndDragGesture()
    {
        _dragGesture?.Dispose();
        _dragGesture = null;
    }

    private Route CreateRoute()
    {
        var name = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Routes_NewRouteNameFormat,
            _routes.Routes.Routes.Count + 1);
        return _routes.Routes.CreateRoute(name);
    }

    /// <summary>
    /// Returns the index of the active-route waypoint within
    /// <see cref="WaypointHitRadiusPx"/> of <paramref name="screen"/>, or
    /// <c>null</c>. The nearest qualifying waypoint wins.
    /// </summary>
    private int? HitTestWaypoint(Point screen)
    {
        if (_context is null || _routes.Routes.ActiveRoute is not { } route)
            return null;

        int? best = null;
        var bestDistSq = WaypointHitRadiusPx * WaypointHitRadiusPx;
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            var p = route.Waypoints[i].Position;
            if (_context.LatLonToScreen(new GeoPosition(p.Latitude, p.Longitude)) is not { } s)
                continue;
            var dx = s.X - screen.X;
            var dy = s.Y - screen.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Returns the index of the active-route leg whose drawn path passes
    /// within <see cref="SegmentHitTolerancePx"/> of <paramref name="screen"/>,
    /// or <c>null</c>.
    /// </summary>
    private int? HitTestLeg(Point screen)
    {
        int? best = null;
        var bestDist = SegmentHitTolerancePx;
        foreach (var leg in LayoutActiveRoute())
        {
            foreach (var part in leg.Parts)
            {
                for (var i = 1; i < part.Count; i++)
                {
                    var dist = DistancePointToSegment(screen, part[i - 1], part[i]);
                    if (dist <= bestDist)
                    {
                        bestDist = dist;
                        best = leg.Index;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Returns the index of the leg whose insert handle is within
    /// <see cref="WaypointHitRadiusPx"/> of <paramref name="screen"/>, or
    /// <c>null</c>. Handles show only on legs longer than
    /// <see cref="RouteOverlayLayout.InsertHandleMinLegPx"/> on screen.
    /// </summary>
    internal int? HitTestHandle(Point screen) => HitTestHandle(LayoutActiveRoute(), screen);

    /// <summary>The leg in <paramref name="layout"/> whose insert handle is under <paramref name="screen"/>.</summary>
    internal static int? HitTestHandle(IReadOnlyList<LegLayout> layout, Point screen)
    {
        int? best = null;
        var bestDistSq = WaypointHitRadiusPx * WaypointHitRadiusPx;
        foreach (var leg in layout)
        {
            if (!leg.ShowInsertHandle || leg.Midpoint is not { } m)
                continue;
            var dx = m.X - screen.X;
            var dy = m.Y - screen.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq <= bestDistSq)
            {
                bestDistSq = distSq;
                best = leg.Index;
            }
        }
        return best;
    }

    /// <summary>
    /// Inserts a waypoint at the midpoint of <paramref name="route"/>'s leg
    /// <paramref name="leg"/> (under its insert handle), selects it and
    /// returns its index. The caller records the edit in the route's history.
    /// </summary>
    internal int InsertAtHandle(Route route, int leg)
    {
        var layout = LayoutActiveRoute();
        var position = leg < layout.Count && layout[leg].MidpointPosition is { } mid
            ? mid
            : Midpoint(route.Waypoints[leg].Position, route.Waypoints[leg + 1].Position);
        route.InsertWaypoint(leg + 1, position);
        _routes.SelectedWaypointIndex = leg + 1;
        return leg + 1;
    }

    private static GeoPosition Midpoint(GeoPosition a, GeoPosition b)
        => new((a.Latitude + b.Latitude) / 2.0, (a.Longitude + b.Longitude) / 2.0);

    /// <summary>Lays out the active route for the current viewport, as the overlay draws it.</summary>
    private IReadOnlyList<LegLayout> LayoutActiveRoute()
    {
        if (_context is not { } context || _routes.Routes.ActiveRoute is not { } route)
            return [];
        return RouteOverlayLayout.Layout(
            RouteOverlayScene.SceneRoute.From(route, isActive: true),
            context.LatLonToScreen,
            _ => 0,
            _routes.SelectedLegIndex,
            isEditing: true);
    }

    private static double DistancePointToSegment(Point p, Point a, Point b)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lenSq = abx * abx + aby * aby;
        if (lenSq < 1e-9)
        {
            var ddx = p.X - a.X;
            var ddy = p.Y - a.Y;
            return Math.Sqrt(ddx * ddx + ddy * ddy);
        }

        var t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lenSq;
        t = Math.Clamp(t, 0.0, 1.0);
        var projX = a.X + t * abx;
        var projY = a.Y + t * aby;
        var dx = p.X - projX;
        var dy = p.Y - projY;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void PushSummary()
    {
        _context?.SetStatusSummary(FormatSummary());
    }

    /// <summary>Builds the status-bar summary for the active route.</summary>
    internal string? FormatSummary()
    {
        var route = _routes.Routes.ActiveRoute;
        if (route is null || route.Waypoints.Count == 0)
            return Strings.Status_RouteEditNoData;

        if (route.Legs.Count == 0)
            return Strings.Status_RouteEditNoData;

        var last = route.ComputeLegMetrics(route.Legs.Count - 1);
        var legText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Status_RouteLeg,
            last.LegIndex + 1,
            last.DistanceNm,
            last.InitialBearingDegrees);
        var totalText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Status_RouteTotal,
            route.TotalDistanceNm());
        return $"{legText}  |  {totalText}";
    }
}
