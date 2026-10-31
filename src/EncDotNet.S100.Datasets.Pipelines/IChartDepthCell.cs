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
    /// <see cref="CellScaleRanking"/>). Smaller is finer;
    /// <see langword="null"/> when the cell declares no usable scale.
    /// </summary>
    int? CellRankingScale { get; }

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
}
