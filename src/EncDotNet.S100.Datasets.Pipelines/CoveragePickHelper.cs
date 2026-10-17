using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
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
/// <remarks>
/// The cell is found by the same lookup as the passage checks
/// (<see cref="PassageGrid"/>), so S-102, S-104 and S-111 grids with swapped
/// axis order, a geographic CRS other than WGS-84, or longitudes in 0…360°
/// are read as if written without the quirk (#943).
/// </remarks>
public static class CoveragePickHelper
{
    /// <summary>
    /// Result of a single-cell coverage sample.
    /// </summary>
    public sealed class SamplePoint
    {
        /// <summary>Zero-based grid row of the sampled cell (row 0 is the southernmost; rows run along latitude even when the grid's axis order is swapped).</summary>
        public required int Row { get; init; }
        /// <summary>Zero-based grid column of the sampled cell (column 0 is the westernmost).</summary>
        public required int Col { get; init; }
        /// <summary>
        /// The click in the grid's own axes: easting and northing for a
        /// projected grid, longitude (in the grid's longitude frame) and
        /// latitude for a geographic one.
        /// </summary>
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
        var encoded = metadata.GridMetadata;
        var grid = Grid(source, transformFactory);
        if (grid.FindCell(latitude, longitude) is not (var row, var col))
            return null;

        // The values keep the encoded layout: a swapped grid's georeferencing
        // is read back to latitude rows, but its array is still rows of
        // latitude written under the encoded (swapped) counts. Read the cell
        // by its flat index.
        var index = (row * grid.Columns) + col;
        var encodedRow = index / encoded.NumColumns;
        var encodedCol = index % encoded.NumColumns;
        var sampled = source.Sample(new GridRegion(encodedRow, encodedRow + 1, encodedCol, encodedCol + 1, 1, 1));
        var values = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (name, array) in sampled.Values)
            values[name] = array[0];

        return new SamplePoint
        {
            Row = row,
            Col = col,
            Native = grid.ToGrid(latitude, longitude),
            Values = values,
            NoDataValue = metadata.NoDataValue,
        };
    }

    // The S-102, S-104 and S-111 sources carry the declared EPSG code and the
    // reader's axis-order assessment; any other source is taken as encoded.
    private static PassageGrid Grid(ICoverageSource source, ICrsTransformFactory transforms)
    {
        var g = source.Metadata.GridMetadata;
        var axisOrder = source switch
        {
            S102CoverageSource s102 => s102.Dataset.AxisOrder,
            S104CoverageSource s104 => s104.Dataset.AxisOrder,
            S111CoverageSource s111 => s111.Dataset.AxisOrder,
            _ => null,
        };
        return PassageGrid.CreateForCrsName(
            g.OriginLatitude, g.OriginLongitude,
            g.SpacingLatitudinal, g.SpacingLongitudinal,
            g.NumRows, g.NumColumns,
            source.Metadata.HorizontalCRS,
            axisOrder,
            transforms);
    }
}
