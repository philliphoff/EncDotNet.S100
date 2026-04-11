using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.S104.Tests;

/// <summary>
/// Integration tests for S104CoverageSource using real S-104 HDF5 data.
/// Place an S-104 .h5 file in tests/datasets/S104/ to enable these tests.
/// </summary>
public class S104CoverageSourceTests : IDisposable
{
    private const string TestDataDir = "TestData";

    private readonly S104Dataset? _dataset;

    public S104CoverageSourceTests()
    {
        var testFile = Directory.Exists(TestDataDir)
            ? Directory.GetFiles(TestDataDir, "*.h5").FirstOrDefault()
            : null;

        if (testFile is not null)
        {
            using var hdf5 = PureHdfFile.Open(testFile);
            _dataset = S104DatasetReader.Read(hdf5);
        }
    }

    public void Dispose() { }

    private void SkipIfNoTestData()
    {
        Skip.If(_dataset is null, $"S-104 test data not found in {TestDataDir}/.");
    }

    [SkippableFact]
    public void Metadata_ProductSpec_IsS104()
    {
        SkipIfNoTestData();

        var source = new S104CoverageSource(_dataset!);

        Assert.Equal("S-104", source.Metadata.ProductSpec);
    }

    [SkippableFact]
    public void Metadata_ValueFields_ContainHeightAndTrend()
    {
        SkipIfNoTestData();

        var source = new S104CoverageSource(_dataset!);
        var fieldNames = source.Metadata.ValueFields.Select(f => f.Name).ToList();

        Assert.Contains("waterLevelHeight", fieldNames);
        Assert.Contains("waterLevelTrend", fieldNames);
    }

    [SkippableFact]
    public void AvailableTimes_ReturnsAllTimeSteps()
    {
        SkipIfNoTestData();

        var source = new S104CoverageSource(_dataset!);
        var times = source.AvailableTimes;

        Assert.Equal(_dataset!.Coverages.Count, times.Count);
        Assert.True(times.Count >= 2);
    }

    [SkippableFact]
    public void SelectTime_ChangesWhichCoverageIsSampled()
    {
        SkipIfNoTestData();

        var source = new S104CoverageSource(_dataset!);
        var times = source.AvailableTimes;
        Assert.True(times.Count >= 2);

        source.SelectTime(times[0]);
        var sample0 = source.Sample(GridRegion.Full);

        source.SelectTime(times[^1]);
        var sampleLast = source.Sample(GridRegion.Full);

        // Same grid size, but likely different height values
        var height0 = sample0.GetField("waterLevelHeight");
        var heightLast = sampleLast.GetField("waterLevelHeight");

        Assert.Equal(height0.GetLength(0), heightLast.GetLength(0));
        Assert.Equal(height0.GetLength(1), heightLast.GetLength(1));
    }

    [SkippableFact]
    public void Sample_Full_ReturnsCorrectDimensions()
    {
        SkipIfNoTestData();

        var source = new S104CoverageSource(_dataset!);
        var sampled = source.Sample(GridRegion.Full);

        var coverage = _dataset!.Coverages[0];
        var height = sampled.GetField("waterLevelHeight");
        var trend = sampled.GetField("waterLevelTrend");

        Assert.Equal(coverage.NumPointsLatitudinal, height.GetLength(0));
        Assert.Equal(coverage.NumPointsLongitudinal, height.GetLength(1));
        Assert.Equal(coverage.NumPointsLatitudinal, trend.GetLength(0));
        Assert.Equal(coverage.NumPointsLongitudinal, trend.GetLength(1));
    }
}
