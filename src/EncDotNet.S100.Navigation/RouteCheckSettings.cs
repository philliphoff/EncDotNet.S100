using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// What the user decides for one check of a <see cref="PassagePlan"/>: the
/// mariner's depths, an optional corridor that replaces every leg's
/// cross-track limits, and the time to judge at where the plan gives none.
/// Engine tuning that stays the same between checks is in each check's
/// options instead.
/// </summary>
/// <remarks>
/// Time follows one rule in every check: the ETA where a leg has one; else
/// the plan's departure; else <see cref="At"/>.
/// </remarks>
public sealed class RouteCheckSettings
{
    /// <summary>
    /// The time to judge at where the plan has no departure, such as the
    /// view time or now. With a departure, legs without a speed are judged
    /// at the departure instead.
    /// </summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>
    /// The safety contour (S-101 PC parameter <c>SafetyContour</c>). Depth,
    /// dredged and drying areas and S-102 cells shallower than it are
    /// reported. With <see cref="SafetyDepth"/>, it enables the depth and
    /// danger checks; <c>null</c> skips them.
    /// </summary>
    public Depth? SafetyContour { get; init; }

    /// <summary>
    /// The safety depth (S-101 PC parameter <c>SafetyDepth</c>). Soundings
    /// and dangers shallower than it are reported. With
    /// <see cref="SafetyContour"/>, it enables the depth and danger checks;
    /// <c>null</c> skips them.
    /// </summary>
    public Depth? SafetyDepth { get; init; }

    /// <summary>
    /// A half-width that replaces every leg's port and starboard limits, or
    /// <c>null</c> to use each leg's own.
    /// </summary>
    public Length? CorridorOverride { get; init; }

    /// <summary>
    /// Departures to sweep for the clearance check, or <c>null</c> for none.
    /// The sweep is in <see cref="RouteCheckReport.Departures"/>.
    /// </summary>
    public DepartureRange? DepartureWindow { get; init; }

    /// <summary>Whether the depth and danger checks run: both the safety contour and the safety depth are set.</summary>
    public bool ChecksDepths => SafetyContour is not null && SafetyDepth is not null;

    /// <summary>The settings for the mariner's safety contour and safety depth.</summary>
    /// <param name="settings">The mariner's settings.</param>
    /// <param name="at">The time to judge at where the plan gives none.</param>
    /// <returns>The settings.</returns>
    public static RouteCheckSettings FromMarinerSettings(MarinerSettings settings, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new RouteCheckSettings
        {
            At = at,
            SafetyContour = settings.SafetyContour,
            SafetyDepth = settings.SafetyDepth,
        };
    }

    /// <summary>The port half-width of a leg, in metres: the override, the leg's limit, or <paramref name="defaultMeters"/>.</summary>
    internal double PortMeters(PassageLeg leg, double defaultMeters) =>
        Math.Max(0, (CorridorOverride ?? leg.PortLimit)?.TotalMetres ?? defaultMeters);

    /// <summary>The starboard half-width of a leg, in metres: the override, the leg's limit, or <paramref name="defaultMeters"/>.</summary>
    internal double StarboardMeters(PassageLeg leg, double defaultMeters) =>
        Math.Max(0, (CorridorOverride ?? leg.StarboardLimit)?.TotalMetres ?? defaultMeters);
}

/// <summary>
/// The departures a clearance check sweeps (<see cref="RouteCheckSettings.DepartureWindow"/>):
/// a fixed range, or the span of the water-level data that covers the route.
/// </summary>
public sealed record DepartureRange
{
    private DepartureRange(DateTimeOffset? from, DateTimeOffset? to, TimeSpan step, int maxDepartures)
    {
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be positive.");
        }

        if (maxDepartures < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepartures), maxDepartures, "At least one departure is needed.");
        }

        From = from;
        To = to;
        Step = step;
        MaxDepartures = maxDepartures;
    }

    /// <summary>The first departure, or <c>null</c> to follow the water-level data.</summary>
    public DateTimeOffset? From { get; }

    /// <summary>The last departure (inclusive), or <c>null</c> to follow the water-level data.</summary>
    public DateTimeOffset? To { get; }

    /// <summary>The time between departures.</summary>
    public TimeSpan Step { get; }

    /// <summary>
    /// The most departures to evaluate when following the water-level data;
    /// a longer span widens the step.
    /// </summary>
    public int MaxDepartures { get; }

    /// <summary>Whether the range follows the water-level data rather than fixed times.</summary>
    public bool FollowsWaterLevelData => From is null;

    /// <summary>Every <paramref name="step"/> from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">The first departure.</param>
    /// <param name="to">The last departure (inclusive).</param>
    /// <param name="step">The time between departures; must be positive.</param>
    /// <returns>The range.</returns>
    public static DepartureRange Between(DateTimeOffset from, DateTimeOffset to, TimeSpan step) => new(from, to, step, int.MaxValue);

    /// <summary>
    /// Every <paramref name="step"/> across the water-level data that covers
    /// the route: from the first whole step (counted from midnight UTC) at or
    /// after the data starts to the data's end. Departures whose arrival falls
    /// after the data ends get <see cref="PassageVerdict.WaterLevelEndsBeforeArrival"/>.
    /// When the range would need more than <paramref name="maxDepartures"/>
    /// departures, the step widens to the smallest multiple of
    /// <paramref name="step"/> that fits, so a long forecast stays cheap.
    /// </summary>
    /// <param name="step">The time between departures; must be positive.</param>
    /// <param name="maxDepartures">The most departures to evaluate; must be at least 1.</param>
    /// <returns>The range.</returns>
    public static DepartureRange WaterLevelData(TimeSpan step, int maxDepartures = 2000) => new(null, null, step, maxDepartures);
}
