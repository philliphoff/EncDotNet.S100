using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Integration tests for S111DatasetReader using real NOAA S-111 HDF5 data.
/// Test file: Delaware Bay (DBOFS) dcf2 regional, 410×448 grid, 48 hourly time steps.
/// </summary>
public class S111DatasetReaderTests : IDisposable
{
    // Point this at a local dcf2 regional file from the NOAA S-111 public dataset.
    private const string TestDataDir = "~/Downloads/aws/noaa-s111-pds/ed1.0.1/model_forecast_guidance/dbofs";

    private readonly string? _testFile;
    private readonly PureHdfFile? _hdf5;

    public S111DatasetReaderTests()
    {
        // Find the first available dcf2 regional file.
        var expanded = TestDataDir.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var dcf2Dir = Directory.EnumerateDirectories(expanded, "dcf2", SearchOption.AllDirectories).FirstOrDefault();

        if (dcf2Dir is not null)
        {
            var regionalDir = Path.Combine(dcf2Dir, "regional");
            _testFile = Directory.EnumerateFiles(regionalDir, "*.h5").FirstOrDefault();
        }

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
        Skip.If(_hdf5 is null, "No S-111 test data found. Set TestDataDir to a NOAA S-111 dataset path.");
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
