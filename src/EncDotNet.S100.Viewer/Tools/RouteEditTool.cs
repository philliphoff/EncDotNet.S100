using System;
using System.Globalization;
using Avalonia;
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
/// move it; click a waypoint to select it; right-click or Delete removes a
/// waypoint; Backspace removes the last.
/// </summary>
/// <remarks>
/// Unlike <see cref="MeasureTool"/>, this tool owns no overlay layer: the
/// route overlay is host-managed and persistent, and redraws reactively
/// when the tool mutates the model (via <see cref="RoutesService"/>). The
/// tool only translates pointer gestures into model edits and publishes a
/// status summary. Drag detection mirrors the measure tool — a press/move
/// beyond <see cref="DragThresholdPx"/> off a waypoint falls through to
/// Mapsui as a pan.
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
    private bool _dragActive;

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
        _context?.SetStatusSummary(null);
        _context = null;
        _pressPosition = null;
        _pressIsLeftButton = false;
        _dragWaypointIndex = null;
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
                route.RemoveWaypoint(index);
                _routes.SelectedWaypointIndex = null;
                PushSummary();
            }
            return true; // always consume so no context menu pops up
        }

        if (props.IsLeftButtonPressed)
        {
            _pressPosition = pos;
            _pressIsLeftButton = true;
            _dragActive = false;
            _dragWaypointIndex = HitTestWaypoint(pos);

            // Pressing on a waypoint begins a potential drag — consume so
            // Mapsui doesn't pan. Pressing empty water lets pan win until we
            // know (on release) that it was a click.
            return _dragWaypointIndex is not null;
        }

        return false;
    }

    public bool OnPointerMoved(PointerEventArgs e)
    {
        if (_context is null || !_pressIsLeftButton || _dragWaypointIndex is not { } index)
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

        var world = _context.ScreenToLatLon(pos);
        if (world is { } w && _routes.Routes.ActiveRoute is { } route && index < route.Waypoints.Count)
        {
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
        var release = e.GetPosition(_context.MapControl);
        var draggedIndex = _dragWaypointIndex;
        var wasDragActive = _dragActive;
        _pressPosition = null;
        _dragWaypointIndex = null;
        _dragActive = false;

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
            var inserted = active.InsertWaypoint(li + 1, position);
            _routes.SelectedWaypointIndex = li + 1;
            _ = inserted;
        }
        else
        {
            active.AppendWaypoint(position);
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
                route.RemoveWaypoint(route.Waypoints.Count - 1);
                _routes.SelectedWaypointIndex = null;
                PushSummary();
                return true;

            case MapToolAction.Discard:
                var target = _routes.SelectedWaypointIndex ?? route.Waypoints.Count - 1;
                if (target >= 0 && target < route.Waypoints.Count)
                {
                    route.RemoveWaypoint(target);
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
    /// Returns the index of the active-route leg whose screen-space segment
    /// passes within <see cref="SegmentHitTolerancePx"/> of
    /// <paramref name="screen"/>, or <c>null</c>.
    /// </summary>
    private int? HitTestLeg(Point screen)
    {
        if (_context is null || _routes.Routes.ActiveRoute is not { } route)
            return null;

        int? best = null;
        var bestDist = SegmentHitTolerancePx;
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var a = route.Waypoints[i].Position;
            var b = route.Waypoints[i + 1].Position;
            if (_context.LatLonToScreen(new GeoPosition(a.Latitude, a.Longitude)) is not { } pa ||
                _context.LatLonToScreen(new GeoPosition(b.Latitude, b.Longitude)) is not { } pb)
                continue;

            var dist = DistancePointToSegment(screen, pa, pb);
            if (dist <= bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }
        return best;
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
