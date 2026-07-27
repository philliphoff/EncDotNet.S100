using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Coverage.Pyramid;

namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// Adapts an S-102 <see cref="S102Dataset"/> to the pipeline-facing
/// <see cref="ICoverageSource"/>. Supports the coverage overview
/// pyramid (issue #486): when a level &gt; 0 is selected via
/// <see cref="SelectOverviewLevel"/> the pyramid is built lazily on
/// first access with <see cref="MinReducer"/> for depth (shoal-biased)
/// and <see cref="MaxReducer"/> for uncertainty (worst-case). Level 0
/// preserves today's behaviour bit-for-bit.
/// </summary>
public class S102CoverageSource : ICoverageSource
{
    /// <summary>S-102 standard fill value for no-data cells.</summary>
    public const float FillValue = 1_000_000f;

    /// <summary>
    /// Maximum number of pyramid levels (base + downsampled). Cap = 6
    /// gives 64× reduction per axis; a 2048×2048 tile shrinks to 32×32.
    /// Approved in issue #486 review.
    /// </summary>
    public const int MaxPyramidLevels = 6;

    private readonly S102Dataset _dataset;
    private readonly BathymetryCoverage _coverage;
    private readonly Lazy<CoveragePyramid> _pyramid;
    private int _selectedLevel;

    public S102CoverageSource(S102Dataset dataset, int coverageIndex = 0)
    {
        _dataset = dataset;
        _coverage = dataset.Coverages[coverageIndex];
        _pyramid = new Lazy<CoveragePyramid>(BuildPyramid, isThreadSafe: true);
    }

    /// <summary>The underlying S-102 dataset wrapped by this source.</summary>
    public S102Dataset Dataset => _dataset;

    /// <summary>The single bathymetry coverage instance this source exposes.</summary>
    public BathymetryCoverage Coverage => _coverage;

    /// <inheritdoc/>
    public IReadOnlyList<CoverageOverviewLevel> AvailableOverviewLevels =>
        _pyramid.Value.Levels;

    /// <inheritdoc/>
    public int SelectedOverviewLevel => _selectedLevel;

    /// <inheritdoc/>
    public void SelectOverviewLevel(int level)
    {
        var available = _pyramid.Value.Levels;
        if (level < 0 || level >= available.Count)
            throw new ArgumentOutOfRangeException(nameof(level), level,
                $"Level must be in [0, {available.Count - 1}]. Available: {available.Count}.");
        _selectedLevel = level;
    }

    public CoverageMetadata Metadata
    {
        get
        {
            // Level-aware metadata: if a downsampled level is selected,
            // report its geometry (rows/cols shrink, spacing doubles)
            // so viewport-derived GridRegion.FromViewport (sibling #487)
            // naturally computes against the selected level.
            var level = _selectedLevel == 0
                ? new CoverageOverviewLevel(
                    0,
                    _coverage.NumPointsLatitudinal,
                    _coverage.NumPointsLongitudinal,
                    _coverage.SpacingLatitudinal,
                    _coverage.SpacingLongitudinal)
                : _pyramid.Value.GetLevel(_selectedLevel);

            return new CoverageMetadata
            {
                Spec = new SpecRef("S-102", default),
                Extent = new BoundingBox(
                    _coverage.OriginLatitude,
                    _coverage.OriginLongitude,
                    _coverage.OriginLatitude + _coverage.SpacingLatitudinal * _coverage.NumPointsLatitudinal,
                    _coverage.OriginLongitude + _coverage.SpacingLongitudinal * _coverage.NumPointsLongitudinal),
                GridMetadata = new GridMetadata
                {
                    NumRows = level.Rows,
                    NumColumns = level.Cols,
                    OriginLatitude = _coverage.OriginLatitude,
                    OriginLongitude = _coverage.OriginLongitude,
                    SpacingLatitudinal = level.SpacingLatitudinal,
                    SpacingLongitudinal = level.SpacingLongitudinal,
                },
                HorizontalCRS = _dataset.HorizontalCRS?.ToString() ?? "EPSG:4326",
                VerticalDatum = VerticalDatums.GetLabel(_dataset.VerticalDatum),
                NoDataValue = FillValue,
                ValueFields =
                [
                    new CoverageValueField
                    {
                        Name = "depth",
                        Type = CoverageValueType.Float,
                        Units = "metres",
                        FillValue = FillValue,
                    },
                    new CoverageValueField
                    {
                        Name = "uncertainty",
                        Type = CoverageValueType.Float,
                        Units = "metres",
                        FillValue = FillValue,
                    },
                ]
            };
        }
    }

    // S-102 is static — no time dimension
    public IReadOnlyList<DateTime> AvailableTimes => [];
    public void SelectTime(DateTime time) { }  // no-op

    public virtual SampledCoverage Sample(GridRegion region, CancellationToken cancellationToken = default)
    {
        // Level > 0 sources sample from the pre-reduced pyramid arrays,
        // which are already flat row-major float[]. Level 0 keeps the
        // original per-cell copy path over the compound BathymetryValue[]
        // (unchanged behaviour for callers that never touch the pyramid).
        return _selectedLevel == 0
            ? SampleLevel0(region, cancellationToken)
            : SamplePyramidLevel(_selectedLevel, region, cancellationToken);
    }

