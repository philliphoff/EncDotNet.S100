using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Integration tests for the S-102 → CoveragePipeline path using real HDF5 data.
/// Test file: Lake Erie 31×21 grid (102US004MI1CI262227.h5).
///   - Depth range: 4.44m to ~8m (real values), fill value = 1000000
///   - UTM Zone 17N (EPSG:32617), grid spacing 16m
/// </summary>
public class S102CoveragePipelineIntegrationTests : IDisposable
{
    private const string TestDataFile = "TestData/102US004MI1CI262227.h5";
    private const string PortrayalCataloguePath = "TestData/PortrayalCatalogue";

    // IHO S-52 Day palette depth colours (matching BathymetryCoverage.lua)
    private const string DEPIT = "#58AF9C";
    private const string DEPVS = "#61B7FF";
    private const string DEPMS = "#82CAFF";
    private const string DEPMD = "#A7D9FB";
    private const string DEPDW = "#C9EDFF";

    private readonly MoonSharpLuaEngine _engine = new();
    private readonly PortrayalCatalogueProvider _provider;

    public S102CoveragePipelineIntegrationTests()
    {
        var source = FileSystemAssetSource.Create(PortrayalCataloguePath);
        _provider = PortrayalCatalogueProvider.OpenAsync(source).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private S102PortrayalCatalogue CreateCatalogue(bool fourShades = true) =>
        new(_engine, _provider) { FourShades = fourShades };

    [Fact]
    public async Task EndToEnd_ReadsHdf5_ProducesColoredLayer()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue();
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        // 31 rows × 21 columns = 651 cells
        Assert.Equal(651, layer.CellColors.Count);
        Assert.Equal("S-102", layer.Metadata.ProductSpec);
    }

    [Fact]
    public async Task EndToEnd_RealDepths_MapToIhoColors()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue(fourShades: true);
        var context = new NavigationContext
        {
            Viewport = new Viewport
            {
                MinLatitude = 0, MaxLatitude = 1,
                MinLongitude = 0, MaxLongitude = 1,
                WidthPixels = 100, HeightPixels = 100,
            },
            ScaleDenominator = 25_000,
            ShallowContour = 2.0,
            SafetyContour = 30.0,
            DeepContour = 30.0,
        };

        var pipeline = new CoveragePipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue, context);

        // The test file has real depths 4.44–8m, all in [ShallowContour=2, SafetyContour=30) → DEPMS
        // and fill-value cells (1000000) → null (no-data)
        var nonNull = layer.CellColors.Where(c => c is not null).ToList();
        Assert.NotEmpty(nonNull);

        // All real depth values (4.44 to ~8m) fall in DEPMS [2, 30)
        Assert.All(nonNull, color => Assert.Equal(DEPMS, color));
    }

    [Fact]
    public async Task EndToEnd_FillValues_ProduceNullColors()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue();
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        // The file has fill value = 1000000 for no-data cells.
        // The source maps NoDataValue=NaN, so fill-value cells won't match NaN.
        // However, 1000000 is outside all color bands → CoverageColorScheme.Resolve returns null.
        // Either way, no-data cells should appear null.
        var nullCount = layer.CellColors.Count(c => c is null);
        var nonNullCount = layer.CellColors.Count(c => c is not null);

        // Both should be present (the file has a mix of real and fill values)
        Assert.True(nullCount > 0, "Expected some no-data/out-of-range cells");
        Assert.True(nonNullCount > 0, "Expected some colored cells");
    }

    [Fact]
    public async Task EndToEnd_GridGeometry_MatchesHdf5Metadata()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue();
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal(31, layer.Grid.NumRows);
        Assert.Equal(21, layer.Grid.NumColumns);
        Assert.Equal(16.0, layer.Grid.SpacingLatitudinal);
        Assert.Equal(16.0, layer.Grid.SpacingLongitudinal);
    }

    [Fact]
    public async Task EndToEnd_TwoShadeMode_AllShallowCellsAreDEPVS()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue(fourShades: false);
        var context = new NavigationContext
        {
            Viewport = new Viewport
            {
                MinLatitude = 0, MaxLatitude = 1,
                MinLongitude = 0, MaxLongitude = 1,
                WidthPixels = 100, HeightPixels = 100,
            },
            ScaleDenominator = 25_000,
            SafetyContour = 30.0,
        };

        var pipeline = new CoveragePipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue, context);

        var nonNull = layer.CellColors.Where(c => c is not null).ToList();
        Assert.NotEmpty(nonNull);

        // Two-shade: depths [0, 30) → DEPVS, ≥30 → DEPDW
        // All real values 4–8m → DEPVS
        Assert.All(nonNull, color => Assert.Equal(DEPVS, color));
    }

    [Fact]
    public void S102PortrayalCatalogue_FourShades_ProducesExpectedBands()
    {
        var catalogue = CreateCatalogue(fourShades: true);
        var context = new NavigationContext
        {
            Viewport = new Viewport
            {
                MinLatitude = 0, MaxLatitude = 1,
                MinLongitude = 0, MaxLongitude = 1,
                WidthPixels = 100, HeightPixels = 100,
            },
            ScaleDenominator = 25_000,
            ShallowContour = 2.0,
            SafetyContour = 10.0,
            DeepContour = 30.0,
        };

        var scheme = catalogue.ResolveColorScheme(context);

        Assert.Equal("depth", scheme.FieldName);
        // 5 bands: intertidal + 4 depth bands
        Assert.Equal(5, scheme.Bands.Count);

        // Verify the band boundaries match the Lua logic
        Assert.Equal(DEPIT, scheme.Resolve(-5f));    // intertidal
        Assert.Equal(DEPVS, scheme.Resolve(1f));     // [0, 2)
        Assert.Equal(DEPMS, scheme.Resolve(5f));     // [2, 10)
        Assert.Equal(DEPMD, scheme.Resolve(20f));    // [10, 30)
        Assert.Equal(DEPDW, scheme.Resolve(50f));    // [30, +inf)
    }

    [Fact]
    public void S102PortrayalCatalogue_TwoShades_ProducesExpectedBands()
    {
        var catalogue = CreateCatalogue(fourShades: false);
        var context = new NavigationContext
        {
            Viewport = new Viewport
            {
                MinLatitude = 0, MaxLatitude = 1,
                MinLongitude = 0, MaxLongitude = 1,
                WidthPixels = 100, HeightPixels = 100,
            },
            ScaleDenominator = 25_000,
            SafetyContour = 10.0,
        };

        var scheme = catalogue.ResolveColorScheme(context);

        Assert.Equal("depth", scheme.FieldName);
        // 3 bands: intertidal + 2 depth bands
        Assert.Equal(3, scheme.Bands.Count);

        Assert.Equal(DEPIT, scheme.Resolve(-5f));
        Assert.Equal(DEPVS, scheme.Resolve(5f));
        Assert.Equal(DEPDW, scheme.Resolve(15f));
    }
}
