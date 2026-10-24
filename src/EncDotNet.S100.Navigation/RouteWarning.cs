using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>How a warning's geometry relates to a route.</summary>
public enum RouteWarningRelation
{
    /// <summary>A line or area of the warning reaches the corridor between the cross-track limits.</summary>
    Crosses,

    /// <summary>
    /// The warning is within <see cref="RouteWarningOptions.NearDistanceMeters"/>
    /// of the track, or is a point inside the corridor (a position can't be crossed).
    /// </summary>
    Near,

    /// <summary>Neither.</summary>
    Off,
}

/// <summary>Whether a warning applies while the vessel is at it.</summary>
public enum RouteWarningStatus
{
    /// <summary>A validity range overlaps the vessel's time there, and the warning isn't cancelled by then.</summary>
    InForce,

    /// <summary>No validity range overlaps the vessel's time there.</summary>
    NotInForce,

    /// <summary>
    /// Its cancellation date, a warning that cancels it, or an in-force
    /// bulletin that leaves it out comes before the vessel gets there
    /// (see <see cref="WarningDetails.Withdrawal"/> for which).
    /// </summary>
    Cancelled,
}

/// <summary>
/// One navigational warning part to check a route against.
/// </summary>
/// <param name="Info">The part's message: identification, validity, cancellation and text.</param>
/// <param name="Shapes">
/// The part's geometry and the areas it affects (<see cref="NavigationalWarningInfo.AffectedAreas"/>),
/// in WGS-84. Points, curves and surfaces are each used; a part with none is never on the route.
/// </param>
/// <param name="Feature">The feature behind it, which the finding refers to, or <c>null</c>.</param>
public sealed record RouteWarning(
    NavigationalWarningInfo Info,
    IReadOnlyList<S100FeatureGeometry> Shapes,
    RouteFindingFeature? Feature = null);

/// <summary>One warning against the route, found or not.</summary>
/// <param name="Warning">The warning.</param>
/// <param name="Relation">How it relates to the route.</param>
/// <param name="LegIndex">The leg where the route first meets it, or <c>null</c> when off the route.</param>
/// <param name="StartDistanceMeters">Distance from departure to where the route first meets it (or passes closest); <c>NaN</c> when off.</param>
/// <param name="EndDistanceMeters">Distance from departure to where the route leaves it; <c>NaN</c> when off.</param>
/// <param name="ClosestMeters">The least distance between the track and the warning, 0 when the track crosses it.</param>
/// <param name="Finding">The finding, or <c>null</c> when off the route.</param>
public sealed record RouteWarningMatch(
    RouteWarning Warning,
    RouteWarningRelation Relation,
    int? LegIndex,
    double StartDistanceMeters,
    double EndDistanceMeters,
    double ClosestMeters,
    RouteFinding? Finding);

/// <summary>What <see cref="WarningCheck"/> found besides its findings: every warning near the route.</summary>
/// <param name="Matches">One entry per warning near the route, in force or not, in the order the source gave them.</param>
public sealed record WarningCheckResult(IReadOnlyList<RouteWarningMatch> Matches);

/// <summary>Tuning for <see cref="WarningCheck"/>.</summary>
public sealed record RouteWarningOptions
{
    /// <summary>The default options.</summary>
    public static RouteWarningOptions Default { get; } = new();

    /// <summary>
    /// How close to the track a warning counts as near the route, in metres.
    /// Default 0,5 NM (926 m).
    /// </summary>
    public double NearDistanceMeters { get; init; } = 0.5 * PassageGeodesy.MetersPerNauticalMile;

    /// <summary>
    /// The length of the pieces a leg is cut into, in metres. Crossings are
    /// found exactly within a piece; it bounds how far a planar piece departs
    /// from the leg's rhumb line or great circle. Default 50 m.
    /// </summary>
    public double SegmentLengthMeters { get; init; } = 50;

    /// <summary>The most pieces per leg; a longer leg uses longer pieces. Default 20 000.</summary>
    public int MaxSegmentsPerLeg { get; init; } = 20_000;
}
