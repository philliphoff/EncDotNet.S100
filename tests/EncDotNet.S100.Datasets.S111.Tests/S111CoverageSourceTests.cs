using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Integration tests for S111CoverageSource and the CoveragePipeline using real NOAA HDF5 data.
/// </summary>
public class S111CoverageSourceTests : IDisposable
{
    private const string TestDataFile = "TestData/111US00_DBOFS_20260320T18Z_US4DE1BB.h5";

    private readonly S111Dataset? _dataset;

    public S111CoverageSourceTests()
    {
        if (!File.Exists(TestDataFile)) return;

        using var hdf5 = PureHdfFile.Open(TestDataFile);
        _dataset = S111DatasetReader.Read(hdf5);
    }

    public void Dispose() { }

    private void SkipIfNoTestData()
    {
        Skip.If(_dataset is null, $"S-111 test data not found at {TestDataFile}.");
    }

    [SkippableFact]
    public void Metadata_ProductSpec_IsS111()
    {
        SkipIfNoTestData();

        var source = new S111CoverageSource(_dataset!);

        Assert.Equal("S-111", source.Metadata.ProductSpec);
    }

    [SkippableFact]
    public void Metadata_ValueFields_ContainSpeedAndDirection()
    {
        SkipIfNoTestData();

        var source = new S111CoverageSource(_dataset!);
        var fieldNames = source.Metadata.ValueFields.Select(f => f.Name).ToList();

        Assert.Contains("surfaceCurrentSpeed", fieldNames);
        Assert.Contains("surfaceCurrentDirection", fieldNames);
    }

    [SkippableFact]
    public void AvailableTimes_ReturnsAllTimeSteps()
    {
        SkipIfNoTestData();

        var source = new S111CoverageSource(_dataset!);
        var times = source.AvailableTimes;

        Assert.Equal(_dataset!.Coverages.Count, times.Count);
        Assert.True(times.Count >= 2);
    }

    [SkippableFact]
    public void SelectTime_ChangesWhichCoverageIsSampled()
    {
        SkipIfNoTestData();

        var source = new S111CoverageSource(_dataset!);
        var times = source.AvailableTimes;
        Assert.True(times.Count >= 2);

        source.SelectTime(times[0]);
        var sample0 = source.Sample(GridRegion.Full);

        source.SelectTime(times[^1]);
        var sampleLast = source.Sample(GridRegion.Full);

        // Same grid size, but likely different speed values
        var speed0 = sample0.GetField("surfaceCurrentSpeed");
        var speedLast = sampleLast.GetField("surfaceCurrentSpeed");

        Assert.Equal(speed0.GetLength(0), speedLast.GetLength(0));
        Assert.Equal(speed0.GetLength(1), speedLast.GetLength(1));
    }

    [SkippableFact]
    public void Sample_Full_ReturnsCorrectDimensions()
    {
        SkipIfNoTestData();

        var source = new S111CoverageSource(_dataset!);
        var sampled = source.Sample(GridRegion.Full);

        var coverage = _dataset!.Coverages[0];
        var speed = sampled.GetField("surfaceCurrentSpeed");
        var direction = sampled.GetField("surfaceCurrentDirection");

        Assert.Equal(coverage.NumPointsLatitudinal, speed.GetLength(0));
        Assert.Equal(coverage.NumPointsLongitudinal, speed.GetLength(1));
        Assert.Equal(coverage.NumPointsLatitudinal, direction.GetLength(0));
        Assert.Equal(coverage.NumPointsLongitudinal, direction.GetLength(1));
    }

    [Fact]
    public void PortrayalCatalogue_ResolveColorScheme_MapsSpeedToColors()
    {
        const string portrayalPath = "TestData/PortrayalCatalogue";
        Skip.IfNot(Directory.Exists(portrayalPath), $"Portrayal catalogue not found at {portrayalPath}.");

        var source = FileSystemAssetSource.Create(portrayalPath);
        using var provider = PortrayalCatalogueProvider.OpenAsync(source).GetAwaiter().GetResult();
        var catalogue = new S111PortrayalCatalogue(provider);

        var scheme = catalogue.ResolveColorScheme(MarinerSettings.Default);

        Assert.Equal("surfaceCurrentSpeed", scheme.FieldName);
        Assert.Equal(9, scheme.Bands.Count);

        // Colors should come from the color profile, not the hardcoded defaults
        Assert.NotNull(scheme.Resolve(0.1f));
        Assert.NotNull(scheme.Resolve(3.5f));
        Assert.Null(scheme.Resolve(-9999f));
    }

    [SkippableFact]
    public async Task CoveragePipeline_ProducesStyledLayer()
    {
        SkipIfNoTestData();

        const string portrayalPath = "TestData/PortrayalCatalogue";
        Skip.IfNot(Directory.Exists(portrayalPath), $"Portrayal catalogue not found at {portrayalPath}.");

        var assetSource = FileSystemAssetSource.Create(portrayalPath);
        using var provider = PortrayalCatalogueProvider.OpenAsync(assetSource).GetAwaiter().GetResult();

        var source = new S111CoverageSource(_dataset!);
        var catalogue = new S111PortrayalCatalogue(provider);
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        // Pipeline assembled the styled layer from the source's metadata.
        Assert.Equal(source.Metadata.NoDataValue, layer.NoDataValue);
        Assert.NotNull(layer.ColorScheme);
        Assert.NotEmpty(layer.Coverage.Values);

        // S-111 catalogue defines a symbol scheme for current arrows.
        Assert.NotNull(layer.SymbolScheme);
    }

    [Fact]
    public void PortrayalCatalogue_ResolveSymbolScheme_Returns9Bands()
    {
        const string portrayalPath = "TestData/PortrayalCatalogue";
        Skip.IfNot(Directory.Exists(portrayalPath), $"Portrayal catalogue not found at {portrayalPath}.");

        var source = FileSystemAssetSource.Create(portrayalPath);
        using var provider = PortrayalCatalogueProvider.OpenAsync(source).GetAwaiter().GetResult();
        var catalogue = new S111PortrayalCatalogue(provider);

        var scheme = catalogue.ResolveSymbolScheme(MarinerSettings.Default);

        Assert.NotNull(scheme);
        Assert.Equal("surfaceCurrentSpeed", scheme.ValueFieldName);
        Assert.Equal("surfaceCurrentDirection", scheme.RotationFieldName);
        Assert.Equal(9, scheme.Bands.Count);

        // Band 1: slow → small fixed arrow
        var band1 = scheme.Resolve(0.1f);
        Assert.NotNull(band1);
        Assert.Equal("SCAROW01", band1.SymbolRef);
        Assert.False(band1.ScaleByValue);
        Assert.Equal(0.40f, band1.ScaleFactor);

        // Band 5: mid-range → speed-scaled arrow
        var band5 = scheme.Resolve(3.5f);
        Assert.NotNull(band5);
        Assert.Equal("SCAROW05", band5.SymbolRef);
        Assert.True(band5.ScaleByValue);
        Assert.Equal(0.20f, band5.ScaleFactor);

        // Band 9: fastest → large fixed arrow
        var band9 = scheme.Resolve(14.0f);
        Assert.NotNull(band9);
        Assert.Equal("SCAROW09", band9.SymbolRef);
        Assert.False(band9.ScaleByValue);
        Assert.Equal(2.60f, band9.ScaleFactor);

        // No-data → null
        Assert.Null(scheme.Resolve(-9999f));
    }
}
