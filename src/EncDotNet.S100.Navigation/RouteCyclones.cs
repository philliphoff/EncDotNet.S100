using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// A tropical cyclone wind radius the weather check tests a route against
/// (#1299): the area where winds reach at least that many knots.
/// </summary>
public enum TropicalCycloneRadius
{
    /// <summary>The 34 kt (gale force) wind radius.</summary>
    Knots34 = 34,

    /// <summary>The 50 kt (storm force) wind radius.</summary>
    Knots50 = 50,

    /// <summary>The 64 kt (hurricane force) wind radius.</summary>
    Knots64 = 64,
}

/// <summary>
/// The mariner's choices for the tropical cyclone rule of the weather check
/// (#1299, design note V39): which wind radius to test, whether to report
/// the closest approach, and which radii must be reviewed. Kept with the
/// mariner settings, not with a route.
/// </summary>
/// <remarks>
/// The defaults are the user's decisions Q6 and Q7: the 34 kt radius, the
/// closest approach reported, and Must review inside the 50 kt and 64 kt
/// radii. The cone is always tested, and is CHECK.
/// </remarks>
public sealed record TropicalCycloneCheckSettings
{
    /// <summary>The defaults: the 34 kt radius, closest approach on, Must review inside 50 kt.</summary>
    public static TropicalCycloneCheckSettings Default { get; } = new();

    /// <summary>
    /// The outermost wind radius the check tests. Radii outside it aren't
    /// reported. Default <see cref="TropicalCycloneRadius.Knots34"/>.
    /// </summary>
    public TropicalCycloneRadius CheckAgainst { get; init; } = TropicalCycloneRadius.Knots34;

    /// <summary>Whether to report the route's closest approach to each storm (<see cref="RouteFindingCodes.CycloneClosestApproach"/>). Default on.</summary>
    public bool ReportClosestApproach { get; init; } = true;

    /// <summary>
    /// The weakest radius whose findings are MUST REVIEW, or <c>null</c> for
    /// never. Radii at or above it are MUST REVIEW; weaker radii and the cone
    /// are CHECK. Default <see cref="TropicalCycloneRadius.Knots50"/>. A value
    /// outside <see cref="CheckAgainst"/> acts as <see cref="CheckAgainst"/>
    /// (<see cref="EffectiveMustReviewInside"/>).
    /// </summary>
    public TropicalCycloneRadius? MustReviewInside { get; init; } = TropicalCycloneRadius.Knots50;

    /// <summary>
    /// <see cref="MustReviewInside"/>, moved in to <see cref="CheckAgainst"/>
    /// when it lies outside it: Must review can't cover a radius the check
    /// doesn't test.
    /// </summary>
    public TropicalCycloneRadius? EffectiveMustReviewInside =>
        MustReviewInside is { } inside && inside < CheckAgainst ? CheckAgainst : MustReviewInside;

    /// <summary>The radii the check tests, weakest first: <see cref="CheckAgainst"/> and every stronger one.</summary>
    public IReadOnlyList<TropicalCycloneRadius> TestedRadii =>
        [.. Enum.GetValues<TropicalCycloneRadius>().Where(r => r >= CheckAgainst).Order()];

    /// <summary>The group of a finding inside a radius.</summary>
    /// <param name="radius">The radius.</param>
    /// <returns>MUST REVIEW at or above <see cref="EffectiveMustReviewInside"/>; otherwise CHECK.</returns>
    public RouteFindingGroup GroupOf(TropicalCycloneRadius radius) =>
        EffectiveMustReviewInside is { } inside && radius >= inside ? RouteFindingGroup.MustReview : RouteFindingGroup.Check;
}

/// <summary>Which tropical cyclone advisory a <see cref="IRouteCyclone"/> is.</summary>
/// <param name="DatasetId">The catalog id of the advisory's dataset.</param>
/// <param name="StormId">The storm's id, such as the ATCF id <c>AL142026</c>.</param>
/// <param name="Name">The storm's name, such as <c>ZELDA</c>.</param>
/// <param name="Advisory">The advisory number, such as <c>17</c> or <c>17A</c>.</param>
/// <param name="IssueTime">When the advisory was issued.</param>
public sealed record RouteCycloneInfo(string DatasetId, string StormId, string Name, string Advisory, DateTimeOffset IssueTime);

