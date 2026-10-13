using System.ComponentModel;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>One waypoint of a route, as seen over the MCP wire.</summary>
[Description("A single route waypoint with its position and optional metadata.")]
internal sealed record WaypointDto(
    [property: Description("Zero-based index of the waypoint in route order.")] int Index,
    [property: Description("WGS-84 latitude in decimal degrees.")] double Lat,
    [property: Description("WGS-84 longitude in decimal degrees.")] double Lon,
    [property: Description("Author-assigned ordinal (S-421 routeWaypointID), or null.")] int? Number,
    [property: Description("Human-readable waypoint name, or null.")] string? Name,
    [property: Description("Whether the waypoint is pinned and must not be moved, or null when unset.")] bool? Fixed,
    [property: Description("Planned turn radius at the waypoint in nautical miles, or null.")] double? TurnRadiusNm);

/// <summary>
/// The passage-inheritable values of one leg: either the effective values or
/// only the leg's overrides.
/// </summary>
[Description("A leg's passage values (speed, draft, cross-track limits, required UKC). In 'effective' each is the leg's override when set, else the route's passage value; in 'overrides' only the leg's own values are present.")]
internal sealed record LegPassageValuesDto(
    [property: Description("Planned speed over ground in knots, or null.")] double? SpeedKnots,
    [property: Description("Draft in metres, or null.")] double? DraftMeters,
    [property: Description("Port cross-track distance limit in metres, or null.")] double? PortCrossTrackMeters,
    [property: Description("Starboard cross-track distance limit in metres, or null.")] double? StarboardCrossTrackMeters,
    [property: Description("Required static under-keel clearance in metres, or null.")] double? RequiredUkcMeters);

/// <summary>One leg of a route, including its computed geometry.</summary>
[Description("A single route leg (the segment joining two consecutive waypoints) with its computed metrics, its passage values (effective and overridden) and the rest of its S-421 navigational envelope.")]
internal sealed record LegDto(
    [property: Description("Zero-based index of the leg; leg i joins waypoint i to waypoint i+1.")] int Index,
    [property: Description("Leg path geometry: \"loxodrome\" (rhumb line, constant bearing) or \"geodesic\" (great circle).")] string GeometryType,
    [property: Description("Computed leg length in nautical miles.")] double DistanceNm,
    [property: Description("Computed initial true bearing in degrees [0, 360).")] double InitialBearingDegrees,
    [property: Description("The passage values that apply to this leg: its override when set, else the route's passage value.")] LegPassageValuesDto Effective,
    [property: Description("Only this leg's overrides; a value absent here is inherited from the route's passage.")] LegPassageValuesDto Overrides,
    [property: Description("Starboard channel limit in metres, or null.")] double? StarboardChannelLimitMeters,
    [property: Description("Port channel limit in metres, or null.")] double? PortChannelLimitMeters,
    [property: Description("Safety contour for the leg in metres, or null.")] double? SafetyContourMeters,
    [property: Description("Safety depth for the leg in metres, or null.")] double? SafetyDepthMeters,
    [property: Description("Minimum speed over ground in knots, or null.")] double? SpeedOverGroundMinKnots,
    [property: Description("Maximum speed over ground in knots, or null.")] double? SpeedOverGroundMaxKnots,
    [property: Description("Minimum speed through water in knots, or null.")] double? SpeedThroughWaterMinKnots,
    [property: Description("Maximum speed through water in knots, or null.")] double? SpeedThroughWaterMaxKnots,
    [property: Description("Dynamic under-keel clearance in metres, or null.")] double? DynamicUnderKeelClearanceMeters,
    [property: Description("Safety margin in metres, or null.")] double? SafetyMarginMeters,
    [property: Description("Free-text note for the leg, or null.")] string? Note);

/// <summary>Route-level passage values.</summary>
[Description("A route's passage values, which every leg inherits unless it overrides them; all optional.")]
internal sealed record PassageDto(
    [property: Description("Draft in metres, or null. Enables Check passage.")] double? DraftMeters,
    [property: Description("Fixed squat allowance in metres, or null.")] double? SquatMeters,
    [property: Description("Planned speed over ground in knots, or null. Enables ETAs.")] double? SpeedKnots,
    [property: Description("Planned departure (ISO-8601), or null to use the view time.")] DateTimeOffset? Departure,
    [property: Description("Required static under-keel clearance in metres, or null.")] double? RequiredUkcMeters,
    [property: Description("Port cross-track distance limit in metres, or null.")] double? PortCrossTrackMeters,
    [property: Description("Starboard cross-track distance limit in metres, or null.")] double? StarboardCrossTrackMeters,
    [property: Description("The vessel (from the route info; set it with set_route_info), or null.")] VesselDto? Vessel);

