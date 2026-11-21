using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S111.Writing;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Tests;

namespace EncDotNet.S100.Datasets.S111.Tests.Writing;

/// <summary>
/// Tidal current predictions written as S-111 station series (#1455) read
/// back through <see cref="S111DatasetReader"/> unchanged, at NOAA's
/// Deception Pass station (PUG1701, bin 33) and the subordinate station
/// Golden Gate Bridge, 0.8 mile east of (PCT0281) from the T6 fixtures.
/// </summary>
public sealed class S111CurrentPredictionsTests : IDisposable
{
    private const string DeceptionPass = "PUG1701_33";
    private static readonly GeoPosition DeceptionPassPosition = new(48.40619, -122.64312);
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Issued = new(2025, 12, 31, 18, 30, 15, DateTimeKind.Utc);

    private readonly string _directory = Directory.CreateTempSubdirectory("s111-predictions-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void DeceptionPass_RoundTripsThroughTheReader()
    {
        var predictor = new CurrentPredictor(NoaaCurrentFixture.Station(DeceptionPass));
        var step = TimeSpan.FromMinutes(6);
        var end = Start.AddDays(2);
        var dataset = S111CurrentPredictions.CreateDataset(
            predictor, DeceptionPass, "Deception Pass (Narrows)", DeceptionPassPosition, 5.49, Start, end, step, Issued);

        var read = WriteAndRead(dataset, S111CurrentPredictions.FileName(DeceptionPass));

        Assert.Equal(8, read.DataCodingFormat);
        Assert.Equal(4326, read.HorizontalCRS);
        Assert.Equal("INT.IHO.S-111.2.0", read.DeclaredProductSpecification);
        Assert.Equal("20251231", read.IssueDate);
        Assert.Equal("183015Z", read.IssueTime);
        Assert.Equal(3, read.TypeOfCurrentData);
        Assert.Equal(-5.49f, read.SurfaceCurrentDepth);
        Assert.Equal("Deception Pass (Narrows) (predicted)", read.GeographicIdentifier);
        Assert.True(S111CurrentPredictions.IsPrediction(read));
        Assert.Contains("PUG1701_33 Deception Pass (Narrows)", read.MethodCurrentsProduct, StringComparison.Ordinal);
        Assert.Contains("not for navigation", read.MethodCurrentsProduct, StringComparison.Ordinal);

        var station = Assert.Single(read.Stations);
        var written = dataset.Stations[0];
        Assert.Equal(DeceptionPass, station.Identifier);
        Assert.Equal("Deception Pass (Narrows)", station.Name);
        Assert.Equal(DeceptionPassPosition.Latitude, station.Latitude);
        Assert.Equal(DeceptionPassPosition.Longitude, station.Longitude);
        Assert.Equal(Start, station.StartTime);
        Assert.Equal(end, station.EndTime);
        Assert.Equal(step, station.TimeRecordInterval);
        Assert.Equal(481, station.NumberOfTimes);
        Assert.Equal(written.SpeedsKnots, station.SpeedsKnots);
        Assert.Equal(written.DirectionsDegreesTrue, station.DirectionsDegreesTrue);
    }

    [Fact]
    public void DeceptionPass_SpeedsAndDirectionsMatchNoaa()
    {
        var predictor = new CurrentPredictor(NoaaCurrentFixture.Station(DeceptionPass));
        var (flood, ebb) = NoaaCurrentFixture.Directions(DeceptionPass);
        var dataset = S111CurrentPredictions.CreateDataset(
            predictor, DeceptionPass, "Deception Pass (Narrows)", DeceptionPassPosition, 5.49, Start, Start.AddDays(1), TimeSpan.FromMinutes(6), Issued);

        var station = Assert.Single(WriteAndRead(dataset, "deception.h5").Stations);
        var noaa = NoaaCurrentFixture.JanuarySixMinute(DeceptionPass);
        for (int index = 0; index < station.NumberOfTimes; index++)
        {
            var knots = Math.Abs(noaa[index]) / TidalCurrent.MetersPerSecondPerKnot;
            Assert.Equal(knots, station.SpeedsKnots[index], 0.01);
            if (Math.Abs(noaa[index]) > 0.01)
            {
                Assert.Equal(noaa[index] > 0 ? flood : ebb, station.DirectionsDegreesTrue[index]);
            }
        }
    }

    [Fact]
    public void Subordinate_WritesAStationWithNoDepth()
    {
        var (flood, ebb) = NoaaCurrentFixture.Directions("PCT0281_1");
        var predictor = new SubordinateCurrentPredictor(
            new CurrentPredictor(NoaaCurrentFixture.Station("SFB1202_17")),
            NoaaCurrentFixture.SubordinateOffsets("PCT0281_1"),
            flood,
            ebb);
        var dataset = S111CurrentPredictions.CreateDataset(
            predictor, "PCT0281_1", "Golden Gate Bridge, 0.8 mile east of", new GeoPosition(37.82167, -122.46167), null,
            Start, Start.AddHours(12), TimeSpan.FromMinutes(30), Issued);

        var read = WriteAndRead(dataset, S111CurrentPredictions.FileName("PCT0281_1"));
        Assert.Null(read.SurfaceCurrentDepth);
        var station = Assert.Single(read.Stations);
        Assert.Equal(25, station.NumberOfTimes);
        Assert.All(station.DirectionsDegreesTrue, d => Assert.True(d == (float)flood || d == (float)ebb));
        Assert.Equal(dataset.Stations[0].SpeedsKnots, station.SpeedsKnots);
    }

    [Fact]
    public void FileName_UsesTheDistinctProducerCode()
    {
        Assert.Equal("111ZZ01PUG170133.h5", S111CurrentPredictions.FileName(DeceptionPass));
    }

    [Fact]
    public void CreateDataset_RefusesLocalTimesBadStepsAndNegativeDepths()
    {
        var predictor = new CurrentPredictor(NoaaCurrentFixture.Station(DeceptionPass));
        var local = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => S111CurrentPredictions.CreateDataset(
            predictor, DeceptionPass, "x", DeceptionPassPosition, null, local, local.AddHours(1), TimeSpan.FromMinutes(6), Issued));
        Assert.Throws<ArgumentException>(() => S111CurrentPredictions.CreateDataset(
            predictor, DeceptionPass, "x", DeceptionPassPosition, null, Start, Start.AddHours(1), TimeSpan.FromMilliseconds(1500), Issued));
        Assert.Throws<ArgumentException>(() => S111CurrentPredictions.CreateDataset(
            predictor, DeceptionPass, "x", DeceptionPassPosition, null, Start, Start.AddHours(-1), TimeSpan.FromMinutes(6), Issued));
        Assert.Throws<ArgumentException>(() => S111CurrentPredictions.CreateDataset(
            predictor, DeceptionPass, "x", DeceptionPassPosition, -2, Start, Start.AddHours(1), TimeSpan.FromMinutes(6), Issued));
    }

