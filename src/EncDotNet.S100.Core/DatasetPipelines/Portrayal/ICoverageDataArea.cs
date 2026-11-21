namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// Where a coverage has values, for S-98 rules that decide one dataset's
/// drawing by another's data: an S-102 cell's limit-of-data line is hidden
/// where a neighbouring S-102 cell has depths beyond it
/// (<c>S98DefaultRules.R_102_102_A_JoinS102Cells</c>).
/// </summary>
public interface ICoverageDataArea
{
    /// <summary>
    /// Whether the coverage has a value at a point: the grid node nearest to
    /// it has one. A point outside the grid has none.
    /// </summary>
    /// <param name="mercatorX">The point's EPSG:3857 x, in metres.</param>
    /// <param name="mercatorY">The point's EPSG:3857 y, in metres.</param>
    /// <returns><see langword="true"/> when the nearest grid node has a value.</returns>
    bool HasData(double mercatorX, double mercatorY);
}
