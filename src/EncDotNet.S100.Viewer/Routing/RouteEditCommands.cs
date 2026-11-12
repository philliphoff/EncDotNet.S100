using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// The waypoint metadata an insert or append can set (S-421
/// <c>routeWaypointID</c>, name, fixed flag and turn radius); <c>null</c>
/// fields are left as they are.
/// </summary>
/// <param name="Number">Author-assigned ordinal.</param>
/// <param name="Name">Waypoint name.</param>
/// <param name="Fixed">Whether the waypoint must not be moved.</param>
/// <param name="TurnRadiusNm">Planned turn radius, in nautical miles.</param>
internal readonly record struct RouteWaypointMetadata(int? Number, string? Name, bool? Fixed, double? TurnRadiusNm)
{
    /// <summary>Whether any field is set.</summary>
    public bool HasAny => Number.HasValue || Name is not null || Fixed.HasValue || TurnRadiusNm.HasValue;

    /// <summary>Sets the supplied fields on <paramref name="waypoint"/>.</summary>
    public void ApplyTo(RouteWaypoint waypoint)
    {
        if (Number.HasValue) waypoint.Number = Number;
        if (Name is not null) waypoint.Name = Name;
        if (Fixed.HasValue) waypoint.Fixed = Fixed;
        if (TurnRadiusNm.HasValue) waypoint.TurnRadiusNm = TurnRadiusNm;
    }
}

/// <summary>
/// The route edits the Routes panel and the MCP route tools share (#1400):
/// each runs as one undoable step under its <see cref="RouteEditLabels"/>
/// label in the route's <see cref="Route.History"/>.
/// </summary>
/// <remarks>
/// The commands are pure model edits. Callers check the arguments (index
/// ranges, read-only routes) and own the UI state around them, such as the
/// selected waypoint, and MCP callers run them on the UI thread.
/// </remarks>
internal static class RouteEditCommands
{
    /// <summary>The eastward nudge, in degrees, for a waypoint inserted after a route's only waypoint.</summary>
    private const double SingleWaypointNudgeDegrees = 0.01;

    /// <summary>Appends a waypoint at <paramref name="position"/>.</summary>
    /// <returns>The new waypoint.</returns>
    public static RouteWaypoint AppendWaypoint(Route route, GeoPosition position, RouteWaypointMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.History.Execute(RouteEditLabels.AppendWaypoint, () =>
            Describe(route, route.AppendWaypoint(position), metadata));
    }

    /// <summary>Inserts a waypoint at <paramref name="position"/> as waypoint <paramref name="index"/>, splitting the leg there.</summary>
    /// <param name="route">The route to edit.</param>
    /// <param name="index">Insertion index in <c>[0, Waypoints.Count]</c>.</param>
    /// <param name="position">The new waypoint's position.</param>
    /// <param name="metadata">Metadata to set on the new waypoint.</param>
    /// <returns>The new waypoint.</returns>
    public static RouteWaypoint InsertWaypoint(Route route, int index, GeoPosition position, RouteWaypointMetadata metadata = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        return route.History.Execute(RouteEditLabels.InsertWaypoint, () =>
            Describe(route, route.InsertWaypoint(index, position), metadata));
    }

    /// <summary>
    /// Inserts a waypoint after waypoint <paramref name="index"/>, where
    /// <see cref="PositionAfter"/> puts it.
    /// </summary>
    /// <param name="route">The route to edit.</param>
    /// <param name="index">The waypoint to insert after, in <c>[0, Waypoints.Count)</c>.</param>
    /// <returns>The new waypoint's index.</returns>
    public static int InsertWaypointAfter(Route route, int index)
    {
        ArgumentNullException.ThrowIfNull(route);
        InsertWaypoint(route, index + 1, PositionAfter(route, index));
        return index + 1;
    }

    /// <summary>
    /// Where a waypoint inserted after waypoint <paramref name="index"/>
    /// goes: the midpoint of the leg leaving it; after the last waypoint, one
    /// inbound leg further on (the previous waypoint mirrored about it); after
    /// a route's only waypoint, a little east of it.
    /// </summary>
    public static GeoPosition PositionAfter(Route route, int index)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, route.Waypoints.Count);

        var at = route.Waypoints[index].Position;
        if (index + 1 < route.Waypoints.Count)
        {
            var next = route.Waypoints[index + 1].Position;
            return new GeoPosition((at.Latitude + next.Latitude) / 2.0, (at.Longitude + next.Longitude) / 2.0);
        }
        if (index > 0)
        {
            var previous = route.Waypoints[index - 1].Position;
            return new GeoPosition(
                at.Latitude + (at.Latitude - previous.Latitude),
                at.Longitude + (at.Longitude - previous.Longitude));
        }
        return new GeoPosition(at.Latitude, at.Longitude + SingleWaypointNudgeDegrees);
    }

    /// <summary>Deletes waypoint <paramref name="index"/>, merging the legs either side of it.</summary>
    public static void DeleteWaypoint(Route route, int index)
    {
        ArgumentNullException.ThrowIfNull(route);
        route.History.Execute(RouteEditLabels.DeleteWaypoint, () => route.RemoveWaypoint(index));
    }

    /// <summary>
    /// Edits leg <paramref name="legIndex"/> as one step: first its passage
    /// overrides through the route (so an override equal to the passage value
    /// is dropped), then its own attributes in place, which notify the route
    /// only when one of them changed, so a no-op edit records no undo step.
    /// </summary>
    /// <param name="route">The route to edit.</param>
    /// <param name="legIndex">The leg, in <c>[0, Legs.Count)</c>.</param>
    /// <param name="overrides">Sets or resets passage overrides through <see cref="Route.SetLegOverride"/>; <c>null</c> for none.</param>
    /// <param name="attributes">Sets the leg's own attributes in place; <c>null</c> for none.</param>
    public static void EditLeg(Route route, int legIndex, Action<Route>? overrides, Action<RouteLeg>? attributes)
    {
        ArgumentNullException.ThrowIfNull(route);
        var leg = route.Legs[legIndex];
        route.History.Execute(RouteEditLabels.EditLeg, () =>
        {
            overrides?.Invoke(route);
            if (attributes is null)
                return;
            var before = leg.Clone();
            attributes(leg);
            if (!before.HasSameAttributes(leg))
                route.NotifyChanged();
        });
    }

    /// <summary>Switches leg <paramref name="legIndex"/> between great circle and rhumb line.</summary>
    public static void ToggleLegGeometry(Route route, int legIndex) =>
        EditLeg(route, legIndex, overrides: null, attributes: leg =>
            leg.GeometryType = leg.GeometryType == RouteLegGeometryType.Geodesic
                ? RouteLegGeometryType.Loxodrome
                : RouteLegGeometryType.Geodesic);

    private static RouteWaypoint Describe(Route route, RouteWaypoint waypoint, RouteWaypointMetadata metadata)
    {
        if (metadata.HasAny)
        {
            metadata.ApplyTo(waypoint);
            route.NotifyChanged();
        }
        return waypoint;
    }
}
