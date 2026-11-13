using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Tides.Stations;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// list_current_stations, predict_current and sample_coverage's S-111
/// fallback (#1454) over NOAA's recorded current stations
/// (tests/datasets/Tides/Stations/noaa-currents.json): Deception Pass
/// PUG1701_33 and Golden Gate SFB1202_17 (harmonic), and Golden Gate
/// PCT0281_1 (subordinate to SFB1202_17).
/// </summary>
public sealed class CurrentToolsTests
{
    private const string DeceptionPass = "PUG1701_33";
    private const string GoldenGate = "SFB1202_17";
    private const string GoldenGateEast = "PCT0281_1";
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

    private static string CataloguePath(string name = "noaa-currents.json") =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", name);

    private static readonly Lazy<TideStationCatalogue> Catalogue = new(() =>
        TideStationCatalogueFile.LoadAsync(CataloguePath()).GetAwaiter().GetResult());

    private static TideCatalogueFileSource Source(string? path = null) => new(path ?? CataloguePath(), "Get one.");

    private static PredictCurrentRequest Predict(
        string? station = DeceptionPass,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        TimeSpan? step = null,
        CurrentPredictionMode mode = CurrentPredictionMode.Series,
        double? latitude = null,
        double? longitude = null,
        int maxPoints = 2000) =>
        new(station, latitude, longitude, null, from, to, step, mode, maxPoints);

    [Fact]
    public async Task Stations_near_the_Golden_Gate_are_nearest_first_with_kind_depth_and_directions()
    {
        var tool = new ListCurrentStationsTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(new ListTideStationsRequest(37.82, -122.46, null, null, null), TestContext.Current.CancellationToken));

        Assert.Equal([GoldenGateEast, GoldenGate], result.Stations.Select(s => s.Id));
        var subordinate = result.Stations[0];
        Assert.Equal("current", subordinate.Type);
        Assert.Equal("subordinate", subordinate.Kind);
        Assert.Equal(GoldenGate, subordinate.ReferenceStationId);
        Assert.Equal("Golden Gate Bridge, 0.88 nm NE of", subordinate.ReferenceStationName);
        Assert.Equal(42, subordinate.Offsets!.SlackBeforeFloodMinutes);
        Assert.Equal(1.4, subordinate.Offsets.MaximumEbbRatio);
        Assert.Null(subordinate.DepthMeters);

        var harmonic = result.Stations[1];
        Assert.Equal("harmonic", harmonic.Kind);
        Assert.Equal(6.4, harmonic.DepthMeters);
        Assert.Equal(52, harmonic.FloodDirectionDegrees);
        Assert.Equal(238, harmonic.EbbDirectionDegrees);
        Assert.False(harmonic.IsRotary);
        Assert.Null(harmonic.Offsets);

        Assert.Equal(6, result.Catalogue.CurrentStationCount);
        Assert.Equal(5, result.Catalogue.StationCount);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task A_catalogue_without_current_stations_lists_none()
    {
        var tool = new ListCurrentStationsTool(Source(CataloguePath("puget-sound.json")), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(new ListTideStationsRequest(47.6, -122.34, null, null, null), TestContext.Current.CancellationToken));

        Assert.Empty(result.Stations);
        Assert.Equal(0, result.Catalogue.CurrentStationCount);
    }

    [Fact]
    public async Task A_series_matches_the_predictor_in_knots_and_degrees()
    {
        var tool = new PredictCurrentTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(Predict(from: Now, to: Now.AddHours(6), step: TimeSpan.FromMinutes(10)), TestContext.Current.CancellationToken));

        Assert.Equal(37, result.Series!.Count);
        Assert.Null(result.Events);
        Assert.Equal(600, result.StepSeconds);
        Assert.Equal("kn", result.Unit);
        Assert.Equal("prediction", result.Kind);
        Assert.Equal("Astronomical tidal current only, not a forecast.", result.Caveat);
        Assert.Equal("current", result.Station.Type);
        Assert.Equal(5.49, result.Station.DepthMeters);
        Assert.Equal(108, result.Station.FloodDirectionDegrees);
        Assert.Null(result.Station.DistanceMeters);
        Assert.Null(result.Method);

