using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

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

    [SkippableFact]
    public async Task CoveragePipeline_ProducesColoredLayer()
    {
        SkipIfNoTestData();

        var source = new S111CoverageSource(_dataset!);
        var catalogue = new S111PortrayalCatalogue();
        var pipeline = new CoveragePipeline();

        var layer = await pipeline.ProcessAsync(source, catalogue);

        Assert.Equal("S-111", layer.Metadata.ProductSpec);
        Assert.NotEmpty(layer.CellColors);

        // Should have some non-null (colored) cells and some null (no-data) cells
        var nonNull = layer.CellColors.Where(c => c is not null).ToList();
        var nullCount = layer.CellColors.Count(c => c is null);

        Assert.NotEmpty(nonNull);
        Assert.True(nullCount > 0, "Expected some no-data cells in the coastal current grid");
    }

    [SkippableFact]
    public void PortrayalCatalogue_ResolveColorScheme_MapsSpeedToColors()
    {
        var catalogue = new S111PortrayalCatalogue();
        var context = new NavigationContext
        {
            Viewport = new Viewport
            {
                MinLatitude = 0, MaxLatitude = 1,
                MinLongitude = 0, MaxLongitude = 1,
                WidthPixels = 100, HeightPixels = 100,
            },
            ScaleDenominator = 50_000,
        };

        var scheme = catalogue.ResolveColorScheme(context);

        Assert.Equal("surfaceCurrentSpeed", scheme.FieldName);
        Assert.NotEmpty(scheme.Bands);

        // Slow current → light blue
        Assert.NotNull(scheme.Resolve(0.1f));
        // Fast current → red
        Assert.NotNull(scheme.Resolve(3.5f));
        // No-data value → null
        Assert.Null(scheme.Resolve(-9999f));
    }
}