/// <summary>How far winds of a strength reach from a storm's centre in each compass quadrant, in nautical miles.</summary>
/// <param name="Radius">The wind radius.</param>
/// <param name="NortheastNauticalMiles">The extent in the northeast quadrant (bearings 0° to 90°).</param>
/// <param name="SoutheastNauticalMiles">The extent in the southeast quadrant (90° to 180°).</param>
/// <param name="SouthwestNauticalMiles">The extent in the southwest quadrant (180° to 270°).</param>
/// <param name="NorthwestNauticalMiles">The extent in the northwest quadrant (270° to 360°).</param>
public sealed record RouteCycloneRadii(
    TropicalCycloneRadius Radius,
    double NortheastNauticalMiles,
    double SoutheastNauticalMiles,
    double SouthwestNauticalMiles,
    double NorthwestNauticalMiles)
{
    /// <summary>The largest extent of the four quadrants, in nautical miles.</summary>
    public double MaximumNauticalMiles =>
        Math.Max(Math.Max(NortheastNauticalMiles, SoutheastNauticalMiles), Math.Max(SouthwestNauticalMiles, NorthwestNauticalMiles));

    /// <summary>The extent toward a bearing from the centre: that bearing's quadrant's.</summary>
    /// <param name="bearingDegrees">The bearing from the centre, in degrees from true north.</param>
    /// <returns>The extent, in nautical miles.</returns>
    public double ExtentToward(double bearingDegrees)
    {
        var b = ((bearingDegrees % 360) + 360) % 360;
        return b switch
        {
            < 90 => NortheastNauticalMiles,
            < 180 => SoutheastNauticalMiles,
            < 270 => SouthwestNauticalMiles,
            _ => NorthwestNauticalMiles,
        };
    }
}

/// <summary>Where a <see cref="RouteCycloneAt"/> comes from.</summary>
public enum RouteCycloneBasis
{
    /// <summary>One of the advisory's forecast points.</summary>
    Forecast,

    /// <summary>Estimated between two forecast points.</summary>
    Estimated,

    /// <summary>The observed best track, before the advisory.</summary>
    BestTrack,
}

/// <summary>A storm at one time: its centre and wind radii, as the cyclone layer draws it (design note V11).</summary>
/// <param name="Time">The time.</param>
/// <param name="Centre">The storm's centre.</param>
/// <param name="Basis">Where it comes from.</param>
/// <param name="Radii">The wind radii known at that time; empty when unknown.</param>
/// <param name="ClassLetter">The storm's class letter (such as <c>H</c>, <c>S</c>, <c>D</c>), or empty.</param>
public sealed record RouteCycloneAt(
    DateTimeOffset Time,
    GeoPosition Centre,
    RouteCycloneBasis Basis,
    IReadOnlyList<RouteCycloneRadii> Radii,
    string ClassLetter = "")
{
    /// <summary>The extents of a radius, or <c>null</c> when it isn't known at this time.</summary>
    /// <param name="radius">The radius.</param>
    /// <returns>The extents, or <c>null</c>.</returns>
    public RouteCycloneRadii? RadiiOf(TropicalCycloneRadius radius) => Radii.FirstOrDefault(r => r.Radius == radius);
}

/// <summary>
/// One tropical cyclone advisory, as the weather check reads it (#1299):
/// the storm at a time, and its forecast cone.
/// <c>EncDotNet.S100.Datasets.Pipelines</c> implements it over loaded NHC
/// advisories.
/// </summary>
/// <remarks>
/// <para>
/// Advisories aren't official S-100 data. Findings from them are advisory and
/// aren't for navigation.
/// </para>
/// <para>Implementations must be safe to call from several threads at once.</para>
/// </remarks>
public interface IRouteCyclone
{
    /// <summary>Which advisory it is.</summary>
    RouteCycloneInfo Info { get; }

    /// <summary>The advisory's synoptic time: its first forecast point. The cone applies from here.</summary>
    DateTimeOffset ForecastStart { get; }

    /// <summary>The time of the advisory's last forecast point. Later times aren't checked.</summary>
    DateTimeOffset ForecastEnd { get; }

    /// <summary>Every position on the best track and the forecast track, in time order: where the storm has been and is forecast to go.</summary>
    IReadOnlyList<GeoPosition> Track { get; }

    /// <summary>
    /// The storm at a time, or <c>null</c> before its track starts and after
    /// <see cref="ForecastEnd"/>.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The storm, or <c>null</c>.</returns>
    RouteCycloneAt? At(DateTimeOffset time);

    /// <summary>Whether a position is inside the advisory's forecast cone.</summary>
    /// <param name="position">The position, in WGS-84.</param>
    /// <returns><c>true</c> inside the cone; <c>false</c> outside it, or when the advisory has none.</returns>
    bool InCone(GeoPosition position);
}
