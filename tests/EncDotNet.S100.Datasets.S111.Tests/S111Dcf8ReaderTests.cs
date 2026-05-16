using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Tests for the S-111 dcf8 (time series at fixed stations) reader path
/// (S-111 Edition 2.0.0 §10.2.3 / §10.2.7).
/// </summary>
public class S111Dcf8ReaderTests
{
    private static S111Dcf8FixtureBuilder.Station<T> Station<T>(
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
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.3f, SurfaceCurrentDirection = 45f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 50f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.8f, SurfaceCurrentDirection = 55f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.6f, SurfaceCurrentDirection = 60f },
                }),
                Station("B", 51.6f, -0.2f, new[]
                {
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 1.0f, SurfaceCurrentDirection = 90f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 1.2f, SurfaceCurrentDirection = 95f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 1.1f, SurfaceCurrentDirection = 100f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.9f, SurfaceCurrentDirection = 105f },
                }),
                Station("C", 51.7f, -0.3f, new[]
                {
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.1f, SurfaceCurrentDirection = 180f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.2f, SurfaceCurrentDirection = 180f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.3f, SurfaceCurrentDirection = 180f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.4f, SurfaceCurrentDirection = 180f },
                }),
            };
            S111Dcf8FixtureBuilder.WriteFile(path, stations);

            using var file = PureHdfFile.Open(path);
            var any = S111DatasetReader.ReadAny(file);

            var stationSeries = Assert.IsType<S111DatasetData.StationSeries>(any);
            var model = stationSeries.Dataset;

            Assert.Equal(8, model.DataCodingFormat);
            Assert.Equal(4326, model.HorizontalCRS);
            Assert.Equal(3, model.Stations.Count);

            Assert.Equal("A", model.Stations[0].Identifier);
            Assert.Equal(51.5, model.Stations[0].Latitude, precision: 4);
            Assert.Equal(-0.1, model.Stations[0].Longitude, precision: 4);
            Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), model.Stations[0].StartTime);
            Assert.Equal(new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc), model.Stations[0].EndTime);
            Assert.Equal(TimeSpan.FromHours(1), model.Stations[0].TimeRecordInterval);
            Assert.Equal(4, model.Stations[0].NumberOfTimes);
            Assert.Equal(0.5f, model.Stations[0].SpeedsMetresPerSecond[1]);
            Assert.Equal(50f, model.Stations[0].DirectionsDegreesTrue[1]);

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
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 90f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 90f },
                }),
            };
            S111Dcf8FixtureBuilder.WriteFile(path, stations, includePositioning: false);

            using var file = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetSchemaException>(() => S111DatasetReader.ReadAny(file));

            Assert.Equal("S-111", ex.Product);
            Assert.Equal("Positioning/geometryValues", ex.AttributeOrDataset);
            Assert.Contains("§10.2.3", ex.SpecReference ?? "");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadAny_SpecMemberNames_RoundTrips()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var stations = new[]
            {
                Station("A", 51.5f, -0.1f, new[]
                {
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.75f, SurfaceCurrentDirection = 270f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.85f, SurfaceCurrentDirection = 275f },
                }),
            };
            S111Dcf8FixtureBuilder.WriteFile(path, stations);

            using var file = PureHdfFile.Open(path);
            var model = ((S111DatasetData.StationSeries)S111DatasetReader.ReadAny(file)).Dataset;

            Assert.Single(model.Stations);
            Assert.Equal(0.75f, model.Stations[0].SpeedsMetresPerSecond[0]);
            Assert.Equal(270f, model.Stations[0].DirectionsDegreesTrue[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadAny_LegacyShortMemberNames_RoundTrips()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var stations = new[]
            {
                Station("A", 50.75f, -1.5f, new[]
                {
                    new S111Dcf8FixtureBuilder.LegacyValueRow { Speed = 1.1f, Direction = 30f },
                    new S111Dcf8FixtureBuilder.LegacyValueRow { Speed = 1.3f, Direction = 35f },
                }),
                Station("B", 50.80f, -1.4f, new[]
                {
                    new S111Dcf8FixtureBuilder.LegacyValueRow { Speed = 0.9f, Direction = 200f },
                    new S111Dcf8FixtureBuilder.LegacyValueRow { Speed = 0.7f, Direction = 205f },
                }),
            };
            S111Dcf8FixtureBuilder.WriteFile(
                path, stations,
                useShortGeometryNames: true,
                positioningUnderInstance: true);

            using var file = PureHdfFile.Open(path);
            var model = ((S111DatasetData.StationSeries)S111DatasetReader.ReadAny(file)).Dataset;

            Assert.Equal(2, model.Stations.Count);
            Assert.Equal("A", model.Stations[0].Identifier);
            Assert.Equal(50.75, model.Stations[0].Latitude, 3);
            Assert.Equal(-1.5, model.Stations[0].Longitude, 3);
            Assert.Equal(1.1f, model.Stations[0].SpeedsMetresPerSecond[0]);
            Assert.Equal(30f, model.Stations[0].DirectionsDegreesTrue[0]);
            Assert.Equal("B", model.Stations[1].Identifier);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadAny_EmptyStations_ReturnsEmptyList()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S111Dcf8FixtureBuilder.WriteEmptyFile(path);

            using var file = PureHdfFile.Open(path);
            var any = S111DatasetReader.ReadAny(file);
            var model = ((S111DatasetData.StationSeries)any).Dataset;

            Assert.Empty(model.Stations);
            Assert.Null(model.MinTime);
            Assert.Null(model.MaxTime);
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
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 90f },
                    new S111Dcf8FixtureBuilder.SpecValueRow { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 90f },
                }),
            };
            S111Dcf8FixtureBuilder.WriteFile(path, stations);

            using var file = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetNotSupportedException>(() => S111DatasetReader.Read(file));

            Assert.Equal("S-111", ex.Product);
            Assert.Contains("8", ex.Feature);
            Assert.Contains("time series", ex.Feature);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SurfaceCurrentStation_NearestTimeIndex_ClampsToBounds()
    {
        var s = new SurfaceCurrentStation
        {
            Identifier = "X",
            Latitude = 0, Longitude = 0,
            StartTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc),
            TimeRecordInterval = TimeSpan.FromHours(1),
            NumberOfTimes = 4,
            SpeedsMetresPerSecond = new[] { 0f, 1f, 2f, 3f },
            DirectionsDegreesTrue = new[] { 0f, 0f, 0f, 0f },
        };

        Assert.Equal(0, s.NearestTimeIndex(new DateTime(2023, 12, 31, 23, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(0, s.NearestTimeIndex(new DateTime(2024, 1, 1, 0, 20, 0, DateTimeKind.Utc)));
        Assert.Equal(1, s.NearestTimeIndex(new DateTime(2024, 1, 1, 0, 40, 0, DateTimeKind.Utc)));
        Assert.Equal(3, s.NearestTimeIndex(new DateTime(2024, 1, 1, 5, 0, 0, DateTimeKind.Utc)));
    }
}
