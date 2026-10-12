namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// The mutable navigational envelope of a single route leg — the segment
/// joining two consecutive <see cref="RouteWaypoint"/>s of a
/// <see cref="Route"/>.
/// </summary>
/// <remarks>
/// <para>
/// A leg does not store its own endpoints: leg <c>i</c> always joins
/// waypoint <c>i</c> to waypoint <c>i+1</c> of the owning route, so the
/// geometry follows automatically when waypoints move or are inserted /
/// deleted. Distance and bearing are computed on demand by
/// <see cref="Route.ComputeLegMetrics"/> using <see cref="GeometryType"/>.
/// </para>
/// <para>
/// The optional attribute fields mirror the S-421 <c>RouteWaypointLeg</c>
/// projection
/// (<see cref="EncDotNet.S100.Datasets.S421.DataModel.S421Leg"/>); their
/// units match S-421 (metres for distances/limits, knots for speeds) so a
/// route exports to S-421 GML with a direct mapping. All are nullable: a
/// freshly created leg carries only its geometry type.
/// </para>
/// <para>
/// The fields named by <see cref="RoutePassageField"/> (draft, planned speed,
/// static UKC and the cross-track limits) are <b>overrides</b> of the owning
/// route's <see cref="RoutePassage"/>: <c>null</c> means "inherit". Read the
/// value that applies with <see cref="Route.GetEffectiveLegValue"/> and edit
/// it with <see cref="Route.SetLegOverride"/>. See <see cref="RoutePassage"/>
/// for the S-421 and RTZ mapping.
/// </para>
/// </remarks>
public sealed class RouteLeg
{
    /// <summary>
    /// How the leg's path is defined. Defaults to
    /// <see cref="RouteLegGeometryType.Loxodrome"/> (the ECDIS convention).
    /// </summary>
    public RouteLegGeometryType GeometryType { get; set; } = RouteLegGeometryType.Loxodrome;

    /// <summary>Starboard cross-track distance limit, in metres (S-421 <c>routeWaypointLegStarboardXTDL</c>).</summary>
    public double? StarboardCrossTrackDistanceLimitMeters { get; set; }

    /// <summary>Port cross-track distance limit, in metres (S-421 <c>routeWaypointLegPortXTDL</c>).</summary>
    public double? PortCrossTrackDistanceLimitMeters { get; set; }

    /// <summary>Starboard channel limit, in metres (S-421 <c>routeWaypointLegStarboardCL</c>).</summary>
    public double? StarboardChannelLimitMeters { get; set; }

    /// <summary>Port channel limit, in metres (S-421 <c>routeWaypointLegPortCL</c>).</summary>
    public double? PortChannelLimitMeters { get; set; }

    /// <summary>Safety contour for this leg, in metres (S-421 <c>routeWaypointLegSafetyContour</c>).</summary>
    public double? SafetyContourMeters { get; set; }

    /// <summary>Safety depth for this leg, in metres (S-421 <c>routeWaypointLegSafetyDepth</c>).</summary>
    public double? SafetyDepthMeters { get; set; }

    /// <summary>Minimum speed over ground, in knots (S-421 <c>routeWaypointLegSOGMin</c>).</summary>
    public double? SpeedOverGroundMinKnots { get; set; }

    /// <summary>Maximum speed over ground, in knots (S-421 <c>routeWaypointLegSOGMax</c>).</summary>
    public double? SpeedOverGroundMaxKnots { get; set; }

    /// <summary>Minimum speed through water, in knots (S-421 <c>routeWaypointLegSTWMin</c>).</summary>
    public double? SpeedThroughWaterMinKnots { get; set; }

    /// <summary>Maximum speed through water, in knots (S-421 <c>routeWaypointLegSTWMax</c>).</summary>
    public double? SpeedThroughWaterMaxKnots { get; set; }

    /// <summary>
    /// Planned speed over ground for this leg, in knots: an override of
    /// <see cref="RoutePassage.SpeedKnots"/> (S-421
    /// <c>routeScheduleElementPlanSOG</c> of the leg's start waypoint).
    /// </summary>
    public double? PlannedSpeedKnots { get; set; }

    /// <summary>Planned draft for this leg, in metres (S-421 <c>routeWaypointLegDraft</c>).</summary>
    public double? DraftMeters { get; set; }

    /// <summary>Static under-keel clearance, in metres (S-421 <c>routeWaypointLegStaticUKC</c>).</summary>
    public double? StaticUnderKeelClearanceMeters { get; set; }

    /// <summary>Dynamic under-keel clearance, in metres (S-421 <c>routeWaypointLegDynamicUKC</c>).</summary>
    public double? DynamicUnderKeelClearanceMeters { get; set; }

    /// <summary>Safety margin, in metres (S-421 <c>routeWaypointLegSafetyMargin</c>).</summary>
    public double? SafetyMarginMeters { get; set; }

    /// <summary>Free-text note for this leg (S-421 <c>routeWaypointLegNote</c>).</summary>
    public string? Note { get; set; }

    /// <summary>Returns this leg's override of <paramref name="field"/>.</summary>
    /// <param name="field">A leg-overridable passage field.</param>
    /// <returns>The override, or <c>null</c> when the leg inherits.</returns>
    /// <exception cref="ArgumentException"><paramref name="field"/> can't be overridden per leg.</exception>
    public double? GetOverride(RoutePassageField field) => field switch
    {
        RoutePassageField.Draft => DraftMeters,
        RoutePassageField.Speed => PlannedSpeedKnots,
        RoutePassageField.RequiredUkc => StaticUnderKeelClearanceMeters,
        RoutePassageField.PortCrossTrack => PortCrossTrackDistanceLimitMeters,
        RoutePassageField.StarboardCrossTrack => StarboardCrossTrackDistanceLimitMeters,
        _ => throw new ArgumentException($"{field} can't be overridden per leg.", nameof(field)),
    };

    internal void SetOverride(RoutePassageField field, double? value)
    {
        switch (field)
        {
            case RoutePassageField.Draft: DraftMeters = value; break;
            case RoutePassageField.Speed: PlannedSpeedKnots = value; break;
            case RoutePassageField.RequiredUkc: StaticUnderKeelClearanceMeters = value; break;
            case RoutePassageField.PortCrossTrack: PortCrossTrackDistanceLimitMeters = value; break;
            case RoutePassageField.StarboardCrossTrack: StarboardCrossTrackDistanceLimitMeters = value; break;
            default: throw new ArgumentException($"{field} can't be overridden per leg.", nameof(field));
        }
    }
}
