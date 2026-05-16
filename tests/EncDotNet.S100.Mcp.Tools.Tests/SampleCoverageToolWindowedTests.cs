using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Mcp.Tools.Time;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class SampleCoverageToolWindowedTests
{
    private static readonly DateTime[] HourlyTimes =
    {
        new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
        new(2024, 1, 1, 2, 0, 0, DateTimeKind.Utc),
        new(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task Instant_Times_lowers_to_Time_and_returns_no_series()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104(
            "s104-1", source: new EncDotNet.S100.Datasets.S104.S104CoverageSource(
                S104Synth.Dataset(times: HourlyTimes))));
        var tool = new SampleCoverageTool(catalog);

        var t = new DateTimeOffset(2024, 1, 1, 2, 30, 0, TimeSpan.Zero);
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec,
            Latitude: 0.02, Longitude: 0.02,
            Times: TimeQuery.At(t)));

        Assert.True(result.TryGetValue(out var v));
        Assert.Null(v!.Series); // Instant → series stays null
        Assert.IsType<WaterLevelSample>(v.Value);
    }

    [Fact]
    public async Task Range_returns_series_with_steps_in_window_only()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104(
            "s104-1", source: new EncDotNet.S100.Datasets.S104.S104CoverageSource(
                S104Synth.Dataset(times: HourlyTimes))));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec,
            Latitude: 0.02, Longitude: 0.02,
            Times: TimeQuery.Between(
                new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 2, 30, 0, TimeSpan.Zero))));

        Assert.True(result.TryGetValue(out var v));
        Assert.NotNull(v!.Series);
        Assert.Equal(2, v.Series!.Value.Length); // 1:00 and 2:00
        Assert.Equal(HourlyTimes[1], v.Series.Value[0].SampleTime);
        Assert.Equal(HourlyTimes[2], v.Series.Value[1].SampleTime);
    }

    [Fact]
    public async Task Series_snaps_each_instant_to_nearest_step()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104(
            "s104-1", source: new EncDotNet.S100.Datasets.S104.S104CoverageSource(
                S104Synth.Dataset(times: HourlyTimes))));
        var tool = new SampleCoverageTool(catalog);

        // Every 30 min from 00:00 to 03:00 = 7 instants.
        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec,
            Latitude: 0.02, Longitude: 0.02,
            Times: TimeQuery.Every(
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero),
                TimeSpan.FromMinutes(30))));

        Assert.True(result.TryGetValue(out var v));
        Assert.NotNull(v!.Series);
        Assert.Equal(7, v.Series!.Value.Length);
        // The requested-time echo preserves each enumerated instant.
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 30, 0, TimeSpan.Zero),
            v.Series.Value[1].RequestedTime);
    }

    [Fact]
    public async Task Range_outside_dataset_returns_TimeOutOfRange()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S104(
            "s104-1", source: new EncDotNet.S100.Datasets.S104.S104CoverageSource(
                S104Synth.Dataset(times: HourlyTimes))));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec,
            Latitude: 0.02, Longitude: 0.02,
            Times: TimeQuery.Between(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 1, 1, 6, 0, 0, TimeSpan.Zero))));

        Assert.True(result.TryGetError(out var err));
        var oor = Assert.IsType<TimeOutOfRange>(err);
        Assert.Equal("times", oor.Parameter);
        Assert.NotNull(oor.DatasetFirstTime);
        Assert.NotNull(oor.DatasetLastTime);
    }

    [Fact]
    public async Task S102_rejects_Range_with_NotSupportedYet()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S102(
            "s102-1", bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S102Spec,
            Latitude: 0.02, Longitude: 0.02,
            Times: TimeQuery.Between(
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 6, 0, 0, TimeSpan.Zero))));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<NotSupportedYet>(err);
    }

    [Fact]
    public async Task S111_Range_returns_per_step_SurfaceCurrentSamples()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111(
            "s111-1", source: new EncDotNet.S100.Datasets.S111.S111CoverageSource(
                S111Synth.Dataset(times: HourlyTimes))));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec,
            Latitude: 0.02, Longitude: 0.02,
            Times: TimeQuery.Between(
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero))));

        Assert.True(result.TryGetValue(out var v));
        Assert.NotNull(v!.Series);
        Assert.Equal(4, v.Series!.Value.Length);
        Assert.All(v.Series.Value, e => Assert.IsType<SurfaceCurrentSample>(e.Value));
    }
}
