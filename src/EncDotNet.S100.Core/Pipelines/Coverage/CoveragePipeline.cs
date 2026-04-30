namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Assembles a <see cref="StyledCoverageLayer"/> from a coverage source and
/// portrayal catalogue: resolves the catalogue's colour and (optional) symbol
/// schemes against the supplied <see cref="MarinerSettings"/>, samples the
/// full grid, and bundles the result with the source's georeferencing.
/// Pixel-level colorization and any reprojection are deferred to the renderer,
/// since coverage grids are typically authored in a non-display CRS
/// (e.g. UTM for S-102) that requires per-pixel reprojection at render time.
/// </summary>
public class CoveragePipeline
{
    public Task<StyledCoverageLayer> ProcessAsync(
        ICoverageSource source,
        ICoveragePortrayalCatalogue catalogue,
        MarinerSettings? mariner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);

        var settings = mariner ?? MarinerSettings.Default;
        var metadata = source.Metadata;

        var colorScheme = catalogue.ResolveColorScheme(settings);
        var symbolScheme = catalogue.ResolveSymbolScheme(settings);
        var sampled = source.Sample(GridRegion.Full);

        var layer = new StyledCoverageLayer
        {
            Coverage = sampled,
            ColorScheme = colorScheme,
            NoDataValue = metadata.NoDataValue,
            Georeferencer = new GridGeoreferencer(
                metadata.GridMetadata,
                metadata.HorizontalCRS),
            SymbolScheme = symbolScheme,
        };

        return Task.FromResult(layer);
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
