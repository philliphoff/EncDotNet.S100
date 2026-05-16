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

    private S102PortrayalCatalogue CreateCatalogue() =>
        new(_engine, _provider);

    [Fact]
    public async Task EndToEnd_ReadsHdf5_ProducesStyledLayer()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue();
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        // 31 rows × 21 columns = 651 cells in the sampled grid
        Assert.Equal(31, layer.Coverage.Metadata.NumRows);
        Assert.Equal(21, layer.Coverage.Metadata.NumColumns);
        Assert.Equal("depth", layer.ColorScheme.FieldName);
    }

    [Fact]
    public async Task EndToEnd_RealDepths_MapToIhoColors()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue();
        var mariner = new MarinerSettings
        {
            FourShades = true,
            ShallowContour = 2.0,
            SafetyContour = 30.0,
            DeepContour = 30.0,
        };

        var pipeline = new CoveragePipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue, mariner);

        // The test file has real depths 4.44–8m — all in [ShallowContour=2, SafetyContour=30) → DEPMS.
        // Walk the depth field and verify each non-fill cell maps to DEPMS via the resolved scheme.
        var depths = layer.Coverage.GetField("depth");
        int rows = depths.GetLength(0);
        int cols = depths.GetLength(1);
        int realCells = 0;
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float v = depths[r, c];
            if (v == layer.NoDataValue) continue;
            realCells++;
            Assert.Equal(DEPMS, layer.ColorScheme.Resolve(v));
        }

        Assert.True(realCells > 0, "Expected some real depth cells in the test file");
    }

    [Fact]
    public async Task EndToEnd_FillValues_AreMarkedAsNoData()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue();
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        // S-102 uses the sentinel fill value 1,000,000f (not NaN).
        Assert.Equal(S102CoverageSource.FillValue, layer.NoDataValue);

        var depths = layer.Coverage.GetField("depth");
        int fillCount = 0;
        int realCount = 0;
        for (int r = 0; r < depths.GetLength(0); r++)
        for (int c = 0; c < depths.GetLength(1); c++)
        {
            if (depths[r, c] == layer.NoDataValue) fillCount++;
            else realCount++;
        }

        Assert.True(fillCount > 0, "Expected some no-data cells in the test file");
        Assert.True(realCount > 0, "Expected some real-depth cells in the test file");
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

        var grid = layer.Coverage.Metadata;
        Assert.Equal(31, grid.NumRows);
        Assert.Equal(21, grid.NumColumns);
        Assert.Equal(16.0, grid.SpacingLatitudinal);
        Assert.Equal(16.0, grid.SpacingLongitudinal);
    }

    [Fact]
    public async Task EndToEnd_TwoShadeMode_AllShallowCellsAreDEPVS()
    {
        using var hdf5 = PureHdfFile.Open(TestDataFile);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var catalogue = CreateCatalogue();
        var mariner = new MarinerSettings
        {
            FourShades = false,
            SafetyContour = 30.0,
        };

        var pipeline = new CoveragePipeline();
        var layer = await pipeline.ProcessAsync(source, catalogue, mariner);

        // Two-shade: depths [0, 30) → DEPVS, ≥30 → DEPDW. All real values 4–8m → DEPVS.
        var depths = layer.Coverage.GetField("depth");
        int realCells = 0;
        for (int r = 0; r < depths.GetLength(0); r++)
        for (int c = 0; c < depths.GetLength(1); c++)
        {
            float v = depths[r, c];
            if (v == layer.NoDataValue) continue;
            realCells++;
            Assert.Equal(DEPVS, layer.ColorScheme.Resolve(v));
        }

        Assert.True(realCells > 0);
    }

    [Fact]
    public void S102PortrayalCatalogue_FourShades_ProducesExpectedBands()
    {
        var catalogue = CreateCatalogue();
        var mariner = new MarinerSettings
        {
            FourShades = true,
            ShallowContour = 2.0,
            SafetyContour = 10.0,
            DeepContour = 30.0,
        };

        var scheme = catalogue.ResolveColorScheme(mariner);

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
        var catalogue = CreateCatalogue();
        var mariner = new MarinerSettings
        {
            FourShades = false,
            SafetyContour = 10.0,
        };

        var scheme = catalogue.ResolveColorScheme(mariner);

        Assert.Equal("depth", scheme.FieldName);
        // 3 bands: intertidal + 2 depth bands
        Assert.Equal(3, scheme.Bands.Count);

        Assert.Equal(DEPIT, scheme.Resolve(-5f));
        Assert.Equal(DEPVS, scheme.Resolve(5f));
        Assert.Equal(DEPDW, scheme.Resolve(15f));
    }
}
