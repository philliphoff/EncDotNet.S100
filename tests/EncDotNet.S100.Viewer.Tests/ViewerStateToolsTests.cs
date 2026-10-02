using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.Tests.Notifications;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The viewer-state MCP tools and their controllers (#715 slice 1).</summary>
public sealed class ViewerStateToolsTests
{
    private static readonly DateTime Run = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static Task Immediate(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    /// <summary>A cbofs run: hourly steps for 48 h.</summary>
    private static MapsuiMapTimeSnapshot RunSnapshot()
    {
        var samples = Enumerable.Range(0, 49).Select(h => Run.AddHours(h)).ToArray();
        return new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets = [new MapsuiMapTimedDataset("111US00_CBOFS_20260930T12Z_US4VA1DD", samples[0], samples[^1])],
        };
    }

    private static (GlobalTimeService Time, ViewerTimelineController Controller, FakeTimeProvider Clock) Timeline(DateTime now)
    {
        var time = new GlobalTimeService();
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        var timeline = new TimelineViewModel(time, timeFormat: null, clock, action => action());
        var controller = new ViewerTimelineController(
            time, timeline, new DatasetsViewModel(new FakeDatasetLoaderService()), clock, Immediate);
        return (time, controller, clock);
    }

    // ── set_view_time / get_timeline_state ─────────────────────────────

    [Fact]
    public async Task Now_follows_and_reports_live()
    {
        var (time, controller, _) = Timeline(Run.AddHours(9).AddMinutes(10));
        time.ApplySnapshot(RunSnapshot());
        var tool = new SetViewTimeTool(controller);
        Assert.True((await tool.InvokeAsync(new SetViewTimeRequest("+2h", null))).TryGetValue(out var pinned));
        Assert.Equal("pinned", pinned!.Mode);

        var result = await tool.InvokeAsync(new SetViewTimeRequest("now", null));

        Assert.True(result.TryGetValue(out var state));
        Assert.Equal("live", state!.Mode);
        Assert.Equal(Run.AddHours(9).AddMinutes(10), state.ViewTime);
        Assert.Equal("now", state.Offset);
        Assert.Equal(["cbofs 12:00Z"], state.Runs);
    }

    [Fact]
    public async Task Exact_times_are_kept_and_nearest_snaps_to_a_sample()
    {
        var (time, controller, _) = Timeline(Run.AddHours(9));
        time.ApplySnapshot(RunSnapshot());
        var tool = new SetViewTimeTool(controller);

        Assert.True((await tool.InvokeAsync(new SetViewTimeRequest("2026-09-30T15:20:00Z", null))).TryGetValue(out var exact));
        Assert.Equal(Run.AddHours(3).AddMinutes(20), exact!.ViewTime);
        Assert.Equal("pinned", exact.Mode);

        Assert.True((await tool.InvokeAsync(new SetViewTimeRequest("+25m", "nearest"))).TryGetValue(out var nearest));
        Assert.Equal(Run.AddHours(4), nearest!.ViewTime);
    }

    [Fact]
    public async Task Now_past_every_window_goes_live_and_says_the_forecast_ended()
    {
        var (time, controller, _) = Timeline(Run.AddDays(5));
        time.ApplySnapshot(RunSnapshot());

        var result = await new SetViewTimeTool(controller).InvokeAsync(new SetViewTimeRequest("now", null));

        Assert.True(result.TryGetValue(out var state));
        Assert.Equal("live", state!.Mode);
        Assert.Equal(Run.AddDays(5), state.ViewTime);
        Assert.Equal("now", state.Offset);
        Assert.StartsWith("Every forecast ended", state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_loaded_is_not_applied()
    {
        var (_, controller, _) = Timeline(Run);

        var result = await new SetViewTimeTool(controller).InvokeAsync(new SetViewTimeRequest("now", null));

        Assert.True(result.TryGetError(out var error));
        Assert.Equal("view_time_not_applied", error!.Code);
    }

    [Theory]
    [InlineData("tomorrow", null, "time")]
    [InlineData("now", "closest", "snap")]
    public async Task Bad_arguments_are_rejected(string time, string? snap, string parameter)
    {
        var (_, controller, _) = Timeline(Run);

        var result = await new SetViewTimeTool(controller).InvokeAsync(new SetViewTimeRequest(time, snap));

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(parameter, Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Theory]
    [InlineData("+6h", 6 * 60)]
    [InlineData("-30m", -30)]
    [InlineData("+1d", 24 * 60)]
    [InlineData("+1.5h", 90)]
    public void Offsets_parse(string text, int minutes)
    {
        Assert.True(SetViewTimeTool.TryParseOffset(text, out var offset));
        Assert.Equal(TimeSpan.FromMinutes(minutes), offset);
    }

    [Fact]
    public async Task A_hidden_layer_reports_drawnTime_as_an_explicit_null()
    {
        var (time, controller, _) = Timeline(Run.AddHours(9));
        time.ApplySnapshot(RunSnapshot());
        var tool = new GetTimelineStateTool(controller);
        Assert.True((await tool.InvokeAsync()).TryGetValue(out var state));
        var layer = new TimelineLayerDto("levels.h5", "S-104", true, null, null, null, 0);

        var json = System.Text.Json.JsonSerializer.Serialize(state! with { Layers = [layer] }, McpAdapterShared.Options);

        Assert.Contains("\"drawnTime\":null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("previousSample", json, StringComparison.Ordinal);
    }

    // ── set_dataset_state ──────────────────────────────────────────────

    [Fact]
    public async Task Dataset_state_shows_a_hidden_dataset_and_reports_the_change()
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var entry = new DatasetEntry("/data/104US004SC1BO_20251217T12Z.h5", "S-104") { IsVisible = false };
        datasets.Entries.Add(entry);
        var tool = new SetDatasetStateTool(new ViewerDatasetStateController(datasets, Immediate));

        var result = await tool.InvokeAsync(new SetDatasetStateRequest(entry.DisplayName, true, 0.5));

        Assert.True(result.TryGetValue(out var state));
        Assert.True(state!.Visible);
        Assert.False(state.PreviousVisible);
        Assert.Equal(0.5, state.Opacity);
        Assert.True(state.Changed);
        Assert.True(entry.IsVisible);
    }

    [Fact]
    public async Task Dataset_state_rejects_unknown_ids_and_bad_opacity()
    {
        var tool = new SetDatasetStateTool(new ViewerDatasetStateController(
            new DatasetsViewModel(new FakeDatasetLoaderService()), Immediate));

        Assert.True((await tool.InvokeAsync(new SetDatasetStateRequest("missing.h5", true, null))).TryGetError(out var missing));
        Assert.Equal("dataset_not_found", missing!.Code);
        Assert.True((await tool.InvokeAsync(new SetDatasetStateRequest("missing.h5", null, 1.5))).TryGetError(out var opacity));
        Assert.Equal("opacity", Assert.IsType<InvalidArgument>(opacity).Parameter);
    }

    // ── list_notifications / dismiss_notification ──────────────────────

    [Fact]
    public async Task Notifications_are_listed_and_dismissed()
    {
        var service = TestNotifications.Create();
        service.Create("Viewer recovered").WithSeverity(NotificationSeverity.Warning).Persistent().Show();
        service.Create("Downloaded 3 tiles").Show();
        var controller = new ViewerNotificationController(service, Immediate);

        Assert.True((await new ListNotificationsTool(controller).InvokeAsync()).TryGetValue(out var listed));
        Assert.Equal(["Viewer recovered", "Downloaded 3 tiles"], listed!.Notifications.Select(n => n.Title));
        Assert.Equal("warning", listed.Notifications[0].Severity);

        var dismiss = new DismissNotificationTool(controller);
        Assert.True((await dismiss.InvokeAsync(new DismissNotificationRequest(listed.Notifications[0].Id.ToString()))).TryGetValue(out var one));
        Assert.Single(one!.Dismissed);
        Assert.Single(service.Active);

        Assert.True((await dismiss.InvokeAsync(new DismissNotificationRequest(null))).TryGetValue(out _));
        Assert.Empty(service.Active);

        Assert.True((await dismiss.InvokeAsync(new DismissNotificationRequest(Guid.NewGuid().ToString()))).TryGetError(out var missing));
        Assert.Equal("notification_not_found", missing!.Code);
    }

    // ── set_test_clock / AdjustableTimeProvider ────────────────────────

    [Fact]
    public void Test_clock_moves_freezes_and_resets()
    {
        var real = new FakeTimeProvider(new DateTimeOffset(Run));
        var clock = new AdjustableTimeProvider(real);
        var tool = new SetTestClockTool(clock);

        Assert.True(tool.Invoke(new SetTestClockRequest(null, "+1h", null, null)).TryGetValue(out var advanced));
        Assert.Equal(new DateTimeOffset(Run.AddHours(1)), advanced!.Now);

        Assert.True(tool.Invoke(new SetTestClockRequest("2026-10-02T06:00:00Z", null, true, null)).TryGetValue(out var frozen));
        Assert.True(frozen!.Frozen);
        real.Advance(TimeSpan.FromHours(3));
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 6, 0, 0, TimeSpan.Zero), clock.GetUtcNow());

        Assert.True(tool.Invoke(new SetTestClockRequest(null, null, null, true)).TryGetValue(out var reset));
        Assert.Equal(real.GetUtcNow(), reset!.Now);

        Assert.True(tool.Invoke(new SetTestClockRequest("2026-10-02T06:00:00Z", "+1h", null, null)).TryGetError(out var both));
        Assert.IsType<InvalidArgument>(both);
    }

    [Fact]
    public void Adjusting_the_clock_fires_periodic_timers_but_not_one_shot_ones()
    {
        var clock = new AdjustableTimeProvider(new FakeTimeProvider(new DateTimeOffset(Run)));
        var ticks = 0;
        var oneShots = 0;
        using var minute = clock.CreateTimer(_ => ticks++, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        using var once = clock.CreateTimer(_ => oneShots++, null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, ticks);
        Assert.Equal(0, oneShots);

        minute.Dispose();
        clock.Reset();
        Assert.Equal(1, ticks);
    }

    [Fact]
    public void Moving_the_test_clock_moves_a_live_timeline_at_once()
    {
        var time = new GlobalTimeService();
        var clock = new AdjustableTimeProvider(new FakeTimeProvider(new DateTimeOffset(Run.AddHours(9))));
        _ = new TimelineViewModel(time, timeFormat: null, clock, action => action());
        time.ApplySnapshot(RunSnapshot());
        Assert.Equal(TimeMode.Live, time.Mode);

        new SetTestClockTool(clock).Invoke(new SetTestClockRequest(null, "+3h", null, null));

        Assert.Equal(Run.AddHours(12), time.CurrentTime);
    }

    // ── adapters ───────────────────────────────────────────────────────

    [Fact]
    public void Every_adapter_builds_its_tool()
    {
        var (_, timeline, _) = Timeline(Run);
        var notifications = new ViewerNotificationController(TestNotifications.Create(), Immediate);
        var tools = new[]
        {
            ViewerStateMcpAdapters.Create(new GetTimelineStateTool(timeline)),
            ViewerStateMcpAdapters.Create(new SetViewTimeTool(timeline)),
            ViewerStateMcpAdapters.Create(new SetDatasetStateTool(new ViewerDatasetStateController(
                new DatasetsViewModel(new FakeDatasetLoaderService()), Immediate))),
            ViewerStateMcpAdapters.Create(new ListNotificationsTool(notifications)),
            ViewerStateMcpAdapters.Create(new DismissNotificationTool(notifications)),
            ViewerStateMcpAdapters.Create(new SetTestClockTool(new AdjustableTimeProvider())),
        };

        Assert.Equal(
            ["get_timeline_state", "set_view_time", "set_dataset_state", "list_notifications", "dismiss_notification", "set_test_clock"],
            tools.Select(tool => tool.ProtocolTool.Name));
    }
}
