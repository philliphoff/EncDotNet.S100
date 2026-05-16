using System;
using System.Collections.Generic;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Shared coverage-sampling helper used by the per-spec processors when
/// servicing <see cref="IDatasetProcessor.GetCoverageInfo"/>. Reprojects a
/// WGS84 click position into the coverage's native CRS, locates the
/// containing grid cell, and samples a single-cell <see cref="GridRegion"/>
/// from the supplied <see cref="ICoverageSource"/>.
/// </summary>
public static class CoveragePickHelper
{
    /// <summary>
    /// Result of a single-cell coverage sample.
    /// </summary>
    public sealed class SamplePoint
    {
        public required int Row { get; init; }
        public required int Col { get; init; }
        /// <summary>The native-CRS coordinates of the sampled cell origin.</summary>
        public required (double X, double Y) Native { get; init; }
        /// <summary>Per-field sampled values keyed by <see cref="CoverageValueField.Name"/>.</summary>
        public required IReadOnlyDictionary<string, float> Values { get; init; }
        /// <summary>The coverage's published NoData value (so callers can compare).</summary>
        public required float NoDataValue { get; init; }
    }

    /// <summary>
    /// Samples the supplied coverage at (<paramref name="latitude"/>,
    /// <paramref name="longitude"/>) in WGS84. Returns <c>null</c> when
    /// the click falls outside the grid extent.
    /// </summary>
    public static SamplePoint? Sample(
        ICoverageSource source,
        ICrsTransformFactory transformFactory,
        double latitude,
        double longitude)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transformFactory);

        var metadata = source.Metadata;
        var grid = metadata.GridMetadata;

        // Convert WGS84 → native. ProjNet's lon-first / lat-second convention
        // matches the inverse transform used by MapsuiCoverageRenderer
        // (native → WGS84 returns (lon, lat)).
        var transform = transformFactory.Create("EPSG:4326", metadata.HorizontalCRS);
        var (nativeX, nativeY) = transform.IsIdentity
            ? (longitude, latitude)
            : transform.Transform(longitude, latitude);

        var (row, col) = ToGrid(grid, nativeX, nativeY);
        if (row < 0 || row >= grid.NumRows || col < 0 || col >= grid.NumColumns)
            return null;

        var sampled = source.Sample(new GridRegion(row, row + 1, col, col + 1, 1, 1));
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (name, array) in sampled.Values)
            values[name] = array[0];

        return new SamplePoint
        {
            Row = row,
            Col = col,
            Native = (nativeX, nativeY),
            Values = values,
            NoDataValue = metadata.NoDataValue,
        };
    }

    /// <summary>
    /// Inverse grid-georeferencing using <see cref="Math.Floor(double)"/>
    /// so that negative row/col indices fall outside the grid (rather than
    /// truncating toward zero, which would map clicks just north of the
    /// origin to row 0). Spacing values in S-100 grids are positive — the
    /// origin is the south-west corner.
    /// </summary>
    private static (int Row, int Col) ToGrid(GridMetadata grid, double x, double y)
    {
        var fr = (y - grid.OriginLatitude) / grid.SpacingLatitudinal;
        var fc = (x - grid.OriginLongitude) / grid.SpacingLongitudinal;
        return ((int)Math.Floor(fr), (int)Math.Floor(fc));
    }
}
