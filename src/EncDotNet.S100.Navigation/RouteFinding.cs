using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// What a finding asks the navigator to do, which is how the findings list
/// groups it (MUST REVIEW, CHECK, INFO).
/// </summary>
/// <remarks>
/// Only <see cref="MustReview"/> findings are drawn in the destructive
/// colour; red is reserved for them and for "Below your UKC setting".
/// </remarks>
public enum RouteFindingGroup
{
    /// <summary>Must review before relying on the route, e.g. below your UKC setting or crossing drying ground.</summary>
    MustReview,

    /// <summary>Worth checking, e.g. a nearby warning or lower-confidence depths.</summary>
    Check,

    /// <summary>For information, e.g. an expired forecast that isn't used.</summary>
    Info,
}

/// <summary>
/// The check that produced a finding: the list's right-aligned tag. It
/// names the check, not the data.
/// </summary>
public enum RouteFindingSource
{
    /// <summary>The under-keel clearance check (#887).</summary>
    Clearance,

    /// <summary>A route check against charted features: dangers, land, limits (#888).</summary>
    RouteCheck,

    /// <summary>An S-124 navigational warning that affects the route (#888).</summary>
    S124,

    /// <summary>How far a result can be trusted, e.g. depths from S-101 rather than S-102.</summary>
    Confidence,

    /// <summary>Missing, ending or expired data.</summary>
    Data,
}

/// <summary>
/// Where a finding is: a point, or a span of the route between two
/// distances from departure.
/// </summary>
/// <param name="Path">One position for a point, or the positions along the span in route order.</param>
/// <param name="StartDistanceMeters">Distance from departure to the start of the span (equal to <paramref name="EndDistanceMeters"/> for a point).</param>
/// <param name="EndDistanceMeters">Distance from departure to the end of the span.</param>
public sealed record RouteFindingLocation(
    IReadOnlyList<GeoPosition> Path,
    double StartDistanceMeters,
    double EndDistanceMeters)
{
    /// <summary><c>true</c> when the location is a single point.</summary>
    public bool IsPoint => Path.Count == 1;

    /// <summary>A point location at <paramref name="distanceMeters"/> from departure.</summary>
    /// <param name="position">The point.</param>
    /// <param name="distanceMeters">Distance from departure.</param>
    /// <returns>The location.</returns>
    public static RouteFindingLocation Point(GeoPosition position, double distanceMeters) =>
        new([position], distanceMeters, distanceMeters);
}

/// <summary>
/// A feature behind a finding, which pick and describe can open.
/// </summary>
/// <param name="DatasetId">The catalog id of the dataset that holds the feature.</param>
/// <param name="FeatureId">The feature's id within the dataset.</param>
public sealed record RouteFindingFeature(string DatasetId, string FeatureId);

/// <summary>
/// One entry in a route's findings list (#905). The clearance check (#887)
/// and the route checks (#888) both produce these.
/// </summary>
/// <param name="Id">Stable within one result, e.g. <c>clearance.below_setting.2</c>, so a selection survives a recompute that finds the same thing.</param>
/// <param name="Code">The kind of finding, e.g. <c>clearance.below_setting</c> or <c>data.water_level_ends</c>.</param>
/// <param name="Group">What to do about it.</param>
/// <param name="Source">The check that produced it.</param>
/// <param name="Title">One line, e.g. "Below your UKC setting for 12 min".</param>
/// <param name="Meta">Where and why, e.g. "Leg 6 → 7 · min 0.4 m at 07:52".</param>
/// <param name="LegIndex">The leg the finding is on (its first leg for a span), or <c>null</c> when it isn't tied to a leg.</param>
/// <param name="DistanceMeters">Distance from departure to the point to show, such as the minimum in a span, or <c>null</c> when it isn't on the route.</param>
/// <param name="Location">Where it is, or <c>null</c> when it has no place on the map.</param>
/// <param name="Feature">The feature behind it, or <c>null</c>.</param>
public sealed record RouteFinding(
    string Id,
    string Code,
    RouteFindingGroup Group,
    RouteFindingSource Source,
    string Title,
    string Meta,
    int? LegIndex,
    double? DistanceMeters,
    RouteFindingLocation? Location,
    RouteFindingFeature? Feature = null);
