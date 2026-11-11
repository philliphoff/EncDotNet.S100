namespace EncDotNet.S100.Datasets.S129.Fusion.Routing;

/// <summary>
/// Tunable parameters for <see cref="S129RouteBinder.Bind"/>.
/// </summary>
/// <param name="WaypointToleranceMeters">
/// Maximum great-circle distance (metres) between a control point and a
/// waypoint for which the binder will record an
/// <see cref="S129RouteMappingKind.OnWaypoint"/> mapping. Default 200 m.
/// </param>
/// <param name="LegToleranceMeters">
/// Maximum perpendicular distance (metres) between a control point and
/// the nearest point on a leg's polyline for which the binder will
/// record an <see cref="S129RouteMappingKind.OnLeg"/> mapping. Default
/// 100 m.
/// </param>
/// <remarks>
/// S-129 Edition 2.0.0 does not require explicit waypoint cross-refs
/// from control points to the source S-421 route. The binder therefore
/// matches by spatial proximity; the defaults are tuned for typical
/// pilotage / harbour-approach UKC plans and may be loosened for
/// open-water plans.
/// </remarks>
public sealed record S129RouteBindingOptions(
    double WaypointToleranceMeters = 200.0,
    double LegToleranceMeters = 100.0)
{
    /// <summary>Default options.</summary>
    public static S129RouteBindingOptions Default { get; } = new();
}
