using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>The codes of the findings <see cref="RouteCheckEngine"/> produces.</summary>
public static class RouteCheckCodes
{
    /// <summary>The track crosses a <c>LandArea</c> or a <c>Coastline</c>. MUST REVIEW.</summary>
    public const string CrossesLand = "route_check.crosses_land";

    /// <summary>Land reaches into the corridor, but the track doesn't cross it. CHECK.</summary>
    public const string LandInCorridor = "route_check.land_in_corridor";

    /// <summary>The track crosses a drying <c>DepthArea</c>. MUST REVIEW.</summary>
    public const string CrossesDrying = "route_check.crosses_drying";

    /// <summary>Drying ground reaches into the corridor, but the track doesn't cross it. CHECK.</summary>
    public const string DryingInCorridor = "route_check.drying_in_corridor";

    /// <summary>
    /// A charted danger (wreck, obstruction or rock) shallower than the
    /// threshold, or of unknown depth, inside the cross-track limits. MUST REVIEW.
    /// </summary>
    public const string DangerInCorridor = "route_check.danger_in_corridor";

    /// <summary>
    /// A charted danger shallower than the threshold within
    /// <see cref="RouteCheckOptions.DangerNearbyMeters"/> beyond the cross-track limits. CHECK.
    /// </summary>
    public const string DangerNearRoute = "route_check.danger_near_route";

    /// <summary>The track crosses depth, dredged or drying areas shallower than the safety contour. MUST REVIEW.</summary>
    public const string CrossesSafetyContour = "route_check.crosses_safety_contour";

    /// <summary>Areas shallower than the safety contour reach into the corridor, but the track doesn't cross them. CHECK.</summary>
    public const string SafetyContourInCorridor = "route_check.safety_contour_in_corridor";

    /// <summary>Soundings shallower than the safety depth inside the cross-track limits. CHECK.</summary>
    public const string SoundingsBelowSafetyDepth = "route_check.soundings_below_safety_depth";

    /// <summary>The track crosses S-102 cells shallower than the safety contour. MUST REVIEW.</summary>
    public const string CrossesS102Shoal = "route_check.crosses_s102_shoal";

    /// <summary>S-102 cells shallower than the safety contour inside the cross-track limits, off the track. CHECK.</summary>
    public const string S102ShoalInCorridor = "route_check.s102_shoal_in_corridor";

    /// <summary>The track leaves the data coverage of every loaded chart cell. CHECK.</summary>
    public const string OutsideChartCoverage = "data.outside_chart_coverage";
}

/// <summary>The corridor the check swept along one leg.</summary>
/// <param name="LegIndex">The leg's index in the route.</param>
/// <param name="StartDistanceMeters">Distance from departure to the leg's start.</param>
/// <param name="EndDistanceMeters">Distance from departure to the leg's end.</param>
/// <param name="PortHalfWidthMeters">How far the corridor reaches to port of the track.</param>
/// <param name="StarboardHalfWidthMeters">How far the corridor reaches to starboard of the track.</param>
/// <param name="Ring">The corridor's closed ring, in WGS-84: port side forward, then starboard side back.</param>
public sealed record RouteCheckCorridor(
    int LegIndex,
    double StartDistanceMeters,
    double EndDistanceMeters,
    double PortHalfWidthMeters,
    double StarboardHalfWidthMeters,
    IReadOnlyList<GeoPosition> Ring);

/// <summary>The result of <see cref="RouteCheckEngine.Check"/>.</summary>
/// <param name="Findings">
/// The findings, sorted by group (MUST REVIEW, CHECK, INFO) and then by
/// distance from departure.
/// </param>
/// <param name="Corridors">The corridor of each leg, in route order.</param>
public sealed record RouteCheckResult(
    IReadOnlyList<RouteFinding> Findings,
    IReadOnlyList<RouteCheckCorridor> Corridors)
{
    /// <summary>The route's length in metres.</summary>
    public double LengthMeters => Corridors.Count == 0 ? 0 : Corridors[^1].EndDistanceMeters;
}
