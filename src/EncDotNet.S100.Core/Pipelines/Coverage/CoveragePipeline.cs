namespace EncDotNet.S100.Pipelines.Coverage;

public class CoveragePipeline
{
    public Task<ICoverageLayer> ProcessAsync(
        ICoverageSource source,
        ICoveragePortrayalCatalogue catalogue,
        NavigationContext? context = null
    )
    {
        var metadata = source.Metadata;
        var colorScheme = catalogue.ResolveColorScheme(
            context ?? DefaultNavigationContext(metadata));

        // Sample the full grid (viewport clipping can narrow this later)
        var sampled = source.Sample(GridRegion.Full);

        // Colorize each cell using the catalogue's color scheme
        var fieldData = sampled.GetField(colorScheme.FieldName);
        int rows = fieldData.GetLength(0);
        int cols = fieldData.GetLength(1);
        var cellColors = new string?[rows * cols];

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float value = fieldData[r, c];
            bool isNoData = float.IsNaN(metadata.NoDataValue)
                ? float.IsNaN(value)
                : value == metadata.NoDataValue;
            cellColors[r * cols + c] = isNoData ? null : colorScheme.Resolve(value);
        }

        // Extract contour lines if the catalogue defines them
        var contours = ExtractContours(sampled, catalogue.Contours);

        ICoverageLayer layer = new DefaultCoverageLayer
        {
            Metadata = metadata,
            Extent = new BoundingBox(
                sampled.Metadata.OriginLatitude,
                sampled.Metadata.OriginLongitude,
                sampled.Metadata.OriginLatitude + sampled.Metadata.SpacingLatitudinal * sampled.Metadata.NumRows,
                sampled.Metadata.OriginLongitude + sampled.Metadata.SpacingLongitudinal * sampled.Metadata.NumColumns),
            Grid = sampled.Metadata,
            CellColors = cellColors,
            Contours = contours,
        };

        return Task.FromResult(layer);
    }

    private static NavigationContext DefaultNavigationContext(CoverageMetadata metadata) =>
        new NavigationContext
        {
            Viewport = new Viewport
            {
                MinLatitude = metadata.Extent.SouthLatitude,
                MaxLatitude = metadata.Extent.NorthLatitude,
                MinLongitude = metadata.Extent.WestLongitude,
                MaxLongitude = metadata.Extent.EastLongitude,
                WidthPixels = metadata.GridMetadata.NumColumns,
                HeightPixels = metadata.GridMetadata.NumRows,
            },
            ScaleDenominator = 50_000,
        };

    private static List<ContourLine> ExtractContours(
        SampledCoverage sampled,
        IReadOnlyList<ContourStyle> contourStyles)
    {
        // TODO: Implement marching-squares contour extraction.
        // For each ContourStyle, trace isolines through the grid at the
        // specified depth value and produce ContourLine geometries.
        return [];
    }
}

public interface ICoverageSource
{
// File-level metadata — available immediately after opening
    CoverageMetadata Metadata { get; }
    
    // Time dimension — null/empty for static products (S-102)
    // Populated for time-varying products (S-111, S-104)
    IReadOnlyList<DateTime> AvailableTimes { get; }
    void SelectTime(DateTime time);
    
    // The actual data access
    SampledCoverage Sample(GridRegion region);
}

public class CoverageMetadata
{
    public required string ProductSpec { get; init; }
    public required BoundingBox Extent { get; init; }
    public required GridMetadata GridMetadata { get; init; }
    public required string HorizontalCRS { get; init; }
    public required string VerticalDatum { get; init; }
    public required float NoDataValue { get; init; }
    
    // What value fields this coverage carries
    // S-102: ["depth", "uncertainty"]
    // S-111: ["surfaceCurrentSpeed", "surfaceCurrentDirection"]
    // S-104: ["waterLevelHeight", "waterLevelTrend"]
    public required IReadOnlyList<CoverageValueField> ValueFields { get; init; }
}

public class CoverageValueField
{
    public required string Name { get; init; }
    public required CoverageValueType Type { get; init; }
    public required string Units { get; init; }
    public required float FillValue { get; init; }
}

