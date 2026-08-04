using EncDotNet.S100.Datasets.S104.Tests.Fixtures;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;

namespace EncDotNet.S100.Datasets.S104.Tests;

public class S104Dcf1ReaderTests
{
    [Fact]
    public void ReadAny_Dcf1_UsesExplicitTimesAndTransposesValues()
    {
        var path = CreateTempPath();
        try
        {
            S104Dcf1FixtureBuilder.WriteFile(
                path,
                [
                    new() { Latitude = 51.5, Longitude = 3.2 },
                    new() { Latitude = 52.0, Longitude = 4.0 },
                ],
                [
                    TimeStep("20240101T000000Z", (1.0f, 1), (2.0f, 2)),
                    TimeStep("20240101T010000Z", (1.5f, 2), (2.5f, 3)),
                    TimeStep("20240101T020000Z", (2.0f, 3), (3.0f, 1)),
                ],
                declaredInterval: 43200);

            using var file = PureHdfFile.Open(path);
            var result = Assert.IsType<S104DatasetData.StationSeries>(
                S104DatasetReader.ReadAny(file));

            Assert.Equal(1, result.Dataset.DataCodingFormat);
            Assert.Equal(4326, result.Dataset.HorizontalCRS);
            Assert.Equal(2, result.Dataset.Stations.Count);

            var first = result.Dataset.Stations[0];
            Assert.Equal("WaterLevel.01:Station_001", first.Identifier);
            Assert.Equal(TimeSpan.FromHours(1), first.TimeRecordInterval);
            Assert.Equal(3, first.SampleTimes.Count);
            Assert.Equal(new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc), first.TimeAt(1));
            Assert.Equal([1.0f, 1.5f, 2.0f], first.Heights);
            Assert.Equal(new byte[] { 1, 2, 3 }, first.Trends);
            Assert.Equal([2.0f, 2.5f, 3.0f], result.Dataset.Stations[1].Heights);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadAny_Dcf1_NonUniformTimesUseExplicitNearestSample()
    {
        var path = CreateTempPath();
        try
        {
            S104Dcf1FixtureBuilder.WriteFile(
                path,
                [new() { Latitude = 51.5, Longitude = 3.2 }],
                [
                    TimeStep("20240101T000000Z", (1.0f, 1)),
                    TimeStep("20240101T003000Z", (1.5f, 2)),
                    TimeStep("20240101T020000Z", (2.0f, 3)),
                ]);

            using var file = PureHdfFile.Open(path);
            var station = Assert.IsType<S104DatasetData.StationSeries>(
                S104DatasetReader.ReadAny(file)).Dataset.Stations[0];

            Assert.Equal(TimeSpan.Zero, station.TimeRecordInterval);
            Assert.Equal(1, station.NearestTimeIndex(
                new DateTime(2024, 1, 1, 0, 50, 0, DateTimeKind.Utc)));
            Assert.Equal(2, station.NearestTimeIndex(
                new DateTime(2024, 1, 1, 1, 15, 0, DateTimeKind.Utc)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadAny_Dcf1_RejectsNonIncreasingTimesAndMismatchedStationCounts(bool nonIncreasingTimes)
    {
        var path = CreateTempPath();
        try
        {
            S104Dcf1FixtureBuilder.WriteFile(
                path,
                [
                    new() { Latitude = 51.5, Longitude = 3.2 },
                    new() { Latitude = 52.0, Longitude = 4.0 },
                ],
                nonIncreasingTimes
                    ? [
                        TimeStep("20240101T010000Z", (1.0f, 1), (2.0f, 2)),
                        TimeStep("20240101T010000Z", (1.5f, 2), (2.5f, 3)),
                    ]
                    : [
                        TimeStep("20240101T000000Z", (1.0f, 1), (2.0f, 2)),
                        TimeStep("20240101T010000Z", (1.5f, 2)),
                    ]);

            using var file = PureHdfFile.Open(path);

            Assert.Throws<S100DatasetSchemaException>(() => S104DatasetReader.ReadAny(file));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".h5");

    private static S104Dcf1FixtureBuilder.TimeStep TimeStep(
        string timePoint,
        params (float Height, short Trend)[] values) =>
        new()
        {
            TimePoint = timePoint,
            Values = values.Select(value => new S104Dcf1FixtureBuilder.ValueRow
            {
                WaterLevelHeight = value.Height,
                WaterLevelTrend = value.Trend,
            }).ToArray(),
        };
}
