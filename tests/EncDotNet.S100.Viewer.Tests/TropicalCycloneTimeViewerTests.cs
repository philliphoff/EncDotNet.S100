using Avalonia.VisualTree;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #1297 in the viewer (V11, V14, §6.5): a tropical cyclone's layer
/// time (estimated, on a forecast point, on the best track, "Beyond
/// forecast"), its Timeline lane under Tropical cyclones with its forecast
/// point ticks and issue and next-advisory diamonds, stepping through its
/// forecast points, and the same in get_timeline_state.
/// </summary>
public sealed class TropicalCycloneTimeViewerTests
{
    // Synoptic 18Z; issued 21Z; next advisory 03Z; forecast points every 12 h
    // to +48 h, and 24 h of past track.
    private static readonly DateTime Synoptic = new(2026, 11, 4, 18, 0, 0, DateTimeKind.Utc);

    private static MapsuiMapTimedDataset Zelda()
    {
        DateTime[] points = [Synoptic, Synoptic.AddHours(12), Synoptic.AddHours(24), Synoptic.AddHours(36), Synoptic.AddHours(48)];
        return new MapsuiMapTimedDataset("al142026_5day_017.zip", Synoptic.AddHours(-24), points[^1])
        {
            DatasetId = "zelda",
            ProductSpec = "TC",
            Samples = points,
            Selection = MapsuiTimeSelectionKind.Continuous,
            Tolerance = TimeSpan.FromHours(24),
            IssueTime = Synoptic.AddHours(3),
            NextIssueTime = Synoptic.AddHours(9),
            TimelineLabel = "ZELDA",
            TimelineDetail = "adv 17",
            Coverage = [new MapsuiMapTimeSegment(Synoptic.AddHours(-24), points[^1])],
        };
    }

    private static LayerTime Describe(DateTime at) =>
        LayerTimes.Describe(Zelda(), at, drawing: false, TimeFormat.Utc, TimeZoneInfo.Utc);

