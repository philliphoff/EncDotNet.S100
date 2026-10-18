using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// One leg of a route to check against the chart: its points and its
/// cross-track limits, which bound the corridor the check sweeps.
/// </summary>
/// <param name="Points">
/// The leg's positions in order: at least two (its waypoints), or more when
/// the caller has already densified the leg.
/// </param>
/// <param name="PortCrossTrackMeters">
/// How far the corridor reaches to port of the track, in metres (S-421
/// <c>routeWaypointLegPortXTDL</c>; RTZ <c>portsideXTD</c>), or <c>null</c>
/// for <see cref="RouteCheckOptions.DefaultHalfWidthMeters"/>. This is one
/// side only, not the corridor's full width.
/// </param>
/// <param name="StarboardCrossTrackMeters">
/// How far the corridor reaches to starboard of the track, in metres, or
/// <c>null</c> for <see cref="RouteCheckOptions.DefaultHalfWidthMeters"/>.
/// </param>
/// <param name="Geometry">Whether the leg is a rhumb line or a great circle.</param>
public sealed record RouteCheckLeg(
    IReadOnlyList<GeoPosition> Points,
    double? PortCrossTrackMeters = null,
    double? StarboardCrossTrackMeters = null,
    LegGeometry Geometry = LegGeometry.Rhumb);

/// <summary>Tuning for <see cref="RouteCheckEngine"/>.</summary>
public sealed record RouteCheckOptions
{
    /// <summary>The default options.</summary>
    public static RouteCheckOptions Default { get; } = new();

    /// <summary>
    /// The corridor's half-width on a side whose cross-track limit isn't
    /// set, in metres: how far it reaches to port, or to starboard, of the
    /// track. The full corridor is twice this when neither side is set.
    /// Default 50 m.
    /// </summary>
    public double DefaultHalfWidthMeters { get; init; } = 50;

    /// <summary>
    /// The spacing of the cross-sections the engine takes along a leg, in
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
    /// The depths that charted dangers and the safety contour are checked
    /// against (#955), or <c>null</c> (the default) to skip those checks.
    /// Hosts fill it from the mariner's settings
    /// (<see cref="RouteCheckDepths.FromMarinerSettings"/>).
    /// </summary>
    public RouteCheckDepths? Depths { get; init; }

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

    /// <summary>
    /// The wording of the findings. Hosts with their own resources (the
    /// viewer's <c>Strings.resx</c>) pass their own.
    /// </summary>
    public RouteCheckText Text { get; init; } = RouteCheckText.Default;
}

/// <summary>
/// The wording of the route check's findings: their titles, and the parts
/// of their meta line.
/// </summary>
public sealed record RouteCheckText
{
    /// <summary>The English wording, formatted with the current culture.</summary>
    public static RouteCheckText Default { get; } = new();

    /// <summary>The title of <see cref="RouteCheckCodes.CrossesLand"/>.</summary>
    public string CrossesLand { get; init; } = "Crosses land";

    /// <summary>The title of <see cref="RouteCheckCodes.LandInCorridor"/>.</summary>
    public string LandInCorridor { get; init; } = "Land inside the cross-track limits";

    /// <summary>The title of <see cref="RouteCheckCodes.CrossesDrying"/>.</summary>
    public string CrossesDrying { get; init; } = "Crosses drying ground";

    /// <summary>The title of <see cref="RouteCheckCodes.DryingInCorridor"/>.</summary>
    public string DryingInCorridor { get; init; } = "Drying ground inside the cross-track limits";

    /// <summary>The title of <see cref="RouteCheckCodes.OutsideChartCoverage"/>.</summary>
    public string OutsideChartCoverage { get; init; } = "Leaves chart coverage";

    /// <summary>The title of <see cref="RouteCheckCodes.DangerInCorridor"/>: the danger's kind ({0}, such as <see cref="Wreck"/>).</summary>
    public string DangerInCorridorFormat { get; init; } = "{0} in the corridor";

    /// <summary>The title of <see cref="RouteCheckCodes.DangerNearRoute"/>: the danger's kind ({0}).</summary>
    public string DangerNearRouteFormat { get; init; } = "{0} near the route";

    /// <summary>The kind of a <c>Wreck</c>, in a danger's title.</summary>
    public string Wreck { get; init; } = "Wreck";

    /// <summary>The kind of an <c>Obstruction</c>, in a danger's title.</summary>
    public string Obstruction { get; init; } = "Obstruction";

    /// <summary>The kind of an <c>UnderwaterAwashRock</c>, in a danger's title.</summary>
    public string Rock { get; init; } = "Rock";

    /// <summary>The title of <see cref="RouteCheckCodes.CrossesSafetyContour"/>.</summary>
    public string CrossesSafetyContour { get; init; } = "Crosses the safety contour";

    /// <summary>The title of <see cref="RouteCheckCodes.SafetyContourInCorridor"/>.</summary>
    public string SafetyContourInCorridor { get; init; } = "Shallower than the safety contour inside the cross-track limits";

    /// <summary>The title of <see cref="RouteCheckCodes.SoundingsBelowSafetyDepth"/>.</summary>
    public string SoundingsBelowSafetyDepth { get; init; } = "Soundings shallower than the safety depth";

    /// <summary>The title of <see cref="RouteCheckCodes.CrossesS102Shoal"/>.</summary>
    public string CrossesS102Shoal { get; init; } = "Crosses S-102 depths shallower than the safety contour";

