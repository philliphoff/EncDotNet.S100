using System.Diagnostics;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Pipelines.Coverage.Pyramid;

/// <summary>
/// Builds an in-memory <see cref="CoveragePyramid"/> from a base grid
/// by iteratively applying an <see cref="IPyramidReducer"/> in
/// non-overlapping 2×2 windows (S-100 Part 10c HDF5 grids; issue #486).
/// </summary>
/// <remarks>
/// <para>
/// The builder is spec-agnostic. Per-product coverage sources
/// (S-102, S-104, S-111) call it with a field-name → base-array
/// map, a per-field reducer + NODATA sentinel, and a level cap.
/// The builder emits one flat row-major <c>float[]</c> per (level,
/// field) pair.
/// </para>
/// <para>
/// Window semantics: level L+1 cell (r, c) pools the 2×2 base window
/// at level L rows {2r, 2r+1} and columns {2c, 2c+1}. Grids with odd
/// dimensions produce short (1- or 2-cell) windows on the trailing
/// row / column; the reducer contract accepts variable-length spans
/// to handle this.
/// </para>
/// <para>
/// Levels are added as long as either dimension is &gt; 1 and the
/// caller-supplied cap has not been reached. Recommended cap for
/// S-102-sized grids is 6 (64× reduction; 2048×2048 → 32×32).
/// </para>
/// </remarks>
public static class CoveragePyramidBuilder
{
    /// <summary>
    /// Builds a pyramid from a base grid.
    /// </summary>
    /// <param name="baseRows">Rows in the base grid (level 0).</param>
    /// <param name="baseCols">Columns in the base grid (level 0).</param>
    /// <param name="baseSpacingLat">Latitudinal spacing of the base grid (native CRS units).</param>
    /// <param name="baseSpacingLon">Longitudinal spacing of the base grid (native CRS units).</param>
    /// <param name="fields">
    /// Per-field descriptors: field name → (base cells as flat
    /// row-major <c>float[baseRows*baseCols]</c>, reducer, NODATA
    /// sentinel).
    /// </param>
    /// <param name="maxLevels">
    /// Upper bound on the number of levels including the base. Must
    /// be ≥ 1. Building stops early if a level shrinks to 1×1.
    /// </param>
    /// <returns>
    /// A <see cref="CoveragePyramid"/> whose level 0 is described but
    /// not stored (the caller's <paramref name="fields"/> arrays
    /// remain the source of truth for the base).
    /// </returns>
    public static CoveragePyramid Build(
        int baseRows,
        int baseCols,
        double baseSpacingLat,
        double baseSpacingLon,
        IReadOnlyDictionary<string, (float[] BaseCells, IPyramidReducer Reducer, float NoDataValue)> fields,
        int maxLevels = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseCols);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLevels);
        if (fields.Count == 0)
            throw new ArgumentException("At least one field must be supplied.", nameof(fields));

        var start = Stopwatch.GetTimestamp();
        using var buildActivity = Telemetry.ActivitySource.StartActivity("s100.coverage.pyramid.build");
        buildActivity?.SetTag("s100.coverage.pyramid.max_levels", maxLevels);
        buildActivity?.SetTag("s100.coverage.pyramid.base_rows", baseRows);
        buildActivity?.SetTag("s100.coverage.pyramid.base_cols", baseCols);

        foreach (var (name, spec) in fields)
        {
            if (spec.BaseCells.Length != baseRows * baseCols)
                throw new ArgumentException(
                    $"Field '{name}' has {spec.BaseCells.Length} cells; expected {baseRows * baseCols} (rows*cols).",
                    nameof(fields));
            ArgumentNullException.ThrowIfNull(spec.Reducer);
        }

        var levels = new List<CoverageOverviewLevel>
        {
            new(0, baseRows, baseCols, baseSpacingLat, baseSpacingLon),
        };
        var levelData = new Dictionary<int, IReadOnlyDictionary<string, float[]>>();

        // Track the "previous level" arrays per field so each new level
        // reduces from the level immediately above it (rather than
        // resampling the base every time). This is what makes the
        // total work O(N·4/3) instead of O(N·log N).
        var prev = new Dictionary<string, float[]>(fields.Count);
        foreach (var (name, spec) in fields)
            prev[name] = spec.BaseCells;

        int prevRows = baseRows;
        int prevCols = baseCols;
        double prevSpacingLat = baseSpacingLat;
        double prevSpacingLon = baseSpacingLon;

        while (levels.Count < maxLevels)
        {
            int nextRows = (prevRows + 1) / 2;
            int nextCols = (prevCols + 1) / 2;
            if (nextRows < 1 || nextCols < 1) break;
            if (nextRows == prevRows && nextCols == prevCols) break;

            double nextSpacingLat = prevSpacingLat * 2.0;
            double nextSpacingLon = prevSpacingLon * 2.0;
            int nextLevel = levels.Count;

            var perField = new Dictionary<string, float[]>(fields.Count);
            foreach (var (name, spec) in fields)
            {
                var src = prev[name];
                var dst = new float[nextRows * nextCols];
                ReduceLevel(src, prevRows, prevCols, dst, nextRows, nextCols, spec.Reducer, spec.NoDataValue);
                perField[name] = dst;
            }

            levels.Add(new CoverageOverviewLevel(nextLevel, nextRows, nextCols, nextSpacingLat, nextSpacingLon));
            levelData[nextLevel] = perField;
            foreach (var (name, dst) in perField)
                prev[name] = dst;
            prevRows = nextRows;
            prevCols = nextCols;
            prevSpacingLat = nextSpacingLat;
            prevSpacingLon = nextSpacingLon;

            if (prevRows <= 1 && prevCols <= 1) break;
        }

        var durationMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        PipelineMetrics.CoveragePyramidBuildDuration.Record(durationMs);
        buildActivity?.SetTag("s100.coverage.pyramid.levels", levels.Count);
        buildActivity?.SetTag("s100.coverage.pyramid.build_duration_ms", durationMs);

        return new CoveragePyramid(levels, levelData);
    }

    /// <summary>
    /// Reduces one level's grid into the next-coarser level by
    /// pooling each non-overlapping 2×2 window through
    /// <paramref name="reducer"/>.
    /// </summary>
    private static void ReduceLevel(
        ReadOnlySpan<float> src,
        int srcRows,
        int srcCols,
        Span<float> dst,
        int dstRows,
        int dstCols,
        IPyramidReducer reducer,
        float noDataValue)
    {
        Span<float> window = stackalloc float[4];
        for (int r = 0; r < dstRows; r++)
        {
            int sr0 = r * 2;
            int sr1 = sr0 + 1;
            for (int c = 0; c < dstCols; c++)
            {
                int sc0 = c * 2;
                int sc1 = sc0 + 1;
                int n = 0;
                window[n++] = src[sr0 * srcCols + sc0];
                if (sc1 < srcCols)
                    window[n++] = src[sr0 * srcCols + sc1];
                if (sr1 < srcRows)
                {
                    window[n++] = src[sr1 * srcCols + sc0];
                    if (sc1 < srcCols)
                        window[n++] = src[sr1 * srcCols + sc1];
                }
                dst[r * dstCols + c] = reducer.Reduce(window[..n], noDataValue);
            }
        }
    }
}
