using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.S129.Fusion;

/// <summary>
/// Pure-data helpers for sampling an
/// <see cref="S102CoverageSource"/> at a geographic position.
/// </summary>
/// <remarks>
/// Sampling is nearest-cell — no bilinear / bicubic interpolation. The
/// helpers never produce drawing instructions and never assume any
/// rendering library is present.
/// </remarks>
public static class S129BathymetryFusion
{
    /// <summary>
    /// Samples the bathymetric coverage at the supplied
    /// <paramref name="position"/>.
    /// </summary>
    /// <param name="coverage">The S-102 coverage source.</param>
    /// <param name="position">The position to sample (WGS-84 lat/lon).</param>
    /// <returns>
    /// The depth + uncertainty at the nearest cell, or <c>null</c> when
    /// <paramref name="position"/> falls outside the coverage extent
    /// (or the sampled cell carries the S-102 fill value).
    /// </returns>
    public static S129BathymetrySample? Sample(
        S102CoverageSource coverage,
        GeoPosition position)
    {
        ArgumentNullException.ThrowIfNull(coverage);

        if (!TryLocateCell(coverage.Metadata.GridMetadata, position, out int row, out int col))
            return null;

        var region = new GridRegion(row, row + 1, col, col + 1, 1, 1);
        var sampled = coverage.Sample(region);
        var depth = sampled.GetField("depth")[0, 0];
        var uncertainty = sampled.GetField("uncertainty")[0, 0];

        if (depth == S102CoverageSource.FillValue)
            return null;

        return new S129BathymetrySample(depth, uncertainty, row, col);
    }

    internal static bool TryLocateCell(
        GridMetadata grid,
        GeoPosition position,
        out int row,
        out int col)
    {
        double rowFractional = (position.Latitude - grid.OriginLatitude) / grid.SpacingLatitudinal;
        double colFractional = (position.Longitude - grid.OriginLongitude) / grid.SpacingLongitudinal;

        row = (int)Math.Round(rowFractional, MidpointRounding.AwayFromZero);
        col = (int)Math.Round(colFractional, MidpointRounding.AwayFromZero);

        if (row < 0 || row >= grid.NumRows || col < 0 || col >= grid.NumColumns)
            return false;

        return true;
    }
}