    private static (GlobalTimeService Service, TimelineViewModel Timeline) Load(DateTime now, DateTime view)
    {
        var service = new GlobalTimeService();
        var timeline = new TimelineViewModel(service, new UtcFormat(), new FakeTimeProvider(new DateTimeOffset(now)), action => action());
        var zelda = Zelda();
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = zelda.First,
            Maximum = zelda.Last,
            Current = view,
            Samples = zelda.Samples,
            CoverageSegments = zelda.Coverage,
            Datasets = [zelda],
        });
        return (service, timeline);
    }

    [Fact]
    public void Between_forecast_points_the_layer_time_is_an_estimate()
    {
        var between = Describe(Synoptic.AddHours(2).AddSeconds(20));
        var onPoint = Describe(Synoptic.AddHours(24));
        var past = Describe(Synoptic.AddHours(-6));

        Assert.Equal("est. 20:00Z · T+2 h", between.Text);
        Assert.Equal(LayerTimeState.Exact, between.State);
        Assert.False(between.IsHidden);
        Assert.Equal("18:00Z · T+24 h", onPoint.Text);
        Assert.Equal("12:00Z · best track", past.Text);
    }

    [Fact]
    public void Beyond_the_forecast_and_before_the_track_the_row_is_hidden_with_its_reason()
    {
        var beyond = Describe(Synoptic.AddHours(54));
        var before = Describe(Synoptic.AddHours(-30));

        Assert.True(beyond.IsHidden);
        Assert.Equal("Beyond forecast", beyond.HiddenTagText);
        Assert.Equal("last forecast point 18:00Z, 6 h earlier", beyond.Text);
        Assert.Equal(Synoptic.AddHours(48), beyond.Nearest);
        Assert.True(before.IsHidden);
        Assert.Equal("Before track", before.HiddenTagText);
        Assert.Equal("track starts 18:00Z, 6 h later", before.Text);
        Assert.Equal(Synoptic.AddHours(-24), before.Nearest);

        // The Datasets row shows the reason as its tag.
        var entry = new DatasetEntry("/tmp/al142026_5day_017.zip", "TC");
        entry.SetLayerTime(beyond, null);
        Assert.True(entry.IsLayerHidden);
        Assert.Equal("Beyond forecast", entry.LayerHiddenTagText);
        entry.SetLayerTime(Describe(Synoptic.AddHours(2)), null);
        Assert.False(entry.IsLayerHidden);
        Assert.Equal("Hidden", new DatasetEntry("/tmp/a.h5", "S-111").LayerHiddenTagText);
    }

    [Fact]
    public void The_Timeline_lane_is_under_Tropical_cyclones_with_ticks_and_diamonds()
    {
        var (_, timeline) = Load(now: Synoptic.AddHours(5), view: Synoptic.AddHours(5));

        var group = Assert.Single(timeline.LaneGroups);
        Assert.Equal("Tropical cyclones", group.Title);
        Assert.Equal("Tropical cyclones", TimelineViewModel.GroupTitle("TC"));
        var lane = Assert.Single(group.Lanes);
        Assert.Equal("ZELDA", lane.Code);
        Assert.Equal("adv 17 · 6 h", lane.Sub);
        Assert.Equal("est. 23:00Z · T+5 h", lane.LayerTime);
        Assert.False(lane.IsExpired);

        // Five forecast-point ticks, the issue diamond and the next-advisory diamond, in time order on the axis.
        Assert.Equal(5, lane.Marks.Count(m => m.Kind == TimelineLaneMarkKind.Step));
        var issued = lane.Marks.Single(m => m.Kind == TimelineLaneMarkKind.Issued);
        var next = lane.Marks.Single(m => m.Kind == TimelineLaneMarkKind.NextIssue);
        var ticks = lane.Marks.Where(m => m.Kind == TimelineLaneMarkKind.Step).Select(m => m.Position).ToArray();
        Assert.True(ticks[0] < issued.Position && issued.Position < next.Position && next.Position < ticks[1]);
        Assert.True(lane.HasMarks);

        // The past track is its own, faded band before the first forecast point.
        var past = Assert.Single(lane.PastBands);
        var forecast = Assert.Single(lane.Bands);
        Assert.Equal(0, past.Start, 3);
        Assert.Equal(ticks[0], forecast.Start, 3);
        Assert.Contains("ZELDA: 5 forecast points", lane.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void After_the_last_forecast_point_the_lane_is_expired()
    {
        var (_, timeline) = Load(now: Synoptic.AddHours(50), view: Synoptic.AddHours(50));

        var lane = Assert.Single(Assert.Single(timeline.LaneGroups).Lanes);
        Assert.True(lane.IsExpired);
        Assert.True(lane.IsHidden);
    }

    [Fact]
    public void The_step_menu_lists_the_storm_and_steps_its_forecast_points()
    {
        var (service, timeline) = Load(now: Synoptic.AddHours(5), view: Synoptic.AddHours(5));

        Assert.Contains(timeline.DriverOptions, o => o.Label == "ZELDA");
        timeline.SetDriverCommand.Execute("al142026_5day_017.zip");
        timeline.StepKind = TimelineStepKind.Sample;

        Assert.Equal("ZELDA", timeline.StepLabel);
        Assert.Equal(Synoptic.AddHours(12), timeline.StepTarget(TimelineStepKind.Sample, 1));
        Assert.Equal(Synoptic, timeline.StepTarget(TimelineStepKind.Sample, -1));
        timeline.StepBy(1);
        Assert.Equal(Synoptic.AddHours(12), service.CurrentTime);
    }

    [Fact]
    public async Task Get_timeline_state_reports_the_lane_marks_and_the_hidden_tag()
    {
        var (service, timeline) = Load(now: Synoptic.AddHours(5), view: Synoptic.AddHours(5));
        var controller = new ViewerTimelineController(
            service, timeline, new DatasetsViewModel(new FakeDatasetLoaderService()), new FakeTimeProvider(new DateTimeOffset(Synoptic.AddHours(5))),
            action => { action(); return Task.CompletedTask; });

        Assert.True((await new GetTimelineStateTool(controller).InvokeAsync(TestContext.Current.CancellationToken)).TryGetValue(out var state));
        var lane = Assert.Single(state!.Lanes);
        Assert.Equal("ZELDA", lane.Label);
        Assert.Equal("Tropical cyclones", lane.Group);
        Assert.Equal("adv 17 · 6 h", lane.Sub);
        Assert.Equal(Synoptic.AddHours(3), lane.Issued);
        Assert.Equal(Synoptic.AddHours(9), lane.NextIssue);
        Assert.Equal(5, lane.StepTicks);
    }

    [AvaloniaFact]
    public async Task The_lane_draws_its_marks_in_the_Timeline_view()
    {
        var (_, timeline) = Load(now: Synoptic.AddHours(5), view: Synoptic.AddHours(5));
        using var host = Headless.ViewHost.Show(new Views.TimelineView { DataContext = timeline }, width: 1100, height: 360);
        var automation = new ViewerUiAutomation(() => [host.Window]);
        static IEnumerable<UiElementSnapshot> Flatten(UiElementSnapshot e) => new[] { e }.Concat((e.Children ?? []).SelectMany(Flatten));
        var tree = (await automation.GetTreeAsync(new UiTreeQuery(null, 40, true, 2000))).Roots.SelectMany(r => Flatten(r.Element)).ToList();

        Assert.Contains(tree, e => e.Id == "Timeline.Lane.Band" && e.Name == "ZELDA");

        // The marks draw over the band, not in the way of its clicks.
        var marks = Assert.Single(host.Window.GetVisualDescendants().OfType<Views.LaneMarksControl>());
        Assert.True(marks.IsEffectivelyVisible);
        Assert.False(marks.IsHitTestVisible);
        Assert.Equal("Timeline.Lane.Marks", Avalonia.Automation.AutomationProperties.GetAutomationId(marks));
        Assert.Equal(7, marks.Marks!.Count);
        Assert.NotNull(marks.MarkBrush);
    }

    private sealed class UtcFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
