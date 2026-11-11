namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// Computed geometry of one route leg: its ordinal plus the distance and
/// initial true bearing derived from the adjacent waypoint positions and
/// the leg's <see cref="RouteLeg.GeometryType"/>.
/// </summary>
/// <param name="LegIndex">Zero-based index of the leg within its route (leg
/// <c>i</c> joins waypoint <c>i</c> to waypoint <c>i+1</c>).</param>
/// <param name="DistanceNm">Leg length in nautical miles.</param>
/// <param name="InitialBearingDegrees">Initial true bearing in degrees,
/// clockwise from true north, in the range [0°, 360°). For a loxodrome this
/// is constant along the leg; for a geodesic it is the bearing at the
/// departure waypoint.</param>
public readonly record struct RouteLegMetrics(
    int LegIndex,
    double DistanceNm,
    double InitialBearingDegrees);