    private SampledCoverage SampleLevel0(GridRegion region, CancellationToken cancellationToken)
    {
        var values = _coverage.Values;
        int gridRows = _coverage.NumPointsLatitudinal;
        int gridCols = _coverage.NumPointsLongitudinal;

        // Apply region subsetting
        var (rowStart, rowEnd, colStart, colEnd) =
            region.Resolve(gridRows, gridCols);

        var rows = (rowEnd - rowStart) / region.RowStride;
        var cols = (colEnd - colStart) / region.ColStride;

        // Flat row-major storage (PR-F): a 1000×1000 grid is 4 MB per field
        // on the LOH as float[,]; flat float[] avoids the 2-D bracket header
        // allocation and lets consumers iterate via Span<float>.
        var depth = new float[rows * cols];
        var uncertainty = new float[rows * cols];

        for (int r = 0; r < rows; r++)
        {
            // Per-row (not per-cell) cancellation check keeps the inner copy
            // loop branch-free while still bounding cancellation latency.
            cancellationToken.ThrowIfCancellationRequested();
            int dstRowBase = r * cols;
            int srcRowBase = (rowStart + r * region.RowStride) * gridCols + colStart;
            for (int c = 0; c < cols; c++)
            {
                int srcIdx = srcRowBase + c * region.ColStride;
                depth[dstRowBase + c] = values[srcIdx].Depth;
                uncertainty[dstRowBase + c] = values[srcIdx].Uncertainty;
            }
        }

        return new SampledCoverage
        {
            Region = region,
            Metadata = new GridMetadata
            {
                NumRows = rows,
                NumColumns = cols,
                OriginLatitude = _coverage.OriginLatitude + rowStart * _coverage.SpacingLatitudinal,
                OriginLongitude = _coverage.OriginLongitude + colStart * _coverage.SpacingLongitudinal,
                SpacingLatitudinal = _coverage.SpacingLatitudinal * region.RowStride,
                SpacingLongitudinal = _coverage.SpacingLongitudinal * region.ColStride,
            },
            Values = new Dictionary<string, float[]>
            {
                ["depth"] = depth,
                ["uncertainty"] = uncertainty,
            },
        };
    }

    private SampledCoverage SamplePyramidLevel(int level, GridRegion region, CancellationToken cancellationToken)
    {
        var pyramid = _pyramid.Value;
        var levelInfo = pyramid.GetLevel(level);
        int gridRows = levelInfo.Rows;
        int gridCols = levelInfo.Cols;

        var depthSrc = pyramid.GetField(level, "depth");
        var uncSrc = pyramid.GetField(level, "uncertainty");

        var (rowStart, rowEnd, colStart, colEnd) = region.Resolve(gridRows, gridCols);
        var rows = (rowEnd - rowStart) / region.RowStride;
        var cols = (colEnd - colStart) / region.ColStride;

        var depth = new float[rows * cols];
        var uncertainty = new float[rows * cols];

        for (int r = 0; r < rows; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int dstRowBase = r * cols;
            int srcRowBase = (rowStart + r * region.RowStride) * gridCols + colStart;
            for (int c = 0; c < cols; c++)
            {
                int srcIdx = srcRowBase + c * region.ColStride;
                depth[dstRowBase + c] = depthSrc[srcIdx];
                uncertainty[dstRowBase + c] = uncSrc[srcIdx];
            }
        }

        return new SampledCoverage
        {
            Region = region,
            Metadata = new GridMetadata
            {
                NumRows = rows,
                NumColumns = cols,
                OriginLatitude = _coverage.OriginLatitude + rowStart * levelInfo.SpacingLatitudinal,
                OriginLongitude = _coverage.OriginLongitude + colStart * levelInfo.SpacingLongitudinal,
                SpacingLatitudinal = levelInfo.SpacingLatitudinal * region.RowStride,
                SpacingLongitudinal = levelInfo.SpacingLongitudinal * region.ColStride,
            },
            Values = new Dictionary<string, float[]>
            {
                ["depth"] = depth,
                ["uncertainty"] = uncertainty,
            },
        };
    }

    /// <summary>
    /// Reifies the compound <see cref="BathymetryValue"/>[] into two
    /// flat <c>float[]</c> arrays and hands them to
    /// <see cref="CoveragePyramidBuilder.Build"/>. Called at most once
    /// per source (guarded by <see cref="Lazy{T}"/>).
    /// </summary>
    private CoveragePyramid BuildPyramid()
    {
        int rows = _coverage.NumPointsLatitudinal;
        int cols = _coverage.NumPointsLongitudinal;
        var values = _coverage.Values;

        var depthBase = new float[rows * cols];
        var uncBase = new float[rows * cols];
        for (int i = 0; i < values.Length; i++)
        {
            depthBase[i] = values[i].Depth;
            uncBase[i] = values[i].Uncertainty;
        }

        var fields = new Dictionary<string, (float[] BaseCells, IPyramidReducer Reducer, float NoDataValue)>
        {
            // Shoal-biased min: a pyramid cell can never appear safer
            // (deeper) than any base cell it pools. Non-negotiable for
            // ECDIS safety (issue #486 acceptance criterion).
            ["depth"] = (depthBase, MinReducer.Instance, FillValue),
            // Worst-case max: uncertainty of a pooled cell is at least
            // the max of its inputs.
            ["uncertainty"] = (uncBase, MaxReducer.Instance, FillValue),
        };

        return CoveragePyramidBuilder.Build(
            rows,
            cols,
            _coverage.SpacingLatitudinal,
            _coverage.SpacingLongitudinal,
            fields,
            MaxPyramidLevels);
    }
}
