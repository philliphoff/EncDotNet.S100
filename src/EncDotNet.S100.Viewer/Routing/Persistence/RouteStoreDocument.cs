using System;
using System.Collections.Generic;

namespace EncDotNet.S100.Viewer.Routing.Persistence;

/// <summary>
/// The on-disk JSON shape of the viewer's saved routes. A deliberately
/// flat, nullable mirror of the in-memory <see cref="RouteCollection"/>
/// object graph: the domain types carry behaviour, invariants, and
/// <c>internal</c> setters that do not round-trip through
/// <c>System.Text.Json</c> directly, so persistence goes through these
/// plain data-transfer records instead.
/// </summary>
/// <remarks>
/// The field names follow the S-421 attribute vocabulary the domain types
/// already document so a saved route reads consistently with an exported
/// S-421 GML route plan. <see cref="SchemaVersion"/> guards forward
/// migrations: an unknown future version is treated as unreadable and the
/// viewer starts with no routes rather than mis-parsing.
/// </remarks>
internal sealed class RouteStoreDocument
{
    /// <summary>
    /// The schema version this codebase reads and writes. Bump when the
    /// shape changes incompatibly so older builds reject newer files.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the persisted document.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// <see cref="Route.Id"/> of the route that was active at save time,
    /// or <c>null</c> when the collection had no active route.
    /// </summary>
    public string? ActiveRouteId { get; set; }

    /// <summary>All saved routes, in collection order.</summary>
    public List<RouteDocument> Routes { get; set; } = new();
}

/// <summary>Persisted form of a single <see cref="Route"/>.</summary>
internal sealed class RouteDocument
{
    /// <summary>Stable route id (<see cref="Route.Id"/>).</summary>
    public string? Id { get; set; }

    /// <summary>Route-level metadata.</summary>
    public RouteInfoDocument? Info { get; set; }

    /// <summary>Waypoints in route order.</summary>
    public List<RouteWaypointDocument> Waypoints { get; set; } = new();

    /// <summary>
    /// Legs in route order; <c>Legs[i]</c> joins <c>Waypoints[i]</c> to
    /// <c>Waypoints[i+1]</c>, so a well-formed document has
    /// <c>max(0, Waypoints.Count - 1)</c> legs.
    /// </summary>
    public List<RouteLegDocument> Legs { get; set; } = new();
}

/// <summary>Persisted form of a <see cref="RouteInfo"/>.</summary>
internal sealed class RouteInfoDocument
{
    /// <inheritdoc cref="RouteInfo.Name"/>
    public string? Name { get; set; }

    /// <inheritdoc cref="RouteInfo.Author"/>
    public string? Author { get; set; }

    /// <inheritdoc cref="RouteInfo.Description"/>
    public string? Description { get; set; }

    /// <inheritdoc cref="RouteInfo.DeparturePortId"/>
    public string? DeparturePortId { get; set; }

    /// <inheritdoc cref="RouteInfo.ArrivalPortId"/>
    public string? ArrivalPortId { get; set; }

    /// <inheritdoc cref="RouteInfo.ValidityStart"/>
    public DateTimeOffset? ValidityStart { get; set; }

    /// <inheritdoc cref="RouteInfo.ValidityEnd"/>
    public DateTimeOffset? ValidityEnd { get; set; }

    /// <inheritdoc cref="RouteInfo.Vessel"/>
    public RouteVesselInfoDocument? Vessel { get; set; }
}

/// <summary>Persisted form of a <see cref="RouteVesselInfo"/>.</summary>
internal sealed class RouteVesselInfoDocument
{
    /// <inheritdoc cref="RouteVesselInfo.Name"/>
    public string? Name { get; set; }

    /// <inheritdoc cref="RouteVesselInfo.Mmsi"/>
    public string? Mmsi { get; set; }

    /// <inheritdoc cref="RouteVesselInfo.Imo"/>
    public string? Imo { get; set; }

    /// <inheritdoc cref="RouteVesselInfo.Callsign"/>
    public string? Callsign { get; set; }

    /// <inheritdoc cref="RouteVesselInfo.LengthMeters"/>
    public double? LengthMeters { get; set; }

    /// <inheritdoc cref="RouteVesselInfo.BeamMeters"/>
    public double? BeamMeters { get; set; }
}

/// <summary>Persisted form of a <see cref="RouteWaypoint"/>.</summary>
internal sealed class RouteWaypointDocument
{
    /// <summary>Latitude in decimal degrees, positive north (WGS-84).</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude in decimal degrees, positive east (WGS-84).</summary>
    public double Longitude { get; set; }

    /// <inheritdoc cref="RouteWaypoint.Number"/>
    public int? Number { get; set; }

    /// <inheritdoc cref="RouteWaypoint.Name"/>
    public string? Name { get; set; }

    /// <inheritdoc cref="RouteWaypoint.Fixed"/>
    public bool? Fixed { get; set; }

    /// <inheritdoc cref="RouteWaypoint.TurnRadiusNm"/>
    public double? TurnRadiusNm { get; set; }
}

/// <summary>Persisted form of a <see cref="RouteLeg"/>.</summary>
internal sealed class RouteLegDocument
{
    /// <summary>
    /// Leg path definition, persisted by enumerator name
    /// (<see cref="RouteLegGeometryType"/>). Unknown values fall back to
    /// <see cref="RouteLegGeometryType.Loxodrome"/> on load.
    /// </summary>
    public string? GeometryType { get; set; }

    /// <inheritdoc cref="RouteLeg.StarboardCrossTrackDistanceLimitMeters"/>
    public double? StarboardCrossTrackDistanceLimitMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.PortCrossTrackDistanceLimitMeters"/>
    public double? PortCrossTrackDistanceLimitMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.StarboardChannelLimitMeters"/>
    public double? StarboardChannelLimitMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.PortChannelLimitMeters"/>
    public double? PortChannelLimitMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.SafetyContourMeters"/>
    public double? SafetyContourMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.SafetyDepthMeters"/>
    public double? SafetyDepthMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.SpeedOverGroundMinKnots"/>
    public double? SpeedOverGroundMinKnots { get; set; }

    /// <inheritdoc cref="RouteLeg.SpeedOverGroundMaxKnots"/>
    public double? SpeedOverGroundMaxKnots { get; set; }

    /// <inheritdoc cref="RouteLeg.SpeedThroughWaterMinKnots"/>
    public double? SpeedThroughWaterMinKnots { get; set; }

    /// <inheritdoc cref="RouteLeg.SpeedThroughWaterMaxKnots"/>
    public double? SpeedThroughWaterMaxKnots { get; set; }

    /// <inheritdoc cref="RouteLeg.DraftMeters"/>
    public double? DraftMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.StaticUnderKeelClearanceMeters"/>
    public double? StaticUnderKeelClearanceMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.DynamicUnderKeelClearanceMeters"/>
    public double? DynamicUnderKeelClearanceMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.SafetyMarginMeters"/>
    public double? SafetyMarginMeters { get; set; }

    /// <inheritdoc cref="RouteLeg.Note"/>
    public string? Note { get; set; }
}
