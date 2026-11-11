namespace EncDotNet.S100.Viewer.Routing;

/// <summary>
/// How a route leg's path between two consecutive waypoints is defined.
/// The numeric values match the S-421 <c>routeWaypointLegGeometryType</c>
/// enumerator codes (S-421 Annex A) so an editable route projects onto an
/// S-421 <see cref="EncDotNet.S100.Datasets.S421.DataModel.S421Leg"/>
/// without remapping.
/// </summary>
public enum RouteLegGeometryType
{
    /// <summary>
    /// Loxodrome (rhumb line) — a path of constant true bearing. This is
    /// the ECDIS default and matches the Measure Mode read-out. S-421
    /// code <c>1</c>.
    /// </summary>
    Loxodrome = 1,

    /// <summary>
    /// Geodesic (great circle) — the shortest path over the sphere; the
    /// bearing changes continuously along the leg. S-421 code <c>2</c>.
    /// </summary>
    Geodesic = 2,
}
