using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// <see cref="ICoverageSource"/> implementation for S-104 Water Level datasets.
/// Provides time-indexed access to water level height and trend grids.
/// </summary>
public class S104CoverageSource : ICoverageSource
{
    /// <summary>S-104 standard fill value for no-data cells.</summary>
    public const float FillValue = -9999f;

    private readonly S104Dataset _dataset;
    private int _selectedTimeIndex;

    /// <summary>
    /// Initializes a new instance of <see cref="S104CoverageSource"/> from the given dataset.
    /// </summary>
    /// <param name="dataset">The S-104 dataset to expose as a coverage source.</param>
    public S104CoverageSource(S104Dataset dataset)
    {
        _dataset = dataset;
        _selectedTimeIndex = 0;
    }

    /// <summary>The underlying parsed S-104 dataset.</summary>
    public S104Dataset Dataset => _dataset;

    /// <inheritdoc/>
    public CoverageMetadata Metadata
    {
        get
        {
            var coverage = _dataset.Coverages[_selectedTimeIndex];
            return new CoverageMetadata
            {
                Spec = new SpecRef("S-104", default),
                Extent = new BoundingBox(
                    coverage.OriginLatitude,
                    coverage.OriginLongitude,
                    coverage.OriginLatitude + coverage.SpacingLatitudinal * coverage.NumPointsLatitudinal,
                    coverage.OriginLongitude + coverage.SpacingLongitudinal * coverage.NumPointsLongitudinal),
                GridMetadata = new GridMetadata
                {
                    NumRows = coverage.NumPointsLatitudinal,
                    NumColumns = coverage.NumPointsLongitudinal,
                    OriginLatitude = coverage.OriginLatitude,
                    OriginLongitude = coverage.OriginLongitude,
                    SpacingLatitudinal = coverage.SpacingLatitudinal,
                    SpacingLongitudinal = coverage.SpacingLongitudinal,
                },
                HorizontalCRS = _dataset.HorizontalCRS?.ToString() ?? "EPSG:4326",
                VerticalDatum = "MSL",
                NoDataValue = FillValue,
                ValueFields =
                [
                    new CoverageValueField
                    {
                        Name = "waterLevelHeight",
                        Type = CoverageValueType.Float,
                        Units = "metres",
                        FillValue = FillValue,
                    },
                    new CoverageValueField
                    {
                        Name = "waterLevelTrend",
                        Type = CoverageValueType.Float,
                        Units = "",
                        FillValue = 0f,
                    },
                ]
            };
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _dataset.Coverages.Select(c => c.TimePoint).ToList();

    /// <inheritdoc/>
    public void SelectTime(DateTime time)
    {
        for (int i = 0; i < _dataset.Coverages.Count; i++)
        {
            if (_dataset.Coverages[i].TimePoint == time)
            {
                _selectedTimeIndex = i;
                return;
            }
        }

        // Find the nearest time step
        int closest = 0;
        var minDiff = TimeSpan.MaxValue;
        for (int i = 0; i < _dataset.Coverages.Count; i++)
        {
            var diff = (_dataset.Coverages[i].TimePoint - time).Duration();
            if (diff < minDiff)
            {
                minDiff = diff;
                closest = i;
            }
        }

        _selectedTimeIndex = closest;
    }

    /// <inheritdoc/>
    public SampledCoverage Sample(GridRegion region, CancellationToken cancellationToken = default)
    {
        var coverage = _dataset.Coverages[_selectedTimeIndex];
        var values = coverage.Values;
        int gridRows = coverage.NumPointsLatitudinal;
        int gridCols = coverage.NumPointsLongitudinal;

        var (rowStart, rowEnd, colStart, colEnd) =
            region.Resolve(gridRows, gridCols);

        var rows = (rowEnd - rowStart) / region.RowStride;
        var cols = (colEnd - colStart) / region.ColStride;

        // Flat row-major storage (PR-F).
        var height = new float[rows * cols];
        var trend = new float[rows * cols];

        for (int r = 0; r < rows; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int dstRowBase = r * cols;
            int srcRowBase = (rowStart + r * region.RowStride) * gridCols + colStart;
            for (int c = 0; c < cols; c++)
            {
                int srcIdx = srcRowBase + c * region.ColStride;
                height[dstRowBase + c] = values[srcIdx].Height;
                trend[dstRowBase + c] = (float)values[srcIdx].Trend;
            }
        }

        return new SampledCoverage
        {
            Region = region,
            Metadata = new GridMetadata
            {
                NumRows = rows,
                NumColumns = cols,
                OriginLatitude = coverage.OriginLatitude + rowStart * coverage.SpacingLatitudinal,
                OriginLongitude = coverage.OriginLongitude + colStart * coverage.SpacingLongitudinal,
                SpacingLatitudinal = coverage.SpacingLatitudinal * region.RowStride,
                SpacingLongitudinal = coverage.SpacingLongitudinal * region.ColStride,
            },
            Values = new Dictionary<string, float[]>
            {
                ["waterLevelHeight"] = height,
                ["waterLevelTrend"] = trend,
            },
        };
    }
}
