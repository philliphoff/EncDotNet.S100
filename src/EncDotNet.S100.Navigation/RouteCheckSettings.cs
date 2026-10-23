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
