using EncDotNet.S100.Collections;
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
        Assert.Equal("US4XX1DD · 18:00Z run · 1 h", cbofs.Sub);
        Assert.Equal("20:00Z (−30 min)", cbofs.LayerTime);
        Assert.Contains("111US00_CBOFS", cbofs.Tooltip, StringComparison.Ordinal);
        Assert.Single(cbofs.Bands);
        Assert.Equal("6 min", timeline.LaneGroups[1].Lanes[0].Sub);
        Assert.True(timeline.ShowLanes);
    }

    [Theory]
    [InlineData("111US00_CBOFS_US4MD1DD", "cbofs", "US4MD1DD")]
    [InlineData("111US00_NGOFS2_20261003T12Z_US4LA1CL", "ngofs2", "US4LA1CL")]
    public void Tiled_forecast_names_give_the_model_and_tile(string name, string model, string tile) =>
        Assert.Equal((model, tile), ForecastRunNames.ModelAndTile(name));

    [Theory]
    [InlineData("104US00_BALTIMORE")]
    [InlineData("104US004SC1BO_20251217T12Z")]
    public void Other_names_give_no_model(string name) => Assert.Null(ForecastRunNames.ModelAndTile(name));

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
        Assert.Equal("S-111 Surface currents", state.Lanes[^1].Group);

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

    // ── Library data on the lanes (#711) ───────────────────────────────

    private static readonly GeoBounds Chesapeake = new(36.8, -77, 39.6, -75.8);
    private static readonly GeoBounds Delaware = new(38.4, -75.6, 39.9, -74.8);

    private static LibraryTimedEntry Entry(string name, DateTime start, int hours, LibraryTimedState state, GeoBounds bounds, bool newRun = false) =>
        new($"feed:{name}", name, "S-111", start, start.AddHours(hours), state)
        {
            Run = start,
            Model = ForecastRunNames.ModelAndTile(name)?.Model,
            IsNewRun = newRun,
            SizeBytes = state == LibraryTimedState.Online ? 500_000 : null,
            Bounds = bounds,
        };

    private static (GlobalTimeService Service, TimelineViewModel Timeline, FakeScope Scope, FakeLibrary Library) CreateWithLibrary(DateTime now, params LibraryTimedEntry[] entries)
    {
        var service = new GlobalTimeService();
        var scope = new FakeScope { Unknown = true };
        var library = new FakeLibrary { Entries = entries };
        var timeline = new TimelineViewModel(service, new UtcFormat(), new FakeTimeProvider(new DateTimeOffset(now)), action => action(), scope: scope, library: library);
        return (service, timeline, scope, library);
    }

    /// <summary>A loaded cbofs tile (12:00Z run, named as Library tiles are) with a newer 18:00Z run online, and dbofs online.</summary>
    private static (GlobalTimeService, TimelineViewModel, FakeScope, FakeLibrary) Chesapeake18Z()
    {
        var older = Run.AddHours(-6);
        var created = CreateWithLibrary(
            Run.AddHours(1),
            Entry("111US00_CBOFS_US4MD1DD", older, 48, LibraryTimedState.Loaded, Chesapeake),
            Entry("111US00_CBOFS_US4MD1DD", Run, 48, LibraryTimedState.Online, Chesapeake, newRun: true),
            Entry("111US00_DBOFS_US4DE1AD", Run, 48, LibraryTimedState.Online, Delaware),
            Entry("111US00_DBOFS_US4NJ1AC", Run, 48, LibraryTimedState.Online, Delaware));
        var cbofs = Model("cbofs", older) with { Name = "111US00_CBOFS_US4MD1DD" };
        Load(created.Service, cbofs);
        return created;
    }

    [Fact]
    public void A_loaded_tile_with_a_newer_run_online_shows_it_dashed_with_New_run()
    {
        var (_, timeline, _, library) = Chesapeake18Z();

        var group = Assert.Single(timeline.LaneGroups);
        Assert.Equal(["cbofs", "dbofs"], group.Lanes.Select(l => l.Code));
        var cbofs = group.Lanes[0];
        Assert.False(cbofs.IsKnown);
        Assert.True(cbofs.IsNewRun);
        Assert.Single(cbofs.OnlineBands);
        Assert.Empty(cbofs.OnDiskBands);
        Assert.True(cbofs.CanGet);
        Assert.Equal("Get · " + LibraryItemViewModel.FormatBytes(500_000), cbofs.GetText);
        // The newer run widens the axis past the loaded data (to 18:00Z + 48 h).
        Assert.Equal(Run.AddHours(48), timeline.VisibleEnd);

        cbofs.GetCommand.Execute(null);
        Assert.Equal(["111US00_CBOFS_US4MD1DD"], library.Got.Select(e => e.Name));
        cbofs.RevealCommand.Execute(null);
        Assert.Equal("111US00_CBOFS_US4MD1DD", library.Revealed?.Name);
    }

    [Fact]
    public void Library_data_not_loaded_gets_its_own_lane_per_model()
    {
        var (_, timeline, _, library) = Chesapeake18Z();

        var dbofs = timeline.LaneGroups[0].Lanes[1];
        Assert.True(dbofs.IsKnown);
        Assert.Equal("online · 2 tiles · " + LibraryItemViewModel.FormatBytes(1_000_000), dbofs.Sub);
        Assert.Equal("18:00Z run", dbofs.LayerTime);
        Assert.Equal("dbofs · 18:00Z run", dbofs.ActionTitle);
        Assert.StartsWith("Valid ", dbofs.ActionDetail, StringComparison.Ordinal);
        Assert.EndsWith(" 18:00Z · 2 tiles · " + LibraryItemViewModel.FormatBytes(1_000_000), dbofs.ActionDetail, StringComparison.Ordinal);
        Assert.Single(dbofs.OnlineBands);
        Assert.Empty(dbofs.Bands);
        Assert.False(dbofs.CanLoad);

        dbofs.GetCommand.Execute(null);
        Assert.Equal(2, library.Got.Count);
    }

    [Fact]
    public void Library_lanes_count_toward_turning_the_filter_on()
    {
        var entries = Nationwide.Skip(1).Take(6)
            .Select(m => Entry($"111US00_{m.ToUpperInvariant()}_US4XX1AA", Run, 48, LibraryTimedState.Online, Delaware))
            .ToArray();
        var (service, timeline, _, _) = CreateWithLibrary(Run, entries);
        Load(service, Model("cbofs"));

        Assert.True(timeline.IsInMapView);

        timeline.ShowOnline = false;
        Assert.False(timeline.IsInMapView);
    }

    [Fact]
    public void Show_online_off_draws_loaded_data_only()
    {
        var (_, timeline, _, _) = Chesapeake18Z();

        timeline.ToggleShowOnlineCommand.Execute(null);

        Assert.False(timeline.ShowOnline);
        var lane = Assert.Single(Assert.Single(timeline.LaneGroups).Lanes);
        Assert.Empty(lane.OnlineBands);
        Assert.True(lane.IsNewRun);
        Assert.Equal(Run.AddHours(42), timeline.VisibleEnd);
    }

    [Fact]
    public void Library_lanes_outside_the_map_view_fold_with_the_rest()
    {
        var (_, timeline, scope, _) = Chesapeake18Z();
        scope.Unknown = false;
        scope.InView = ["cbofs"];
        scope.View = new GeoBounds(37, -76.9, 38, -76);
        timeline.IsInMapView = true;

        Assert.Equal(["cbofs"], Assert.Single(timeline.LaneGroups).Lanes.Select(l => l.Code));
        Assert.Equal("dbofs", Assert.Single(timeline.OutsideLanes).Code);

        timeline.HoverLane(timeline.OutsideLanes[0]);
        Assert.Equal(2, scope.HighlightedAreas.Count);
    }

    [Fact]
    public void A_download_fills_its_band()
    {
        var (_, timeline, _, library) = Chesapeake18Z();
        var dbofs = timeline.LaneGroups[0].Lanes[1];

        library.Progress = 0.4;
        library.RaiseProgress();

        Assert.Equal(0.4, dbofs.Progress, 3);
        Assert.False(dbofs.CanGet);
        Assert.Equal("Downloading · 40%", dbofs.ActionDetail.Replace(" %", "%", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_timeline_state_reports_library_lanes_and_set_timeline_view_turns_show_online()
    {
        var (service, timeline, _, _) = Chesapeake18Z();
        var controller = new ViewerTimelineController(
            service, timeline, new DatasetsViewModel(new FakeDatasetLoaderService()), new FakeTimeProvider(new DateTimeOffset(Run)),
            action => { action(); return Task.CompletedTask; });

        Assert.True((await new GetTimelineStateTool(controller).InvokeAsync()).TryGetValue(out var state));
        Assert.True(state!.ShowOnline);
        var cbofs = state.Lanes[0];
        Assert.True(cbofs.NewRun);
        Assert.False(cbofs.Library);
        Assert.Equal(["loaded", "online"], cbofs.Windows.Select(w => w.State));
        var dbofs = state.Lanes[1];
        Assert.True(dbofs.Library);
        Assert.Equal("library:S-111/dbofs", dbofs.Id);
        Assert.All(dbofs.Windows, w => Assert.StartsWith("feed:", w.ItemId, StringComparison.Ordinal));

        Assert.True((await new SetTimelineViewTool(controller).InvokeAsync(new SetTimelineViewRequest(null, null, null, null, ShowOnline: false))).TryGetValue(out var off));
        Assert.False(off!.ShowOnline);
        Assert.Single(off.Lanes);
    }

    private sealed class FakeLibrary : ILibraryTimeSource
    {
        public IReadOnlyList<LibraryTimedEntry> Entries { get; set; } = [];

        public double? Progress { get; set; }

        public List<LibraryTimedEntry> Got { get; } = [];

        public LibraryTimedEntry? Revealed { get; private set; }

        public event Action? Changed { add { } remove { } }

        public event Action? ProgressChanged;

        public void RaiseProgress() => ProgressChanged?.Invoke();

        public double? ProgressOf(LibraryTimedEntry entry) => entry.State == LibraryTimedState.Online ? Progress : null;

        public Task GetAsync(IReadOnlyList<LibraryTimedEntry> entries)
        {
            Got.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task LoadAsync(IReadOnlyList<LibraryTimedEntry> entries) => Task.CompletedTask;

        public void Reveal(LibraryTimedEntry entry) => Revealed = entry;
    }

    // ── run times follow the user's setting (#730) ─────────────────────

    [Fact]
    public void Run_times_read_one_way_on_every_lane_and_follow_the_setting()
    {
        var older = Run.AddHours(-6);
        var service = new GlobalTimeService();
        var format = new SwitchableFormat { Current = TimeFormat.Local };
        var clock = new FakeTimeProvider(new DateTimeOffset(Run.AddHours(1)));
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        clock.SetLocalTimeZone(eastern);
        var library = new FakeLibrary
        {
            Entries =
            [
                Entry("111US00_CBOFS_US4MD1DD", older, 48, LibraryTimedState.Loaded, Chesapeake),
                Entry("111US00_DBOFS_US4DE1AD", Run, 48, LibraryTimedState.Online, Delaware),
            ],
        };
        var timeline = new TimelineViewModel(service, format, clock, action => action(), scope: new FakeScope { Unknown = true }, library: library);
        Load(service, Model("cbofs", older) with { Name = "111US00_CBOFS_US4MD1DD" });
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        string Local(DateTime utc) => LayerTimes.Clock(utc, TimeFormat.Local, eastern, culture);

        var cbofs = timeline.LaneGroups[0].Lanes[0];
        var dbofs = timeline.LaneGroups[0].Lanes[1];
        Assert.Contains($"{Local(older)} run", cbofs.Sub, StringComparison.Ordinal);
        Assert.Equal($"{Local(Run)} run", dbofs.LayerTime);
        Assert.Equal($"dbofs · {Local(Run)} run", dbofs.ActionTitle);
        Assert.Equal([$"cbofs {Local(older)}"], timeline.DisplayRuns);
        Assert.Equal(["cbofs 12:00Z"], timeline.Runs);

        format.Switch(TimeFormat.Utc);

        Assert.Contains("12:00Z run", cbofs.Sub, StringComparison.Ordinal);
        Assert.Equal("18:00Z run", dbofs.LayerTime);
        Assert.Equal(["cbofs 12:00Z"], timeline.DisplayRuns);
        Assert.Equal(["cbofs 12:00Z"], timeline.Runs);
    }

    [Fact]
    public void A_run_far_from_now_carries_its_day()
    {
        var far = ForecastRunNames.FormatRun(Run, TimeFormat.Utc, TimeZoneInfo.Utc, Run.AddDays(3));

        Assert.EndsWith(" 18:00Z", far, StringComparison.Ordinal);
        Assert.NotEqual("18:00Z", far);
        Assert.Equal("18:00Z", ForecastRunNames.FormatRun(Run, TimeFormat.Utc, TimeZoneInfo.Utc, Run.AddHours(5)));
    }

    private sealed class SwitchableFormat : ITimeFormatProvider
    {
        public TimeFormat Current { get; set; }

        public event Action<TimeFormat>? TimeFormatChanged;

        public void Switch(TimeFormat format)
        {
            Current = format;
            TimeFormatChanged?.Invoke(format);
        }
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

        /// <summary>The map view for Library footprints; null before the first view.</summary>
        public GeoBounds? View { get; set; }

        public IReadOnlyList<GeoBounds> HighlightedAreas { get; private set; } = [];

        public bool? Intersects(GeoBounds bounds) => View is { } view ? bounds.Intersects(view) : null;

        public void HighlightAreas(IReadOnlyList<GeoBounds> areas, (byte R, byte G, byte B) color = default)
        {
            HighlightedAreas = areas;
            Color = color;
        }
    }
}