/// <summary>Result of get_passage.</summary>
[Description("Result of get_passage: a route's passage values.")]
internal sealed record GetPassageResult(
    [property: Description("Stable route identifier.")] string RouteId,
    [property: Description("The route's passage values.")] PassageDto Passage);

/// <summary>Vessel metadata carried by a route's info block.</summary>
[Description("Vessel metadata the route is planned for; all fields optional.")]
internal sealed record VesselDto(
    [property: Description("Vessel name, or null.")] string? Name,
    [property: Description("Maritime Mobile Service Identity, or null.")] string? Mmsi,
    [property: Description("IMO number, or null.")] string? Imo,
    [property: Description("Call sign, or null.")] string? Callsign,
    [property: Description("Overall length in metres, or null.")] double? LengthMeters,
    [property: Description("Beam in metres, or null.")] double? BeamMeters);

/// <summary>Route-level metadata.</summary>
[Description("Route-level metadata (S-421 RouteInfo); all fields optional.")]
internal sealed record RouteInfoDto(
    [property: Description("Route name, or null.")] string? Name,
    [property: Description("Route author / originator, or null.")] string? Author,
    [property: Description("Free-text description, or null.")] string? Description,
    [property: Description("Departure port identifier, or null.")] string? DeparturePortId,
    [property: Description("Arrival port identifier, or null.")] string? ArrivalPortId,
    [property: Description("Planned start of validity (UTC ISO-8601), or null.")] DateTimeOffset? ValidityStart,
    [property: Description("Planned end of validity (UTC ISO-8601), or null.")] DateTimeOffset? ValidityEnd,
    [property: Description("Vessel metadata, or null when no vessel has been set.")] VesselDto? Vessel);

/// <summary>A compact route summary returned by list_routes.</summary>
[Description("A compact route summary: identity plus aggregate metrics.")]
internal sealed record RouteSummary(
    [property: Description("Stable route identifier.")] string RouteId,
    [property: Description("Route name, or null.")] string? Name,
    [property: Description("Whether this is the collection's active (default-target) route.")] bool IsActive,
    [property: Description("Number of waypoints in the route.")] int WaypointCount,
    [property: Description("Number of legs (max(0, waypointCount - 1)).")] int LegCount,
    [property: Description("Total route length in nautical miles.")] double TotalDistanceNm);

/// <summary>The full projection of a single route.</summary>
[Description("The full state of one route: identity, metadata, passage values, ordered waypoints and legs, and total distance.")]
internal sealed record RouteDetail(
    [property: Description("Stable route identifier.")] string RouteId,
    [property: Description("Route name, or null.")] string? Name,
    [property: Description("Whether this is the collection's active (default-target) route.")] bool IsActive,
    [property: Description("Route-level metadata.")] RouteInfoDto Info,
    [property: Description("Route-level passage values that legs inherit.")] PassageDto Passage,
    [property: Description("Waypoints in route order.")] IReadOnlyList<WaypointDto> Waypoints,
    [property: Description("Legs in route order; leg i joins waypoint i to waypoint i+1.")] IReadOnlyList<LegDto> Legs,
    [property: Description("Total route length in nautical miles.")] double TotalDistanceNm,
    [property: Description("Whether undo_route_edit has an edit to undo on this route.")] bool CanUndo,
    [property: Description("Whether redo_route_edit has an edit to redo on this route.")] bool CanRedo,
    [property: Description("What undo_route_edit would revert (e.g. \"move waypoint\"), or null.")] string? UndoLabel,
    [property: Description("What redo_route_edit would replay, or null.")] string? RedoLabel,
    [property: Description("The selected waypoint's index on the active route (set with select_route_item), or null; always null for other routes.")] int? SelectedWaypointIndex,
    [property: Description("The selected leg's index on the active route (haloed on the map, set with select_route_item), or null; always null for other routes.")] int? SelectedLegIndex);

/// <summary>Result of list_routes.</summary>
[Description("Result of list_routes: every route in the collection plus the active route id.")]
internal sealed record ListRoutesResult(
    [property: Description("All routes in insertion order.")] IReadOnlyList<RouteSummary> Routes,
    [property: Description("Id of the active route, or null when the collection is empty.")] string? ActiveRouteId,
    [property: Description("Whether the interactive route editor is on (set it with set_route_edit_mode).")] bool EditMode);

/// <summary>Result of set_route_edit_mode.</summary>
[Description("Result of set_route_edit_mode: the route edit mode now in force.")]
internal sealed record RouteEditModeResult(
    [property: Description("Whether the interactive route editor is on.")] bool Enabled,
    [property: Description("Id of the active route edits apply to, or null when there is none.")] string? ActiveRouteId);

