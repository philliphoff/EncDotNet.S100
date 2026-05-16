using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Sample-coverage tests for the S-111 dcf8 (time series at fixed
/// stations) path (S-111 Edition 2.0.0 §10.2.3 / §10.2.7).
/// </summary>
public class SampleCoverageToolS111StationSeriesTests
{
    private static SurfaceCurrentStation Station(
        string id, double lat, double lon,
        float[] speeds, float[] directions)
    {
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new SurfaceCurrentStation
        {
            Identifier = id,
            Latitude = lat,
            Longitude = lon,
            StartTime = start,
            EndTime = start.AddHours(speeds.Length - 1),
            TimeRecordInterval = TimeSpan.FromHours(1),
            NumberOfTimes = speeds.Length,
            SpeedsMetresPerSecond = speeds,
            DirectionsDegreesTrue = directions,
        };
    }

    private static S111StationSeriesDataset Synth()
    {
        var stations = new[]
        {
            Station("A", 51.5, -0.1,
                new[] { 0.5f, 1.0f, 1.5f, 1.0f },
                new[] { 45f, 50f, 55f, 60f }),
            Station("B", 51.6, -0.2,
                new[] { 0.3f, 0.7f, 0.4f, 0.2f },
                new[] { 90f, 95f, 100f, 105f }),
            Station("C", 60.0, 10.0,
                new[] { 2.0f, 2.1f, 2.2f, 2.3f },
                new[] { 180f, 180f, 180f, 180f }),
        };
        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new S111StationSeriesDataset
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
        catalog.Add(LoadedDatasetFactory.S111Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 51.5, Longitude: -0.1));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<SurfaceCurrentStationSample>(value.Value);
        Assert.Equal("A", sample.StationId);
        Assert.Equal(0.5, sample.SpeedMetresPerSecond, 5);
        Assert.Equal(0.5 * 1.9438444924406046, sample.SpeedKnots, 5);
        Assert.Equal(45.0, sample.DirectionDegreesTrue, 5);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), sample.SampleTime);
        Assert.True(sample.StationDistanceMetres < 1.0);
        Assert.Null(sample.RequestedTime);
    }

    [Fact]
    public async Task ClampsTimeBeforeStart_ToFirstStep()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        var requested = new DateTimeOffset(2023, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 51.5, Longitude: -0.1, Time: requested));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<SurfaceCurrentStationSample>(value.Value);
        Assert.Equal(0.5, sample.SpeedMetresPerSecond, 5);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), sample.SampleTime);
        Assert.Equal(requested, sample.RequestedTime);
    }

    [Fact]
    public async Task ClampsTimeAfterEnd_ToLastStep()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        var requested = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 51.5, Longitude: -0.1, Time: requested));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<SurfaceCurrentStationSample>(value.Value);
        Assert.Equal(1.0, sample.SpeedMetresPerSecond, 5);
        Assert.Equal(60.0, sample.DirectionDegreesTrue, 5);
        Assert.Equal(new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc), sample.SampleTime);
    }

    [Fact]
    public async Task NearestTime_RoundsToClosestStep()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        // 01:40 → rounds to 02:00 (index 2).
        var requested = new DateTimeOffset(2024, 1, 1, 1, 40, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 51.5, Longitude: -0.1, Time: requested));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<SurfaceCurrentStationSample>(value.Value);
        Assert.Equal("A", sample.StationId);
        Assert.Equal(1.5, sample.SpeedMetresPerSecond, 5);
        Assert.Equal(55.0, sample.DirectionDegreesTrue, 5);
        Assert.Equal(new DateTime(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc), sample.SampleTime);
    }

    [Fact]
    public async Task NoMaxDistanceCap_FarPointStillReturnsNearestStation()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("dcf8", Synth()));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: -80.0, Longitude: 0.0));

        Assert.True(result.TryGetValue(out var value));
        var sample = Assert.IsType<SurfaceCurrentStationSample>(value.Value);
        Assert.True(sample.StationDistanceMetres > 10_000_000);
    }
}
