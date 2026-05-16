using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S111;

public class S111CoverageSource : ICoverageSource
{
    /// <summary>S-111 standard fill value for no-data cells.</summary>
    public const float FillValue = -9999f;

    private readonly S111Dataset _dataset;
    private int _selectedTimeIndex;

    public S111CoverageSource(S111Dataset dataset)
    {
        _dataset = dataset;
        _selectedTimeIndex = 0;
    }

    /// <summary>The underlying parsed S-111 dataset.</summary>
    public S111Dataset Dataset => _dataset;

    public CoverageMetadata Metadata
    {
        get
        {
            var coverage = _dataset.Coverages[_selectedTimeIndex];
            return new CoverageMetadata
            {
                Spec = new SpecRef("S-111", default),
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
                        Name = "surfaceCurrentSpeed",
                        Type = CoverageValueType.Float,
                        Units = "knots",
                        FillValue = FillValue,
                    },
                    new CoverageValueField
                    {
                        Name = "surfaceCurrentDirection",
                        Type = CoverageValueType.Float,
                        Units = "degrees",
                        FillValue = FillValue,
                    },
                ]
            };
        }
    }

    public IReadOnlyList<DateTime> AvailableTimes =>
        _dataset.Coverages.Select(c => c.TimePoint).ToList();

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

    public SampledCoverage Sample(GridRegion region)
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
        var speed = new float[rows * cols];
        var direction = new float[rows * cols];

        for (int r = 0; r < rows; r++)
        {
            int dstRowBase = r * cols;
            int srcRowBase = (rowStart + r * region.RowStride) * gridCols + colStart;
            for (int c = 0; c < cols; c++)
            {
                int srcIdx = srcRowBase + c * region.ColStride;
                speed[dstRowBase + c] = values[srcIdx].Speed;
                direction[dstRowBase + c] = values[srcIdx].Direction;
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
                ["surfaceCurrentSpeed"] = speed,
                ["surfaceCurrentDirection"] = direction,
            },
        };
    }
}
