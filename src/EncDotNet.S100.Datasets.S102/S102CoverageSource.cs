using System.Diagnostics;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Diagnostics;
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
/// and <see cref="MaxReducer"/> for uncertainty (worst-case). Residual
/// viewport stride applies equivalent reductions over balanced blocks
/// that preserve the sampled footprint's regular georeferencing.
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
            var (levelOriginLatitude, levelOriginLongitude) =
                GetLevelOrigin(_selectedLevel);

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
                    OriginLatitude = levelOriginLatitude,
                    OriginLongitude = levelOriginLongitude,
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
        Activity.Current?.SetTag(TelemetryTags.CoverageReducer, "min");

        // Level > 0 sources sample from the pre-reduced pyramid arrays,
        // which are already flat row-major float[]. Level 0 keeps the
        // compound BathymetryValue[] as its source. Both paths apply the
        // same shoal-safe reduction for residual viewport stride.
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

        var rows = DivideRoundUp(rowEnd - rowStart, region.RowStride);
        var cols = DivideRoundUp(colEnd - colStart, region.ColStride);

        // Flat row-major storage (PR-F): a 1000×1000 grid is 4 MB per field
        // on the LOH as float[,]; flat float[] avoids the 2-D bracket header
        // allocation and lets consumers iterate via Span<float>.
        var depth = new float[rows * cols];
        var uncertainty = new float[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int dstRowBase = r * cols;
            var (sourceRowStart, sourceRowEnd) =
                GetWindowBounds(r, rows, rowStart, rowEnd);

            for (int c = 0; c < cols; c++)
            {
                var (sourceColStart, sourceColEnd) =
                    GetWindowBounds(c, cols, colStart, colEnd);
                int destinationIndex = dstRowBase + c;
                if (region.RowStride == 1 && region.ColStride == 1)
                {
                    var value = values[sourceRowStart * gridCols + sourceColStart];
                    depth[destinationIndex] = value.Depth;
                    uncertainty[destinationIndex] = value.Uncertainty;
                    continue;
                }

                float minimumDepth = float.PositiveInfinity;
                float maximumUncertainty = float.NegativeInfinity;
                bool hasDepth = false;
                bool hasUncertainty = false;
                for (int sourceRow = sourceRowStart; sourceRow < sourceRowEnd; sourceRow++)
                {
                    int sourceRowBase = sourceRow * gridCols;
                    for (int sourceCol = sourceColStart; sourceCol < sourceColEnd; sourceCol++)
                    {
                        var value = values[sourceRowBase + sourceCol];
                        if (value.Depth != FillValue)
                        {
                            minimumDepth = Math.Min(minimumDepth, value.Depth);
                            hasDepth = true;
                        }
                        if (value.Uncertainty != FillValue)
                        {
                            maximumUncertainty = Math.Max(maximumUncertainty, value.Uncertainty);
                            hasUncertainty = true;
                        }
                    }
                }
                depth[destinationIndex] = hasDepth ? minimumDepth : FillValue;
                uncertainty[destinationIndex] = hasUncertainty ? maximumUncertainty : FillValue;
            }
        }

        double rowCellSpan = GetOutputCellSpan(rowStart, rowEnd, rows);
        double colCellSpan = GetOutputCellSpan(colStart, colEnd, cols);
        return new SampledCoverage
        {
            Region = region,
            Metadata = new GridMetadata
            {
                NumRows = rows,
                NumColumns = cols,
                OriginLatitude = GetSampleOrigin(
                    _coverage.OriginLatitude,
                    _coverage.SpacingLatitudinal,
                    rowStart,
                    rowCellSpan),
                OriginLongitude = GetSampleOrigin(
                    _coverage.OriginLongitude,
                    _coverage.SpacingLongitudinal,
                    colStart,
                    colCellSpan),
                SpacingLatitudinal = _coverage.SpacingLatitudinal * rowCellSpan,
                SpacingLongitudinal = _coverage.SpacingLongitudinal * colCellSpan,
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
        var rows = DivideRoundUp(rowEnd - rowStart, region.RowStride);
        var cols = DivideRoundUp(colEnd - colStart, region.ColStride);

        var depth = new float[rows * cols];
        var uncertainty = new float[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int dstRowBase = r * cols;
            var (sourceRowStart, sourceRowEnd) =
                GetWindowBounds(r, rows, rowStart, rowEnd);

            for (int c = 0; c < cols; c++)
            {
                var (sourceColStart, sourceColEnd) =
                    GetWindowBounds(c, cols, colStart, colEnd);
                int destinationIndex = dstRowBase + c;
                if (region.RowStride == 1 && region.ColStride == 1)
                {
                    int sourceIndex = sourceRowStart * gridCols + sourceColStart;
                    depth[destinationIndex] = depthSrc[sourceIndex];
                    uncertainty[destinationIndex] = uncSrc[sourceIndex];
                    continue;
                }

                float minimumDepth = float.PositiveInfinity;
                float maximumUncertainty = float.NegativeInfinity;
                bool hasDepth = false;
                bool hasUncertainty = false;
                for (int sourceRow = sourceRowStart; sourceRow < sourceRowEnd; sourceRow++)
                {
                    int sourceRowBase = sourceRow * gridCols;
                    for (int sourceCol = sourceColStart; sourceCol < sourceColEnd; sourceCol++)
                    {
                        int sourceIndex = sourceRowBase + sourceCol;
                        float sourceDepth = depthSrc[sourceIndex];
                        if (sourceDepth != FillValue)
                        {
                            minimumDepth = Math.Min(minimumDepth, sourceDepth);
                            hasDepth = true;
                        }
                        float sourceUncertainty = uncSrc[sourceIndex];
                        if (sourceUncertainty != FillValue)
                        {
                            maximumUncertainty = Math.Max(maximumUncertainty, sourceUncertainty);
                            hasUncertainty = true;
                        }
                    }
                }
                depth[destinationIndex] = hasDepth ? minimumDepth : FillValue;
                uncertainty[destinationIndex] = hasUncertainty ? maximumUncertainty : FillValue;
            }
        }

        var (levelOriginLatitude, levelOriginLongitude) = GetLevelOrigin(level);
        double rowCellSpan = GetOutputCellSpan(rowStart, rowEnd, rows);
        double colCellSpan = GetOutputCellSpan(colStart, colEnd, cols);
        return new SampledCoverage
        {
            Region = region,
            Metadata = new GridMetadata
            {
                NumRows = rows,
                NumColumns = cols,
                OriginLatitude = GetSampleOrigin(
                    levelOriginLatitude,
                    levelInfo.SpacingLatitudinal,
                    rowStart,
                    rowCellSpan),
                OriginLongitude = GetSampleOrigin(
                    levelOriginLongitude,
                    levelInfo.SpacingLongitudinal,
                    colStart,
                    colCellSpan),
                SpacingLatitudinal = levelInfo.SpacingLatitudinal * rowCellSpan,
                SpacingLongitudinal = levelInfo.SpacingLongitudinal * colCellSpan,
            },
            Values = new Dictionary<string, float[]>
            {
                ["depth"] = depth,
                ["uncertainty"] = uncertainty,
            },
        };
    }

    private static int DivideRoundUp(int value, int divisor) =>
        value == 0 ? 0 : (value - 1) / divisor + 1;

    private static (int Start, int End) GetWindowBounds(
        int outputIndex,
        int outputCount,
        int sourceStart,
        int sourceEnd)
    {
        int sourceLength = sourceEnd - sourceStart;
        int start = sourceStart + (int)((long)outputIndex * sourceLength / outputCount);
        int end = sourceStart + (int)((long)(outputIndex + 1) * sourceLength / outputCount);
        return (start, end);
    }

    private static double GetOutputCellSpan(int start, int end, int outputCount) =>
        outputCount == 0 ? 1 : (end - start) / (double)outputCount;

    private (double Latitude, double Longitude) GetLevelOrigin(int level)
    {
        int poolingFactor = 1 << level;
        double centreOffset = (poolingFactor - 1) / 2.0;
        return (
            _coverage.OriginLatitude + centreOffset * _coverage.SpacingLatitudinal,
            _coverage.OriginLongitude + centreOffset * _coverage.SpacingLongitudinal);
    }

    private static double GetSampleOrigin(
        double sourceOrigin,
        double sourceSpacing,
        int start,
        double cellSpan) =>
        sourceOrigin + (start + (cellSpan - 1) / 2.0) * sourceSpacing;

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

        int uniformLevelCount = 1;
        int levelRows = rows;
        int levelColumns = cols;
        while (uniformLevelCount < MaxPyramidLevels &&
               levelRows > 1 &&
               levelColumns > 1 &&
               levelRows % 2 == 0 &&
               levelColumns % 2 == 0)
        {
            uniformLevelCount++;
            levelRows /= 2;
            levelColumns /= 2;
        }

        return CoveragePyramidBuilder.Build(
            rows,
            cols,
            _coverage.SpacingLatitudinal,
            _coverage.SpacingLongitudinal,
            fields,
            uniformLevelCount);
    }
}
