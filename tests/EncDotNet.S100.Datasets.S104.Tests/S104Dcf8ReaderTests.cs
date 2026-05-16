using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S104.Tests;

/// <summary>
/// Tests for the S-104 dcf8 (time series at fixed stations) reader path
/// (S-104 Edition 2.0.0 §10.2.3 / §10.2.7).
/// </summary>
public class S104Dcf8ReaderTests
{
    private static S104Dcf8FixtureBuilder.Station<T> Station<T>(
        string id, float lat, float lon, T[] values,
        string start = "20240101T000000Z",
        string end = "20240101T030000Z",
        long interval = 3600)
        where T : struct =>
        new()
        {
            Identifier = id,
            Latitude = lat,
            Longitude = lon,
            StartDateTime = start,
            EndDateTime = end,
            TimeRecordInterval = interval,
            Values = values,
        };

    [Fact]
    public void ReadAny_HappyPath_ThreeStationsFourTimeSteps_RoundTrips()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var stations = new[]
            {
                Station("A", 51.5f, -0.1f, new[]
                {
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 1.0f, WaterLevelTrend = 1 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 1.2f, WaterLevelTrend = 1 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 0.8f, WaterLevelTrend = 3 },
                }),
                Station("B", 51.6f, -0.2f, new[]
                {
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = -0.5f, WaterLevelTrend = 2 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 0.0f, WaterLevelTrend = 2 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 0.6f, WaterLevelTrend = 2 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 0.4f, WaterLevelTrend = 1 },
                }),
                Station("C", 51.7f, -0.3f, new[]
                {
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 0.1f, WaterLevelTrend = 0 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 0.2f, WaterLevelTrend = 0 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 0.3f, WaterLevelTrend = 0 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 0.4f, WaterLevelTrend = 0 },
                }),
            };
            S104Dcf8FixtureBuilder.WriteFile(path, stations, waterLevelTrendThreshold: 0.5);

            using var file = PureHdfFile.Open(path);
            var any = S104DatasetReader.ReadAny(file);

            var stationSeries = Assert.IsType<S104DatasetData.StationSeries>(any);
            var model = stationSeries.Dataset;

            Assert.Equal(8, model.DataCodingFormat);
            Assert.Equal(4326, model.HorizontalCRS);
            Assert.Equal(0.5, model.WaterLevelTrendThreshold);
            Assert.Equal(3, model.Stations.Count);

            Assert.Equal("A", model.Stations[0].Identifier);
            Assert.Equal(51.5, model.Stations[0].Latitude, precision: 4);
            Assert.Equal(-0.1, model.Stations[0].Longitude, precision: 4);
            Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), model.Stations[0].StartTime);
            Assert.Equal(new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc), model.Stations[0].EndTime);
            Assert.Equal(TimeSpan.FromHours(1), model.Stations[0].TimeRecordInterval);
            Assert.Equal(4, model.Stations[0].NumberOfTimes);
            Assert.Equal(1.5f, model.Stations[0].Heights[1]);
            Assert.Equal((byte)2, model.Stations[0].Trends[1]);

            Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), model.MinTime);
            Assert.Equal(new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc), model.MaxTime);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadAny_MissingPositioning_ThrowsSchemaException()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var stations = new[]
            {
                Station("A", 1f, 1f, new[]
                {
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 1f, WaterLevelTrend = 1 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 2f, WaterLevelTrend = 2 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 3f, WaterLevelTrend = 3 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 4f, WaterLevelTrend = 0 },
                }),
            };
            S104Dcf8FixtureBuilder.WriteFile(path, stations, includePositioning: false);

            using var file = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetSchemaException>(() => S104DatasetReader.ReadAny(file));

            Assert.Equal("S-104", ex.Product);
            Assert.Equal("Positioning/geometryValues", ex.AttributeOrDataset);
            Assert.Contains("§10.2.3", ex.SpecReference ?? "");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadAny_UkhoMemberNames_RoundTrips()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var stations = new[]
            {
                Station("A", 51.5f, -0.1f, new[]
                {
                    new S104Dcf8FixtureBuilder.UkhoValueRow { SurfaceHeight = 2.5f, Trend = 2 },
                    new S104Dcf8FixtureBuilder.UkhoValueRow { SurfaceHeight = 3.0f, Trend = 2 },
                    new S104Dcf8FixtureBuilder.UkhoValueRow { SurfaceHeight = 2.8f, Trend = 1 },
                    new S104Dcf8FixtureBuilder.UkhoValueRow { SurfaceHeight = 2.2f, Trend = 1 },
                }),
            };
            S104Dcf8FixtureBuilder.WriteFile(path, stations);

            using var file = PureHdfFile.Open(path);
            var any = S104DatasetReader.ReadAny(file);
            var model = ((S104DatasetData.StationSeries)any).Dataset;

            Assert.Single(model.Stations);
            Assert.Equal(2.5f, model.Stations[0].Heights[0]);
            Assert.Equal((byte)2, model.Stations[0].Trends[0]);
            Assert.Equal((byte)1, model.Stations[0].Trends[3]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadAny_F32Trend_RoundsAndClamps()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var stations = new[]
            {
                Station("A", 1f, 1f, new[]
                {
                    new S104Dcf8FixtureBuilder.UkhoValueRowF32Trend { SurfaceHeight = 1.0f, Trend = 1.6f },
                    new S104Dcf8FixtureBuilder.UkhoValueRowF32Trend { SurfaceHeight = 1.0f, Trend = 0.4f },
                    new S104Dcf8FixtureBuilder.UkhoValueRowF32Trend { SurfaceHeight = 1.0f, Trend = 3.0f },
                    new S104Dcf8FixtureBuilder.UkhoValueRowF32Trend { SurfaceHeight = 1.0f, Trend = -5.0f },
                }),
            };
            S104Dcf8FixtureBuilder.WriteFile(path, stations);

            using var file = PureHdfFile.Open(path);
            var model = ((S104DatasetData.StationSeries)S104DatasetReader.ReadAny(file)).Dataset;
            var trends = model.Stations[0].Trends;

            // 1.6 rounds to 2; 0.4 to 0; 3.0 stays 3; -5.0 clamps to 0.
            Assert.Equal((byte)2, trends[0]);
            Assert.Equal((byte)0, trends[1]);
            Assert.Equal((byte)3, trends[2]);
            Assert.Equal((byte)0, trends[3]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_OnDcf8_ThrowsNotSupportedForGriddedCallers()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var stations = new[]
            {
                Station("A", 1f, 1f, new[]
                {
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 1f, WaterLevelTrend = 1 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 1f, WaterLevelTrend = 1 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 1f, WaterLevelTrend = 1 },
                    new S104Dcf8FixtureBuilder.SpecValueRow { WaterLevelHeight = 1f, WaterLevelTrend = 1 },
                }),
            };
            S104Dcf8FixtureBuilder.WriteFile(path, stations);

            using var file = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetNotSupportedException>(() => S104DatasetReader.Read(file));

            Assert.Equal("S-104", ex.Product);
            Assert.Contains("8", ex.Feature);
            Assert.Contains("time series", ex.Feature);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WaterLevelStation_NearestTimeIndex_ClampsToBounds()
    {
        var s = new WaterLevelStation
        {
            Identifier = "X",
            Latitude = 0, Longitude = 0,
            StartTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc),
            TimeRecordInterval = TimeSpan.FromHours(1),
            NumberOfTimes = 4,
            Heights = new[] { 0f, 1f, 2f, 3f },
            Trends = new byte[] { 0, 0, 0, 0 },
        };

        Assert.Equal(0, s.NearestTimeIndex(new DateTime(2023, 12, 31, 23, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(0, s.NearestTimeIndex(new DateTime(2024, 1, 1, 0, 20, 0, DateTimeKind.Utc)));
        Assert.Equal(1, s.NearestTimeIndex(new DateTime(2024, 1, 1, 0, 40, 0, DateTimeKind.Utc)));
        Assert.Equal(3, s.NearestTimeIndex(new DateTime(2024, 1, 1, 5, 0, 0, DateTimeKind.Utc)));
    }
}
