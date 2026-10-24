using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

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

    /// <summary>For information, e.g. an area that isn't in force while the vessel is there.</summary>
    Info,
}

/// <summary>
/// What a finding is about. Every <see cref="RouteFindingCode"/> belongs to
/// one category (<see cref="RouteFindingCode.Category"/>), so a host can
/// filter or tag findings without matching their codes.
/// </summary>
public enum RouteFindingCategory
{
    /// <summary>Charted land and drying ground: <c>route_check.crosses_land</c>, <c>route_check.drying_in_corridor</c> and the like.</summary>
    Chart,

    /// <summary>
    /// Depths against the safety settings and the keel: the safety contour,
    /// soundings, S-102 shoals and charted dangers.
    /// </summary>
    Depth,

    /// <summary>Regulated areas: S-101 restricted, caution, traffic and anchorage areas, and S-122 protected areas.</summary>
    Area,

    /// <summary>S-124 navigational warnings.</summary>
    Warning,

    /// <summary>The under-keel clearance check: stretches below your UKC setting.</summary>
    Clearance,

    /// <summary>How far a clearance can be trusted, e.g. depths from S-101 rather than S-102.</summary>
    Confidence,

    /// <summary>Missing or ending data: water-level gaps, no charted depth, leaving chart coverage.</summary>
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
public sealed record RouteFindingFeature(string DatasetId, string FeatureId)
{
    /// <summary>The feature's type, such as <c>"LandArea"</c>, when the check that found it knows it; otherwise <c>null</c>.</summary>
    public string? FeatureType { get; init; }
}

/// <summary>
/// One entry in a route's findings list (#905): what a route check found,
/// where it is, the numbers behind it (<see cref="Details"/>), and its text
/// as a <see cref="RouteFindingFormatter"/> rendered it.
/// </summary>
/// <remarks>
/// The checks render <see cref="Title"/> and <see cref="Meta"/> with the
/// formatter in their options. A host can render them again in another
/// language, depth unit or time format with
/// <see cref="RouteFindingFormatter.Render(RouteFinding)"/>, without checking
/// again.
/// </remarks>
public sealed class RouteFinding
{
    /// <summary>Creates a finding without text; render it with a <see cref="RouteFindingFormatter"/>.</summary>
    /// <param name="id">Stable within one result, e.g. <c>clearance.below_setting.2</c>, so a selection survives a recompute that finds the same thing.</param>
    /// <param name="code">The kind of finding.</param>
    /// <param name="group">What to do about it; <c>null</c> for the code's <see cref="RouteFindingCode.DefaultGroup"/>.</param>
    public RouteFinding(string id, RouteFindingCode code, RouteFindingGroup? group = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(code);
        Id = id;
        Code = code;
        Group = group ?? code.DefaultGroup;
    }

    /// <summary>Stable within one result, e.g. <c>clearance.below_setting.2</c>, so a selection survives a recompute that finds the same thing.</summary>
    public string Id { get; }

    /// <summary>The kind of finding, e.g. <see cref="RouteFindingCodes.CrossesDrying"/>.</summary>
    public RouteFindingCode Code { get; }

    /// <summary>What to do about it.</summary>
    public RouteFindingGroup Group { get; }

    /// <summary>What it's about: its code's <see cref="RouteFindingCode.Category"/>.</summary>
    public RouteFindingCategory Category => Code.Category;

    /// <summary>The leg the finding is on (its first leg for a span), or <c>null</c> when it isn't tied to a leg.</summary>
    public int? LegIndex { get; init; }

    /// <summary>Distance from departure to the point to show, such as the minimum in a span, or <c>null</c> when it isn't on the route.</summary>
    public Length? Distance { get; init; }

    /// <summary>Where it is, or <c>null</c> when it has no place on the map.</summary>
    public RouteFindingLocation? Location { get; init; }

    /// <summary>The feature behind it, or <c>null</c>.</summary>
    public RouteFindingFeature? Feature { get; init; }

    /// <summary>The numbers behind it, by kind (<see cref="DangerDetails"/>, <see cref="WarningDetails"/>, …), or <c>null</c> when its code says it all.</summary>
    public RouteFindingDetails? Details { get; init; }

    /// <summary>One line, e.g. "Below your UKC setting for 12 min"; empty until rendered.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Where and why, e.g. "Leg 6 → 7 · min 0.4 m at 07:52"; empty until rendered.</summary>
    public string Meta { get; init; } = string.Empty;

    /// <summary>A copy of this finding with other text.</summary>
    /// <param name="title">The title.</param>
    /// <param name="meta">The meta line.</param>
    /// <returns>The copy.</returns>
    public RouteFinding WithText(string title, string meta) => new(Id, Code, Group)
    {
        LegIndex = LegIndex,
        Distance = Distance,
        Location = Location,
        Feature = Feature,
        Details = Details,
        Title = title ?? string.Empty,
        Meta = meta ?? string.Empty,
    };

    /// <summary>
    /// Sorts findings by group (MUST REVIEW, CHECK, INFO), then by distance
    /// from departure (findings not on the route last), keeping ties in order.
    /// </summary>
    /// <param name="findings">The findings.</param>
    /// <returns>The sorted list.</returns>
    public static IReadOnlyList<RouteFinding> Sort(IEnumerable<RouteFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return findings
            .OrderBy(f => f.Group)
            .ThenBy(f => f.Distance?.TotalMetres ?? double.MaxValue)
            .ToList();
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Id}: {Title}";
}
