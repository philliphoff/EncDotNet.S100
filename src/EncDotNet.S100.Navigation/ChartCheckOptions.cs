namespace EncDotNet.S100.Navigation;

/// <summary>Tuning for <see cref="ChartCheck"/>.</summary>
public sealed record ChartCheckOptions
{
    /// <summary>The default options.</summary>
    public static ChartCheckOptions Default { get; } = new();

    /// <summary>
    /// The spacing of the cross-sections the check takes along a leg, in
    /// metres. Crossings of the track itself are found exactly; a feature
    /// that only reaches into the corridor is found at the nearest
    /// cross-section, so it is located to within this spacing, and a
    /// feature narrower than it may fall between two. Default 10 m.
    /// </summary>
    public double StationSpacingMeters { get; init; } = 10;

    /// <summary>
    /// The most cross-sections taken along one leg. A longer leg spaces them
    /// further apart rather than exceeding this. Default 20 000.
    /// </summary>
    public int MaxStationsPerLeg { get; init; } = 20_000;

    /// <summary>
    /// How far beyond a side's cross-track limit a charted danger is still
    /// worth checking, in metres. A danger inside the limits must be
    /// reviewed; one in this band beyond them is worth checking. Default
    /// 185,2 m (0,1 NM).
    /// </summary>
    public double DangerNearbyMeters { get; init; } = 185.2;

    /// <summary>
    /// How far apart two shoal soundings or S-102 shoal cells along the
    /// route can be and still make one finding, in metres, so a long shallow
    /// bank is one finding rather than many. Default 100 m.
    /// </summary>
    public double ShoalMergeGapMeters { get; init; } = 100;
}

/// <summary>
/// The depths one leg's charted dangers and safety contour are checked
/// against: the safety contour and safety depth from
/// <see cref="RouteCheckSettings"/>, and the leg's keel
/// (<see cref="PassageLeg"/> draft + squat + required UKC) when it has a
/// draft.
/// </summary>
/// <remarks>
/// Dangers are judged against <see cref="SafetyContourMeters"/>, counting
/// one at the contour, as the S-101 portrayal's <c>UDWHAZ05</c> judges them.
/// With a draft they're also judged against
/// <see cref="KeelMeters"/> on the static charted depth, without the tide;
/// the finding's meta line names the threshold that fired, the keel one when
/// both do. Depth, dredged and drying areas are judged against
/// <see cref="SafetyContourMeters"/>, and soundings against
/// <see cref="SafetyDepthMeters"/>.
/// </remarks>
/// <param name="SafetyContourMeters">The safety contour, in metres.</param>
/// <param name="SafetyDepthMeters">The safety depth, in metres.</param>
/// <param name="KeelMeters">The leg's draft + squat + required UKC, or <c>null</c> without a draft.</param>
internal sealed record LegDepths(double SafetyContourMeters, double SafetyDepthMeters, double? KeelMeters);
