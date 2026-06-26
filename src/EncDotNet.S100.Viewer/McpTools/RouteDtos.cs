using System;
using System.Collections.Generic;
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

/// <summary>One leg of a route, including its computed geometry.</summary>
[Description("A single route leg (the segment joining two consecutive waypoints) with its computed metrics and navigational envelope.")]
internal sealed record LegDto(
    [property: Description("Zero-based index of the leg; leg i joins waypoint i to waypoint i+1.")] int Index,
    [property: Description("Leg path geometry: \"loxodrome\" (rhumb line, constant bearing) or \"geodesic\" (great circle).")] string GeometryType,
    [property: Description("Computed leg length in nautical miles.")] double DistanceNm,
    [property: Description("Computed initial true bearing in degrees [0, 360).")] double InitialBearingDegrees,
    [property: Description("Starboard cross-track distance limit in metres, or null.")] double? StarboardCrossTrackDistanceLimitMeters,
    [property: Description("Port cross-track distance limit in metres, or null.")] double? PortCrossTrackDistanceLimitMeters,
    [property: Description("Starboard channel limit in metres, or null.")] double? StarboardChannelLimitMeters,
    [property: Description("Port channel limit in metres, or null.")] double? PortChannelLimitMeters,
    [property: Description("Safety contour for the leg in metres, or null.")] double? SafetyContourMeters,
    [property: Description("Safety depth for the leg in metres, or null.")] double? SafetyDepthMeters,
    [property: Description("Minimum speed over ground in knots, or null.")] double? SpeedOverGroundMinKnots,
    [property: Description("Maximum speed over ground in knots, or null.")] double? SpeedOverGroundMaxKnots,
    [property: Description("Minimum speed through water in knots, or null.")] double? SpeedThroughWaterMinKnots,
    [property: Description("Maximum speed through water in knots, or null.")] double? SpeedThroughWaterMaxKnots,
    [property: Description("Planned draft for the leg in metres, or null.")] double? DraftMeters,
    [property: Description("Static under-keel clearance in metres, or null.")] double? StaticUnderKeelClearanceMeters,
    [property: Description("Dynamic under-keel clearance in metres, or null.")] double? DynamicUnderKeelClearanceMeters,
    [property: Description("Safety margin in metres, or null.")] double? SafetyMarginMeters,
    [property: Description("Free-text note for the leg, or null.")] string? Note);

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
[Description("The full state of one route: identity, metadata, ordered waypoints and legs, and total distance.")]
internal sealed record RouteDetail(
    [property: Description("Stable route identifier.")] string RouteId,
    [property: Description("Route name, or null.")] string? Name,
    [property: Description("Whether this is the collection's active (default-target) route.")] bool IsActive,
    [property: Description("Route-level metadata.")] RouteInfoDto Info,
    [property: Description("Waypoints in route order.")] IReadOnlyList<WaypointDto> Waypoints,
    [property: Description("Legs in route order; leg i joins waypoint i to waypoint i+1.")] IReadOnlyList<LegDto> Legs,
    [property: Description("Total route length in nautical miles.")] double TotalDistanceNm);

/// <summary>Result of list_routes.</summary>
[Description("Result of list_routes: every route in the collection plus the active route id.")]
internal sealed record ListRoutesResult(
    [property: Description("All routes in insertion order.")] IReadOnlyList<RouteSummary> Routes,
    [property: Description("Id of the active route, or null when the collection is empty.")] string? ActiveRouteId);

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
                leg.StarboardCrossTrackDistanceLimitMeters,
                leg.PortCrossTrackDistanceLimitMeters,
                leg.StarboardChannelLimitMeters,
                leg.PortChannelLimitMeters,
                leg.SafetyContourMeters,
                leg.SafetyDepthMeters,
                leg.SpeedOverGroundMinKnots,
                leg.SpeedOverGroundMaxKnots,
                leg.SpeedThroughWaterMinKnots,
                leg.SpeedThroughWaterMaxKnots,
                leg.DraftMeters,
                leg.StaticUnderKeelClearanceMeters,
                leg.DynamicUnderKeelClearanceMeters,
                leg.SafetyMarginMeters,
                leg.Note));
        }

        return new RouteDetail(
            route.Id,
            route.Name,
            ReferenceEquals(route, routes.Routes.ActiveRoute),
            Info(route.Info),
            waypoints,
            legs,
            route.TotalDistanceNm());
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

    private static RouteInfoDto Info(RouteInfo info)
        => new(
            info.Name,
            info.Author,
            info.Description,
            info.DeparturePortId,
            info.ArrivalPortId,
            info.ValidityStart,
            info.ValidityEnd,
            info.Vessel is { } v
                ? new VesselDto(v.Name, v.Mmsi, v.Imo, v.Callsign, v.LengthMeters, v.BeamMeters)
                : null);
}
