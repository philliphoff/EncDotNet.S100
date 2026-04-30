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
        ]
    };

    [Fact]
    public async Task ProcessAsync_AssemblesStyledLayer_FromSourceAndCatalogue()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            originLatitude: 47.5,
            originLongitude: -122.3,
            spacingLat: 0.001,
            spacingLon: 0.001,
            horizontalCRS: "EPSG:4326",
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 5f, 6f, 7f }, { 8f, 9f, 10f } }
            });

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        // Color scheme is the catalogue's resolved scheme
        Assert.Same(DepthColorScheme, layer.ColorScheme);

        // Sampled coverage carries the source's grid metadata and field
        Assert.Equal(2, layer.Coverage.Metadata.NumRows);
        Assert.Equal(3, layer.Coverage.Metadata.NumColumns);
        Assert.Equal(47.5, layer.Coverage.Metadata.OriginLatitude);
        Assert.Equal(-122.3, layer.Coverage.Metadata.OriginLongitude);
        Assert.True(layer.Coverage.Values.ContainsKey("depth"));

        // Georeferencer carries the source's CRS
        Assert.Equal("EPSG:4326", layer.Georeferencer.CRS);

        // No-data value flows from the source's metadata
        Assert.True(float.IsNaN(layer.NoDataValue));

        // No symbol scheme by default
        Assert.Null(layer.SymbolScheme);
    }

    [Fact]
    public async Task ProcessAsync_WithSymbolScheme_PopulatesSymbolScheme()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 5f } }
            });

        var symbolScheme = new CoverageSymbolScheme
        {
            ValueFieldName = "speed",
            RotationFieldName = "direction",
            Bands = [
                new SymbolBand { MinValue = 0f, MaxValue = 5f, SymbolRef = "ARROW" },
            ],
        };
        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme, symbolScheme: symbolScheme);

        var pipeline = new CoveragePipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Same(symbolScheme, layer.SymbolScheme);
    }

    [Fact]
    public async Task ProcessAsync_PassesMarinerSettings_ToCatalogueResolution()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 5f } }
            });
        var mariner = new MarinerSettings
        {
            SafetyContour = 30.0,
            ShallowContour = 2.0,
            DeepContour = 30.0,
        };
        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);

        var pipeline = new CoveragePipeline();
        await pipeline.ProcessAsync(source, catalogue, mariner);

        Assert.Same(mariner, catalogue.LastSettings);
    }

    [Fact]
    public async Task ProcessAsync_NullMariner_PassesDefaultsToCatalogue()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 5f } }
            });
        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);

        var pipeline = new CoveragePipeline();
        await pipeline.ProcessAsync(source, catalogue);

        Assert.NotNull(catalogue.LastSettings);
    }

    [Fact]
    public async Task ProcessAsync_SentinelNoDataValue_FlowsThrough()
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

        Assert.Equal(noData, layer.NoDataValue);
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
        private readonly CoverageSymbolScheme? _symbolScheme;
        private readonly IReadOnlyList<ContourStyle> _contours;

        public FakeCoveragePortrayalCatalogue(
            CoverageColorScheme colorScheme,
            CoverageSymbolScheme? symbolScheme = null,
            IReadOnlyList<ContourStyle>? contours = null)
        {
            _colorScheme = colorScheme;
            _symbolScheme = symbolScheme;
            _contours = contours ?? [];
        }

        public string ProductSpec => "S-102";
        public string Edition => "1.0";
        public ColorPalette ActivePalette => ColorPalette.Default;
        public void SwitchPalette(PaletteType type) { }

        public MarinerSettings? LastSettings { get; private set; }

        public CoverageColorScheme ResolveColorScheme(MarinerSettings settings)
        {
            LastSettings = settings;
            return _colorScheme;
        }

        public CoverageSymbolScheme? ResolveSymbolScheme(MarinerSettings settings) => _symbolScheme;

        public IReadOnlyList<ContourStyle> Contours => _contours;
    }

    #endregion
}
