using EncDotNet.S100.Datasets.S104.Writing;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S104.Tests.Writing;

/// <summary>
/// <see cref="S104StationSeriesWriter"/> writes data coding format 8 that
/// <see cref="S104DatasetReader"/> reads back, and refuses what S-104 can't carry.
/// </summary>
public sealed class S104StationSeriesWriterTests : IDisposable
{
    private static readonly DateTime Start = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _directory = Directory.CreateTempSubdirectory("s104-writer-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void TwoStations_WithUncertainty_RoundTrip()
    {
        var dataset = Dataset(
            Station("A", 50.1, -1.2, TimeSpan.FromHours(1), [1.0f, 1.5f, 2.25f], [2, 2, 3], [0.1f, 0.2f, 0.3f]),
            Station("B", 50.4, -0.8, TimeSpan.FromMinutes(15), [-0.5f, -0.25f], [0, 1], null));

        var read = WriteAndRead(dataset);

        Assert.Equal(2, read.Stations.Count);
        Assert.Null(read.DataDynamicity);
        Assert.Null(read.TrendInterval);
        Assert.Equal(23, read.VerticalDatum);
        var a = read.Stations[0];
        Assert.Equal("A", a.Identifier);
        Assert.Equal("Station A", a.Name);
        Assert.Equal(50.1, a.Latitude);
        Assert.Equal(-1.2, a.Longitude);
        Assert.Equal([1.0f, 1.5f, 2.25f], a.Heights);
        Assert.Equal([2, 2, 3], a.Trends);
        Assert.Equal([0.1f, 0.2f, 0.3f], Assert.IsType<float[]>(a.Uncertainties));
        Assert.Equal(TimeSpan.FromHours(1), a.TimeRecordInterval);
        var b = read.Stations[1];
        Assert.Equal("B", b.Identifier);
        Assert.Equal(TimeSpan.FromMinutes(15), b.TimeRecordInterval);
        Assert.Equal(Start.AddMinutes(15), b.EndTime);
        Assert.Equal([0, 1], b.Trends);
        // One values compound per instance: B's uncertainty is the fill value.
        Assert.All(Assert.IsType<float[]>(b.Uncertainties), value => Assert.Equal(WaterLevelCoverage.UncertaintyFillValue, value));
    }

    [Fact]
    public void Encode_DeclaresTheFormatAndDatumAttributes()
    {
        var root = S104StationSeriesWriter.Encode(Dataset(
            Station("A", 50.1, -1.2, TimeSpan.FromHours(1), [1.0f, 3.0f], [2, 2], null),
            Station("B", 50.4, -0.8, TimeSpan.FromHours(1), [-0.5f, -9999.0f], [1, 0], null)));

        Assert.Equal(23, root.Attributes["verticalDatum"]);
        Assert.Equal(6499, root.Attributes["verticalCS"]);
        Assert.Equal(-1.2f, root.Attributes["westBoundLongitude"]);
        Assert.Equal(50.4f, root.Attributes["northBoundLatitude"]);
        var waterLevel = root.Groups["WaterLevel"];
        Assert.Equal(8, Convert.ToInt32(waterLevel.Attributes["dataCodingFormat"]));
        Assert.Equal(-0.5f, waterLevel.Attributes["minDatasetHeight"]);
        Assert.Equal(3.0f, waterLevel.Attributes["maxDatasetHeight"]);
        var instance = waterLevel.Groups["WaterLevel.01"];
        Assert.Equal(2u, instance.Attributes["numberOfStations"]);
        Assert.Equal((ushort)3600, instance.Attributes["timeRecordInterval"]);
        Assert.Equal("20260301T120000Z", instance.Attributes["dateTimeOfFirstRecord"]);
        Assert.Equal("20260301T130000Z", instance.Attributes["dateTimeOfLastRecord"]);
        Assert.Equal(["Group_001", "Group_002", "Positioning"], instance.Groups.Keys);
        Assert.Equal(["featureCode", "WaterLevel"], root.Groups["Group_F"].Datasets.Keys);
    }

    [Theory]
    [InlineData(47.6026, -122.3393)]
    [InlineData(-33.8568, 151.2153)]
    public void Encode_BoundsContainTheStationsDespiteSinglePrecision(double latitude, double longitude)
    {
        var root = S104StationSeriesWriter.Encode(Dataset(Station("A", latitude, longitude, TimeSpan.FromHours(1), [1f], [0], null)));

        Assert.True((float)root.Attributes["southBoundLatitude"] <= latitude);
        Assert.True((float)root.Attributes["northBoundLatitude"] >= latitude);
        Assert.True((float)root.Attributes["westBoundLongitude"] <= longitude);
        Assert.True((float)root.Attributes["eastBoundLongitude"] >= longitude);
    }

    [Fact]
    public void Encode_RefusesADatasetWithoutAnS100VerticalDatum()
    {
        var station = Station("A", 50, 0, TimeSpan.FromHours(1), [1f], [0], null);

        Assert.Throws<ArgumentException>(() => S104StationSeriesWriter.Encode(DatasetOn(null, station)));
        Assert.Throws<ArgumentException>(() => S104StationSeriesWriter.Encode(DatasetOn(42, station)));
    }

    [Fact]
    public void Encode_RefusesInconsistentStations()
    {
        Assert.Throws<ArgumentException>(() => S104StationSeriesWriter.Encode(
            Dataset(Station("A", 50, 0, TimeSpan.FromHours(1), [1f, 2f], [0], null))));
        Assert.Throws<ArgumentException>(() => S104StationSeriesWriter.Encode(
            Dataset(Station("A", 50, 0, TimeSpan.FromHours(1), [1f], [4], null))));
        Assert.Throws<ArgumentException>(() => S104StationSeriesWriter.Encode(
            Dataset(Station("A", 50, 0, TimeSpan.Zero, [1f, 2f], [0, 0], null))));
        Assert.Throws<ArgumentException>(() => S104StationSeriesWriter.Encode(
            Dataset(Station("A", 50, 0, TimeSpan.FromHours(20), [1f, 2f], [0, 0], null))));
        Assert.Throws<ArgumentException>(() => S104StationSeriesWriter.Encode(
            Dataset(Station("A", 95, 0, TimeSpan.FromHours(1), [1f], [0], null))));
        Assert.Throws<ArgumentException>(() => S104StationSeriesWriter.Encode(Dataset()));
    }

    private S104StationSeriesDataset WriteAndRead(S104StationSeriesDataset dataset)
    {
        var path = Path.Combine(_directory, "stations.h5");
        PureHdfWriter.Write(S104StationSeriesWriter.Encode(dataset), path);
        using var file = PureHdfFile.Open(path);
        return Assert.IsType<S104DatasetData.StationSeries>(S104DatasetReader.ReadAny(file)).Dataset;
    }

    private static S104StationSeriesDataset Dataset(params WaterLevelStation[] stations) => DatasetOn(23, stations);

    private static S104StationSeriesDataset DatasetOn(int? verticalDatum, params WaterLevelStation[] stations) => new()
    {
        VerticalDatum = verticalDatum,
        IssueDate = "20260301",
        Stations = stations,
    };

    private static WaterLevelStation Station(
        string id, double latitude, double longitude, TimeSpan interval, float[] heights, byte[] trends, float[]? uncertainties) => new()
        {
            Identifier = id,
            Name = $"Station {id}",
            Latitude = latitude,
            Longitude = longitude,
            StartTime = Start,
            EndTime = Start + (interval * (heights.Length - 1)),
            TimeRecordInterval = interval,
            NumberOfTimes = heights.Length,
            Heights = heights,
            Trends = trends,
            Uncertainties = uncertainties,
        };
}