    /// <summary>The title of <see cref="RouteCheckCodes.S102ShoalInCorridor"/>.</summary>
    public string S102ShoalInCorridor { get; init; } = "S-102 depths shallower than the safety contour inside the cross-track limits";

    /// <summary>How far a danger is to port of the track, in metres ({0}).</summary>
    public string ToPortFormat { get; init; } = "{0:0} m to port";

    /// <summary>How far a danger is to starboard of the track, in metres ({0}).</summary>
    public string ToStarboardFormat { get; init; } = "{0:0} m to starboard";

    /// <summary>A danger the track passes over.</summary>
    public string OnTrack { get; init; } = "on the track";

    /// <summary>A charted depth, in metres ({0}).</summary>
    public string DepthFormat { get; init; } = "{0:0.0} m";

    /// <summary>The least of several charted depths, in metres ({0}).</summary>
    public string LeastDepthFormat { get; init; } = "least {0:0.0} m";

    /// <summary>A sounding count ({0}).</summary>
    public string SoundingCountFormat { get; init; } = "{0} soundings";

    /// <summary>An S-102 depth ({0}) with its uncertainty ({1}), in metres, and its grid cell ({2}, as <c>row,col</c>) in a dataset ({3}).</summary>
    public string S102CellWithUncertaintyFormat { get; init; } = "least {0:0.0} m ± {1:0.0} m at cell {2} of {3}";

    /// <summary>An S-102 depth ({0}) in metres without an uncertainty, and its grid cell ({2}) in a dataset ({3}).</summary>
    public string S102CellFormat { get; init; } = "least {0:0.0} m at cell {2} of {3}";

    /// <summary>A danger of unknown depth.</summary>
    public string DepthUnknown { get; init; } = "depth unknown";

    /// <summary>A danger that is awash (<c>waterLevelEffect</c> 5).</summary>
    public string Awash { get; init; } = "awash";

    /// <summary>A danger that covers and uncovers (<c>waterLevelEffect</c> 4).</summary>
    public string CoversAndUncovers { get; init; } = "covers and uncovers";

    /// <summary>A danger that shows above water (<c>waterLevelEffect</c> 1 or 2).</summary>
    public string AboveWater { get; init; } = "above water";

    /// <summary>The safety-depth threshold fired, in metres ({0}).</summary>
    public string BelowSafetyDepthFormat { get; init; } = "shallower than safety depth {0:0.#} m";

    /// <summary>The draft + squat + required UKC threshold fired, in metres ({0}).</summary>
    public string BelowKeelFormat { get; init; } = "shallower than draft + squat + UKC ({0:0.#} m)";

    /// <summary>The safety-contour threshold fired, in metres ({0}).</summary>
    public string BelowSafetyContourFormat { get; init; } = "shallower than safety contour {0:0.#} m";

    /// <summary>A leg, from its first waypoint number ({0}) to its last ({1}).</summary>
    public string LegFormat { get; init; } = "Leg {0} → {1}";

    /// <summary>A length in nautical miles ({0}).</summary>
    public string LengthFormat { get; init; } = "{0:0.00} NM";

    /// <summary>Joins the parts of a meta line.</summary>
    public string Separator { get; init; } = " · ";

    /// <summary>The culture numbers are formatted in, or <c>null</c> for the current culture.</summary>
    public CultureInfo? Culture { get; init; }
}

/// <summary>
/// The depths a route's charted dangers and safety contour are checked
/// against (#955): the mariner's safety contour and safety depth, and, when
/// the route has a draft, the depth the keel needs.
/// </summary>
/// <remarks>
/// <para>
/// Dangers are judged against <see cref="SafetyDepthMeters"/>, as an ECDIS
/// judges them. With a draft they are also judged against
/// <see cref="KeelMeters"/> (draft + squat + required UKC) on the static
/// charted depth, without the tide; the finding's meta line names the
/// threshold that fired, the keel one when both do.
/// </para>
/// <para>
/// Depth, dredged and drying areas are judged against
/// <see cref="SafetyContourMeters"/>, and soundings against
/// <see cref="SafetyDepthMeters"/>. Per-leg S-421 safety contour and depth
/// aren't used.
/// </para>
/// </remarks>
/// <param name="SafetyContourMeters">The safety contour (S-101 PC parameter <c>SafetyContour</c>), in metres.</param>
/// <param name="SafetyDepthMeters">The safety depth (S-101 PC parameter <c>SafetyDepth</c>), in metres.</param>
public sealed record RouteCheckDepths(double SafetyContourMeters, double SafetyDepthMeters)
{
    /// <summary>The route's draft in metres, or <c>null</c> when it has none.</summary>
    public double? DraftMeters { get; init; }

    /// <summary>The squat allowance in metres, used only with a draft.</summary>
    public double SquatMeters { get; init; }

    /// <summary>The required under-keel clearance in metres, used only with a draft.</summary>
    public double RequiredUkcMeters { get; init; }

    /// <summary>Draft + squat + required UKC, or <c>null</c> without a draft.</summary>
    public double? KeelMeters => DraftMeters is { } draft ? draft + SquatMeters + RequiredUkcMeters : null;

    /// <summary>The safety contour and safety depth from the mariner's settings, without a draft.</summary>
    /// <param name="settings">The mariner's settings.</param>
    /// <returns>The depths.</returns>
    public static RouteCheckDepths FromMarinerSettings(MarinerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new RouteCheckDepths(settings.SafetyContour.TotalMetres, settings.SafetyDepth.TotalMetres);
    }
}
