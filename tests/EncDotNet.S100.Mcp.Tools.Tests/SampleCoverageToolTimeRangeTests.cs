using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Requested times outside a dataset's range (issue #789): time-aware
/// dataset selection, strict-by-default single instants, the opt-in
/// nearest-step policy, one-step tolerance, and truncated windows.
/// </summary>
public class SampleCoverageToolTimeRangeTests
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime[] Hourly(int fromHour, int count) =>
        Enumerable.Range(fromHour, count).Select(h => Day.AddHours(h)).ToArray();

    private static DateTimeOffset At(int hour, int minute = 0) =>
        new(Day.AddHours(hour).AddMinutes(minute));

    /// <summary>A gridded S-111 run whose speed encodes <paramref name="speed"/> so tests can tell runs apart.</summary>
    private static LoadedDataset S111Run(
        string id, DateTime[] times, float speed = 0.5f, double spacing = 0.01, int cells = 4,
        string? issueDate = null, string? issueTime = null)
    {
        var synth = S111Synth.Dataset(spacingLat: spacing, spacingLon: spacing, numRows: cells, numCols: cells, speed: speed, times: times);
        var model = new S111Dataset
        {
            IssueDate = issueDate,
            IssueTime = issueTime,
            DataCodingFormat = synth.DataCodingFormat,
            Coverages = synth.Coverages,
        };
        return LoadedDatasetFactory.S111(id, source: new S111CoverageSource(model));
    }

    private static LoadedDataset S104Run(string id, DateTime[] times) =>
        LoadedDatasetFactory.S104(id, source: new S104CoverageSource(S104Synth.Dataset(times: times)));

    private static Task<ToolResult<SampleCoverageResult>> SampleS111(
        FakeDatasetCatalog catalog, DateTimeOffset? time = null, TimeQuery? times = null,
        TimeOutOfRangePolicy outOfRange = TimeOutOfRangePolicy.Error) =>
        new SampleCoverageTool(catalog).InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 0.02, Longitude: 0.02, time, times, outOfRange),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Instant_in_range_reports_in_range()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog, At(2));

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(SampleTimeStatus.InRange, v.TimeStatus);
        Assert.Equal(Day.AddHours(2), Assert.IsType<SurfaceCurrentSample>(v.Value).SampleTime);
        Assert.Null(v.Truncated);
    }

    [Fact]
    public async Task No_requested_time_leaves_time_status_null()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog);

        Assert.True(result.TryGetValue(out var v));
        Assert.Null(v.TimeStatus);
        Assert.Equal(Day, Assert.IsType<SurfaceCurrentSample>(v.Value).SampleTime);
    }

    [Fact]
    public async Task Instant_after_end_is_time_out_of_range_by_default()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4), issueDate: "20261005", issueTime: "000000Z"));

        var requested = At(50);
        var result = await SampleS111(catalog, requested);

        Assert.True(result.TryGetError(out var err));
        var oor = Assert.IsType<TimeOutOfRange>(err);
        Assert.Equal("time_out_of_range", oor.Code);
        Assert.Equal("time", oor.Parameter);
        Assert.Equal(requested, oor.RequestedTime);
        Assert.Equal(new DatasetId("run"), oor.DatasetId);
        Assert.Equal(Day, oor.ValidFrom.UtcDateTime);
        Assert.Equal(Day.AddHours(3), oor.ValidTo.UtcDateTime);
        Assert.Equal(Day.AddHours(3), oor.NearestStep.UtcDateTime);
        Assert.Equal(Day, oor.Run?.UtcDateTime);
        var candidate = Assert.Single(oor.Candidates);
        Assert.Equal(new DatasetId("run"), candidate.DatasetId);
    }

    [Fact]
    public async Task Instant_before_start_is_time_out_of_range_with_first_step_as_nearest()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(10, 4)));

        var result = await SampleS111(catalog, At(2));

        Assert.True(result.TryGetError(out var err));
        var oor = Assert.IsType<TimeOutOfRange>(err);
        Assert.Equal(Day.AddHours(10), oor.NearestStep.UtcDateTime);
        Assert.Null(oor.Run);
    }

    [Fact]
    public async Task Instant_TimeQuery_out_of_range_names_the_times_parameter()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog, times: TimeQuery.At(At(50)));

        Assert.True(result.TryGetError(out var err));
        Assert.Equal("times", Assert.IsType<TimeOutOfRange>(err).Parameter);
    }

    [Fact]
    public async Task Nearest_policy_samples_last_step_and_flags_after_end()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog, At(50), outOfRange: TimeOutOfRangePolicy.Nearest);

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(SampleTimeStatus.AfterEnd, v.TimeStatus);
        var sample = Assert.IsType<SurfaceCurrentSample>(v.Value);
        Assert.Equal(Day.AddHours(3), sample.SampleTime);
        Assert.Equal(At(50), sample.RequestedTime);
    }

    [Theory]
    [InlineData(3, 20)]   // 20 min past the last step
    [InlineData(4, 0)]    // exactly one interval past the last step
    [InlineData(-1, 0)]   // exactly one interval before the first step
    public async Task Within_one_interval_of_either_end_is_in_range(int hour, int minute)
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog, At(hour, minute));

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(SampleTimeStatus.InRange, v.TimeStatus);
        var expected = hour < 0 ? Day : Day.AddHours(3);
        Assert.Equal(expected, Assert.IsType<SurfaceCurrentSample>(v.Value).SampleTime);
    }

    [Fact]
    public async Task Beyond_one_interval_is_out_of_range()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog, At(4, 1));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<TimeOutOfRange>(err);
    }

    [Fact]
    public async Task Two_runs_at_one_point_pick_the_run_covering_the_time()
    {
        // Same grid, consecutive runs: whichever run is listed first must not
        // win when only the other covers the request.
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("early", Hourly(0, 4), speed: 1.0f));
        catalog.Add(S111Run("late", Hourly(24, 4), speed: 2.0f));

        var late = await SampleS111(catalog, At(26));
        Assert.True(late.TryGetValue(out var lv));
        Assert.Equal(new DatasetId("late"), lv.DatasetId);
        Assert.Equal(SampleTimeStatus.InRange, lv.TimeStatus);
        Assert.Equal(Day.AddHours(26), Assert.IsType<SurfaceCurrentSample>(lv.Value).SampleTime);

        var early = await SampleS111(catalog, At(1));
        Assert.True(early.TryGetValue(out var ev));
        Assert.Equal(new DatasetId("early"), ev.DatasetId);
    }

    [Fact]
    public async Task Finer_grid_that_misses_the_time_yields_to_a_coarser_one_that_covers_it()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("fine", Hourly(0, 4), spacing: 0.005, cells: 8));
        catalog.Add(S111Run("coarse", Hourly(0, 48), spacing: 0.01, cells: 4));

        var result = await SampleS111(catalog, At(30));

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(new DatasetId("coarse"), v.DatasetId);

        // Both cover early times, so the finer grid wins there.
        var early = await SampleS111(catalog, At(1));
        Assert.True(early.TryGetValue(out var ev));
        Assert.Equal(new DatasetId("fine"), ev.DatasetId);
    }

    [Fact]
    public async Task Overlapping_runs_prefer_the_newest_issue()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("older", Hourly(0, 48), issueDate: "20261004", issueTime: "000000Z"));
        catalog.Add(S111Run("newer", Hourly(6, 48), issueDate: "20261005", issueTime: "060000Z"));

        var result = await SampleS111(catalog, At(12));

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(new DatasetId("newer"), v.DatasetId);
    }

    [Fact]
    public async Task Out_of_range_error_lists_every_candidate_and_names_the_nearest()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("early", Hourly(0, 4)));
        catalog.Add(S111Run("late", Hourly(24, 4)));

        var result = await SampleS111(catalog, At(40));

        Assert.True(result.TryGetError(out var err));
        var oor = Assert.IsType<TimeOutOfRange>(err);
        Assert.Equal(new DatasetId("late"), oor.DatasetId);
        Assert.Equal(Day.AddHours(27), oor.NearestStep.UtcDateTime);
        Assert.Equal(
            new[] { "early", "late" },
            oor.Candidates.Select(c => c.DatasetId.ToString()).Order().ToArray());
    }

    [Fact]
    public async Task S104_two_runs_pick_the_run_covering_the_time()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S104Run("early", Hourly(0, 4)));
        catalog.Add(S104Run("late", Hourly(24, 4)));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0.02, Longitude: 0.02, Time: At(25)),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(new DatasetId("late"), v.DatasetId);
        Assert.Equal(SampleTimeStatus.InRange, v.TimeStatus);

        var outside = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S104Spec, Latitude: 0.02, Longitude: 0.02, Time: At(60)),
            TestContext.Current.CancellationToken);
        Assert.True(outside.TryGetError(out var err));
        Assert.Equal(2, Assert.IsType<TimeOutOfRange>(err).Candidates.Count);
    }

    [Fact]
    public async Task Range_past_the_end_is_truncated_with_covered_window()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog, times: TimeQuery.Between(At(2), At(50)));

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(2, v.Series!.Count);
        Assert.True(v.Truncated);
        Assert.Equal(At(2), v.CoveredFrom);
        Assert.Equal(At(3), v.CoveredTo);
        Assert.Null(v.TimeStatus);
    }

    [Fact]
    public async Task Range_inside_the_data_is_not_truncated()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog, times: TimeQuery.Between(At(1), At(2)));

        Assert.True(result.TryGetValue(out var v));
        Assert.False(v.Truncated);
        Assert.Equal(At(1), v.CoveredFrom);
        Assert.Equal(At(2), v.CoveredTo);
    }

    [Fact]
    public async Task Series_drops_instants_beyond_the_data_instead_of_clamping()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        // 02:00 .. 08:00 hourly: 02, 03, 04 (within one interval) are kept;
        // 05..08 are dropped rather than all snapping to 03:00.
        var result = await SampleS111(catalog, times: TimeQuery.Every(At(2), At(8), TimeSpan.FromHours(1)));

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(new[] { At(2), At(3), At(4) }, v.Series!.Select(e => e.RequestedTime).ToArray());
        Assert.True(v.Truncated);
        Assert.Equal(At(3), v.CoveredTo);
    }

    [Fact]
    public async Task Series_entirely_beyond_the_data_is_time_out_of_range()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));

        var result = await SampleS111(catalog, times: TimeQuery.Every(At(10), At(12), TimeSpan.FromHours(1)));

        Assert.True(result.TryGetError(out var err));
        var oor = Assert.IsType<TimeOutOfRange>(err);
        Assert.Null(oor.RequestedTime);
        Assert.Equal(At(10), oor.WindowStart);
        Assert.Equal(Day.AddHours(3), oor.NearestStep.UtcDateTime);
    }

    [Fact]
    public async Task Range_prefers_the_run_that_spans_the_whole_window()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("early", Hourly(0, 4)));
        catalog.Add(S111Run("late", Hourly(2, 12)));

        var result = await SampleS111(catalog, times: TimeQuery.Between(At(4), At(10)));

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(new DatasetId("late"), v.DatasetId);
        Assert.False(v.Truncated);
        Assert.Equal(7, v.Series!.Count);
    }

    [Fact]
    public async Task Range_spanning_two_runs_takes_the_run_covering_more_of_it()
    {
        // Window 04:00..30:00 — "early" (00..20) covers 16 h, "late" (24..71)
        // covers 6 h; neither spans it, so the larger overlap wins even
        // though "late" is the newer run.
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("early", Hourly(0, 21), issueDate: "20261005"));
        catalog.Add(S111Run("late", Hourly(24, 48), issueDate: "20261006"));

        var result = await SampleS111(catalog, times: TimeQuery.Between(At(4), At(30)));

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(new DatasetId("early"), v.DatasetId);
        Assert.True(v.Truncated);
        Assert.Equal(At(20), v.CoveredTo);
    }

    private static S111StationSeriesDataset StationRun(DateTime start, float speed, string issueDate) => new()
    {
        HorizontalCRS = 4326,
        DataCodingFormat = 8,
        IssueDate = issueDate,
        MinTime = start,
        MaxTime = start.AddHours(3),
        Stations =
        [
            new SurfaceCurrentStation
            {
                Identifier = "SFB01",
                Latitude = 37.82,
                Longitude = -122.48,
                StartTime = start,
                EndTime = start.AddHours(3),
                TimeRecordInterval = TimeSpan.FromHours(1),
                NumberOfTimes = 4,
                SpeedsKnots = [speed, speed, speed, speed],
                DirectionsDegreesTrue = [90f, 90f, 90f, 90f],
            },
        ],
    };

    [Fact]
    public async Task Station_runs_at_one_site_pick_the_run_covering_the_time()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("early", StationRun(Day, 1.0f, "20261005")));
        catalog.Add(LoadedDatasetFactory.S111Stations("late", StationRun(Day.AddHours(24), 2.0f, "20261006")));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 37.82, Longitude: -122.48, Time: At(25)),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var v));
        Assert.Equal(new DatasetId("late"), v.DatasetId);
        Assert.Equal(SampleTimeStatus.InRange, v.TimeStatus);
        Assert.Equal(2.0, Assert.IsType<SurfaceCurrentStationSample>(v.Value).SpeedKnots, 5);
    }

    [Fact]
    public async Task Station_time_out_of_range_is_an_error_by_default()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(LoadedDatasetFactory.S111Stations("run", StationRun(Day, 1.0f, "20261005")));
        var tool = new SampleCoverageTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageRequest(
            LoadedDatasetFactory.S111Spec, Latitude: 37.82, Longitude: -122.48, Time: At(50)),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var err));
        var oor = Assert.IsType<TimeOutOfRange>(err);
        Assert.Equal(Day.AddHours(3), oor.NearestStep.UtcDateTime);
        Assert.Equal(Day, oor.Run?.UtcDateTime);
    }

    private static GeoPolyline Route(params (double Lat, double Lon)[] vertices) =>
        new(vertices.Select(v => new GeoPoint(v.Lat, v.Lon)).ToList());

    [Fact]
    public async Task Along_route_entirely_past_the_data_is_time_out_of_range()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("run", Hourly(0, 4)));
        var tool = new SampleCoverageAlongTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageAlongRequest(
            LoadedDatasetFactory.S111Spec, Route((0.01, 0.01), (0.02, 0.02)), Time: At(50)),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<TimeOutOfRange>(err);
    }

    [Fact]
    public async Task Along_marks_out_of_range_vertices_and_keeps_the_rest()
    {
        // The first vertex is covered by a run that reaches the requested
        // time; the second only by one that does not.
        var catalog = new FakeDatasetCatalog();
        catalog.Add(S111Run("here", Hourly(0, 48)));
        var elsewhere = S111Synth.Dataset(originLat: 1.0, originLon: 1.0, times: Hourly(0, 4));
        catalog.Add(LoadedDatasetFactory.S111("there", LoadedDatasetFactory.Box(1, 1, 1.04, 1.04), new S111CoverageSource(elsewhere)));
        var tool = new SampleCoverageAlongTool(catalog);

        var result = await tool.InvokeAsync(new SampleCoverageAlongRequest(
            LoadedDatasetFactory.S111Spec, Route((0.02, 0.02), (1.02, 1.02)), Time: At(30)),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var v));
        Assert.NotNull(v.Samples[0].Result);
        Assert.Null(v.Samples[0].Error);
        Assert.Null(v.Samples[1].Result);
        Assert.Equal("time_out_of_range", v.Samples[1].Error?.Code);

        var nearest = await tool.InvokeAsync(new SampleCoverageAlongRequest(
            LoadedDatasetFactory.S111Spec, Route((0.02, 0.02), (1.02, 1.02)), Time: At(30),
            OutOfRange: TimeOutOfRangePolicy.Nearest),
            TestContext.Current.CancellationToken);
        Assert.True(nearest.TryGetValue(out var nv));
        Assert.Equal(SampleTimeStatus.AfterEnd, nv.Samples[1].Result?.TimeStatus);
    }
}
