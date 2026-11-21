using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// sample falls back to tide predictions
/// for S-104 where no S-104 covers the point and time (#1452), over NOAA's
/// Puget Sound stations, with no charts loaded (straight-line reach).
/// </summary>
public sealed class PredictedSampleCoverageTests
{
    private const string Seattle = "9447130";
    private static readonly DateTimeOffset At = new(2026, 11, 18, 20, 0, 0, TimeSpan.Zero);

    private static readonly Lazy<TideStationCatalogue> Catalogue = new(() =>
        TideStationCatalogueFile.LoadAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "puget-sound.json")).GetAwaiter().GetResult());

    private static (SampleTool Tool, IDatasetCatalog Catalog) Create(bool predictions)
    {
        var transforms = new ProjNetCrsTransformFactory();
        var cache = new RouteCheckSourceCache(transforms, new RouteCheckSourceOptions { TidePredictions = predictions ? Catalogue.Value : null });
        var catalog = new EmptyCatalog();
        return (new SampleTool(catalog, transforms, cache, new FixedTime(At)), catalog);
    }

    private static SampleCoverageRequest S104(double latitude, double longitude, DateTimeOffset? time = null, TimeQuery? times = null) =>
        new(new SpecRef("S-104", default), latitude, longitude, time, times);

    [Fact]
    public async Task Without_S104_a_point_near_a_station_is_predicted()
    {
        var (tool, _) = Create(predictions: true);

        var result = Ok(await tool.SampleCoverageAsync(S104(47.605, -122.34, At), TestContext.Current.CancellationToken));

        var expected = Catalogue.Value.TryCreatePrediction(Seattle, VerticalDatum.MeanLowerLowWater)!.Predictor.HeightAt(At.UtcDateTime);
        var value = Assert.IsType<WaterLevelStationSample>(result.Value);
        Assert.Equal(Seattle, value.StationId);
        Assert.Equal(expected, value.WaterLevelHeight, 3);
        Assert.Equal(At.UtcDateTime, value.SampleTime);
        Assert.Contains(value.Trend, new[] { "increasing", "decreasing", "steady" });
        Assert.Equal("tide-predictions", result.DatasetId.Value);
        Assert.Equal("in_range", result.TimeStatus);

        var source = Assert.IsType<SampleSource>(result.Source);
        Assert.Equal("prediction", source.Kind);
        Assert.Equal(Seattle, source.Station.Id);
        Assert.Equal("tide", source.Station.Type);
        Assert.Equal("harmonic", source.Station.Kind);
        Assert.Null(source.Station.ReferenceStationId);
        Assert.InRange(source.Station.DistanceMeters!.Value, 0, 1_000);
        Assert.Equal(12, source.VerticalDatum);
        Assert.Null(source.Confidence);
        Assert.Null(source.Note);
        Assert.Equal("Astronomical tide only, not a forecast.", source.Caveat);
        Assert.Equal(Catalogue.Value.Retrieved, source.CatalogueDate);
    }

    [Fact]
    public async Task A_subordinate_station_names_its_reference()
    {
        var (tool, _) = Create(predictions: true);

        var result = Ok(await tool.SampleCoverageAsync(S104(47.27, -122.55, At), TestContext.Current.CancellationToken));

        Assert.Equal("subordinate", result.Source!.Station.Kind);
        Assert.Equal(Seattle, result.Source.Station.ReferenceStationId);
    }

    [Fact]
    public async Task A_series_window_is_predicted_at_each_instant()
    {
        var (tool, _) = Create(predictions: true);
        var window = new TimeQuery.Series(At, At.AddHours(6), TimeSpan.FromMinutes(10));

        var result = Ok(await tool.SampleCoverageAsync(S104(47.605, -122.34, times: window), TestContext.Current.CancellationToken));

        Assert.Equal(37, result.Series!.Count);
        Assert.All(result.Series, s => Assert.IsType<WaterLevelStationSample>(s.Value));
        Assert.False(result.Truncated);
        Assert.NotNull(result.Source);
    }

    [Fact]
    public async Task With_predictions_off_or_out_of_reach_the_S104_error_stands()
    {
        var (off, _) = Create(predictions: false);
        var (on, _) = Create(predictions: true);

        var offResult = await off.SampleCoverageAsync(S104(47.605, -122.34, At), TestContext.Current.CancellationToken);
        var farResult = await on.SampleCoverageAsync(S104(10, 10, At), TestContext.Current.CancellationToken);
        var s102 = await on.SampleCoverageAsync(new SampleCoverageRequest(new SpecRef("S-102", default), 47.605, -122.34), TestContext.Current.CancellationToken);

        Assert.IsType<NoDatasetCoversPoint>(Error(offResult));
        var far = Assert.IsType<NoStationWithinReach>(Error(farResult));
        Assert.Equal("water_level", far.Quantity);
        Assert.Equal(10_000, far.ReachMeters);
        Assert.True(far.NearestStation.DistanceMeters > 10_000);
        Assert.IsType<NoDatasetCoversPoint>(Error(s102));
    }

    [Fact]
    public async Task Along_a_line_each_vertex_in_reach_is_predicted()
    {
        var transforms = new ProjNetCrsTransformFactory();
        var cache = new RouteCheckSourceCache(transforms, new RouteCheckSourceOptions { TidePredictions = Catalogue.Value });
        var tool = new SampleTool(new EmptyCatalog(), transforms, cache, new FixedTime(At));
        var line = new GeoPolyline([new GeoPoint(47.605, -122.34), new GeoPoint(10, 10)]);

        var result = await tool.ProfileAsync(TestSampleTool.Along(new SpecRef("S-104", default), line, At), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var along));
        Assert.Equal("prediction", along!.Samples[0].Result!.Source!.Kind);
        Assert.Null(along.Samples[1].Result);
        Assert.Equal("no_station_within_reach", along.Samples[1].Error!.Code);
    }

    private static T Ok<T>(ToolResult<T> result)
    {
        if (result.TryGetError(out var error))
            Assert.Fail($"{error.Code}: {error.Message}");
        result.TryGetValue(out var value);
        return value!;
    }

    private static ToolError Error<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetError(out var error), "Expected an error.");
        return error!;
    }

    private sealed class EmptyCatalog : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets { get; } = [];

        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
