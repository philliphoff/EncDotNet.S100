using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

public class ListTimeStepsToolTests
{
    [Fact]
    public async Task Missing_dataset_returns_dataset_not_found()
    {
        var catalog = new FakeDatasetCatalog();
        var tool = new ListTimeStepsTool(catalog);

        var result = await tool.InvokeAsync(new ListTimeStepsRequest(new DatasetId("nope")));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var error));
        Assert.IsType<DatasetNotFound>(error);
    }

    [Fact]
    public async Task S102_dataset_returns_empty_series()
    {
        var catalog = new FakeDatasetCatalog();
        var ds = LoadedDatasetFactory.S102("bathy");
        catalog.Add(ds);
        var tool = new ListTimeStepsTool(catalog);

        var result = await tool.InvokeAsync(new ListTimeStepsRequest(ds.Id));

        Assert.True(result.TryGetValue(out var value));
        Assert.Empty(value.Times);
        Assert.Null(value.Cadence);
        Assert.Null(value.FirstTime);
        Assert.Null(value.LastTime);
        Assert.Equal("S-102", value.Spec.Name);
    }

    [Fact]
    public async Task S104_gridded_returns_per_step_times_with_detected_cadence()
    {
        var catalog = new FakeDatasetCatalog();
        var ds = LoadedDatasetFactory.S104("wl");
        catalog.Add(ds);
        var tool = new ListTimeStepsTool(catalog);

        var result = await tool.InvokeAsync(new ListTimeStepsRequest(ds.Id));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.Times.Length);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), value.FirstTime);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 2, 0, 0, TimeSpan.Zero), value.LastTime);
        Assert.Equal(TimeSpan.FromHours(1), value.Cadence);
        for (int i = 0; i < value.Times.Length; i++)
        {
            Assert.Equal(TimeSpan.Zero, value.Times[i].Offset);
        }
    }

    [Fact]
    public async Task S104_station_series_derives_from_first_station()
    {
        var catalog = new FakeDatasetCatalog();
        var stations = S104Synth.StationSeries(stationCount: 2, samplesPerStation: 5);
        var ds = LoadedDatasetFactory.S104Stations("ss", stations);
        catalog.Add(ds);
        var tool = new ListTimeStepsTool(catalog);

        var result = await tool.InvokeAsync(new ListTimeStepsRequest(ds.Id));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(5, value.Times.Length);
        Assert.Equal(TimeSpan.FromHours(1), value.Cadence);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), value.FirstTime);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 4, 0, 0, TimeSpan.Zero), value.LastTime);
    }

    [Fact]
    public async Task S111_gridded_returns_per_step_times()
    {
        var catalog = new FakeDatasetCatalog();
        var ds = LoadedDatasetFactory.S111("cur");
        catalog.Add(ds);
        var tool = new ListTimeStepsTool(catalog);

        var result = await tool.InvokeAsync(new ListTimeStepsRequest(ds.Id));

        Assert.True(result.TryGetValue(out var value));
        Assert.NotEmpty(value.Times);
        Assert.Equal("S-111", value.Spec.Name);
    }

    [Fact]
    public async Task Non_uniform_grid_returns_null_cadence()
    {
        var catalog = new FakeDatasetCatalog();
        var times = new[]
        {
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 3, 0, 0, DateTimeKind.Utc),
        };
        var s104 = S104Synth.Dataset(times: times);
        var ds = LoadedDatasetFactory.S104("wl", source: new EncDotNet.S100.Datasets.S104.S104CoverageSource(s104));
        catalog.Add(ds);
        var tool = new ListTimeStepsTool(catalog);

        var result = await tool.InvokeAsync(new ListTimeStepsRequest(ds.Id));

        Assert.True(result.TryGetValue(out var value));
        Assert.Equal(3, value.Times.Length);
        Assert.Null(value.Cadence);
    }

    [Fact]
    public async Task Non_coverage_dataset_returns_not_supported_yet()
    {
        var catalog = new FakeDatasetCatalog();
        var ds = LoadedDatasetFactory.S124("warn");
        catalog.Add(ds);
        var tool = new ListTimeStepsTool(catalog);

        var result = await tool.InvokeAsync(new ListTimeStepsRequest(ds.Id));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var error));
        Assert.IsType<NotSupportedYet>(error);
    }
}
