using System.Diagnostics;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines.Coverage.Pyramid;

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
    /// <summary>
    /// Assembles a <see cref="StyledCoverageLayer"/> from a coverage source.
    /// </summary>
    /// <param name="source">The coverage source to sample.</param>
    /// <param name="catalogue">The coverage portrayal catalogue.</param>
    /// <param name="viewport">Optional map viewport in WGS-84. When supplied the
    /// pipeline calls <see cref="GridRegion.FromViewport"/> to sample only the
    /// cells that fall inside the viewport at the viewport's ground resolution
    /// (issue #487). When <see langword="null"/> the full grid is sampled
    /// (unchanged behaviour, preserves headless / CLI callers).</param>
    /// <param name="wgs84ToNative">Optional CRS transform from WGS-84 to the
    /// grid's native CRS. Required when <paramref name="viewport"/> is supplied
    /// and the grid CRS is not EPSG:4326 (e.g. an S-102 grid authored in UTM).</param>
    /// <param name="mariner">Optional mariner settings for palette / symbol
    /// resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<StyledCoverageLayer> ProcessAsync(
        ICoverageSource source,
        ICoveragePortrayalCatalogue catalogue,
        Viewport? viewport = null,
        ICrsTransform? wgs84ToNative = null,
        MarinerSettings? mariner = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        cancellationToken.ThrowIfCancellationRequested();

        using var activity = Telemetry.ActivitySource.StartActivity("s100.pipeline.coverage.process");
        activity?.SetTag(TelemetryTags.PipelineStage, "portray");
        activity?.SetTag(TelemetryTags.Product, source.Metadata.Spec.Name);
        if (viewport is not null)
        {
            activity?.SetTag(TelemetryTags.ViewportScale, viewport.ScaleDenominator);
        }
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

            // Pre-warm the catalogue so the synchronous Resolve*Scheme
            // calls below can read entirely from cached state. We only
            // trigger a switch when the catalogue is still at its empty
            // Default palette (catalogue uninitialised); when the dataset
            // processor has already invoked SwitchPaletteAsync the active
            // palette is already populated and we leave it untouched.
            if (catalogue.ActivePalette.Colors.Count == 0)
            {
                await catalogue.SwitchPaletteAsync(PaletteType.Day, cancellationToken)
                    .ConfigureAwait(false);
            }

            // Stage 1 — resolve colour and symbol schemes from catalogue
            CoverageColorScheme? colorScheme;
            CoverageSymbolScheme? symbolScheme;
            using (Telemetry.ActivitySource.StartActivity("s100.pipeline.coverage.stage.resolve"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                colorScheme = catalogue.ResolveColorScheme(settings);
                symbolScheme = catalogue.ResolveSymbolScheme(settings);
                RecordCoverageStageDuration(stageStart, "resolve");
            }

            // Compute the region to sample. Viewport-driven subset + stride
            // (issue #487) cuts per-cell projection cost in downstream renderers
            // when the viewer is zoomed out or panned partially off the grid.
            GridRegion region = viewport is null
                ? GridRegion.Full
                : GridRegion.FromViewport(viewport, metadata.GridMetadata, metadata.HorizontalCRS, wgs84ToNative);

            // Stage 2 — sample the grid
            SampledCoverage sampled;
            using (var readActivity = Telemetry.ActivitySource.StartActivity("s100.pipeline.coverage.stage.read"))
            {
                var stageStart = Stopwatch.GetTimestamp();
                readActivity?.SetTag(TelemetryTags.CoverageReducer, "nearest");
                sampled = source.Sample(region, cancellationToken);
                RecordCoverageStageDuration(stageStart, "read");

                readActivity?.SetTag("s100.coverage.stride.row", region.RowStride);
                readActivity?.SetTag("s100.coverage.stride.col", region.ColStride);
                readActivity?.SetTag("s100.coverage.subset", viewport is not null);

                // Overview level telemetry (issue #486). Tag the read span
                // and record a per-product histogram sample so dashboards can
                // show the distribution of levels served across a session.
                // A source without a pyramid always reports level 0.
                var overviewLevel = source.SelectedOverviewLevel;
                readActivity?.SetTag(TelemetryTags.CoverageOverviewLevel, overviewLevel);
                PipelineMetrics.CoverageOverviewLevelSelected.Record(overviewLevel, productTag);
            }

            long gridCells = (long)metadata.GridMetadata.NumRows * metadata.GridMetadata.NumColumns;
            long sampledCells = (long)sampled.Metadata.NumRows * sampled.Metadata.NumColumns;
            PipelineMetrics.CoverageCells.Record(sampledCells, productTag);
            // Preserve the historical "s100.coverage.cells" tag value so
            // existing dashboards continue to work; also emit the new
            // grid / sampled split for #487 telemetry.
            activity?.SetTag("s100.coverage.cells", sampledCells);
            activity?.SetTag("s100.coverage.cells.grid", gridCells);
            activity?.SetTag("s100.coverage.cells.sampled", sampledCells);
            activity?.SetTag("s100.coverage.stride.row", region.RowStride);
            activity?.SetTag("s100.coverage.stride.col", region.ColStride);
            activity?.SetTag("s100.coverage.subset", viewport is not null);

            var layer = new StyledCoverageLayer
            {
                Coverage = sampled,
                ColorScheme = colorScheme,
                NoDataValue = metadata.NoDataValue,
                // Build the georeferencer from the *sampled* metadata so a
                // subset+stride region (issue #487) is drawn in its true
                // geographic location. sampled.Metadata carries the
                // subset-adjusted origin (Origin + start*Spacing) and
                // stride-scaled spacing (Spacing * stride); the source's
                // GridMetadata still describes the full grid.
                Georeferencer = new GridGeoreferencer(
                    sampled.Metadata,
                    metadata.HorizontalCRS),
                SymbolScheme = symbolScheme,
            };

            return layer;
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
    /// <summary>
    /// Copies the requested grid region into a <see cref="SampledCoverage"/>.
    /// The underlying grid is already resident in memory (the HDF5 read
    /// happens at construction time), so this is a CPU-bound copy rather than
    /// an I/O operation; <paramref name="cancellationToken"/> lets a large
    /// copy be abandoned cooperatively. The method remains synchronous.
    /// </summary>
    /// <param name="region">The grid subset and stride to sample.</param>
    /// <param name="cancellationToken">Signals that the render has been cancelled.</param>
    SampledCoverage Sample(GridRegion region, CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------
    // Overview pyramid (S-100 Part 10c mipmaps; issue #486)
    //
    // The additive members below are default-implemented so every
    // existing ICoverageSource keeps its current behaviour (a single
    // Level 0 "base" level and a no-op selector). Sources that expose
    // a real pyramid override them and *also* make their Metadata and
    // Sample honour the currently-selected level so callers do not
    // need to change code paths — including the sibling viewport-
    // scoping work in issue #487 which computes GridRegion.FromViewport
    // against Metadata.GridMetadata.
    // -----------------------------------------------------------------

    /// <summary>
    /// The overview levels this source can serve, in increasing order.
    /// Level 0 is always the native (base) grid; higher levels are
    /// downsampled (level <c>N</c> = 2^N × coarser per axis).
    /// Sources without an overview pyramid return a single-element
    /// list describing the base grid.
    /// </summary>
    IReadOnlyList<CoverageOverviewLevel> AvailableOverviewLevels =>
        [
            new CoverageOverviewLevel(
                0,
                Metadata.GridMetadata.NumRows,
                Metadata.GridMetadata.NumColumns,
                Metadata.GridMetadata.SpacingLatitudinal,
                Metadata.GridMetadata.SpacingLongitudinal),
        ];

    /// <summary>
    /// The level index currently selected by <see cref="SelectOverviewLevel"/>.
    /// Defaults to <c>0</c> (base grid).
    /// </summary>
    int SelectedOverviewLevel => 0;

    /// <summary>
    /// Selects the overview level to serve on subsequent
    /// <see cref="Metadata"/> reads and <see cref="Sample"/> calls.
    /// After the call, <see cref="Metadata"/>'s
    /// <see cref="CoverageMetadata.GridMetadata"/> reflects the
    /// level's rows/cols/spacing so viewport-derived
    /// <see cref="GridRegion"/>s pick up the correct geometry
    /// automatically. Sources without a pyramid must accept
    /// <paramref name="level"/> == 0 as a no-op; other values may
    /// throw or be silently clamped (see the source's documentation).
    /// </summary>
    /// <param name="level">
    /// Zero-based level index, expected to be in
    /// <c>[0, AvailableOverviewLevels.Count - 1]</c>.
    /// </param>
    void SelectOverviewLevel(int level)
    {
        if (level != 0)
            throw new ArgumentOutOfRangeException(nameof(level), level,
                "This coverage source has no overview pyramid; only level 0 is valid.");
    }
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

    /// <summary>
    /// Computes the <see cref="GridRegion"/> that samples only the cells the
    /// supplied <paramref name="viewport"/> can display, at a stride matched
    /// to the viewport's ground resolution.
    /// </summary>
    /// <param name="viewport">
    /// The current display area (EPSG:4326 bounding box + pixel dimensions).
    /// </param>
    /// <param name="grid">
    /// The coverage's <see cref="GridMetadata"/>. Its <c>Origin*</c> and
    /// <c>Spacing*</c> fields are interpreted in the grid's native CRS
    /// (degrees for EPSG:4326, metres for projected CRSs such as UTM).
    /// </param>
    /// <param name="gridCrs">
    /// The grid's native CRS, e.g. <c>"EPSG:4326"</c> or <c>"EPSG:32608"</c>.
    /// Defaults to <c>"EPSG:4326"</c>.
    /// </param>
    /// <param name="wgs84ToNative">
    /// Optional transform from EPSG:4326 (viewport CRS) to the grid's native
    /// CRS. Required when <paramref name="gridCrs"/> is not
    /// <c>"EPSG:4326"</c>. Ignored when the grid CRS is EPSG:4326 or the
    /// supplied transform reports <see cref="ICrsTransform.IsIdentity"/>.
    /// Obtain via
    /// <c>factory.Create("EPSG:4326", gridCrs)</c>.
    /// </param>
    /// <returns>
    /// A concrete <see cref="GridRegion"/> (no null bounds) with:
    /// <list type="bullet">
    /// <item><description>
    /// Row/col bounds clamped to the grid dimensions. When the viewport
    /// does not intersect the grid at all the region is empty
    /// (<c>RowStart == RowEnd</c> or <c>ColStart == ColEnd</c>), which the
    /// grid sources handle as a zero-cell sample without allocation.
    /// </description></item>
    /// <item><description>
    /// Stride derived from the ratio of viewport ground resolution to grid
    /// cell size, floored to an integer and clamped to ≥ 1. When the
    /// viewport resolves each cell to less than one screen pixel the
    /// stride collapses to 1 (do not oversample below the display).
    /// </description></item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="gridCrs"/> is not EPSG:4326 and no
    /// <paramref name="wgs84ToNative"/> transform is supplied.
    /// </exception>
    /// <remarks>
    /// This is the render-time entry point for viewport-scoped coverage
    /// sampling (issue #487). Assumes the viewport does not cross the
    /// antimeridian.
    /// </remarks>
    public static GridRegion FromViewport(
        Viewport viewport,
        GridMetadata grid,
        string gridCrs = "EPSG:4326",
        ICrsTransform? wgs84ToNative = null)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(gridCrs);

        bool gridIsWgs84 = IsWgs84(gridCrs);
        bool haveTransform = wgs84ToNative is not null && !wgs84ToNative.IsIdentity;
        if (!gridIsWgs84 && !haveTransform)
        {
            throw new ArgumentException(
                $"A wgs84ToNative transform is required when the grid CRS ({gridCrs}) is not EPSG:4326.",
                nameof(wgs84ToNative));
        }

        // 1. Project the viewport corners into the grid's native CRS. For
        //    an EPSG:4326 grid the transform is a no-op. For a projected
        //    grid (e.g. UTM) a rotated axis-aligned WGS-84 rectangle maps
        //    to a slightly rotated quadrilateral in native coords; take
        //    its axis-aligned bounding box in native coords (a superset of
        //    the true footprint, which is exactly what we want for
        //    subset-and-clamp — we never *undershoot* the visible area).
        var (viewportNativeXMin, viewportNativeXMax, viewportNativeYMin, viewportNativeYMax) =
            ProjectViewportCorners(viewport, gridIsWgs84, wgs84ToNative);

        // 2. Compute the node-centred grid's outer footprint in native
        //    coordinates. Cell 0 is at Origin* and extends half a spacing
        //    beyond it; the final cell does the same at the opposite edge.
        //    Either axis's spacing may be negative (image-style top-down
        //    grids), so use min/max rather than assuming a direction.
        double gridXStart = grid.OriginLongitude;
        double gridXEnd = grid.OriginLongitude + (grid.NumColumns - 1) * grid.SpacingLongitudinal;
        double gridYStart = grid.OriginLatitude;
        double gridYEnd = grid.OriginLatitude + (grid.NumRows - 1) * grid.SpacingLatitudinal;
        double gridXHalfCell = Math.Abs(grid.SpacingLongitudinal) / 2;
        double gridYHalfCell = Math.Abs(grid.SpacingLatitudinal) / 2;
        double gridXMin = Math.Min(gridXStart, gridXEnd) - gridXHalfCell;
        double gridXMax = Math.Max(gridXStart, gridXEnd) + gridXHalfCell;
        double gridYMin = Math.Min(gridYStart, gridYEnd) - gridYHalfCell;
        double gridYMax = Math.Max(gridYStart, gridYEnd) + gridYHalfCell;

        // 3. Intersect.
        double interXMin = Math.Max(viewportNativeXMin, gridXMin);
        double interXMax = Math.Min(viewportNativeXMax, gridXMax);
        double interYMin = Math.Max(viewportNativeYMin, gridYMin);
        double interYMax = Math.Min(viewportNativeYMax, gridYMax);

        // 4. Empty intersection → return a zero-cell region. Sources
        //    already handle rows==0 and cols==0 correctly.
        if (interXMin >= interXMax || interYMin >= interYMax)
        {
            return new GridRegion(0, 0, 0, 0, 1, 1);
        }

        // 5. Map native intersection → row/col indices. Grid cells are
        //    node-centred with a half-cell pad in each direction (per
        //    S-102 §... — node position is the cell centre, not the
        //    corner). A cell "overlaps" the intersection when its node
        //    lies within (intersection ± half-cell). This gives:
        //      start = clamp(floor(fracMin + 0.5), 0, N)
        //      end   = clamp(floor(fracMax + 0.5) + 1, 0, N)
        //    The +0.5 shift also absorbs the floating-point rounding
        //    that flips 449.9999… back to index 450 for a viewport
        //    whose latitude divides exactly into cell size. Handle
        //    either sign of Spacing* by swapping the min/max fractions.
        double colFracMin = (interXMin - grid.OriginLongitude) / grid.SpacingLongitudinal;
        double colFracMax = (interXMax - grid.OriginLongitude) / grid.SpacingLongitudinal;
        if (grid.SpacingLongitudinal < 0)
            (colFracMin, colFracMax) = (colFracMax, colFracMin);
        int colStart = Math.Clamp((int)Math.Floor(colFracMin + 0.5), 0, grid.NumColumns);
        int colEnd = Math.Clamp((int)Math.Floor(colFracMax + 0.5) + 1, 0, grid.NumColumns);

        double rowFracMin = (interYMin - grid.OriginLatitude) / grid.SpacingLatitudinal;
        double rowFracMax = (interYMax - grid.OriginLatitude) / grid.SpacingLatitudinal;
        if (grid.SpacingLatitudinal < 0)
            (rowFracMin, rowFracMax) = (rowFracMax, rowFracMin);
        int rowStart = Math.Clamp((int)Math.Floor(rowFracMin + 0.5), 0, grid.NumRows);
        int rowEnd = Math.Clamp((int)Math.Floor(rowFracMax + 0.5) + 1, 0, grid.NumRows);

        if (rowStart >= rowEnd || colStart >= colEnd)
        {
            return new GridRegion(0, 0, 0, 0, 1, 1);
        }

        // 6. Derive stride from the ground resolution of the *whole*
        //    viewport (uniform native-units per pixel across the viewport),
        //    not just the intersection. When one screen pixel spans
        //    multiple grid cells we can skip cells at that ratio; when a
        //    single cell spans multiple pixels we sample every cell.
        double nativeViewportWidth = viewportNativeXMax - viewportNativeXMin;
        double nativeViewportHeight = viewportNativeYMax - viewportNativeYMin;
        double cellWidth = Math.Abs(grid.SpacingLongitudinal);
        double cellHeight = Math.Abs(grid.SpacingLatitudinal);
        int colStride = ComputeStride(nativeViewportWidth, viewport.WidthPixels, cellWidth);
        int rowStride = ComputeStride(nativeViewportHeight, viewport.HeightPixels, cellHeight);

        return new GridRegion(rowStart, rowEnd, colStart, colEnd, rowStride, colStride);
    }

    private static (double XMin, double XMax, double YMin, double YMax) ProjectViewportCorners(
        Viewport viewport, bool gridIsWgs84, ICrsTransform? wgs84ToNative)
    {
        // Viewport is authored in EPSG:4326 with X=longitude, Y=latitude —
        // matching the (X, Y) convention exposed by ICrsTransform.
        if (gridIsWgs84 || wgs84ToNative is null || wgs84ToNative.IsIdentity)
        {
            return (viewport.MinLongitude, viewport.MaxLongitude,
                    viewport.MinLatitude, viewport.MaxLatitude);
        }

        var (x1, y1) = wgs84ToNative.Transform(viewport.MinLongitude, viewport.MinLatitude);
        var (x2, y2) = wgs84ToNative.Transform(viewport.MaxLongitude, viewport.MinLatitude);
        var (x3, y3) = wgs84ToNative.Transform(viewport.MinLongitude, viewport.MaxLatitude);
        var (x4, y4) = wgs84ToNative.Transform(viewport.MaxLongitude, viewport.MaxLatitude);
        double xMin = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
        double xMax = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
        double yMin = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
        double yMax = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
        return (xMin, xMax, yMin, yMax);
    }

    private static int ComputeStride(double nativeViewportSpan, int viewportPixels, double cellSize)
    {
        if (viewportPixels <= 0 || cellSize <= 0 || nativeViewportSpan <= 0)
            return 1;

        // Ground resolution: native units per screen pixel.
        double groundResolution = nativeViewportSpan / viewportPixels;
        // How many grid cells fit in one screen pixel?
        double cellsPerPixel = groundResolution / cellSize;
        if (cellsPerPixel <= 1.0)
            return 1;
        int stride = (int)Math.Floor(cellsPerPixel);
        return stride < 1 ? 1 : stride;
    }

    private static bool IsWgs84(string crs)
    {
        // Accept "EPSG:4326" and "4326"; both are used across the codebase.
        return crs.Equals("EPSG:4326", StringComparison.OrdinalIgnoreCase)
            || crs.Equals("4326", StringComparison.OrdinalIgnoreCase);
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
    public GeoPosition GetPosition(int row, int col)
    {
        double lat = Metadata.OriginLatitude + row * Metadata.SpacingLatitudinal;
        double lon = Metadata.OriginLongitude + col * Metadata.SpacingLongitudinal;
        return new GeoPosition(lat, lon);
    }
}
