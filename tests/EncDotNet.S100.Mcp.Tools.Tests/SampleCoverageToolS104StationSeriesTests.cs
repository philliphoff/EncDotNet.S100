using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Sample-coverage tests for the S-104 dcf8 (time series at fixed
/// stations) path (S-104 Edition 2.0.0 §10.2.3 / §10.2.7).
/// </summary>
public class SampleCoverageToolS104StationSeriesTests
{
    private static WaterLevelStation Station(
        string id, double lat, double lon,
        float[] heights, byte[] trends)
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new WaterLevelStation
        {
            Identifier = id,
            Latitude = lat,
            Longitude = lon,
            StartTime = start,
            EndTime = start.AddHours(heights.Length - 1),
            TimeRecordInterval = TimeSpan.FromHours(1),
            NumberOfTimes = heights.Length,
            Heights = heights,
            Trends = trends,
        };
    }

    private static S104StationSeriesDataset Synth()
    {
        var stations = new[]
        {
            Station("A", 51.5, -0.1, new[] { 1.0f, 1.5f, 2.0f, 1.5f }, new byte[] { 2, 2, 3, 1 }),
            Station("B", 51.6, -0.2, new[] { 0.5f, 1.0f, 0.7f, 0.4f }, new byte[] { 2, 3, 1, 1 }),
            Station("C", 60.0, 10.0, new[] { 3.0f, 3.1f, 3.2f, 3.3f }, new byte[] { 2, 2, 2, 2 }),
        };
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new S104StationSeriesDataset
        {
            HorizontalCRS = 4326,
            DataCodingFormat = 8,
            Stations = stations,
            MinTime = start,
            MaxTime = start.AddHours(3),
        };
    }

    [Fact]
    public async Task SelectsNearestStation_AndFirstSampleByDefault()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        // Request very close to station A (51.5, -0.1).
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 51.5, Longitude: -0.1));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<WaterLevelStationSample>(value.Value);
        Assert.Equal("A", sample.StationId);
        Assert.Equal(1.0, sample.WaterLevelHeight, 5);
        Assert.Equal("increasing", sample.Trend);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), sample.SampleTime);
        Assert.True(sample.StationDistanceMetres < 1.0);
        Assert.Null(sample.RequestedTime);
    }

    [Fact]
    public async Task ClampsTimeBeforeStart_ToFirstStep()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        var requested = new DateTimeOffset(2023, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 51.5, Longitude: -0.1, Time: requested));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<WaterLevelStationSample>(value.Value);
        Assert.Equal(1.0, sample.WaterLevelHeight, 5);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), sample.SampleTime);
        Assert.Equal(requested, sample.RequestedTime);
    }

    [Fact]
    public async Task ClampsTimeAfterEnd_ToLastStep()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        var requested = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 51.5, Longitude: -0.1, Time: requested));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<WaterLevelStationSample>(value.Value);
        Assert.Equal(1.5, sample.WaterLevelHeight, 5);
        Assert.Equal("decreasing", sample.Trend);
        Assert.Equal(new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc), sample.SampleTime);
    }

    [Fact]
    public async Task NearestTime_RoundsToClosestStep()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        // 01:40 → rounds to 02:00 (index 2).
        var requested = new DateTimeOffset(2024, 1, 1, 1, 40, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 51.5, Longitude: -0.1, Time: requested));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<WaterLevelStationSample>(value.Value);
        Assert.Equal("A", sample.StationId);
        Assert.Equal(2.0, sample.WaterLevelHeight, 5);
        Assert.Equal("steady", sample.Trend);
        Assert.Equal(new DateTime(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc), sample.SampleTime);
    }

    [Fact]
    public async Task NoMaxDistanceCap_FarPointStillReturnsNearestStation()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        // Far away from every station — should still pick the closest
        // (no max-distance cap).
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: -80.0, Longitude: 0.0));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<WaterLevelStationSample>(value.Value);
        // Stations A and B are at ~51-52 N; C is at 60 N. All are very
        // far from -80 N — accept any but verify a distance is reported.
        Assert.True(sample.StationDistanceMetres > 10_000_000);
    }
}
