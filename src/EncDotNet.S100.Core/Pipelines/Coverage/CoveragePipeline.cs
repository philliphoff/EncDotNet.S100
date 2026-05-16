using System.Diagnostics;
using EncDotNet.S100.Core;
using EncDotNet.S100.Diagnostics;

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

        using var activity = Telemetry.ActivitySource.StartActivity("s100.pipeline.coverage.process");
        activity?.SetTag(TelemetryTags.PipelineStage, "portray");
        activity?.SetTag(TelemetryTags.Product, source.Metadata.Spec.Name);
        var start = Stopwatch.GetTimestamp();
        var productTag = new KeyValuePair<string, object?>(TelemetryTags.Product, source.Metadata.Spec.Name);
        var stageTag = new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, "coverage");

        int gc0Before = GC.CollectionCount(0);
        int gc1Before = GC.CollectionCount(1);
        int gc2Before = GC.CollectionCount(2);

        try
        {
            var settings = mariner ?? MarinerSettings.Default;
            var metadata = source.Metadata;

            // Stage 1 — resolve colour and symbol schemes from catalogue
            CoverageColorScheme colorScheme;
            CoverageSymbolScheme? symbolScheme;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.coverage.stage.resolve"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                colorScheme = catalogue.ResolveColorScheme(settings);
                symbolScheme = catalogue.ResolveSymbolScheme(settings);
                RecordCoverageStageDuration(stageStart, "resolve");
            }

            // Stage 2 — sample the grid
            SampledCoverage sampled;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.coverage.stage.read"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                sampled = source.Sample(GridRegion.Full);
                RecordCoverageStageDuration(stageStart, "read");
            }

            long cells = (long)metadata.GridMetadata.NumRows * metadata.GridMetadata.NumColumns;
            PipelineMetrics.CoverageCells.Record(cells, productTag);
            activity?.SetTag("s100.coverage.cells", cells);

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
        catch
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            activity?.SetTag(TelemetryTags.GcGen0Delta, GC.CollectionCount(0) - gc0Before);
            activity?.SetTag(TelemetryTags.GcGen1Delta, GC.CollectionCount(1) - gc1Before);
            activity?.SetTag(TelemetryTags.GcGen2Delta, GC.CollectionCount(2) - gc2Before);

            PipelineMetrics.Duration.Record(
                (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
                productTag, stageTag);
        }
    }

    private static void RecordCoverageStageDuration(long stageStart, string stageName)
    {
        PipelineMetrics.StageDuration.Record(
            (Stopwatch.GetTimestamp() - stageStart) * 1000.0 / Stopwatch.Frequency,
            new KeyValuePair<string, object?>(TelemetryTags.PipelineStage, stageName));
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
    /// <summary>The product specification (name + edition) this coverage declares conformance to.</summary>
    public required SpecRef Spec { get; init; }
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

/// <summary>
/// A read-only 2-D view over a row-major flat <c>float</c> grid. Allows
/// consumers to iterate sampled coverage data with familiar <c>[row,
/// col]</c> indexing without paying the LOH cost of allocating a
/// <c>float[,]</c> per sample (PR-F).
/// </summary>
public readonly struct CoverageGridView
{
    private readonly float[] _data;

    public CoverageGridView(float[] data, int rows, int cols)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (cols < 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (data.Length < rows * cols)
            throw new ArgumentException(
                $"Backing array length ({data.Length}) is smaller than rows*cols ({rows * cols}).",
                nameof(data));

        _data = data;
        Rows = rows;
        Cols = cols;
    }

    /// <summary>Number of grid rows.</summary>
    public int Rows { get; }

    /// <summary>Number of grid columns.</summary>
    public int Cols { get; }

    /// <summary>Flat row-major span over the underlying data.</summary>
    public ReadOnlySpan<float> Span => _data.AsSpan(0, Rows * Cols);

    /// <summary>Indexer matching the legacy <c>float[,]</c> shape.</summary>
    public float this[int row, int col] => _data[row * Cols + col];

    /// <summary>Length of the longest axis (mirrors <c>float[,].GetLength</c>).</summary>
    public int GetLength(int dimension) => dimension switch
    {
        0 => Rows,
        1 => Cols,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };
}

public class SampledCoverage
{
    public required GridRegion Region { get; init; }
    public required GridMetadata Metadata { get; init; }

    /// <summary>
    /// Per-field sampled values keyed by field name. Each value is a flat
    /// row-major <c>float[]</c> of length <c>Rows*Cols</c>. Flat storage
    /// avoids the LOH allocations a <c>float[,]</c> pair (depth +
    /// uncertainty on a 1000×1000 S-102 grid is ~8 MB) would incur per
    /// <see cref="ICoverageSource.Sample"/> call (PR-F).
    /// </summary>
    public required IReadOnlyDictionary<string, float[]> Values { get; init; }

    /// <summary>Returns a 2-D view over the named field's flat backing array.</summary>
    public CoverageGridView GetField(string fieldName)
    {
        var data = Values[fieldName];
        return new CoverageGridView(data, Metadata.NumRows, Metadata.NumColumns);
    }

    // Geolocate a grid cell within the sampled region
    public (double Longitude, double Latitude) GetPosition(int row, int col)
    {
        double lat = Metadata.OriginLatitude + row * Metadata.SpacingLatitudinal;
        double lon = Metadata.OriginLongitude + col * Metadata.SpacingLongitudinal;
        return (lon, lat);
    }
}