    [Fact]
    public void Encode_RefusesAnInvalidDataset()
    {
        var predictor = new CurrentPredictor(NoaaCurrentFixture.Station(DeceptionPass));
        var dataset = S111CurrentPredictions.CreateDataset(
            predictor, DeceptionPass, "x", DeceptionPassPosition, null, Start, Start.AddHours(1), TimeSpan.FromMinutes(6), Issued);

        Assert.Throws<ArgumentException>(() => S111StationSeriesWriter.Encode(new S111StationSeriesDataset
        {
            DataCodingFormat = 2,
            IssueDate = "20260101",
            Stations = dataset.Stations,
        }));
        Assert.Throws<ArgumentException>(() => S111StationSeriesWriter.Encode(new S111StationSeriesDataset
        {
            IssueDate = "20260101",
            TypeOfCurrentData = 9,
            Stations = dataset.Stations,
        }));
        Assert.Throws<ArgumentException>(() => S111StationSeriesWriter.Encode(new S111StationSeriesDataset
        {
            IssueDate = "20260101",
            Stations =
            [
                new SurfaceCurrentStation
                {
                    Identifier = "bad",
                    Latitude = 0,
                    Longitude = 0,
                    StartTime = Start,
                    EndTime = Start,
                    TimeRecordInterval = TimeSpan.Zero,
                    NumberOfTimes = 1,
                    SpeedsKnots = [1],
                    DirectionsDegreesTrue = [360],
                },
            ],
        }));
    }

    private S111StationSeriesDataset WriteAndRead(S111StationSeriesDataset dataset, string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        PureHdfWriter.Write(S111StationSeriesWriter.Encode(dataset), path);
        using var file = PureHdfFile.Open(path);
        var data = Assert.IsType<S111DatasetData.StationSeries>(S111DatasetReader.ReadAny(file));
        return data.Dataset;
    }
}