/// <summary>Result of delete_route.</summary>
[Description("Result of delete_route: which route was removed and the new active route id.")]
internal sealed record DeleteRouteResult(
    [property: Description("Id of the route that was requested for deletion.")] string RouteId,
    [property: Description("Whether the route was present and removed.")] bool Deleted,
    [property: Description("Id of the active route after deletion, or null when the collection is now empty.")] string? ActiveRouteId);

/// <summary>
/// Builds wire DTOs from the live route model. All members assume they are
/// called on the UI thread (inside an <see cref="IRouteEditInvoker"/>
/// callback), so they read the mutable model without locking.
/// </summary>
internal static class RouteProjection
{
    /// <summary>Maps a geometry-type enum to its wire token.</summary>
    public static string GeometryToken(RouteLegGeometryType type)
        => type == RouteLegGeometryType.Geodesic ? "geodesic" : "loxodrome";

    /// <summary>Projects a single route to a <see cref="RouteDetail"/>.</summary>
    public static RouteDetail Detail(Route route, RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(routes);

        var waypoints = new List<WaypointDto>(route.Waypoints.Count);
        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            var wp = route.Waypoints[i];
            waypoints.Add(new WaypointDto(
                i, wp.Position.Latitude, wp.Position.Longitude,
                wp.Number, wp.Name, wp.Fixed, wp.TurnRadiusNm));
        }

        var legs = new List<LegDto>(route.Legs.Count);
        for (var i = 0; i < route.Legs.Count; i++)
        {
            var leg = route.Legs[i];
            var metrics = route.ComputeLegMetrics(i);
            legs.Add(new LegDto(
                i,
                GeometryToken(leg.GeometryType),
                metrics.DistanceNm,
                metrics.InitialBearingDegrees,
                LegValues(field => route.GetEffectiveLegValue(i, field)),
                LegValues(leg.GetOverride),
                leg.StarboardChannelLimitMeters,
                leg.PortChannelLimitMeters,
                leg.SafetyContourMeters,
                leg.SafetyDepthMeters,
                leg.SpeedOverGroundMinKnots,
                leg.SpeedOverGroundMaxKnots,
                leg.SpeedThroughWaterMinKnots,
                leg.SpeedThroughWaterMaxKnots,
                leg.DynamicUnderKeelClearanceMeters,
                leg.SafetyMarginMeters,
                leg.Note));
        }

        var isActive = ReferenceEquals(route, routes.Routes.ActiveRoute);
        return new RouteDetail(
            route.Id,
            route.Name,
            isActive,
            Info(route.Info),
            Passage(route),
            waypoints,
            legs,
            route.TotalDistanceNm(),
            route.History.CanUndo,
            route.History.CanRedo,
            route.History.UndoLabel,
            route.History.RedoLabel,
            isActive ? routes.SelectedWaypointIndex : null,
            isActive ? routes.SelectedLegIndex : null);
    }

    /// <summary>Projects a single route to a compact <see cref="RouteSummary"/>.</summary>
    public static RouteSummary Summary(Route route, RoutesService routes)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(routes);
        return new RouteSummary(
            route.Id,
            route.Name,
            ReferenceEquals(route, routes.Routes.ActiveRoute),
            route.Waypoints.Count,
            route.Legs.Count,
            route.TotalDistanceNm());
    }

    /// <summary>Projects a route's passage values to a <see cref="PassageDto"/>.</summary>
    public static PassageDto Passage(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var p = route.Passage;
        return new PassageDto(
            p.DraftMeters,
            p.SquatMeters,
            p.SpeedKnots,
            p.Departure,
            p.RequiredUkcMeters,
            p.PortCrossTrackMeters,
            p.StarboardCrossTrackMeters,
            Vessel(route.Info.Vessel));
    }

    private static LegPassageValuesDto LegValues(Func<RoutePassageField, double?> value)
        => new(
            value(RoutePassageField.Speed),
            value(RoutePassageField.Draft),
            value(RoutePassageField.PortCrossTrack),
            value(RoutePassageField.StarboardCrossTrack),
            value(RoutePassageField.RequiredUkc));

    private static VesselDto? Vessel(RouteVesselInfo? vessel)
        => vessel is { } v
            ? new VesselDto(v.Name, v.Mmsi, v.Imo, v.Callsign, v.LengthMeters, v.BeamMeters)
            : null;

    private static RouteInfoDto Info(RouteInfo info)
        => new(
            info.Name,
            info.Author,
            info.Description,
            info.DeparturePortId,
            info.ArrivalPortId,
            info.ValidityStart,
            info.ValidityEnd,
            Vessel(info.Vessel));
}
