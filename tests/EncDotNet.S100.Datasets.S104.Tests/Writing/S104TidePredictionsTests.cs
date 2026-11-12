using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S104.Writing;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Tides;
using EncDotNet.S100.Tides.Tests;

namespace EncDotNet.S100.Datasets.S104.Tests.Writing;

/// <summary>
/// Harmonic predictions written as S-104 station series (#1455) read back
/// through <see cref="S104DatasetReader"/> unchanged, at NOAA's Seattle
/// station (9447130) from the T1 fixtures.
/// </summary>
public sealed class S104TidePredictionsTests : IDisposable
{
    private const string Seattle = "9447130";
    private static readonly GeoPosition SeattlePosition = new(47.6026, -122.3393);
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Issued = new(2025, 12, 31, 18, 30, 15, DateTimeKind.Utc);

    private readonly string _directory = Directory.CreateTempSubdirectory("s104-predictions-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Seattle_RoundTripsThroughTheReader()
    {
        var station = NoaaFixture.StationOnMllw(Seattle);
        var step = TimeSpan.FromMinutes(6);
        var end = Start.AddDays(2);
        var dataset = S104TidePredictions.CreateDataset(station, SeattlePosition, Start, end, step, Issued);

        var read = WriteAndRead(dataset, S104TidePredictions.FileName(station));

        Assert.Equal(8, read.DataCodingFormat);
        Assert.Equal((int)VerticalDatum.MeanLowerLowWater, read.VerticalDatum);
        Assert.Equal(4326, read.HorizontalCRS);
        Assert.Equal("INT.IHO.S-104.2.0", read.DeclaredProductSpecification);
        Assert.Equal("20251231", read.IssueDate);
        Assert.Equal("183015Z", read.IssueTime);
        Assert.Equal(2, read.DataDynamicity);
        Assert.Equal(60, read.TrendInterval);
        Assert.Equal(0.2, read.WaterLevelTrendThreshold!.Value, 6);
        Assert.Equal("Seattle (predicted)", read.GeographicIdentifier);
        Assert.True(S104TidePredictions.IsPrediction(read));
        Assert.Contains("9447130 Seattle", read.MethodWaterLevelProduct, StringComparison.Ordinal);
        Assert.Contains("not for navigation", read.MethodWaterLevelProduct, StringComparison.Ordinal);

        var readStation = Assert.Single(read.Stations);
        var written = dataset.Stations[0];
        Assert.Equal(Seattle, readStation.Identifier);
        Assert.Equal("Seattle", readStation.Name);
        Assert.Equal(SeattlePosition.Latitude, readStation.Latitude);
        Assert.Equal(SeattlePosition.Longitude, readStation.Longitude);
        Assert.Equal(Start, readStation.StartTime);
        Assert.Equal(end, readStation.EndTime);
        Assert.Equal(step, readStation.TimeRecordInterval);
        Assert.Equal(481, readStation.NumberOfTimes);
        Assert.Equal(written.Heights, readStation.Heights);
        Assert.Equal(written.Trends, readStation.Trends);
        Assert.Null(readStation.Uncertainties);
        Assert.Equal(end, readStation.TimeAt(480));
    }

    [Fact]
    public void Seattle_HeightsArePredictorHeightsOnMllw_AndMatchNoaa()
    {
        var station = NoaaFixture.StationOnMllw(Seattle);
        var predictor = new TidePredictor(station);
        var dataset = S104TidePredictions.CreateDataset(
            station, SeattlePosition, Start, Start.AddDays(1), TimeSpan.FromMinutes(6), Issued);

        var read = Assert.Single(WriteAndRead(dataset, "seattle.h5").Stations);

        var noaa = NoaaFixture.MllwJanuary(Seattle).Take(read.NumberOfTimes).ToList();
        Assert.Equal(read.NumberOfTimes, noaa.Count);
        for (int index = 0; index < read.NumberOfTimes; index++)
        {
            Assert.Equal(noaa[index].Time, read.TimeAt(index));
            Assert.Equal((float)predictor.HeightAt(read.TimeAt(index)), read.Heights[index], 4);
            Assert.Equal(noaa[index].Height, read.Heights[index], 0.01);
        }
    }

    [Fact]
    public void Trends_FollowTheRiseOverAnHour()
    {
        var station = NoaaFixture.StationOnMllw(Seattle);
        var predictor = new TidePredictor(station);
        var dataset = S104TidePredictions.CreateDataset(
            station, SeattlePosition, Start, Start.AddDays(1), TimeSpan.FromMinutes(30), Issued);

        var written = dataset.Stations[0];
        Assert.Contains((byte)1, written.Trends);
        Assert.Contains((byte)2, written.Trends);
        Assert.Contains((byte)3, written.Trends);
        for (int index = 0; index < written.NumberOfTimes; index++)
        {
            var rise = predictor.RateAt(written.TimeAt(index));
            byte expected = Math.Abs(rise) < 0.2 ? (byte)3 : rise > 0 ? (byte)2 : (byte)1;
            Assert.Equal(expected, written.Trends[index]);
        }
    }

    [Fact]
    public void FileName_UsesTheDistinctProducerCode()
    {
        var station = NoaaFixture.StationOnMllw(Seattle);

        Assert.Equal("104ZZ019447130.h5", S104TidePredictions.FileName(station));
        Assert.NotEqual("ZZ00", S104TidePredictions.ProducerCode);
    }

    [Theory]
    [InlineData("STND")]
    [InlineData("MTL")]
    [InlineData("DTL")]
    [InlineData("NAVD88")]
    [InlineData("12")]
    public void CreateDataset_RefusesADatumWithNoS100Code(string datum)
    {
        var station = NoaaFixture.StationOnMllw(Seattle).WithDatum(new TideDatum(datum, 1.0));

        var error = Assert.Throws<ArgumentException>(() => S104TidePredictions.CreateDataset(
            station, SeattlePosition, Start, Start.AddHours(1), TimeSpan.FromMinutes(6), Issued));

        Assert.Contains($"'{datum}'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MLLW", VerticalDatum.MeanLowerLowWater)]
    [InlineData("mllw", VerticalDatum.MeanLowerLowWater)]
    [InlineData("MSL", VerticalDatum.MeanSeaLevel)]
    [InlineData("MLW", VerticalDatum.MeanLowWater)]
    [InlineData("MHHW", VerticalDatum.MeanHigherHighWater)]
    [InlineData("LAT", VerticalDatum.LowestAstronomicalTide)]
    [InlineData("LowestAstronomicalTide", VerticalDatum.LowestAstronomicalTide)]
    public void TryGetVerticalDatum_KnowsNoaaAbbreviationsAndS100Names(string name, VerticalDatum expected)
    {
        Assert.True(S104TidePredictions.TryGetVerticalDatum(new TideDatum(name, 0), out var datum));
        Assert.Equal(expected, datum);
    }

    [Fact]
    public void CreateDataset_RefusesLocalTimesAndBadSteps()
    {
        var station = NoaaFixture.StationOnMllw(Seattle);
        var local = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => S104TidePredictions.CreateDataset(
            station, SeattlePosition, local, local.AddHours(1), TimeSpan.FromMinutes(6), Issued));
        Assert.Throws<ArgumentException>(() => S104TidePredictions.CreateDataset(
            station, SeattlePosition, Start, Start.AddHours(1), TimeSpan.FromMilliseconds(1500), Issued));
        Assert.Throws<ArgumentException>(() => S104TidePredictions.CreateDataset(
            station, SeattlePosition, Start, Start.AddDays(2), TimeSpan.FromDays(1), Issued));
        Assert.Throws<ArgumentException>(() => S104TidePredictions.CreateDataset(
            station, SeattlePosition, Start, Start.AddHours(-1), TimeSpan.FromMinutes(6), Issued));
    }

    [Fact]
    public void CreateDataset_EndsOnTheLastWholeStep()
    {
        var station = NoaaFixture.StationOnMllw(Seattle);

        var dataset = S104TidePredictions.CreateDataset(
            station, SeattlePosition, Start, Start.AddMinutes(20), TimeSpan.FromMinutes(6), Issued);

        var written = Assert.Single(dataset.Stations);
        Assert.Equal(4, written.NumberOfTimes);
        Assert.Equal(Start.AddMinutes(18), written.EndTime);
    }

    private S104StationSeriesDataset WriteAndRead(S104StationSeriesDataset dataset, string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        PureHdfWriter.Write(S104StationSeriesWriter.Encode(dataset), path);
        using var file = PureHdfFile.Open(path);
        var data = Assert.IsType<S104DatasetData.StationSeries>(S104DatasetReader.ReadAny(file));
        return data.Dataset;
    }
}
