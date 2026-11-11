using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S101;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A loaded chart cell (S-101, S-401, or S-57 translated to either) that a
/// point depth lookup ranks against overlapping cells and samples soundings
/// from, as the viewer's pick report does (issues #938, #1040).
/// </summary>
public interface IChartDepthCell
{
    /// <summary>
    /// The cell's scale rank against overlapping cells (see
    /// <see cref="CellScaleRanking"/>), or <see langword="null"/> when the
    /// cell declares no usable scale.
    /// </summary>
    CellRank? CellRank { get; }

    /// <summary>
    /// Whether the cell's <c>DataCoverage</c> contains a WGS-84 point. A cell
    /// that declares no coverage is treated as covering every point.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <returns><see langword="true"/> when the cell covers the point.</returns>
    bool CoversPoint(double latitude, double longitude);

    /// <summary>
    /// Samples the cell's nearest charted <c>Sounding</c> to a WGS-84 point,
    /// or <see langword="null"/> when the cell has no soundings.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <returns>The nearest sounding sample, or <see langword="null"/>.</returns>
    S101SoundingSample? SampleNearestSounding(double latitude, double longitude);

    /// <summary>
    /// The vertical datum the cell's depths are relative to at a WGS-84 point:
    /// the S-100 vertical datum code of the <c>SoundingDatum</c> area that
    /// contains it, or <see langword="null"/> when none does. The route check
    /// reads the same areas.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <returns>The datum code, or <see langword="null"/>.</returns>
    int? SoundingDatumAt(double latitude, double longitude) => null;

    /// <summary>
    /// The cell's vertical datum: the S-100 code its <c>SoundingDatum</c>
    /// areas declare (the most common one when they declare several), or
    /// <see langword="null"/> when the cell declares none.
    /// </summary>
    int? SoundingDatum => null;

    /// <summary>
    /// The cell's edition, the last update applied to it and its issue date,
    /// for the S-98 legend (Main §12.10.1 items 9 and 10); <see langword="null"/>
    /// when the cell declares no edition number.
    /// </summary>
    ChartCellEdition? Edition => null;

    /// <summary>
    /// The scale the cell was compiled for, as a denominator: the S-57 DSPM
    /// compilation scale (CSCL, Appendix B.1 §7.3.1.1), or for S-101 the
    /// finest <c>DataCoverage.maximumDisplayScale</c> (FC §3.1.1), the scale
    /// past which the display is overscaled. <see langword="null"/> when the
    /// cell declares neither.
    /// </summary>
    int? CompilationScale => null;
}

/// <summary>A chart cell's edition (S-57 DSID EDTN/UPDN/ISDT; S-101 DSID DSED and update number).</summary>
/// <param name="Number">The edition number.</param>
/// <param name="Update">The last update applied: 0 for a base cell with none.</param>
/// <param name="IssueDate">The issue (S-101 reference) date, or <see langword="null"/> when not declared.</param>
public sealed record ChartCellEdition(int Number, int Update, DateOnly? IssueDate);

/// <summary>Reads <see cref="ChartCellEdition"/> fields from DSID text.</summary>
internal static class ChartCellEditions
{
    /// <summary>A DSID number subfield, which S-57 and S-101 hold as text.</summary>
    public static int? Number(string? value) =>
        int.TryParse(value?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>A DSID date subfield (<c>YYYYMMDD</c>).</summary>
    public static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
}
