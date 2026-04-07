using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.S102;

public class S102CoverageSource : ICoverageSource
{
    /// <summary>S-102 standard fill value for no-data cells.</summary>
    public const float FillValue = 1_000_000f;

    private readonly S102Dataset _dataset;
    private readonly BathymetryCoverage _coverage;
    
    public S102CoverageSource(S102Dataset dataset, int coverageIndex = 0)
    {
        _dataset = dataset;
        _coverage = dataset.Coverages[coverageIndex];
    }
    
    public CoverageMetadata Metadata => new CoverageMetadata
    {
        ProductSpec = "S-102",
        Extent = new BoundingBox(
            _coverage.OriginLatitude,
            _coverage.OriginLongitude,
            _coverage.OriginLatitude + _coverage.SpacingLatitudinal * _coverage.NumPointsLatitudinal,
            _coverage.OriginLongitude + _coverage.SpacingLongitudinal * _coverage.NumPointsLongitudinal),
        GridMetadata = new GridMetadata
        {
            NumRows = _coverage.NumPointsLatitudinal,
            NumColumns = _coverage.NumPointsLongitudinal,
            OriginLatitude = _coverage.OriginLatitude,
            OriginLongitude = _coverage.OriginLongitude,
            SpacingLatitudinal = _coverage.SpacingLatitudinal,
            SpacingLongitudinal = _coverage.SpacingLongitudinal,
        },
        HorizontalCRS = _dataset.HorizontalCRS?.ToString() ?? "EPSG:4326",
        VerticalDatum = "MSL",
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
    
    // S-102 is static — no time dimension
    public IReadOnlyList<DateTime> AvailableTimes => [];
    public void SelectTime(DateTime time) { }  // no-op
    
    public SampledCoverage Sample(GridRegion region)
    {
        var values = _coverage.Values;
        int gridRows = _coverage.NumPointsLatitudinal;
        int gridCols = _coverage.NumPointsLongitudinal;
        
        // Apply region subsetting
        var (rowStart, rowEnd, colStart, colEnd) = 
            region.Resolve(gridRows, gridCols);
        
        var rows = (rowEnd - rowStart) / region.RowStride;
        var cols = (colEnd - colStart) / region.ColStride;
        
        var depth = new float[rows, cols];
        var uncertainty = new float[rows, cols];
        
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int srcIdx = (rowStart + r * region.RowStride) * gridCols
                       + (colStart + c * region.ColStride);
            depth[r, c] = values[srcIdx].Depth;
            uncertainty[r, c] = values[srcIdx].Uncertainty;
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
            Values = new Dictionary<string, float[,]>
            {
                ["depth"] = depth,
                ["uncertainty"] = uncertainty,
            },
        };
    }
}