        var predictor = Catalogue.Value.TryCreateCurrentPrediction(DeceptionPass)!.Predictor;
        foreach (var value in result.Series)
        {
            var expected = predictor.CurrentAt(value.Time.UtcDateTime);
            Assert.Equal(expected.SpeedKnots, value.SpeedKnots, 2);
            Assert.Equal(expected.IsFlood ? "flood" : "ebb", value.Phase);
            Assert.Equal(expected.IsFlood ? 108 : 277, value.DirectionDegrees, 1);
        }
    }

    [Fact]
    public async Task Events_are_slacks_and_maxima_with_directions_off_slack()
    {
        var tool = new PredictCurrentTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(Predict(from: Now, to: Now.AddDays(1), mode: CurrentPredictionMode.Events), TestContext.Current.CancellationToken));

        Assert.Null(result.Series);
        Assert.NotEmpty(result.Events!);
        Assert.Contains(result.Events!, e => e.Kind == "slack_before_flood");
        Assert.Contains(result.Events!, e => e.Kind == "max_ebb");
        Assert.All(result.Events!, e =>
        {
            Assert.Equal(0, e.Time.Second);
            if (e.Kind.StartsWith("slack", StringComparison.Ordinal))
            {
                Assert.Equal(0, e.SpeedKnots);
                Assert.Null(e.DirectionDegrees);
            }
            else
            {
                Assert.True(e.SpeedKnots > 0);
                Assert.Equal(e.Kind.EndsWith("flood", StringComparison.Ordinal) ? 108 : 277, e.DirectionDegrees!.Value, 1);
            }
        });
    }

    [Fact]
    public async Task A_subordinate_station_says_how_it_is_derived()
    {
        var tool = new PredictCurrentTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(Predict(station: GoldenGateEast, mode: CurrentPredictionMode.Both), TestContext.Current.CancellationToken));

        Assert.Equal("subordinate", result.Station.Kind);
        Assert.Equal(GoldenGate, result.Station.ReferenceStationId);
        Assert.Equal($"from Golden Gate Bridge, 0.88 nm NE of {GoldenGate}, with offsets", result.Method);
        Assert.NotNull(result.Series);
        Assert.NotNull(result.Events);
    }

    [Theory]
    [InlineData("PUG1701")]
    [InlineData("deception pass")]
    [InlineData("PUG1701_33")]
    public async Task A_station_is_found_by_id_base_id_or_name(string text)
    {
        var tool = new PredictCurrentTool(Source(), new FixedTime(Now));

        var result = Ok(await tool.InvokeAsync(Predict(station: text), TestContext.Current.CancellationToken));

        Assert.Equal(DeceptionPass, result.Station.Id);
    }

    [Fact]
    public async Task An_ambiguous_name_lists_candidates()
    {
        var tool = new PredictCurrentTool(Source(), new FixedTime(Now));

        var error = Assert.IsType<CurrentStationNotFound>(Error(await tool.InvokeAsync(Predict(station: "Golden Gate"), TestContext.Current.CancellationToken)));

        Assert.Equal(2, error.Candidates.Count);
        Assert.Equal("current_station_not_found", error.Code);
    }

    [Fact]
    public async Task A_point_uses_the_nearest_station_within_the_radius()
    {
        var tool = new PredictCurrentTool(Source(), new FixedTime(Now));

        var near = Ok(await tool.InvokeAsync(Predict(station: null, latitude: 37.83, longitude: -122.462), TestContext.Current.CancellationToken));
        var far = await tool.InvokeAsync(Predict(station: null, latitude: 47.6, longitude: -122.34), TestContext.Current.CancellationToken);

        Assert.Equal(GoldenGate, near.Station.Id);
        Assert.InRange(near.Station.DistanceMeters!.Value, 0, 200);
        Assert.IsType<NoCurrentStationNear>(Error(far));
    }

    [Fact]
    public async Task Too_many_points_is_refused()
    {
        var tool = new PredictCurrentTool(Source(), new FixedTime(Now));

        var result = await tool.InvokeAsync(Predict(from: Now, to: Now.AddDays(10), step: TimeSpan.FromMinutes(1), maxPoints: 100), TestContext.Current.CancellationToken);

        Assert.Equal("stepMinutes", Assert.IsType<InvalidArgument>(Error(result)).Parameter);
    }

    [Fact]
    public async Task Without_S111_a_point_near_a_current_station_is_predicted()
    {
        var tool = SampleTool(predictions: true);

        var result = Ok(await tool.InvokeAsync(S111(48.405, -122.645, Now), TestContext.Current.CancellationToken));

        var expected = Catalogue.Value.TryCreateCurrentPrediction(DeceptionPass)!.Predictor.CurrentAt(Now.UtcDateTime);
        var value = Assert.IsType<SurfaceCurrentStationSample>(result.Value);
        Assert.Equal(DeceptionPass, value.StationId);
        Assert.Equal(expected.SpeedKnots, value.SpeedKnots, 2);
        Assert.Equal(expected.DirectionDegrees, value.DirectionDegreesTrue, 1);
        Assert.Equal("current-predictions", result.DatasetId.Value);
        Assert.Equal("in_range", result.TimeStatus);

        var source = Assert.IsType<SampleSource>(result.Source);
        Assert.Equal("prediction", source.Kind);
        Assert.Equal("current", source.Station.Type);
        Assert.Equal("harmonic", source.Station.Kind);
        Assert.Null(source.VerticalDatum);
        Assert.Equal("Astronomical tidal current only, not a forecast.", source.Caveat);
        Assert.Equal(Catalogue.Value.Retrieved, source.CatalogueDate);
        Assert.InRange(source.Station.DistanceMeters!.Value, 0, 1_000);
    }

    [Fact]
    public async Task A_predicted_current_series_has_a_value_per_instant()
    {
        var tool = SampleTool(predictions: true);
        var window = new TimeQuery.Series(Now, Now.AddHours(3), TimeSpan.FromMinutes(15));

        var result = Ok(await tool.InvokeAsync(S111(37.822, -122.462, times: window), TestContext.Current.CancellationToken));

        Assert.Equal(13, result.Series!.Count);
        Assert.All(result.Series, s => Assert.Equal(GoldenGateEast, Assert.IsType<SurfaceCurrentStationSample>(s.Value).StationId));
        Assert.Equal("subordinate", result.Source!.Station.Kind);
        Assert.Equal(GoldenGate, result.Source.Station.ReferenceStationId);
    }

    [Fact]
    public async Task With_predictions_off_or_out_of_reach_the_S111_error_stands()
    {
        var off = await SampleTool(predictions: false).InvokeAsync(S111(48.405, -122.645, Now), TestContext.Current.CancellationToken);
        var far = await SampleTool(predictions: true).InvokeAsync(S111(10, 10, Now), TestContext.Current.CancellationToken);

        Assert.IsType<NoDatasetCoversPoint>(Error(off));
        Assert.IsType<NoDatasetCoversPoint>(Error(far));
    }

    private static SampleCoverageTool SampleTool(bool predictions)
    {
        var transforms = new ProjNetCrsTransformFactory();
        var cache = new RouteCheckSourceCache(transforms, new RouteCheckSourceOptions { TidePredictions = predictions ? Catalogue.Value : null });
        return new SampleCoverageTool(new EmptyCatalog(), transforms, cache, new FixedTime(Now));
    }

    private static SampleCoverageRequest S111(double latitude, double longitude, DateTimeOffset? time = null, TimeQuery? times = null) =>
        new(new SpecRef("S-111", default), latitude, longitude, time, times);

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
