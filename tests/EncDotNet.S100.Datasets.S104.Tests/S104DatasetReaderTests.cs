using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S104.Tests;

/// <summary>
/// Integration tests for S104DatasetReader using real S-104 HDF5 data.
/// Place an S-104 .h5 file in tests/datasets/S104/ to enable these tests.
/// </summary>
public class S104DatasetReaderTests : IDisposable
{
    private const string TestDataDir = "TestData";

    private readonly PureHdfFile? _hdf5;
    private readonly string? _testFile;

    public S104DatasetReaderTests()
    {
        _testFile = Directory.Exists(TestDataDir)
            ? Directory.GetFiles(TestDataDir, "*.h5").FirstOrDefault()
            : null;

        if (_testFile is not null)
        {
            _hdf5 = PureHdfFile.Open(_testFile);
        }
    }

    public void Dispose()
    {
        _hdf5?.Dispose();
    }

    private void SkipIfNoTestData()
    {
        Skip.If(_hdf5 is null, $"S-104 test data not found in {TestDataDir}/.");
    }

    [SkippableFact]
    public void Read_RootAttributes_ParsedCorrectly()
    {
        SkipIfNoTestData();

        var dataset = S104DatasetReader.Read(_hdf5!);

        Assert.NotNull(dataset.HorizontalCRS);
        Assert.NotNull(dataset.GeographicIdentifier);
        Assert.NotNull(dataset.IssueDate);
        Assert.Equal(2, dataset.DataCodingFormat);
    }

    [SkippableFact]
    public void Read_Coverages_HasMultipleTimeSteps()
    {
        SkipIfNoTestData();

        var dataset = S104DatasetReader.Read(_hdf5!);

        Assert.NotEmpty(dataset.Coverages);
        Assert.True(dataset.Coverages.Count >= 2, $"Expected multiple time steps, got {dataset.Coverages.Count}");
    }

    [SkippableFact]
    public void Read_Coverages_TimePointsAreOrdered()
    {
        SkipIfNoTestData();

        var dataset = S104DatasetReader.Read(_hdf5!);
        var times = dataset.Coverages.Select(c => c.TimePoint).ToList();

        for (int i = 1; i < times.Count; i++)
        {
            Assert.True(times[i] > times[i - 1],
                $"Time step {i} ({times[i]}) should be after step {i - 1} ({times[i - 1]})");
        }
    }

    [SkippableFact]
    public void Read_CoverageGrid_HasExpectedDimensions()
    {
        SkipIfNoTestData();

        var dataset = S104DatasetReader.Read(_hdf5!);
        var first = dataset.Coverages[0];

        Assert.True(first.NumPointsLatitudinal > 0);
        Assert.True(first.NumPointsLongitudinal > 0);
        Assert.True(first.SpacingLatitudinal > 0);
        Assert.True(first.SpacingLongitudinal > 0);
        Assert.Equal(first.NumPointsLatitudinal * first.NumPointsLongitudinal, first.Values.Length);
    }

    [SkippableFact]
    public void Read_Values_ContainRealisticWaterLevelData()
    {
        SkipIfNoTestData();

        var dataset = S104DatasetReader.Read(_hdf5!);
        var first = dataset.Coverages[0];

        // Real water level heights should be within a reasonable range (e.g. -20m to +20m).
        var realValues = first.Values
            .Where(v => v.Height > -100f && v.Height < 100f && v.Height != S104CoverageSource.FillValue)
            .ToList();

        Assert.NotEmpty(realValues);
        Assert.All(realValues, v =>
        {
            Assert.InRange(v.Height, -20f, 20f);
        });
    }
}
