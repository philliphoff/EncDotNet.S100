using System.Globalization;

namespace EncDotNet.S100.Navigation;

/// <summary>
/// Which kind of source supplied a charted depth, in the priority order the
/// <see cref="ChartedDepthResolver"/> applies.
/// </summary>
public enum ChartedDepthKind
{
    /// <summary>
    /// An S-102 bathymetric surface cell: continuous, surveyed, and carrying
    /// an uncertainty and a declared vertical datum (S-102 Ed 3.0.0 §5.3).
    /// </summary>
    Bathymetry,

    /// <summary>
    /// The minimum depth of an S-101 <c>DredgedArea</c>
    /// (<c>depthRangeMinimumValue</c>, DRVAL1): a maintained depth.
    /// </summary>
    DredgedArea,

    /// <summary>
    /// The minimum depth of an S-101 <c>DepthArea</c>
    /// (<c>depthRangeMinimumValue</c>, DRVAL1): the shoalest depth anywhere
    /// in the area.
    /// </summary>
    DepthArea,

    /// <summary>The nearest S-101 <c>Sounding</c>.</summary>
    Sounding,
}

/// <summary>A charted depth at a point and where it came from.</summary>
/// <param name="DepthMeters">Depth in metres below the vertical datum (positive down; negative for drying heights).</param>
/// <param name="Kind">The kind of source.</param>
/// <param name="UncertaintyMeters">The S-102 vertical uncertainty in metres, or <c>null</c>.</param>
/// <param name="VerticalDatum">
/// The vertical datum as an S-100 register code (source identifier 996), or
/// <c>null</c> when the source doesn't declare one.
/// </param>
/// <param name="DatasetId">Identifier of the dataset or cell that supplied the depth.</param>
/// <param name="Reference">
/// Where in the dataset the depth came from: the grid cell (<c>row,col</c>)
/// for S-102, or the feature record id for S-101.
/// </param>
/// <param name="SoundingDistanceMeters">Distance to the sounding, for <see cref="ChartedDepthKind.Sounding"/>.</param>
public sealed record ChartedDepth(
    double DepthMeters,
    ChartedDepthKind Kind,
    double? UncertaintyMeters = null,
    int? VerticalDatum = null,
    string? DatasetId = null,
    string? Reference = null,
    double? SoundingDistanceMeters = null)
{
    /// <summary>
    /// <c>true</c> when the depth comes from S-101 rather than an S-102
    /// surface. A vector depth is a band minimum or a single sounding, not a
    /// surveyed surface, so the check marks it low confidence.
    /// </summary>
    public bool IsLowConfidence => Kind != ChartedDepthKind.Bathymetry;
}

/// <summary>Supplies charted depth at a point.</summary>
public interface IChartedDepthSource
{
    /// <summary>
    /// The finest gridded-depth (S-102) cell size in metres among the
    /// surfaces that overlap the box, or <c>null</c> when none does. The
    /// engine samples at this spacing so it doesn't step over grid cells.
    /// </summary>
    /// <param name="south">Southern latitude of the box.</param>
    /// <param name="west">Western longitude of the box.</param>
    /// <param name="north">Northern latitude of the box.</param>
    /// <param name="east">Eastern longitude of the box.</param>
    /// <returns>The finest cell size in metres, or <c>null</c>.</returns>
    double? FinestGridSpacingMeters(double south, double west, double north, double east);

    /// <summary>The charted depth at a point, or <c>null</c> when no source covers it.</summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <returns>The depth, or <c>null</c>.</returns>
    ChartedDepth? Sample(double latitude, double longitude);
}

/// <summary>
/// The depth priority chain shared by the viewer's pick report and the
/// passage check: S-102 bathymetry, then S-101 <c>DredgedArea</c>, then
/// S-101 <c>DepthArea</c>, then the nearest S-101 <c>Sounding</c>.
/// </summary>
public static class ChartedDepthResolver
{
    /// <summary>
    /// Attribute codes that carry an S-101 area's minimum depth
    /// (<c>depthRangeMinimumValue</c>, and its S-57 acronym DRVAL1).
    /// </summary>
    public static IReadOnlyList<string> MinimumDepthAttributeCodes { get; } =
    [
        "depthRangeMinimumValue",
        "DRVAL1",
    ];

    /// <summary>Picks the highest-priority candidate that is present.</summary>
    /// <param name="bathymetry">The S-102 sample, or <c>null</c>.</param>
    /// <param name="dredgedArea">The shoalest covering S-101 dredged area, or <c>null</c>.</param>
    /// <param name="depthArea">The shoalest covering S-101 depth area, or <c>null</c>.</param>
    /// <param name="sounding">The nearest S-101 sounding, or <c>null</c>.</param>
    /// <returns>The chosen depth, or <c>null</c> when every candidate is absent.</returns>
    public static ChartedDepth? Resolve(
        ChartedDepth? bathymetry,
        ChartedDepth? dredgedArea,
        ChartedDepth? depthArea,
        ChartedDepth? sounding) =>
        bathymetry ?? dredgedArea ?? depthArea ?? sounding;

    /// <summary>The shoaler of two candidates (smaller depth); either may be <c>null</c>.</summary>
    /// <param name="a">A candidate.</param>
    /// <param name="b">Another candidate.</param>
    /// <returns>The shoaler candidate, or <c>null</c> when both are absent.</returns>
    public static ChartedDepth? Shoaler(ChartedDepth? a, ChartedDepth? b) =>
        a is null ? b : b is null ? a : b.DepthMeters < a.DepthMeters ? b : a;

    /// <summary>
    /// Reads a depth attribute value as metres. S-101 stores attribute values
    /// as text in the dataset's encoding; numbers parse with the invariant
    /// culture.
    /// </summary>
    /// <param name="value">The raw attribute value.</param>
    /// <param name="meters">The parsed value.</param>
    /// <returns><c>true</c> when the value is a finite number.</returns>
    public static bool TryReadMeters(object? value, out double meters)
    {
        switch (value)
        {
            case double d when double.IsFinite(d):
                meters = d;
                return true;
            case float f when float.IsFinite(f):
                meters = f;
                return true;
            case int i:
                meters = i;
                return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                               && double.IsFinite(parsed):
                meters = parsed;
                return true;
            default:
                meters = 0;
                return false;
        }
    }
}
