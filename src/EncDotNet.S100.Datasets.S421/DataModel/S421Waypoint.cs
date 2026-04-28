using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S421.DataModel;

/// <summary>
/// Strongly-typed projection of an S-421 <c>RouteWaypoint</c> feature.
/// Spec reference: S-421 Annex A "RouteWaypoint" (FC).
/// </summary>
public sealed class S421Waypoint
{
    /// <summary>The <c>gml:id</c> of the source <c>RouteWaypoint</c> feature.</summary>
    public required string Id { get; init; }

    /// <summary>FC code <c>routeWaypointID</c> (the author-assigned ordinal).</summary>
    public int? WaypointNumber { get; init; }

    /// <summary>FC code <c>routeWaypointName</c>.</summary>
    public string? Name { get; init; }

    /// <summary>FC code <c>routeWaypointExternalReferenceID</c>.</summary>
    public string? ExternalReferenceId { get; init; }

    /// <summary>
    /// FC code <c>routeWaypointFixed</c> — whether the waypoint position
    /// is fixed (cannot be moved). Tolerates <c>1/0</c> or <c>true/false</c>.
    /// </summary>
    public bool? Fixed { get; init; }

    /// <summary>FC code <c>routeWaypointTurnRadius</c> (nautical miles).</summary>
    public double? TurnRadius { get; init; }

    /// <summary>The geographic position of this waypoint.</summary>
    public required GeoPosition Position { get; init; }

    /// <summary>
    /// The leg departing this waypoint, when this waypoint references a
    /// <c>routeWaypointLeg</c>. The final waypoint of a route has no
    /// outgoing leg.
    /// </summary>
    public S421Leg? OutgoingLeg { get; init; }

    /// <summary>Unrecognised / extension attributes preserved verbatim.</summary>
    public required ImmutableDictionary<string, string> ExtraAttributes { get; init; }
}

/// <summary>
/// Strongly-typed projection of an S-421 <c>RouteWaypointLeg</c> feature.
/// Spec reference: S-421 Annex A "RouteWaypointLeg" (FC).
/// </summary>
public sealed class S421Leg
{
    /// <summary>The <c>gml:id</c> of the source <c>RouteWaypointLeg</c> feature.</summary>
    public required string Id { get; init; }

    /// <summary>The leg's curve geometry as an ordered sequence of positions.</summary>
    public required ImmutableArray<GeoPosition> Coordinates { get; init; }

    /// <summary>FC code <c>routeWaypointLegStarboardXTDL</c> (metres).</summary>
    public double? StarboardCrossTrackDistanceLimit { get; init; }

    /// <summary>FC code <c>routeWaypointLegPortXTDL</c> (metres).</summary>
    public double? PortCrossTrackDistanceLimit { get; init; }

    /// <summary>FC code <c>routeWaypointLegStarboardCL</c> (metres).</summary>
    public double? StarboardChannelLimit { get; init; }

    /// <summary>FC code <c>routeWaypointLegPortCL</c> (metres).</summary>
    public double? PortChannelLimit { get; init; }

    /// <summary>FC code <c>routeWaypointLegSafetyContour</c> (metres).</summary>
    public double? SafetyContour { get; init; }

    /// <summary>FC code <c>routeWaypointLegSafetyDepth</c> (metres).</summary>
    public double? SafetyDepth { get; init; }

    /// <summary>
    /// FC code <c>routeWaypointLegGeometryType</c> as the raw enumerator code
    /// (e.g. 1 = Loxodrome, 2 = Geodesic; see S-421 Annex A).
    /// </summary>
    public int? GeometryTypeCode { get; init; }

    /// <summary>FC code <c>routeWaypointLegSOGMin</c> (knots).</summary>
    public double? SpeedOverGroundMin { get; init; }

    /// <summary>FC code <c>routeWaypointLegSOGMax</c> (knots).</summary>
    public double? SpeedOverGroundMax { get; init; }

    /// <summary>FC code <c>routeWaypointLegSTWMin</c> (knots).</summary>
    public double? SpeedThroughWaterMin { get; init; }

    /// <summary>FC code <c>routeWaypointLegSTWMax</c> (knots).</summary>
    public double? SpeedThroughWaterMax { get; init; }

    /// <summary>FC code <c>routeWaypointLegDraft</c> (metres).</summary>
    public double? Draft { get; init; }

    /// <summary>FC code <c>routeWaypointLegStaticUKC</c> (metres).</summary>
    public double? StaticUnderKeelClearance { get; init; }

    /// <summary>FC code <c>routeWaypointLegDynamicUKC</c> (metres).</summary>
    public double? DynamicUnderKeelClearance { get; init; }

    /// <summary>FC code <c>routeWaypointLegSafetyMargin</c> (metres).</summary>
    public double? SafetyMargin { get; init; }

    /// <summary>FC code <c>routeWaypointLegNote</c>.</summary>
    public string? Note { get; init; }

    /// <summary>Unrecognised / extension attributes preserved verbatim.</summary>
    public required ImmutableDictionary<string, string> ExtraAttributes { get; init; }
}
