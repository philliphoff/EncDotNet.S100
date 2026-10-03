using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The Timeline's lanes and its In map view filter (#710, handoff Part E).</summary>
public sealed class TimelineLanesTests
{
    private static readonly DateTime Run = new(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>An hourly S-111 run of <paramref name="model"/>, 48 h from <paramref name="start"/>.</summary>
    private static MapsuiMapTimedDataset Model(string model, DateTime? start = null, int hours = 49)
    {
        var first = start ?? Run;
        return new MapsuiMapTimedDataset($"111US00_{model.ToUpperInvariant()}_{first:yyyyMMdd'T'HH}Z_US4XX1DD", first, first.AddHours(hours - 1))
        {
            DatasetId = model,
            ProductSpec = "S-111",
            Samples = [.. Enumerable.Range(0, hours).Select(h => first.AddHours(h))],
            Selection = MapsuiTimeSelectionKind.Nearest,
            Tolerance = TimeSpan.FromHours(1),
            Coverage = [new MapsuiMapTimeSegment(first.AddHours(-1), first.AddHours(hours))],
        };
    }

    /// <summary>A six-minute water-level station with a day of observations ending at <paramref name="end"/>.</summary>
    private static MapsuiMapTimedDataset Station(string id, DateTime end)
    {
        var start = end.AddDays(-1);
        var count = 241;
        return new MapsuiMapTimedDataset($"104US00_{id.ToUpperInvariant()}", start, end)
        {
            DatasetId = id,
            ProductSpec = "S-104",
            Samples = [.. Enumerable.Range(0, count).Select(i => start.AddMinutes(6 * i))],
            Selection = MapsuiTimeSelectionKind.AtOrBefore,
            Tolerance = TimeSpan.FromMinutes(6),
            Coverage = [new MapsuiMapTimeSegment(start, end.AddMinutes(6))],
        };
    }

    private static void Load(GlobalTimeService service, params MapsuiMapTimedDataset[] datasets)
    {
        var samples = datasets.SelectMany(d => d.Samples).Distinct().Order().ToArray();
        var coverage = datasets.SelectMany(d => d.Coverage).OrderBy(c => c.Start).ToList();
        var merged = new List<MapsuiMapTimeSegment>();
        foreach (var segment in coverage)
        {
            if (merged.Count > 0 && segment.Start <= merged[^1].End)
                merged[^1] = new MapsuiMapTimeSegment(merged[^1].Start, segment.End > merged[^1].End ? segment.End : merged[^1].End);
            else
                merged.Add(segment);
        }
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = merged,
            Datasets = datasets,
        });
    }

    private static (GlobalTimeService Service, TimelineViewModel Timeline, FakeScope Scope) Create(DateTime now)
    {
        var service = new GlobalTimeService();
        var scope = new FakeScope();
        var timeline = new TimelineViewModel(service, new UtcFormat(), new FakeTimeProvider(new DateTimeOffset(now)), action => action(), scope: scope);
        return (service, timeline, scope);
    }

    /// <summary>The 14 NOAA OFS models of scenario 5, cbofs and dbofs in the Chesapeake view.</summary>
    private static readonly string[] Nationwide =
        ["cbofs", "dbofs", "gomofs", "ngofs2", "nyofs", "sfbofs", "tbofs", "creofs", "lmhofs", "leofs", "loofs", "lsofs", "wcofs", "ciofs"];

    [Fact]
    public void Lanes_group_by_product_with_layer_time_and_run()
    {
        var (service, timeline, _) = Create(Run.AddHours(2.5));
        Load(service, Station("baltimore", Run.AddHours(2.5)), Model("cbofs"), Model("dbofs"));

        Assert.False(timeline.IsInMapView);
        Assert.Equal(["S-111 Surface currents", "S-104 Water level"], timeline.LaneGroups.Select(g => g.Title));
        var cbofs = timeline.LaneGroups[0].Lanes[0];
        Assert.Equal("cbofs", cbofs.Code);
        Assert.Equal("18:00Z run · 1 h", cbofs.Sub);
        Assert.Equal("20:00Z (−30 min)", cbofs.LayerTime);
        Assert.Contains("111US00_CBOFS", cbofs.Tooltip, StringComparison.Ordinal);
        Assert.Single(cbofs.Bands);
        Assert.Equal("6 min", timeline.LaneGroups[1].Lanes[0].Sub);
        Assert.True(timeline.ShowLanes);
    }

    [Fact]
    public void Scenario_5_lists_only_the_models_in_view_and_they_set_the_axis()
    {
        var (service, timeline, scope) = Create(Run.AddHours(2.5));
        // cbofs and dbofs run 18:00Z; the others an older 06:00Z run.
        var datasets = Nationwide.Select(m => m is "cbofs" or "dbofs" ? Model(m) : Model(m, Run.AddHours(-36))).ToArray();
        scope.InView = ["cbofs", "dbofs"];
        Load(service, datasets);

        Assert.True(timeline.IsInMapView);
        var group = Assert.Single(timeline.LaneGroups);
        Assert.Equal(["cbofs", "dbofs"], group.Lanes.Select(l => l.Code));
        Assert.True(timeline.HasOutsideLanes);
        Assert.Equal("12 more outside the map view", timeline.OutsideLabel);
        Assert.NotEmpty(timeline.OutsideBands);
        Assert.Equal("In map view", timeline.SummaryLabel);
        Assert.StartsWith("2 of 14 layers in map view · ", timeline.RangeLabel, StringComparison.Ordinal);
        // The axis starts at the in-view runs (an hour before their first sample), not the older ones.
        Assert.Equal(Run, timeline.VisibleStart);

        // Panning to the Gulf of Maine re-filters without moving the view time.
        var viewTime = service.CurrentTime;
        scope.InView = ["gomofs"];
        scope.RaiseChanged();

        Assert.Equal(["gomofs"], Assert.Single(timeline.LaneGroups).Lanes.Select(l => l.Code));
        Assert.Equal("13 more outside the map view", timeline.OutsideLabel);
        Assert.Equal(viewTime, service.CurrentTime);
        Assert.Equal(Run.AddHours(-36), timeline.VisibleStart);
    }

    [Fact]
    public void The_filter_starts_on_above_six_lanes_and_the_user_can_turn_it_off()
    {
        var (service, timeline, scope) = Create(Run);
        scope.InView = ["cbofs"];
        Load(service, Model("cbofs"), Model("dbofs"));
        Assert.False(timeline.IsInMapView);
        Assert.Equal(2, timeline.LaneGroups[0].Count);

        Load(service, [.. Nationwide.Take(7).Select(m => Model(m))]);
        Assert.True(timeline.IsInMapView);
        Assert.Equal(1, timeline.LaneGroups[0].Count);

        timeline.ToggleInMapViewCommand.Execute(null);

        Assert.False(timeline.IsInMapView);
        Assert.False(timeline.HasOutsideLanes);
        Assert.Equal(7, timeline.LaneGroups[0].Count);
    }

    [Fact]
    public void With_nothing_in_view_every_layer_still_sets_the_axis()
    {
        var (service, timeline, scope) = Create(Run);
        scope.InView = [];
        Load(service, [.. Nationwide.Select(m => Model(m))]);

        Assert.Empty(timeline.LaneGroups);
        Assert.Equal("14 more outside the map view", timeline.OutsideLabel);
        Assert.NotNull(timeline.VisibleStart);
    }

    [Fact]
    public void An_unknown_footprint_keeps_the_lane_listed()
    {
        var (service, timeline, scope) = Create(Run);
        scope.Unknown = true;
        Load(service, [.. Nationwide.Select(m => Model(m))]);

        Assert.True(timeline.IsInMapView);
        Assert.Equal(14, Assert.Single(timeline.LaneGroups).Count);
        Assert.False(timeline.HasOutsideLanes);
    }

    [Fact]
    public void An_ended_forecast_is_expired_and_its_lane_turns_grey()
    {
        var (service, timeline, _) = Create(Run.AddDays(3));
        Load(service, Model("cbofs"), Station("baltimore", Run.AddDays(3)));

        var cbofs = timeline.LaneGroups[0].Lanes[0];
        var station = timeline.LaneGroups[1].Lanes[0];
        Assert.True(cbofs.IsExpired);
        Assert.True(cbofs.IsHidden);
        Assert.StartsWith("no data · last ", cbofs.LayerTime, StringComparison.Ordinal);
        Assert.NotEqual(cbofs.Color.ToString(), ((Avalonia.Media.ISolidColorBrush)cbofs.Swatch).Color.ToString());
        Assert.False(station.IsExpired);

        cbofs.JumpCommand.Execute(null);
        Assert.Equal(Run.AddHours(48), service.CurrentTime);
    }

    [Fact]
    public void Hovering_a_lane_highlights_its_footprint()
    {
        var (service, timeline, scope) = Create(Run);
        Load(service, Model("cbofs"));

        timeline.HoverLane(timeline.LaneGroups[0].Lanes[0]);
        Assert.Equal("cbofs", scope.Highlighted);
        Assert.Equal((byte)0x25, scope.Color.R);

        timeline.HoverLane(null);
        Assert.Null(scope.Highlighted);
    }

    [Fact]
    public void The_in_view_preset_spans_the_layers_in_the_map_view()
    {
        var (service, timeline, scope) = Create(Run.AddHours(2));
        scope.InView = ["dbofs"];
        Load(service, Model("cbofs", Run.AddHours(-36)), Model("dbofs"));

        Assert.True(timeline.ApplyPreset(TimelinePreset.InView));
        Assert.Equal("In view", timeline.PresetLabel);
        Assert.Equal(Run, timeline.VisibleStart);

        scope.InView = [];
        Assert.False(timeline.ApplyPreset(TimelinePreset.InView));
    }

    [Fact]
    public void Groups_fold_and_the_dock_collapses_to_the_strip()
    {
        var (service, timeline, _) = Create(Run);
        Load(service, Model("cbofs"), Station("baltimore", Run));

        timeline.LaneGroups[0].ToggleCommand.Execute(null);
        Assert.False(timeline.LaneGroups[0].IsExpanded);
        Load(service, Model("cbofs"), Model("dbofs"), Station("baltimore", Run));
        Assert.False(timeline.LaneGroups[0].IsExpanded);

        timeline.ToggleStripCommand.Execute(null);
        Assert.True(timeline.IsCollapsedToStrip);
        Assert.False(timeline.ShowLanes);
        Assert.Equal("Show lanes", timeline.StripToggleLabel);
    }

    [Fact]
    public async Task Set_timeline_view_turns_the_filter_and_layout_and_reports_the_lanes()
    {
        var (service, timeline, scope) = Create(Run.AddHours(2.5));
        scope.InView = ["cbofs", "dbofs"];
        Load(service, [.. Nationwide.Select(m => Model(m))]);
        var controller = new ViewerTimelineController(
            service, timeline, new DatasetsViewModel(new FakeDatasetLoaderService()), new FakeTimeProvider(new DateTimeOffset(Run.AddHours(2.5))),
            action => { action(); return Task.CompletedTask; });
        var tool = new SetTimelineViewTool(controller);

        Assert.True((await new GetTimelineStateTool(controller).InvokeAsync()).TryGetValue(out var state));
        Assert.True(state!.InMapView);
        Assert.Equal("lanes", state.Layout);
        Assert.Equal(["cbofs", "dbofs"], state.Lanes.Where(l => l.Listed).Select(l => l.Label));
        Assert.Equal(12, state.Lanes.Count(l => !l.Listed));
        Assert.Equal("S-111 Surface currents", state.Lanes[0].Group);

        Assert.True((await tool.InvokeAsync(new SetTimelineViewRequest(null, null, null, null, InMapView: false, Layout: "strip"))).TryGetValue(out var off));
        Assert.False(off!.InMapView);
        Assert.Equal("strip", off.Layout);
        Assert.All(off.Lanes, l => Assert.True(l.Listed));

        Assert.True((await tool.InvokeAsync(new SetTimelineViewRequest("in_view", null, null, null, InMapView: true))).TryGetValue(out var inView));
        Assert.Equal("In view", inView!.Preset);

        Assert.True((await tool.InvokeAsync(new SetTimelineViewRequest(null, null, null, null))).TryGetError(out var none));
        Assert.Equal("preset", Assert.IsType<InvalidArgument>(none).Parameter);
        Assert.True((await tool.InvokeAsync(new SetTimelineViewRequest(null, null, null, null, Layout: "grid"))).TryGetError(out var bad));
        Assert.Equal("layout", Assert.IsType<InvalidArgument>(bad).Parameter);
    }

    private sealed class UtcFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class FakeScope : ITimelineMapScope
    {
        public HashSet<string> InView { get; set; } = [];

        public bool Unknown { get; set; }

        public string? Highlighted { get; private set; }

        public (byte R, byte G, byte B) Color { get; private set; }

        public event Action? Changed;

        public bool? IsInMapView(string datasetId) => Unknown ? null : InView.Contains(datasetId);

        public void Highlight(string? datasetId, (byte R, byte G, byte B) color = default)
        {
            Highlighted = datasetId;
            Color = color;
        }

        public void RaiseChanged() => Changed?.Invoke();
    }
}
