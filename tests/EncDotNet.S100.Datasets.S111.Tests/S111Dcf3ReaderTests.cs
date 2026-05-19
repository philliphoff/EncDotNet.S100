using System.Reflection;
using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S111.Tests;

/// <summary>
/// Tests for the S-111 dcf3 (ungeorectified grid) reader path
/// (S-100 Part 10c §10.2.1).
/// </summary>
public class S111Dcf3ReaderTests
{
    [Fact]
    public void ReadAny_Dcf3_ThreeNodesFourTimeSteps_RoundTrips()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var nodes = new[]
            {
                new S111Dcf3FixtureBuilder.Node { Latitude = 46.83f, Longitude = -71.16f },
                new S111Dcf3FixtureBuilder.Node { Latitude = 46.84f, Longitude = -71.15f },
                new S111Dcf3FixtureBuilder.Node { Latitude = 46.85f, Longitude = -71.14f },
            };

            var timeSteps = new[]
            {
                new S111Dcf3FixtureBuilder.TimeStep
                {
                    TimePoint = "20240101T000000Z",
                    Values =
                    [
                        new() { SurfaceCurrentSpeed = 0.3f, SurfaceCurrentDirection = 45f },
                        new() { SurfaceCurrentSpeed = 1.0f, SurfaceCurrentDirection = 90f },
                        new() { SurfaceCurrentSpeed = 0.1f, SurfaceCurrentDirection = 180f },
                    ],
                },
                new S111Dcf3FixtureBuilder.TimeStep
                {
                    TimePoint = "20240101T010000Z",
                    Values =
                    [
                        new() { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 50f },
                        new() { SurfaceCurrentSpeed = 1.2f, SurfaceCurrentDirection = 95f },
                        new() { SurfaceCurrentSpeed = 0.2f, SurfaceCurrentDirection = 185f },
                    ],
                },
                new S111Dcf3FixtureBuilder.TimeStep
                {
                    TimePoint = "20240101T020000Z",
                    Values =
                    [
                        new() { SurfaceCurrentSpeed = 0.8f, SurfaceCurrentDirection = 55f },
                        new() { SurfaceCurrentSpeed = 1.1f, SurfaceCurrentDirection = 100f },
                        new() { SurfaceCurrentSpeed = 0.3f, SurfaceCurrentDirection = 190f },
                    ],
                },
                new S111Dcf3FixtureBuilder.TimeStep
                {
                    TimePoint = "20240101T030000Z",
                    Values =
                    [
                        new() { SurfaceCurrentSpeed = 0.6f, SurfaceCurrentDirection = 60f },
                        new() { SurfaceCurrentSpeed = 0.9f, SurfaceCurrentDirection = 105f },
                        new() { SurfaceCurrentSpeed = 0.4f, SurfaceCurrentDirection = 195f },
                    ],
                },
            };

            S111Dcf3FixtureBuilder.WriteFile(path, nodes, timeSteps);

            using var file = PureHdfFile.Open(path);
            var any = S111DatasetReader.ReadAny(file);

            var stationSeries = Assert.IsType<S111DatasetData.StationSeries>(any);
            var model = stationSeries.Dataset;

            Assert.Equal(3, model.DataCodingFormat);
            Assert.Equal(4326, model.HorizontalCRS);
            Assert.Equal(3, model.Stations.Count);

            // Node 0 transposed to a station with 4 time steps.
            var node0 = model.Stations[0];
            Assert.Equal("Node_001", node0.Identifier);
            Assert.Equal(46.83, node0.Latitude, precision: 2);
            Assert.Equal(-71.16, node0.Longitude, precision: 2);
            Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), node0.StartTime);
            Assert.Equal(new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc), node0.EndTime);
            Assert.Equal(TimeSpan.FromHours(1), node0.TimeRecordInterval);
            Assert.Equal(4, node0.NumberOfTimes);
            Assert.Equal(0.3f, node0.SpeedsMetresPerSecond[0]);
            Assert.Equal(45f, node0.DirectionsDegreesTrue[0]);
            Assert.Equal(0.5f, node0.SpeedsMetresPerSecond[1]);
            Assert.Equal(50f, node0.DirectionsDegreesTrue[1]);

            // Node 1.
            var node1 = model.Stations[1];
            Assert.Equal("Node_002", node1.Identifier);
            Assert.Equal(1.0f, node1.SpeedsMetresPerSecond[0]);
            Assert.Equal(90f, node1.DirectionsDegreesTrue[0]);

            // Node 2 — last time step.
            var node2 = model.Stations[2];
            Assert.Equal("Node_003", node2.Identifier);
            Assert.Equal(0.4f, node2.SpeedsMetresPerSecond[3]);
            Assert.Equal(195f, node2.DirectionsDegreesTrue[3]);

            Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), model.MinTime);
            Assert.Equal(new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc), model.MaxTime);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadAny_Dcf3_MissingPositioning_ThrowsSchemaException()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            // Write a DCF 3 file manually without Positioning.
            WriteDcf3WithoutPositioning(path);

            using var file = PureHdfFile.Open(path);
            var ex = Assert.Throws<S100DatasetSchemaException>(() => S111DatasetReader.ReadAny(file));

            Assert.Equal("S-111", ex.Product);
            Assert.Contains("Positioning", ex.AttributeOrDataset ?? "");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_OnDcf3_ThrowsNotSupportedForGriddedCallers()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            var nodes = new[]
            {
                new S111Dcf3FixtureBuilder.Node { Latitude = 46.83f, Longitude = -71.16f },
            };
            var timeSteps = new[]
            {
                new S111Dcf3FixtureBuilder.TimeStep
                {
                    TimePoint = "20240101T000000Z",
                    Values = [new() { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 90f }],
                },
            };
            S111Dcf3FixtureBuilder.WriteFile(path, nodes, timeSteps,
                firstDateTime: "20240101T000000Z",
                lastDateTime: "20240101T000000Z",
                timeRecordInterval: 0);

            using var file = PureHdfFile.Open(path);
            // Read() only supports dcf2; dcf3 should throw like dcf8.
            var ex = Assert.Throws<S100DatasetNotSupportedException>(() => S111DatasetReader.Read(file));

            Assert.Equal("S-111", ex.Product);
        }
        finally { File.Delete(path); }
    }

    private static void WriteDcf3WithoutPositioning(string path)
    {
        using var stream = File.Create(path);
        var instance = new PureHDF.H5Group
        {
            Attributes = new()
            {
                ["numberOfNodes"] = 1L,
                ["numberOfTimes"] = 1L,
                ["dateTimeOfFirstRecord"] = "20240101T000000Z",
                ["dateTimeOfLastRecord"] = "20240101T000000Z",
                ["timeRecordInterval"] = 3600L,
            },
            ["Group_001"] = new PureHDF.H5Group
            {
                Attributes = new() { ["timePoint"] = "20240101T000000Z" },
                ["values"] = new S111Dcf3FixtureBuilder.ValueRow[]
                {
                    new() { SurfaceCurrentSpeed = 0.5f, SurfaceCurrentDirection = 90f },
                },
            },
        };

        var file = new PureHDF.H5File
        {
            Attributes = new Dictionary<string, object> { ["horizontalDatumValue"] = 4326 },
            ["SurfaceCurrent"] = new PureHDF.H5Group
            {
                Attributes = new() { ["dataCodingFormat"] = (byte)3 },
                ["SurfaceCurrent.01"] = instance,
            },
        };

        var options = new PureHDF.H5WriteOptions(
            FieldNameMapper: f => f.GetCustomAttribute<PureHDF.H5NameAttribute>()?.Name);
        file.Write(stream, options);
    }
}
