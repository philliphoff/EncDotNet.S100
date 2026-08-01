using EncDotNet.S100.Datasets.S111.Tests.Fixtures;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Hdf5;

namespace EncDotNet.S100.Datasets.S111.Tests;

public class S111Dcf1ReaderTests
{
    [Fact]
    public void ReadAny_Dcf1_UsesExplicitTimesAndTransposesValues()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S111Dcf1FixtureBuilder.WriteFile(
                path,
                [
                    new() { Latitude = 52.88, Longitude = 4.61 },
                    new() { Latitude = 52.89, Longitude = 4.69 },
                ],
                [
                    TimeStep("20220101T050700Z", (0.4f, 138f), (0.3f, 142f)),
                    TimeStep("20220101T060700Z", (0.8f, 180f), (0.8f, 186f)),
                    TimeStep("20220101T070700Z", (1.0f, 203f), (1.2f, 203f)),
                ],
                declaredInterval: 43200);

            using var file = PureHdfFile.Open(path);
            var result = Assert.IsType<S111DatasetData.StationSeries>(
                S111DatasetReader.ReadAny(file));

            Assert.Equal(1, result.Dataset.DataCodingFormat);
            Assert.Equal(4326, result.Dataset.HorizontalCRS);
            Assert.Equal(2, result.Dataset.Stations.Count);

            var first = result.Dataset.Stations[0];
            Assert.Equal("SurfaceCurrent.01:Station_001", first.Identifier);
            Assert.Equal(TimeSpan.FromHours(1), first.TimeRecordInterval);
            Assert.Equal(3, first.SampleTimes.Count);
            Assert.Equal(new DateTime(2022, 1, 1, 6, 7, 0, DateTimeKind.Utc), first.TimeAt(1));
            Assert.Equal([0.4f, 0.8f, 1.0f], first.SpeedsMetresPerSecond);
            Assert.Equal([138f, 180f, 203f], first.DirectionsDegreesTrue);
            Assert.Equal([0.3f, 0.8f, 1.2f], result.Dataset.Stations[1].SpeedsMetresPerSecond);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadAny_Dcf1_NonUniformTimesUseExplicitNearestSample()
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S111Dcf1FixtureBuilder.WriteFile(
                path,
                [new() { Latitude = 52.88, Longitude = 4.61 }],
                [
                    TimeStep("20220101T050700Z", (0.4f, 138f)),
                    TimeStep("20220101T053700Z", (0.8f, 180f)),
                    TimeStep("20220101T070700Z", (1.0f, 203f)),
                ]);

            using var file = PureHdfFile.Open(path);
            var station = Assert.IsType<S111DatasetData.StationSeries>(
                S111DatasetReader.ReadAny(file)).Dataset.Stations[0];

            Assert.Equal(TimeSpan.Zero, station.TimeRecordInterval);
            Assert.Equal(1, station.NearestTimeIndex(
                new DateTime(2022, 1, 1, 5, 57, 0, DateTimeKind.Utc)));
            Assert.Equal(2, station.NearestTimeIndex(
                new DateTime(2022, 1, 1, 6, 22, 0, DateTimeKind.Utc)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadAny_Dcf1_RejectsNonIncreasingTimesAndMismatchedNodeCounts(bool nonIncreasingTimes)
    {
        var path = Path.GetTempFileName() + ".h5";
        try
        {
            S111Dcf1FixtureBuilder.WriteFile(
                path,
                [
                    new() { Latitude = 52.88, Longitude = 4.61 },
                    new() { Latitude = 52.89, Longitude = 4.69 },
                ],
                nonIncreasingTimes
                    ? [
                        TimeStep("20220101T060700Z", (0.4f, 138f), (0.3f, 142f)),
                        TimeStep("20220101T060700Z", (0.8f, 180f), (0.8f, 186f)),
                    ]
                    : [
                        TimeStep("20220101T050700Z", (0.4f, 138f), (0.3f, 142f)),
                        TimeStep("20220101T060700Z", (0.8f, 180f)),
                    ]);

            using var file = PureHdfFile.Open(path);

            Assert.Throws<S100DatasetSchemaException>(() => S111DatasetReader.ReadAny(file));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static S111Dcf1FixtureBuilder.TimeStep TimeStep(
        string timePoint,
        params (float Speed, float Direction)[] values) =>
        new()
        {
            TimePoint = timePoint,
            Values = values.Select(value => new S111Dcf1FixtureBuilder.ValueRow
            {
                SurfaceCurrentSpeed = value.Speed,
                SurfaceCurrentDirection = value.Direction,
            }).ToArray(),
        };
}
