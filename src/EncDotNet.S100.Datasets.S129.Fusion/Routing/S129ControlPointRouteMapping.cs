using EncDotNet.S100.Datasets.S421.DataModel;

namespace EncDotNet.S100.Datasets.S129.Fusion.Routing;

/// <summary>
/// The mapping of a single <c>S129ControlPoint</c> onto an S-421
/// <see cref="S421Route"/>.
/// </summary>
/// <remarks>
/// Implemented as a single record with a discriminator
/// (<see cref="Kind"/>) plus nullable per-variant fields. This mirrors
/// the discriminated-union shape used elsewhere in the codebase and
/// keeps the type cheap to enumerate over.
/// </remarks>
/// <param name="Kind">Which variant this mapping is.</param>
/// <param name="Waypoint">
/// The matched waypoint when <see cref="Kind"/> is
/// <see cref="S129RouteMappingKind.OnWaypoint"/>; otherwise <c>null</c>.
/// </param>
/// <param name="Leg">
/// The matched leg when <see cref="Kind"/> is
/// <see cref="S129RouteMappingKind.OnLeg"/>; otherwise <c>null</c>.
/// </param>
/// <param name="LegPositionFraction">
/// The fractional position (0..1) along <see cref="Leg"/>'s polyline
/// at which the control point projects, when <see cref="Kind"/> is
/// <see cref="S129RouteMappingKind.OnLeg"/>; otherwise <c>null</c>.
/// <c>0</c> is the leg's start, <c>1</c> is the leg's end.
/// </param>
/// <param name="DistanceMeters">
/// The great-circle (waypoint) or perpendicular-to-polyline (leg)
/// distance, in metres. <c>null</c> for
/// <see cref="S129RouteMappingKind.Unmapped"/>.
/// </param>
public sealed record S129ControlPointRouteMapping(
    S129RouteMappingKind Kind,
    S421Waypoint? Waypoint,
    S421Leg? Leg,
    double? LegPositionFraction,
    double? DistanceMeters)
{
    /// <summary>The canonical "unmapped" instance.</summary>
    public static S129ControlPointRouteMapping Unmapped { get; } =
        new(S129RouteMappingKind.Unmapped, null, null, null, null);

    /// <summary>Constructs an <see cref="S129RouteMappingKind.OnWaypoint"/> mapping.</summary>
    public static S129ControlPointRouteMapping OnWaypoint(S421Waypoint waypoint, double distanceMeters) =>
        new(S129RouteMappingKind.OnWaypoint, waypoint, null, null, distanceMeters);

    /// <summary>Constructs an <see cref="S129RouteMappingKind.OnLeg"/> mapping.</summary>
    public static S129ControlPointRouteMapping OnLeg(S421Leg leg, double positionFraction, double distanceMeters) =>
        new(S129RouteMappingKind.OnLeg, null, leg, positionFraction, distanceMeters);
}
