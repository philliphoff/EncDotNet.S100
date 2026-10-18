using System.Globalization;
using EncDotNet.S100.DataModel;

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

    /// <summary>A leg, from its first waypoint number ({0}) to its last ({1}).</summary>
    public string LegFormat { get; init; } = "Leg {0} → {1}";

    /// <summary>A length in nautical miles ({0}).</summary>
    public string LengthFormat { get; init; } = "{0:0.00} NM";

    /// <summary>Joins the parts of a meta line.</summary>
    public string Separator { get; init; } = " · ";

    /// <summary>The culture numbers are formatted in, or <c>null</c> for the current culture.</summary>
    public CultureInfo? Culture { get; init; }
}