public class GridMetadata
{
    public required int NumRows { get; init; }
    public required int NumColumns { get; init; }
    public required double OriginLongitude { get; init; }
    public required double OriginLatitude { get; init; }
    public required double SpacingLongitudinal { get; init; }
    public required double SpacingLatitudinal { get; init; }
}

public class GridRegion
{
    public GridRegion(int? rowStart, int? rowEnd, int? colStart, int? colEnd, int rowStride, int colStride)
    {
        RowStart = rowStart;
        RowEnd = rowEnd;
        ColStart = colStart;
        ColEnd = colEnd;
        RowStride = rowStride;
        ColStride = colStride;
    }

    // Subset of the grid to sample
    // null means entire grid
    public int? RowStart { get; }
    public int? RowEnd { get; }
    public int? ColStart { get; }
    public int? ColEnd { get; }
    
    // Optional downsampling stride
    public int RowStride { get; }
    public int ColStride { get; }
    
    public static GridRegion Full => new GridRegion(null, null, null, null, 1, 1);

    /// <summary>
    /// Resolves nullable bounds against actual grid dimensions.
    /// Returns (rowStart, rowEnd, colStart, colEnd) with null replaced by grid extents.
    /// </summary>
    public (int RowStart, int RowEnd, int ColStart, int ColEnd) Resolve(int numRows, int numColumns) =>
        (RowStart ?? 0, RowEnd ?? numRows, ColStart ?? 0, ColEnd ?? numColumns);
    
    // Factory — compute region from viewport intersection with grid
    public static GridRegion FromViewport(Viewport viewport, GridMetadata grid)
    {
        // TODO: Intersect viewport with grid extent and compute row/col bounds.
        return Full;
    }
}

public class SampledCoverage
{
    public required GridRegion Region { get; init; }
    public required GridMetadata Metadata { get; init; }
    
    // Values keyed by field name
    // Each array is [row, col] for the sampled region
    public required IReadOnlyDictionary<string, float[,]> Values { get; init; }
    
    // Convenience accessors for common fields
    public float[,] GetField(string fieldName) => Values[fieldName];
    
    // Geolocate a grid cell within the sampled region
    public (double Longitude, double Latitude) GetPosition(int row, int col)
    {
        double lat = Metadata.OriginLatitude + row * Metadata.SpacingLatitudinal;
        double lon = Metadata.OriginLongitude + col * Metadata.SpacingLongitudinal;
        return (lon, lat);
    }
}

/// <summary>
/// A styled coverage grid ready for rendering as a raster tile.
/// Product-agnostic: carries coloured cells plus optional contour lines,
/// regardless of whether the source was S-102, S-111, S-104, etc.
/// </summary>
public interface ICoverageLayer
{
    /// <summary>Metadata describing the source coverage.</summary>
    CoverageMetadata Metadata { get; }

    /// <summary>Geographic extent of this layer.</summary>
    BoundingBox Extent { get; }

    /// <summary>Grid dimensions and spacing for the styled output.</summary>
    GridMetadata Grid { get; }

    /// <summary>
    /// The colour assigned to each cell, row-major order.
    /// Null entries represent no-data cells (transparent).
    /// </summary>
    IReadOnlyList<string?> CellColors { get; }

    /// <summary>
    /// Optional contour line geometries extracted from the coverage.
    /// Empty when a catalogue specifies no contours.
    /// </summary>
    IReadOnlyList<ContourLine> Contours { get; }
}

/// <summary>
/// A single contour line extracted from a coverage grid.
/// </summary>
public sealed class ContourLine
{
    /// <summary>The depth/value this contour represents.</summary>
    public required float Value { get; init; }

    /// <summary>Ordered vertices forming the contour polyline (lat/lon pairs).</summary>
    public required IReadOnlyList<(double Latitude, double Longitude)> Vertices { get; init; }

    /// <summary>The style to apply when rendering this contour.</summary>
    public required ContourStyle Style { get; init; }
}