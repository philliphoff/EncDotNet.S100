using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S421.DataModel;

/// <summary>
/// Strongly-typed projection of an S-421 <c>Route</c> feature plus the
/// related <c>RouteInfo</c> information type.
/// Spec reference: S-421 Annex A "Route" feature (FC).
/// </summary>
public sealed class S421Route
{
    /// <summary>The <c>gml:id</c> of the source <c>Route</c> feature.</summary>
    public required string Id { get; init; }

    /// <summary>The S-421 route format version (FC code <c>routeFormatVersion</c>).</summary>
    public string? FormatVersion { get; init; }

    /// <summary>The author-supplied unique identifier for this route (FC code <c>routeID</c>).</summary>
    public string? RouteId { get; init; }

    /// <summary>The route edition number (FC code <c>routeEditionNo</c>).</summary>
    public int? EditionNumber { get; init; }

    /// <summary>The resolved <see cref="S421RouteInfo"/> information type, when present.</summary>
    public S421RouteInfo? Info { get; init; }

    /// <summary>
    /// Waypoints in route order, resolved via
    /// <c>RouteWaypoints/routeWaypoint</c> <c>xlink:href</c> references.
    /// </summary>
    public required ImmutableArray<S421Waypoint> Waypoints { get; init; }

    /// <summary>
    /// Route legs in geometric order. Each leg corresponds to a
    /// <c>RouteWaypointLeg</c> feature; legs are typically resolved via
    /// <c>routeWaypointLeg</c> references on waypoints.
    /// </summary>
    public required ImmutableArray<S421Leg> Legs { get; init; }

    /// <summary>Action points referenced by the route, in document order.</summary>
    public required ImmutableArray<S421ActionPoint> ActionPoints { get; init; }

    /// <summary>Schedules referenced by the route, in document order.</summary>
    public required ImmutableArray<S421Schedule> Schedules { get; init; }

    /// <summary>
    /// Attributes on the source <c>Route</c> feature that are not recognised
    /// by this typed projection. Unknown / extension / future-edition
    /// attributes round-trip here verbatim.
    /// </summary>
    public required ImmutableDictionary<string, string> ExtraAttributes { get; init; }
}

/// <summary>
/// Strongly-typed projection of an S-421 <c>RouteInfo</c> information type.
/// Spec reference: S-421 Annex A "RouteInfo" (FC).
/// </summary>
public sealed class S421RouteInfo
{
    /// <summary>The <c>gml:id</c> of the source <c>RouteInfo</c> element.</summary>
    public required string Id { get; init; }

    /// <summary>FC code <c>routeInfoName</c>.</summary>
    public string? Name { get; init; }

    /// <summary>FC code <c>routeInfoAuthor</c>.</summary>
    public string? Author { get; init; }

    /// <summary>FC code <c>routeInfoDescription</c>.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// FC code <c>routeInfoStatus</c> as the raw enumerator code
    /// (1 = Original, 2 = Update, … 11; see S-421 Annex A).
    /// </summary>
    public int? Status { get; init; }

    /// <summary>FC code <c>routeInfoEditionTime</c>.</summary>
    public DateTimeOffset? EditionTime { get; init; }

    /// <summary>FC code <c>routeInfoValidityStart</c>.</summary>
    public DateTimeOffset? ValidityStart { get; init; }

    /// <summary>FC code <c>routeInfoValidityEnd</c>.</summary>
    public DateTimeOffset? ValidityEnd { get; init; }

    /// <summary>FC code <c>routeInfoDeparturePortID1</c>.</summary>
    public string? DeparturePortId1 { get; init; }

    /// <summary>FC code <c>routeInfoDeparturePortID2</c>.</summary>
    public string? DeparturePortId2 { get; init; }

    /// <summary>FC code <c>routeInfoDeparturePortCall</c>.</summary>
    public string? DeparturePortCall { get; init; }

    /// <summary>FC code <c>routeInfoArrivalPortID1</c>.</summary>
    public string? ArrivalPortId1 { get; init; }

    /// <summary>FC code <c>routeInfoArrivalPortID2</c>.</summary>
    public string? ArrivalPortId2 { get; init; }

    /// <summary>FC code <c>routeInfoArrivalPortCall</c>.</summary>
    public string? ArrivalPortCall { get; init; }

    /// <summary>FC code <c>routeInfoReferencePrevRoute</c>.</summary>
    public string? PreviousRouteReference { get; init; }

    /// <summary>FC code <c>routeInfoReferenceNextRoute</c>.</summary>
    public string? NextRouteReference { get; init; }

    /// <summary>Vessel-specific metadata (vessel name, MMSI, IMO, etc.).</summary>
    public S421VesselInfo? Vessel { get; init; }

    /// <summary>Unrecognised / extension attributes preserved verbatim.</summary>
    public required ImmutableDictionary<string, string> ExtraAttributes { get; init; }
}

/// <summary>
/// Vessel-specific metadata carried inside an S-421 <c>RouteInfo</c>
/// information type. All properties may be absent.
/// </summary>
public sealed class S421VesselInfo
{
    /// <summary>FC code <c>routeInfoVesselType</c> as the raw enumerator code.</summary>
    public int? VesselType { get; init; }

    /// <summary>FC code <c>routeInfoVesselName</c>.</summary>
    public string? Name { get; init; }

    /// <summary>FC code <c>routeInfoVesselMMSI</c>.</summary>
    public string? Mmsi { get; init; }

    /// <summary>FC code <c>routeInfoVesselCallsign</c>.</summary>
    public string? Callsign { get; init; }

    /// <summary>FC code <c>routeInfoVesselIMO</c>.</summary>
    public string? Imo { get; init; }

    /// <summary>FC code <c>routeInfoVesselVoyage</c>.</summary>
    public string? VoyageId { get; init; }

    /// <summary>FC code <c>routeInfoVesselHeight</c> (metres).</summary>
    public double? HeightMeters { get; init; }

    /// <summary>FC code <c>routeInfoVesselLength</c> (metres).</summary>
    public double? LengthMeters { get; init; }

    /// <summary>FC code <c>routeInfoVesselBeam</c> (metres).</summary>
    public double? BeamMeters { get; init; }
}
