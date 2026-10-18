using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Timeline's passage lanes and Follow passage (#904): the passage lane
/// (E1), the departure window lane (E2), Follow passage (E3) and the Expired
/// tag beside them (E4), on <see cref="PassageCheckFixture"/> with the
/// fixture's S-104 loaded on the Timeline.
/// </summary>
public sealed class PassageTimelineTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public PassageTimelineTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static DateTime At(int hour, int minute) => new(2024, 6, 1, hour, minute, 0, DateTimeKind.Utc);

    private static DateTimeOffset AtO(int hour, int minute) => new(At(hour, minute));

    // The day after the data: the Timeline stays pinned and the S-111 run has expired.
    private static readonly DateTimeOffset Now = new(2024, 6, 2, 0, 0, 0, TimeSpan.Zero);

    private sealed record Setup(
        PassageCheckFixture F,
        GlobalTimeService Time,
        TimelineViewModel Timeline,
        FollowPassageService Follow,
        FakeTimeProvider Clock)
    {
        public PassageTimelineViewModel Lanes => Timeline.Passage!;
    }

    private static Setup Create(bool routeDeparture = true, Func<DateTime, float>? tide = null, Action<Action>? post = null)
    {
        var time = new GlobalTimeService();
        var clock = new FakeTimeProvider(Now);
        var f = new PassageCheckFixture(tide: tide ?? PassageCheckFixture.HighWaterUntil0750, viewTime: new TimelineViewTime(time, clock), post: post);
        if (!routeDeparture)
            f.Route.SetDeparture(null);
        var timeline = new TimelineViewModel(time, new Utc(), clock, action => action());
        var follow = new FollowPassageService(f.Check, time);
        timeline.AttachPassage(f.Check, follow);

        var samples = Enumerable.Range(0, 37).Select(i => At(6, 0).AddMinutes(10 * i)).ToArray();
        time.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = At(7, 0),
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets =
            [
                new MapsuiMapTimedDataset("104FIXTURE", samples[0], samples[^1]) { ProductSpec = "S-104", DatasetId = "104FIXTURE", Samples = samples },
                new MapsuiMapTimedDataset("111FIXTURE", samples[0], samples[^1]) { ProductSpec = "S-111", DatasetId = "111FIXTURE", Samples = samples },
            ],
        });
        return new Setup(f, time, timeline, follow, clock);
    }

    private static TimelineLaneViewModel Lane(TimelineViewModel timeline, string spec) =>
        timeline.LaneGroups.SelectMany(g => g.Lanes).Single(l => l.Spec == spec);

    [Fact]
    public async Task The_passage_and_window_lanes_show_in_check_mode_with_etas()
    {
        var s = Create();
        Assert.False(s.Lanes.HasLanes);
        Assert.False(s.Lanes.ShowFollow);

        await s.F.CheckAsync();

        var lanes = s.Lanes;
        Assert.True(lanes.HasLanes);
        Assert.True(lanes.ShowFollow);
        Assert.Equal("dep 07:10Z · arr 08:46Z", lanes.PassageSub);
        Assert.InRange(lanes.PassageStart, 0.15, 0.25);   // 07:10 on a 06:00–12:00 axis
        Assert.True(lanes.PassageEnd > lanes.PassageStart);
        Assert.InRange(lanes.MinimumPosition, lanes.PassageStart, lanes.PassageEnd);
        Assert.Equal(CheckTone.Destructive, lanes.Tone);
        Assert.Equal(AtO(7, 10), lanes.Departure);
        Assert.Equal(AtO(8, 46), lanes.Arrival!.Value, TimeSpan.FromMinutes(1));

        Assert.True(lanes.HasWindow);
        Assert.Equal("06:00Z–06:40Z", lanes.WindowSub);
        Assert.Equal(
            [DepartureBandKind.Meets, DepartureBandKind.Below, DepartureBandKind.ArrivalAfterDataEnds],
            lanes.WindowSegments.Select(x => x.Kind));
        Assert.Equal(lanes.PassageStart, lanes.DeparturePosition!.Value, 6);
        Assert.Equal(0, lanes.WindowSegments[0].Start, 3);
        // The window lane ends where the S-104 data does (the axis runs on to now, collapsed).
        var data = s.Timeline.CoverageBands[^1];
        Assert.InRange(lanes.WindowSegments[^1].End, data.Start + data.Width - 0.001, data.Start + data.Width + 0.01);

        // E4: the expired S-111 run keeps its tag beside the passage lanes.
        var currents = Lane(s.Timeline, "S-111");
        Assert.True(currents.IsExpired);
        Assert.True(currents.IsExpiredTag);

        // Without ETAs (no speed) or with Check passage off, the lanes go.
        s.F.Route.SetPassageValue(RoutePassageField.Speed, null);
        await s.F.Check.WhenIdle();
        Assert.False(lanes.HasLanes);
        s.F.Route.SetPassageValue(RoutePassageField.Speed, 6);
        await s.F.Check.WhenIdle();
        Assert.True(lanes.HasLanes);
        s.F.Routes.IsCheckModeActive = false;
        Assert.False(lanes.HasLanes);
    }

    [Fact]
    public async Task The_lanes_follow_departure_and_speed_changes()
    {
        var s = Create();
        await s.F.CheckAsync();
        var lanes = s.Lanes;
        var start = lanes.PassageStart;
        var length = lanes.PassageEnd - lanes.PassageStart;
        var window = lanes.WindowSegments;

        s.F.Check.SetDeparture(AtO(6, 40));
        await s.F.Check.WhenIdle();
        Assert.True(lanes.PassageStart < start);
        Assert.Equal(lanes.PassageStart, lanes.DeparturePosition!.Value, 6);
        Assert.Equal(CheckTone.Success, lanes.Tone);
        Assert.Equal("dep 06:40Z · arr 08:16Z", lanes.PassageSub);
        Assert.Same(window, lanes.WindowSegments);

        // Twice the speed: half the passage, and the window is swept again.
        s.F.Route.SetPassageValue(RoutePassageField.Speed, 12);
        await s.F.Check.WhenIdle();
        Assert.Equal(length / 2, lanes.PassageEnd - lanes.PassageStart, 2);
        Assert.NotSame(window, lanes.WindowSegments);
    }

    [Fact]
    public async Task Dragging_the_window_lane_sets_the_departure_in_one_undo_step()
    {
        var s = Create();
        await s.F.CheckAsync();
        double Pos(DateTime t) => Math.Clamp(PositionOf(s, t), 0, 1);
        var undo = s.F.Route.History.UndoCount;

        s.Lanes.BeginDrag(Pos(At(7, 30)));
        await s.F.Check.WhenIdle();
        Assert.Equal(AtO(7, 30), s.F.Check.Departure);
        s.Lanes.DragTo(Pos(At(6, 52)));
        s.Lanes.DragTo(Pos(At(6, 31)));
        s.Lanes.EndDrag();
        await s.F.Check.WhenIdle();

        Assert.Equal(AtO(6, 30), s.F.Route.Passage.Departure);
        Assert.Equal(undo + 1, s.F.Route.History.UndoCount);
        Assert.Equal(PassageVerdict.MeetsSettings, s.F.Check.Current!.Result.Verdict);

        s.Lanes.Nudge(-1);
        await s.F.Check.WhenIdle();
        Assert.Equal(AtO(6, 25), s.F.Route.Passage.Departure);
    }

    // The window lane's departure tick sits at the departure's position, so
    // it gives the axis mapping without reaching into the Timeline.
    private static double PositionOf(Setup s, DateTime time)
    {
        var lanes = s.Lanes;
        var a = lanes.PassageStart;
        var b = lanes.PassageEnd;
        var from = lanes.Departure!.Value.UtcDateTime;
        var to = lanes.Arrival!.Value.UtcDateTime;
        // The 06:00–12:00 axis has no gaps, so positions are linear in time.
        return a + ((time - from).TotalMinutes * (b - a) / (to - from).TotalMinutes);
    }

    [Fact]
    public async Task Follow_passage_pins_the_view_time_to_the_cursor_eta_and_off_leaves_it()
    {
        var s = Create();
        await s.F.CheckAsync();
        var tide = Lane(s.Timeline, "S-104");
        var before = tide.LayerTime;
        Assert.True(s.Lanes.CanFollow);

        Assert.True(s.Follow.SetOn(true));
        Assert.True(s.Lanes.IsFollowing);
        Assert.Equal(TimeMode.Pinned, s.Time.Mode);

        // No cursor: the view time is the ETA at the minimum UKC.
        var min = s.F.Check.Current!.Result.Minimum!;
        Assert.Equal(min.Time.UtcDateTime, s.Time.CurrentTime);
        Assert.NotEqual(before, tide.LayerTime);

        // Moving the cursor moves the view time, and the S-104 lane's time with it.
        s.F.Check.SetCursor(3 * PassageGeodesy.MetersPerNauticalMile);
        var cursorEta = s.F.Check.Cursor!.Value.Eta!.Value;
        Assert.Equal(cursorEta.UtcDateTime, s.Time.CurrentTime);
        Assert.Contains("07:40", tide.LayerTime, StringComparison.Ordinal);
        Assert.Equal(AtO(7, 10), s.F.Route.Passage.Departure);

        // Off: the view time stays where it was, and the cursor no longer moves it.
        Assert.True(s.Follow.SetOn(false));
        Assert.False(s.Lanes.IsFollowing);
        s.F.Check.SetCursor(6 * PassageGeodesy.MetersPerNauticalMile);
        Assert.Equal(cursorEta.UtcDateTime, s.Time.CurrentTime);
    }

    [Fact]
    public async Task Choosing_another_time_or_going_live_turns_follow_passage_off()
    {
        var s = Create();
        await s.F.CheckAsync();
        s.Follow.SetOn(true);

        s.Time.SetCurrentTime(At(11, 0));
        Assert.False(s.Follow.IsOn);
        Assert.Equal(At(11, 0), s.Time.CurrentTime);

        s.Follow.SetOn(true);
        s.Timeline.NowCommand.Execute(null);
        Assert.False(s.Follow.IsOn);

        s.Follow.SetOn(true);
        s.F.Routes.IsCheckModeActive = false;
        Assert.False(s.Follow.IsOn);
        Assert.False(s.Follow.SetOn(true));
    }

    [Fact]
    public async Task Following_a_route_without_a_departure_fixes_it_at_the_view_time_first()
    {
        var s = Create(routeDeparture: false);
        await s.F.CheckAsync();
        Assert.Equal(AtO(7, 0), s.F.Check.Departure);
        Assert.False(s.F.Check.HasRouteDeparture);
        var undo = s.F.Route.History.UndoCount;

        s.Follow.SetOn(true);
        await s.F.Check.WhenIdle();

        // The departure no longer tracks the view time, so following doesn't move it.
        Assert.Equal(AtO(7, 0), s.F.Route.Passage.Departure);
        Assert.Equal(undo + 1, s.F.Route.History.UndoCount);
        s.F.Check.SetCursor(2 * PassageGeodesy.MetersPerNauticalMile);
        await s.F.Check.WhenIdle();
        Assert.Equal(s.F.Check.Cursor!.Value.Eta!.Value.UtcDateTime, s.Time.CurrentTime);
        Assert.Equal(AtO(7, 0), s.F.Check.Departure);
    }

    [Fact]
    public async Task Mcp_reports_the_passage_lanes_and_sets_follow_passage()
    {
        var s = Create();
        await s.F.CheckAsync();
        var controller = new ViewerTimelineController(
            s.Time, s.Timeline, new DatasetsViewModel(new FakeDatasetLoaderService()), s.Clock,
            action => { action(); return Task.CompletedTask; });
        var ct = TestContext.Current.CancellationToken;

        Assert.True((await new GetTimelineStateTool(controller).InvokeAsync(ct)).TryGetValue(out var state));
        var passage = Assert.IsType<TimelinePassageDto>(state!.Passage);
        Assert.False(state.FollowPassage);
        Assert.Equal(AtO(7, 10), passage.Departure);
        Assert.Equal(AtO(8, 46), passage.Arrival, TimeSpan.FromMinutes(1));
        Assert.Equal("below_setting", passage.Verdict);
        Assert.True(passage.CanFollow);
        Assert.Equal("06:00Z–06:40Z", passage.Windows);
        Assert.Equal(["meets_settings", "below_setting", "arrival_after_data_ends"], passage.WindowSegments.Select(x => x.Kind));
        Assert.Equal(passage.Start, passage.DeparturePosition!.Value, 3);

        var tool = new SetFollowPassageTool(s.Follow, s.F.Check, new InlineInvoker(), controller);
        Assert.True((await tool.InvokeAsync(new SetFollowPassageRequest(true), ct)).TryGetValue(out var on));
        Assert.True(on!.FollowPassage);
        Assert.Equal("pinned", on.Mode);
        Assert.Equal(passage.MinUkcTime!.Value.UtcDateTime, on.ViewTime);

        Assert.True((await tool.InvokeAsync(new SetFollowPassageRequest(false), ct)).TryGetValue(out var off));
        Assert.False(off!.FollowPassage);
        Assert.Equal(on.ViewTime, off.ViewTime);

        s.F.Routes.IsCheckModeActive = false;
        Assert.True((await tool.InvokeAsync(new SetFollowPassageRequest(true), ct)).TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
        Assert.True((await new GetTimelineStateTool(controller).InvokeAsync(ct)).TryGetValue(out var cleared));
        Assert.Null(cleared!.Passage);
    }

    [Fact]
    public void Timeline_passage_wire_properties_have_descriptions()
    {
        var missing = new[] { typeof(TimelinePassageDto), typeof(SetFollowPassageRequest), typeof(TimelineStateDto) }
            .SelectMany(t => t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(p => p.Name != "EqualityContract")
                .Where(p => string.IsNullOrWhiteSpace(System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>(p)?.Description))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        Assert.Empty(missing);
    }

    [AvaloniaFact]
    public void The_timeline_view_shows_the_lanes_drags_the_window_and_toggles_follow_passage()
    {
        var s = Create(post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        using var host = Headless.ViewHost.Show(new Views.TimelineView { DataContext = s.Timeline }, width: 1100, height: 360);
        Assert.False(host.IsShown("Timeline.PassageLane"));
        Assert.False(host.IsShown("Timeline.FollowPassage"));

        s.F.Routes.IsCheckModeActive = true;
        Settle(host, s);

        Assert.True(host.IsShown("Timeline.PassageLane"));
        Assert.True(host.IsShown("Timeline.WindowLane"));
        Assert.Equal("dep 07:10Z · arr 08:46Z", host.Find<Avalonia.Controls.TextBlock>("Timeline.PassageLane.Times").Text);
        Assert.Equal("06:00Z–06:40Z", host.Find<Avalonia.Controls.TextBlock>("Timeline.WindowLane.Windows").Text);

        // Drag the window lane from 07:30 to 06:30 with the pointer.
        var lane = host.Find<Views.DepartureBandControl>("Timeline.WindowLane");
        Avalonia.Point At(double position) =>
            lane.TranslatePoint(new Avalonia.Point(position * lane.Bounds.Width, lane.Bounds.Height / 2), host.Window)!.Value;
        var from = PositionOf(s, PassageTimelineTests.At(7, 30));
        var to = PositionOf(s, PassageTimelineTests.At(6, 30));
        host.Window.MouseDown(At(from), Avalonia.Input.MouseButton.Left);
        host.Window.MouseMove(At(to));
        host.Window.MouseUp(At(to), Avalonia.Input.MouseButton.Left);
        Settle(host, s);
        Assert.Equal(AtO(6, 30), s.F.Route.Passage.Departure);

        var follow = host.Find<Avalonia.Controls.Primitives.ToggleButton>("Timeline.FollowPassage");
        Assert.True(host.IsShown("Timeline.FollowPassage"));
        host.Click(follow);
        Settle(host, s);
        Assert.True(s.Follow.IsOn);
        Assert.True(follow.IsChecked);
        Assert.Equal(s.F.Check.Current!.Result.Minimum!.Time.UtcDateTime, s.Time.CurrentTime);
        host.Click(follow);
        Settle(host, s);
        Assert.False(s.Follow.IsOn);

        var frames = Environment.GetEnvironmentVariable("SCRATCH_FRAMES");
        if (frames is not null)
            File.WriteAllBytes(Path.Combine(frames, "timeline-passage.png"), host.CaptureFrame());
    }

    private static void Settle(Headless.ViewHost host, Setup s)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do
        {
            host.Settle();
            if (!s.F.Check.IsComputing && !s.F.Check.IsWindowComputing && s.F.Check.Current is not null)
            {
                host.Settle();
                return;
            }

            Thread.Sleep(10);
        }
        while (DateTime.UtcNow < deadline);
    }
}
