using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Pipelines.Tests;

public class CoveragePipelineTests
{
    private static readonly CoverageColorScheme DepthColorScheme = new()
    {
        FieldName = "depth",
        Bands =
        [
            new ColorBand { MinValue = 0f, MaxValue = 5f, Color = "#ADE3FF" },
            new ColorBand { MinValue = 5f, MaxValue = 10f, Color = "#6BC5FF" },
            new ColorBand { MinValue = 10f, MaxValue = 20f, Color = "#2196F3" },
            new ColorBand { MinValue = 20f, MaxValue = 50f, Color = "#0D47A1" },
        ]
    };

    [Fact]
    public async Task ProcessAsync_ColorizesDepthValues()
    {
        // 2x3 grid with known depth values
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 2f, 7f, 15f }, { 25f, 3f, 12f } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(6, layer.CellColors.Count);

        // Row 0: 2m → [0,5) shallow, 7m → [5,10), 15m → [10,20)
        Assert.Equal("#ADE3FF", layer.CellColors[0]);
        Assert.Equal("#6BC5FF", layer.CellColors[1]);
        Assert.Equal("#2196F3", layer.CellColors[2]);

        // Row 1: 25m → [20,50), 3m → [0,5), 12m → [10,20)
        Assert.Equal("#0D47A1", layer.CellColors[3]);
        Assert.Equal("#ADE3FF", layer.CellColors[4]);
        Assert.Equal("#2196F3", layer.CellColors[5]);
    }

    [Fact]
    public async Task ProcessAsync_NaNNoDataValues_ProduceNullColors()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 2f, float.NaN }, { float.NaN, 8f } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(4, layer.CellColors.Count);
        Assert.Equal("#ADE3FF", layer.CellColors[0]); // 2m
        Assert.Null(layer.CellColors[1]);               // NaN
        Assert.Null(layer.CellColors[2]);               // NaN
        Assert.Equal("#6BC5FF", layer.CellColors[3]);   // 8m
    }

    [Fact]
    public async Task ProcessAsync_SentinelNoDataValues_ProduceNullColors()
    {
        const float noData = -9999f;
        var source = new FakeCoverageSource(
            noDataValue: noData,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 3f, noData } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal("#ADE3FF", layer.CellColors[0]);
        Assert.Null(layer.CellColors[1]);
    }

    [Fact]
    public async Task ProcessAsync_OutOfRangeValues_ProduceNullColors()
    {
        // 100m is outside all bands (max is 50)
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 100f } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Null(layer.CellColors[0]);
    }

    [Fact]
    public async Task ProcessAsync_GridMetadata_PassesThrough()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            originLatitude: 47.5,
            originLongitude: -122.3,
            spacingLat: 0.001,
            spacingLon: 0.001,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 5f, 6f, 7f }, { 8f, 9f, 10f } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(2, layer.Grid.NumRows);
        Assert.Equal(3, layer.Grid.NumColumns);
        Assert.Equal(47.5, layer.Grid.OriginLatitude);
        Assert.Equal(-122.3, layer.Grid.OriginLongitude);
        Assert.Equal(0.001, layer.Grid.SpacingLatitudinal);
        Assert.Equal(0.001, layer.Grid.SpacingLongitudinal);
    }

    [Fact]
    public async Task ProcessAsync_Extent_ComputedFromGrid()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            originLatitude: 10.0,
            originLongitude: 20.0,
            spacingLat: 1.0,
            spacingLon: 2.0,
            fields: new Dictionary<string, float[,]>
            {
                // 3 rows x 2 cols
                ["depth"] = new float[,] { { 1f, 2f }, { 3f, 4f }, { 5f, 6f } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(10.0, layer.Extent.SouthLatitude);
        Assert.Equal(20.0, layer.Extent.WestLongitude);
        Assert.Equal(13.0, layer.Extent.NorthLatitude);  // 10 + 1.0 * 3
        Assert.Equal(24.0, layer.Extent.EastLongitude);   // 20 + 2.0 * 2
    }

    [Fact]
    public async Task ProcessAsync_EmptyContours_WhenCatalogueHasNone()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 5f } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme, contours: []);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Empty(layer.Contours);
    }

    [Fact]
    public async Task ProcessAsync_Metadata_PassesThrough()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            productSpec: "S-102",
            horizontalCRS: "EPSG:4326",
            verticalDatum: "MSL",
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 5f } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal("S-102", layer.Metadata.ProductSpec);
        Assert.Equal("EPSG:4326", layer.Metadata.HorizontalCRS);
        Assert.Equal("MSL", layer.Metadata.VerticalDatum);
    }

    #region Fakes

    private sealed class FakeCoverageSource : ICoverageSource
    {
        private readonly Dictionary<string, float[,]> _fields;
        private readonly float _noDataValue;
        private readonly double _originLat;
        private readonly double _originLon;
        private readonly double _spacingLat;
        private readonly double _spacingLon;
        private readonly string _productSpec;
        private readonly string _horizontalCRS;
        private readonly string _verticalDatum;

        public FakeCoverageSource(
            float noDataValue,
            Dictionary<string, float[,]> fields,
            double originLatitude = 0.0,
            double originLongitude = 0.0,
            double spacingLat = 0.01,
            double spacingLon = 0.01,
            string productSpec = "S-102",
            string horizontalCRS = "EPSG:4326",
            string verticalDatum = "MSL")
        {
            _noDataValue = noDataValue;
            _fields = fields;
            _originLat = originLatitude;
            _originLon = originLongitude;
            _spacingLat = spacingLat;
            _spacingLon = spacingLon;
            _productSpec = productSpec;
            _horizontalCRS = horizontalCRS;
            _verticalDatum = verticalDatum;
        }

        private (int Rows, int Cols) GridSize
        {
            get
            {
                var first = _fields.Values.First();
                return (first.GetLength(0), first.GetLength(1));
            }
        }

        public CoverageMetadata Metadata
        {
            get
            {
                var (rows, cols) = GridSize;
                return new CoverageMetadata
                {
                    ProductSpec = _productSpec,
                    Extent = new BoundingBox(
                        _originLat, _originLon,
                        _originLat + _spacingLat * rows,
                        _originLon + _spacingLon * cols),
                    GridMetadata = new GridMetadata
                    {
                        NumRows = rows,
                        NumColumns = cols,
                        OriginLatitude = _originLat,
                        OriginLongitude = _originLon,
                        SpacingLatitudinal = _spacingLat,
                        SpacingLongitudinal = _spacingLon,
                    },
                    HorizontalCRS = _horizontalCRS,
                    VerticalDatum = _verticalDatum,
                    NoDataValue = _noDataValue,
                    ValueFields = _fields.Keys.Select(name => new CoverageValueField
                    {
                        Name = name,
                        Type = CoverageValueType.Float,
                        Units = "metres",
                        FillValue = _noDataValue,
                    }).ToList(),
                };
            }
        }

        public IReadOnlyList<DateTime> AvailableTimes => [];
        public void SelectTime(DateTime time) { }

        public SampledCoverage Sample(GridRegion region)
        {
            var (rows, cols) = GridSize;
            return new SampledCoverage
            {
                Region = region,
                Metadata = new GridMetadata
                {
                    NumRows = rows,
                    NumColumns = cols,
                    OriginLatitude = _originLat,
                    OriginLongitude = _originLon,
                    SpacingLatitudinal = _spacingLat,
                    SpacingLongitudinal = _spacingLon,
                },
                Values = _fields,
            };
        }
    }

    private sealed class FakeCoveragePortrayalCatalogue : ICoveragePortrayalCatalogue
    {
        private readonly CoverageColorScheme _colorScheme;
        private readonly IReadOnlyList<ContourStyle> _contours;

        public FakeCoveragePortrayalCatalogue(
            CoverageColorScheme colorScheme,
            IReadOnlyList<ContourStyle>? contours = null)
        {
            _colorScheme = colorScheme;
            _contours = contours ?? [];
        }

        public string ProductSpec => "S-102";
        public string Edition => "1.0";
        public ColorPalette ActivePalette => ColorPalette.Default;
        public void SwitchPalette(PaletteType type) { }

        public CoverageColorScheme ResolveColorScheme(NavigationContext context) => _colorScheme;
        public IReadOnlyList<ContourStyle> Contours => _contours;
    }

    #endregion
}
