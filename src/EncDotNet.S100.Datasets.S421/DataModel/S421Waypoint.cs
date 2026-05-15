using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

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

    /// <summary>
    /// The leg arriving at this waypoint — the <see cref="OutgoingLeg"/> of
    /// the previous waypoint in route order. The first waypoint of a route
    /// has no incoming leg. Populated during projection in a second pass
    /// after all waypoints and legs are materialised; see
    /// <see cref="S421RoutePlan.From"/>.
    /// </summary>
    public S421Leg? IncomingLeg { get; internal set; }

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

    /// <summary>
    /// The waypoint at the start of this leg — the waypoint whose
    /// <see cref="S421Waypoint.OutgoingLeg"/> is this leg. Populated during
    /// projection in a second pass after all waypoints and legs are
    /// materialised; see <see cref="S421RoutePlan.From"/>. Nullable to
    /// tolerate malformed fixtures where the leg is referenced from no
    /// waypoint.
    /// Spec reference: S-421 Annex A "RouteWaypointLeg" / "RouteWaypoint"
    /// (FC) — a leg connects two consecutive waypoints in route order.
    /// </summary>
    public S421Waypoint? StartWaypoint { get; internal set; }

    /// <summary>
    /// The waypoint at the end of this leg — the next waypoint in route
    /// order after <see cref="StartWaypoint"/>. Populated during projection
    /// in a second pass after all waypoints and legs are materialised; see
    /// <see cref="S421RoutePlan.From"/>. Nullable to tolerate malformed
    /// fixtures where the originating waypoint has no successor.
    /// Spec reference: S-421 Annex A "RouteWaypointLeg" / "RouteWaypoint"
    /// (FC) — a leg connects two consecutive waypoints in route order.
    /// </summary>
    public S421Waypoint? EndWaypoint { get; internal set; }

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
