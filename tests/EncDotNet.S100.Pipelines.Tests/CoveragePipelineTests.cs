using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Quantities;

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
    public async Task ProcessAsync_GeoreferencerReflectsSampledSubset()
    {
        // Regression guard for issue #487: the georeferencer on the
        // returned layer must be built from the *sampled* subset's
        // metadata, not from the source's full-grid metadata. Otherwise
        // a subset+stride sample from GridRegion.FromViewport would be
        // painted in the wrong geographic location.
        var source = new SubsettingCoverageSource(
            fullGridOriginLat: 47.5,
            fullGridOriginLon: -122.3,
            fullGridSpacingLat: 0.001,
            fullGridSpacingLon: 0.001,
            fullGridRows: 100,
            fullGridCols: 100,
            // Emit a "sampled" coverage that pretends we asked for
            // rows/cols starting at (10, 20) with stride 2.
            sampledRowStart: 10,
            sampledColStart: 20,
            sampledRowStride: 2,
            sampledColStride: 2,
            sampledRows: 40,
            sampledCols: 30);

        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        // Georeferencer's metadata should match the sampled subset (origin
        // shifted by rowStart*Spacing, spacing scaled by stride).
        var geoMeta = layer.Georeferencer.Metadata;
        Assert.Equal(47.5 + 10 * 0.001, geoMeta.OriginLatitude, 9);
        Assert.Equal(-122.3 + 20 * 0.001, geoMeta.OriginLongitude, 9);
        Assert.Equal(0.001 * 2, geoMeta.SpacingLatitudinal, 12);
        Assert.Equal(0.001 * 2, geoMeta.SpacingLongitudinal, 12);
        Assert.Equal(40, geoMeta.NumRows);
        Assert.Equal(30, geoMeta.NumColumns);
    }

    /// <summary>
    /// A coverage source whose <see cref="Sample"/> returns a
    /// <see cref="SampledCoverage"/> whose <see cref="GridMetadata"/>
    /// is deliberately DIFFERENT from the source's full-grid metadata
    /// — mimicking what S102/S104/S111 sources do when given a subset+stride
    /// <see cref="GridRegion"/>. Used only by
    /// <see cref="ProcessAsync_GeoreferencerReflectsSampledSubset"/>.
    /// </summary>
    private sealed class SubsettingCoverageSource : ICoverageSource
    {
        private readonly double _originLat, _originLon, _spacingLat, _spacingLon;
        private readonly int _rows, _cols;
        private readonly double _sampledOriginLat, _sampledOriginLon;
        private readonly double _sampledSpacingLat, _sampledSpacingLon;
        private readonly int _sampledRows, _sampledCols;

        public SubsettingCoverageSource(
            double fullGridOriginLat, double fullGridOriginLon,
            double fullGridSpacingLat, double fullGridSpacingLon,
            int fullGridRows, int fullGridCols,
            int sampledRowStart, int sampledColStart,
            int sampledRowStride, int sampledColStride,
            int sampledRows, int sampledCols)
        {
            _originLat = fullGridOriginLat;
            _originLon = fullGridOriginLon;
            _spacingLat = fullGridSpacingLat;
            _spacingLon = fullGridSpacingLon;
            _rows = fullGridRows;
            _cols = fullGridCols;
            _sampledOriginLat = fullGridOriginLat + sampledRowStart * fullGridSpacingLat;
            _sampledOriginLon = fullGridOriginLon + sampledColStart * fullGridSpacingLon;
            _sampledSpacingLat = fullGridSpacingLat * sampledRowStride;
            _sampledSpacingLon = fullGridSpacingLon * sampledColStride;
            _sampledRows = sampledRows;
            _sampledCols = sampledCols;
        }

        public CoverageMetadata Metadata => new()
        {
            Spec = new SpecRef("S-102", default),
            Extent = new BoundingBox(
                _originLat, _originLon,
                _originLat + _spacingLat * _rows,
                _originLon + _spacingLon * _cols),
            GridMetadata = new GridMetadata
            {
                NumRows = _rows,
                NumColumns = _cols,
                OriginLatitude = _originLat,
                OriginLongitude = _originLon,
                SpacingLatitudinal = _spacingLat,
                SpacingLongitudinal = _spacingLon,
            },
            HorizontalCRS = "EPSG:4326",
            VerticalDatum = "MSL",
            NoDataValue = float.NaN,
            ValueFields = new List<CoverageValueField>
            {
                new()
                {
                    Name = "depth",
                    Type = CoverageValueType.Float,
                    Units = "metres",
                    FillValue = float.NaN,
                },
            },
        };

        public IReadOnlyList<DateTime> AvailableTimes => [];
        public void SelectTime(DateTime time) { }

        public SampledCoverage Sample(GridRegion region, CancellationToken cancellationToken = default)
        {
            return new SampledCoverage
            {
                Region = region,
                Metadata = new GridMetadata
                {
                    NumRows = _sampledRows,
                    NumColumns = _sampledCols,
                    OriginLatitude = _sampledOriginLat,
                    OriginLongitude = _sampledOriginLon,
                    SpacingLatitudinal = _sampledSpacingLat,
                    SpacingLongitudinal = _sampledSpacingLon,
                },
                Values = new Dictionary<string, float[]>
                {
                    ["depth"] = new float[_sampledRows * _sampledCols],
                },
            };
        }
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
            SafetyContour = Depth.FromMetres(30.0),
            ShallowContour = Depth.FromMetres(2.0),
            DeepContour = Depth.FromMetres(30.0),
        };
        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);

        var pipeline = new CoveragePipeline();
        await pipeline.ProcessAsync(source, catalogue, mariner: mariner);

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

    [Fact]
    public async Task ProcessAsync_PreCancelledToken_ThrowsWithoutSampling()
    {
        var source = new FakeCoverageSource(
            noDataValue: float.NaN,
            fields: new Dictionary<string, float[,]>
            {
                ["depth"] = new float[,] { { 5f } }
            });
        var catalogue = new FakeCoveragePortrayalCatalogue(DepthColorScheme);
        var pipeline = new CoveragePipeline();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.ProcessAsync(source, catalogue, cancellationToken: cts.Token));

        Assert.False(source.WasSampled);
    }

    #region Fakes

    private sealed class FakeCoverageSource : ICoverageSource
    {
        private readonly Dictionary<string, float[]> _fields;
        private readonly int _rows;
        private readonly int _cols;
        private readonly float _noDataValue;
        private readonly double _originLat;
        private readonly double _originLon;
        private readonly double _spacingLat;
        private readonly double _spacingLon;
        private readonly string _productSpec;
        private readonly string _horizontalCRS;
        private readonly string _verticalDatum;

        public bool WasSampled { get; private set; }

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
            var first = fields.Values.First();
            _rows = first.GetLength(0);
            _cols = first.GetLength(1);
            _fields = new Dictionary<string, float[]>();
            foreach (var (name, arr) in fields)
            {
                var flat = new float[_rows * _cols];
                for (int r = 0; r < _rows; r++)
                    for (int c = 0; c < _cols; c++)
                        flat[r * _cols + c] = arr[r, c];
                _fields[name] = flat;
            }
            _originLat = originLatitude;
            _originLon = originLongitude;
            _spacingLat = spacingLat;
            _spacingLon = spacingLon;
            _productSpec = productSpec;
            _horizontalCRS = horizontalCRS;
            _verticalDatum = verticalDatum;
        }

        private (int Rows, int Cols) GridSize => (_rows, _cols);

        public CoverageMetadata Metadata
        {
            get
            {
                var (rows, cols) = GridSize;
                return new CoverageMetadata
                {
                    Spec = new SpecRef(_productSpec, default),
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

        public SampledCoverage Sample(GridRegion region, CancellationToken cancellationToken = default)
        {
            WasSampled = true;
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

        public SpecRef Spec => new("S-102", default);
        public string Edition => "1.0";
        public ColorPalette ActivePalette => ColorPalette.Default;
        public void SwitchPalette(PaletteType type) { }
        public ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public MarinerSettings? LastSettings { get; private set; }

        public CoverageColorScheme? ResolveColorScheme(MarinerSettings settings)
        {
            LastSettings = settings;
            return _colorScheme;
        }

        public CoverageSymbolScheme? ResolveSymbolScheme(MarinerSettings settings) => _symbolScheme;

        public IReadOnlyList<ContourStyle> Contours => _contours;
    }

    #endregion
}
