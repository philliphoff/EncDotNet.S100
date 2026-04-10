using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Integration tests for S111DatasetReader using real NOAA S-111 HDF5 data.
/// Test file: Delaware Bay (DBOFS) dcf2 tile, 53×53 grid, 48 hourly time steps.
/// </summary>
public class S111DatasetReaderTests : IDisposable
{
    private const string TestDataFile = "TestData/111US00_DBOFS_20260320T18Z_US4DE1BB.h5";

    private readonly PureHdfFile? _hdf5;

    public S111DatasetReaderTests()
    {
        if (File.Exists(TestDataFile))
        {
            _hdf5 = PureHdfFile.Open(TestDataFile);
        }
    }

    public void Dispose()
    {
        _hdf5?.Dispose();
    }

    private void SkipIfNoTestData()
    {
        Skip.If(_hdf5 is null, $"S-111 test data not found at {TestDataFile}.");
    }

    [SkippableFact]
    public void Read_RootAttributes_ParsedCorrectly()
    {
        SkipIfNoTestData();

        var dataset = S111DatasetReader.Read(_hdf5!);

        Assert.Equal(4326, dataset.HorizontalCRS);
        Assert.NotNull(dataset.GeographicIdentifier);
        Assert.NotNull(dataset.IssueDate);
        Assert.NotNull(dataset.SurfaceCurrentDepth);
        Assert.Equal(2, dataset.DataCodingFormat);
    }

    [SkippableFact]
    public void Read_Coverages_HasMultipleTimeSteps()
    {
        SkipIfNoTestData();

        var dataset = S111DatasetReader.Read(_hdf5!);

        // DBOFS typically has 48 or 49 hourly time steps
        Assert.NotEmpty(dataset.Coverages);
        Assert.True(dataset.Coverages.Count >= 2, $"Expected multiple time steps, got {dataset.Coverages.Count}");
    }

    [SkippableFact]
    public void Read_Coverages_TimePointsAreOrdered()
    {
        SkipIfNoTestData();

        var dataset = S111DatasetReader.Read(_hdf5!);
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

        var dataset = S111DatasetReader.Read(_hdf5!);
        var first = dataset.Coverages[0];

        Assert.True(first.NumPointsLatitudinal > 0);
        Assert.True(first.NumPointsLongitudinal > 0);
        Assert.True(first.SpacingLatitudinal > 0);
        Assert.True(first.SpacingLongitudinal > 0);
        Assert.Equal(first.NumPointsLatitudinal * first.NumPointsLongitudinal, first.Values.Length);
    }

    [SkippableFact]
    public void Read_Values_ContainRealisticCurrentData()
    {
        SkipIfNoTestData();

        var dataset = S111DatasetReader.Read(_hdf5!);
        var first = dataset.Coverages[0];

        // Fill value for S-111 is -9999.
        // Real current speeds should be between 0 and ~5 knots for coastal data.
        var realValues = first.Values.Where(v => v.Speed >= 0f && v.Speed < 100f).ToList();
        Assert.NotEmpty(realValues);

        foreach (var v in realValues)
        {
            Assert.InRange(v.Speed, 0f, 10f);
            Assert.InRange(v.Direction, 0f, 360f);
        }
    }

    [SkippableFact]
    public void Read_AllCoverages_ShareGridMetadata()
    {
        SkipIfNoTestData();

        var dataset = S111DatasetReader.Read(_hdf5!);
        var first = dataset.Coverages[0];

        // In dcf2, all time steps share the same grid structure.
        foreach (var coverage in dataset.Coverages)
        {
            Assert.Equal(first.NumPointsLatitudinal, coverage.NumPointsLatitudinal);
            Assert.Equal(first.NumPointsLongitudinal, coverage.NumPointsLongitudinal);
            Assert.Equal(first.OriginLatitude, coverage.OriginLatitude);
            Assert.Equal(first.OriginLongitude, coverage.OriginLongitude);
        }
    }
}
