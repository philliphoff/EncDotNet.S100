using System.Collections.Concurrent;
using System.Diagnostics;
using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The settled signal, the metric arithmetic and the harness tools of map
/// rendering slice 1, against fakes. The end-to-end jumps run needs the NOAA
/// "Washington" source and is run by hand (see the PerfRunner README).
/// </summary>
public sealed class PerfHarnessTests
{
    private static readonly TileSettleState TilesSettled = new(2, 0, 0, 0, 0, LatestFrameTimestamp: 1_000);
    private static readonly LazyLoadVisibleWork LoadsDone = new(false, 0, 0, 3, 1, 10, 200, LastLoadFinishedTimestamp: 500);

    // ── Settled decision ─────────────────────────────────────────────

    [Fact]
    public void IsSettled_LoadsDoneAndTilesExactAfterTrigger_IsTrue() =>
        Assert.True(ViewSettleMonitor.IsSettled(new SettleSample(2_000, LoadsDone, TilesSettled), triggerTimestamp: 100));

    [Fact]
    public void IsSettled_SpeculativeWorkDoesNotHoldItOpen() =>
        // QueuedOther / RunningOther are ahead, covered and unbounded cells.
        Assert.True(ViewSettleMonitor.IsSettled(
            new SettleSample(2_000, LoadsDone with { QueuedOther = 40, RunningOther = 1 }, TilesSettled), 100));

    [Theory]
    [InlineData(true, 0, 0)]
    [InlineData(false, 1, 0)]
    [InlineData(false, 0, 1)]
    public void IsSettled_VisibleLoadWork_IsFalse(bool evaluationPending, int queued, int running)
    {
        var loads = LoadsDone with { EvaluationPending = evaluationPending, QueuedVisible = queued, RunningVisible = running };
        Assert.False(ViewSettleMonitor.IsSettled(new SettleSample(2_000, loads, TilesSettled), 100));
    }

    [Fact]
    public void IsSettled_TilesNotSettled_IsFalse()
    {
        var tiles = TilesSettled with { LayersIncomplete = 1, MissingTiles = 3 };
        Assert.False(ViewSettleMonitor.IsSettled(new SettleSample(2_000, LoadsDone, tiles), 100));
    }

    [Fact]
    public void IsSettled_LatestFrameBeforeTrigger_IsFalse() =>
        // The tiles were exact for the old view; the new view hasn't painted.
        Assert.False(ViewSettleMonitor.IsSettled(new SettleSample(2_000, LoadsDone, TilesSettled), triggerTimestamp: 1_500));

    [Fact]
    public void IsSettled_LatestFrameBeforeLastLoadFinished_IsFalse() =>
        // A cell finished loading after the last frame: its layer hasn't painted yet.
        Assert.False(ViewSettleMonitor.IsSettled(
            new SettleSample(2_000, LoadsDone with { LastLoadFinishedTimestamp = 1_200 }, TilesSettled), 100));

    [Fact]
    public void IsSettled_NoCoordinator_UsesTilesOnly() =>
        Assert.True(ViewSettleMonitor.IsSettled(new SettleSample(2_000, null, TilesSettled), 100));

    // ── Settle wait ──────────────────────────────────────────────────

    [Fact]
    public async Task WaitForSettled_ReportsTimeToTheSettlingFrame_NotToTheEndOfConfirmation()
    {
        bool? tracking = null;
        var monitor = new ViewSettleMonitor(enabled => tracking = enabled);
        monitor.MarkTrigger();
        var settledFrame = 0L;
        var reads = 0;
        monitor.LoadProbe = () => LoadsDone with { LastLoadFinishedTimestamp = 0 };
        monitor.TileProbe = () =>
        {
            // Unsettled for the first few samples, then a frame completes the view.
            if (Interlocked.Increment(ref reads) < 4)
                return new TileSettleState(1, 0, 1, 2, 2, Stopwatch.GetTimestamp());
            if (settledFrame == 0)
                settledFrame = Stopwatch.GetTimestamp();
            return new TileSettleState(1, 0, 0, 0, 0, settledFrame);
        };

        var outcome = await monitor.WaitForSettledAsync(
            confirm: TimeSpan.FromMilliseconds(100),
            timeout: TimeSpan.FromSeconds(10),
            poll: TimeSpan.FromMilliseconds(5),
            TestContext.Current.CancellationToken);

        Assert.True(tracking);
        Assert.True(outcome.Settled);
        Assert.False(outcome.TimedOut);
        Assert.True(outcome.HadTrigger);
        Assert.NotNull(outcome.SettleMs);
        // Settle time excludes the confirmation period the call also waited.
        Assert.True(outcome.WaitedMs >= 100);
        Assert.True(outcome.SettleMs < outcome.WaitedMs + outcome.TriggerAgeMs - 90);
    }

    [Fact]
    public async Task WaitForSettled_RestartsConfirmationWhenTheViewUnsettles()
    {
        var monitor = new ViewSettleMonitor(_ => { });
        monitor.MarkTrigger();
        var reads = 0;
        monitor.TileProbe = () =>
        {
            var n = Interlocked.Increment(ref reads);
            // Settled, then briefly not (a just-loaded cell's layer arrives), then settled.
            return n is >= 3 and <= 5
                ? new TileSettleState(1, 1, 0, 0, 0, Stopwatch.GetTimestamp())
                : new TileSettleState(1, 0, 0, 0, 0, Stopwatch.GetTimestamp());
        };

        var outcome = await monitor.WaitForSettledAsync(
            TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(5),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.Settled);
        Assert.True(reads > 6);
    }

    [Fact]
    public async Task WaitForSettled_NeverSettles_TimesOut()
    {
        var monitor = new ViewSettleMonitor(_ => { });
        monitor.TileProbe = () => new TileSettleState(1, 1, 0, 0, 0, 0);

        var outcome = await monitor.WaitForSettledAsync(
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(5),
            TestContext.Current.CancellationToken);

        Assert.False(outcome.Settled);
        Assert.True(outcome.TimedOut);
        Assert.Null(outcome.SettleMs);
        Assert.False(outcome.HadTrigger);
        Assert.Equal(1, outcome.Last.Tiles.LayersNotPainted);
    }

    [Fact]
    public void EnableTracking_TurnsTrackingOnAndRequestsOneRedraw()
    {
        var calls = new List<bool>();
        var redraws = 0;
        var monitor = new ViewSettleMonitor(calls.Add) { RequestRedraw = () => redraws++ };

        Assert.False(monitor.IsTracking);
        monitor.EnableTracking();
        monitor.MarkTrigger();

        Assert.True(monitor.IsTracking);
        Assert.Equal([true], calls);
        Assert.Equal(1, redraws);
    }

    // ── Coordinator visible work ─────────────────────────────────────

    [Fact]
    public async Task VisibleWork_CountsInViewLoadsUntilTheyFinish()
    {
        var notifier = new FakeNotifier();
        var release = new TaskCompletionSource();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (_, _) => release.Task,
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 1 },
            marshal: null,
            hasVisibleTileWork: null,
            unloadMany: null,
            planner: null);

        coordinator.Register([Cell("A", 40, -75, 41, -74.5), Cell("B", 40, -74.5, 41, -74)]);
        notifier.Publish(Viewport(40, -75, 41, -74));

        var during = coordinator.GetVisibleWork();
        Assert.False(during.EvaluationPending);
        Assert.Equal(1, during.RunningVisible);
        Assert.Equal(1, during.QueuedVisible);
        Assert.False(during.VisibleLoaded);

        release.SetResult();
        Assert.True(await WaitUntilAsync(() => coordinator.GetVisibleWork().VisibleLoaded));
        var after = coordinator.GetVisibleWork();
        Assert.Equal(2, after.Loaded);
        Assert.True(after.LastLoadFinishedTimestamp > 0);
    }

    [Fact]
    public async Task VisibleWork_ViewportChangeInsideTheDebounce_IsPending()
    {
        var notifier = new FakeNotifier();
        var loaded = new ConcurrentBag<DatasetEntry>();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (entry, _) => { loaded.Add(entry); return Task.CompletedTask; },
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.FromMilliseconds(80) },
            marshal: null,
            hasVisibleTileWork: null,
            unloadMany: null,
            planner: null);
        coordinator.Register([Cell("A", 40, -75, 41, -74)]);

        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(coordinator.GetVisibleWork().EvaluationPending);
        Assert.True(await WaitUntilAsync(() => coordinator.GetVisibleWork().VisibleLoaded));
        Assert.Single(loaded);
    }

    [Fact]
    public async Task VisibleWork_AheadCellsDoNotCount()
    {
        var notifier = new FakeNotifier();
        var release = new TaskCompletionSource();
        using var coordinator = new ExchangeSetLazyLoadCoordinator(
            notifier,
            (_, _) => release.Task,
            _ => { },
            new LazyLoadOptions { ViewportDebounce = TimeSpan.Zero, MaxConcurrency = 2 },
            marshal: null,
            hasVisibleTileWork: null,
            unloadMany: null,
            planner: null);

        // Just east of the view, inside the ahead margin.
        coordinator.Register([Cell("Ahead", 40, -73.9, 41, -73.6)]);
        notifier.Publish(Viewport(40, -75, 41, -74));

        Assert.True(await WaitUntilAsync(() => coordinator.GetVisibleWork().RunningOther == 1));
        Assert.True(coordinator.GetVisibleWork().VisibleLoaded);
        release.SetResult();
    }

    // ── Metric arithmetic ────────────────────────────────────────────

    [Fact]
    public void MetricsWindow_DerivesCoresBusyPauseShareAndDeltas()
    {
        var second = Stopwatch.Frequency;
        var start = Reading(0, cpuMs: 1_000, pauseMs: 100, gen: (10, 4, 1), allocated: 1_000, workingSet: 500);
        var end = Reading(2 * second, cpuMs: 9_000, pauseMs: 600, gen: (30, 9, 3), allocated: 4_001_000, workingSet: 700);
        var peaks = ProcessMetricsPeaks.Of(start).With(Reading(second, 0, 0, (0, 0, 0), 0, workingSet: 900));

        var w = ProcessMetricsWindow.Compute(start, end, peaks, processorCount: 16);

        Assert.Equal(2_000, w.WallMs, 6);
        Assert.Equal(8_000, w.CpuMs, 6);
        Assert.Equal(4, w.CoresBusy, 6);
        Assert.Equal(0.25, w.CpuUtilisation, 6);
        Assert.Equal(500, w.GcPauseMs, 6);
        Assert.Equal(0.25, w.GcPauseShare, 6);
        Assert.Equal((20, 5, 2), (w.Gen0Collections, w.Gen1Collections, w.Gen2Collections));
        Assert.Equal(4_000_000, w.AllocatedBytes);
        Assert.Equal(2_000_000, w.AllocationRateBytesPerSecond, 6);
        Assert.Equal(900, w.Peaks.WorkingSetBytes);
        Assert.Equal(4, w.Peaks.Samples);
    }

    [Fact]
    public void MetricsWindow_ZeroWallTime_ReportsZeroRates()
    {
        var r = Reading(0, 10, 10, (0, 0, 0), 0, 1);
        var w = ProcessMetricsWindow.Compute(r, r, ProcessMetricsPeaks.Of(r), 8);
        Assert.Equal(0, w.CoresBusy);
        Assert.Equal(0, w.GcPauseShare);
        Assert.Equal(0, w.AllocationRateBytesPerSecond);
    }

    [Fact]
    public void Sampler_TracksPeaksOnlyWhileAWindowIsOpen()
    {
        var source = new FakeSource();
        using var sampler = new ProcessMetricsSampler(source, interval: TimeSpan.FromHours(1), processorCount: 4, uiStalls: null);

        source.WorkingSet = 5_000;
        sampler.SampleOnce();
        Assert.Null(sampler.ReadWindow(restart: false));

        source.WorkingSet = 100;
        sampler.StartWindow();
        source.WorkingSet = 3_000;
        sampler.SampleOnce();
        source.WorkingSet = 200;
        var first = sampler.ReadWindow(restart: true);

        Assert.NotNull(first);
        Assert.Equal(3_000, first.Peaks.WorkingSetBytes);

        // The restarted window starts at the last reading, so the old peak is gone.
        var second = sampler.ReadWindow(restart: false);
        Assert.NotNull(second);
        Assert.Equal(200, second.Peaks.WorkingSetBytes);

        sampler.StopWindow();
        Assert.False(sampler.IsWindowOpen);
    }

    [Fact]
    public async Task Sampler_ReportsUiThreadDelaysOverTheWindow()
    {
        // A fake UI thread that holds every probe for about 20 ms, as a busy
        // UI thread would.
        using var stalls = new UiThreadStallMonitor(action => ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(20);
            action();
        }));
        using var sampler = new ProcessMetricsSampler(
            new FakeSource(), interval: TimeSpan.FromHours(1), processorCount: 4, uiStalls: stalls);
        var tool = new GetProcessMetricsTool(sampler, renderMonitor: null, () => Environment(), tileCount: null, schedulerStats: null, predictionStats: null);

        Assert.True(tool.Invoke("start").TryGetValue(out _));
        var deadline = Stopwatch.StartNew();
        while (stalls.Snapshot().Count < 3 && deadline.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(tool.Invoke("restart").TryGetValue(out var metrics));

        var ui = metrics!.Window!.UiThread;
        Assert.NotNull(ui);
        Assert.True(ui.Count >= 3);
        Assert.InRange(ui.MaxMs, 18, 2_000);
        Assert.Equal(ui.Count, ui.Over8Ms);

        sampler.StopWindow();
        var stopped = stalls.Snapshot().Count;
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.InRange(stalls.Snapshot().Count, stopped, stopped + 1);
    }

    [Fact]
    public void LatencySnapshot_CountAbove_CountsLongerDurations()
    {
        var histogram = new LatencyHistogram();
        foreach (var ms in new[] { 1.0, 5.0, 9.0, 20.0, 60.0 })
            histogram.Record(ms);

        var snapshot = histogram.Snapshot();
        Assert.Equal(3, snapshot.CountAbove(8));
        Assert.Equal(1, snapshot.CountAbove(50));
        Assert.Equal(0, snapshot.CountAbove(100));
    }

    [Fact]
    public void GetProcessMetrics_StartRestartStop()
    {
        var source = new FakeSource();
        using var sampler = new ProcessMetricsSampler(source, interval: TimeSpan.FromHours(1), processorCount: 4, uiStalls: null);
        var tool = new GetProcessMetricsTool(sampler, renderMonitor: null, () => Environment(), () => 7, schedulerStats: null, predictionStats: null);

        Assert.True(tool.Invoke(null).TryGetValue(out var none));
        Assert.Null(none!.Window);
        Assert.Equal(7, none.Memory.TileCount);

        Assert.True(tool.Invoke("start").TryGetValue(out var started));
        Assert.NotNull(started!.Window);
        source.Allocated += 1_000;
        Assert.True(tool.Invoke("restart").TryGetValue(out var restarted));
        Assert.Equal(1_000, restarted!.Window!.AllocatedBytes);
        Assert.True(tool.Invoke("stop").TryGetValue(out var stopped));
        Assert.Equal(0, stopped!.Window!.AllocatedBytes);
        Assert.False(sampler.IsWindowOpen);
        Assert.False(tool.Invoke("bogus").TryGetValue(out _));
    }

    [Fact]
    public async Task GetProcessMetrics_ReportsTheSchedulerOverTheWindow()
    {
        var source = new FakeSource();
        using var sampler = new ProcessMetricsSampler(source, interval: TimeSpan.FromHours(1), processorCount: 4, uiStalls: null);
        using var scheduler = new WorkScheduler(new WorkSchedulerOptions { WorkerCount = 2, ThreadName = "metrics test" });
        var tool = new GetProcessMetricsTool(sampler, renderMonitor: null, () => Environment(), schedulerStats: scheduler.GetStats, tileCount: null, predictionStats: null);

        Assert.True(tool.Invoke(null).TryGetValue(out var none));
        Assert.Null(none!.Scheduler);

        // Work before the window doesn't count.
        await scheduler.Schedule(new WorkKey("before"), WorkStage.Raster, WorkPriority.Visible(), _ => ValueTask.FromResult(true))
            .Result.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        // A job's result completes before its worker records the run, so wait
        // for the workers to go idle before opening or closing the window.
        await WaitForIdleWorkersAsync(scheduler);
        Assert.True(tool.Invoke("start").TryGetValue(out _));
        var jobs = Enumerable.Range(0, 3)
            .Select(i => scheduler.Schedule(new WorkKey("in", i), WorkStage.Raster, WorkPriority.Visible(), _ => ValueTask.FromResult(true)).Result)
            .Append(scheduler.Schedule(new WorkKey("bg"), WorkStage.Raster, WorkPriority.Background(), _ => ValueTask.FromResult(true)).Result);
        await Task.WhenAll(jobs).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await WaitForIdleWorkersAsync(scheduler);

        Assert.True(tool.Invoke("restart").TryGetValue(out var window));
        var reported = window!.Scheduler;
        Assert.NotNull(reported);
        Assert.Equal(2, reported.Workers);
        Assert.Equal(3, reported.VisibleWait.Count);
        Assert.Equal(1, reported.BackgroundWait.Count);
        Assert.Equal(4, reported.Run.Count);
        Assert.Equal(4, reported.Completed);

        Assert.True(tool.Invoke("stop").TryGetValue(out var stopped));
        Assert.Equal(0, stopped!.Scheduler!.Completed);
        Assert.True(tool.Invoke(null).TryGetValue(out var after));
        Assert.Null(after!.Scheduler);
    }

    private static async Task WaitForIdleWorkersAsync(WorkScheduler scheduler)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 10;
        while (scheduler.GetStats().BusyWorkers > 0)
        {
            Assert.True(Stopwatch.GetTimestamp() < deadline, "Scheduler workers didn't go idle.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    // ── Pre-warm control ─────────────────────────────────────────────

    [Fact]
    public void MacAppNap_Disable_HoldsTheActivityOnMacOSOnly()
    {
        var disabled = MacAppNap.Disable("EncDotNet.S100.Viewer.Tests");

        Assert.Equal(OperatingSystem.IsMacOS(), disabled);
        Assert.Equal(disabled, MacAppNap.IsDisabled);
        Assert.Equal(disabled, MacAppNap.Disable("again"));
    }

    [Fact]
    public void PortrayalPrewarm_OverridesTheSettingForTheSession()
    {
        var holder = new PortrayalPrewarmOverride();
        var tool = new PortrayalPrewarmTool(
            () => new PortrayalWarmStatus(PortrayalWarmState.CacheFull, 12, 298),
            holder,
            () => true,
            () => 6);

        Assert.True(tool.Invoke(null).TryGetValue(out var read));
        Assert.Equal(6, read!.ConcurrencyLimit);
        Assert.Equal("cache_full", read!.State);
        Assert.Equal((12, 298), (read.Warm, read.Total));
        Assert.True(read.Enabled);
        Assert.Null(read.Override);

        Assert.True(tool.Invoke("off").TryGetValue(out var off));
        Assert.False(off!.Enabled);
        Assert.False(holder.Enabled);

        Assert.True(tool.Invoke("setting").TryGetValue(out var reset));
        Assert.True(reset!.Enabled);
        Assert.Null(holder.Enabled);

        Assert.False(tool.Invoke("maybe").TryGetValue(out _));
    }

    // ── Cache folder and trigger ─────────────────────────────────────

    [Fact]
    public void CacheDir_MovesOnlyTheRenderCaches()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "encdotnet-base-" + Guid.NewGuid().ToString("N"));
        var cacheDir = Path.Combine(Path.GetTempPath(), "encdotnet-cold-" + Guid.NewGuid().ToString("N"));
        var paths = new ViewerDataPaths(baseDir, renderCacheDirectory: cacheDir);

        Assert.Equal(Path.Combine(cacheDir, "PortrayalInstructionCache"), paths.PortrayalInstructionCacheDirectory);
        Assert.Equal(Path.Combine(cacheDir, "PatternClipCache"), paths.PatternClipCacheDirectory);
        Assert.Equal(Path.Combine(cacheDir, "tiles"), paths.TileDiskCacheDirectory);
        Assert.Equal(cacheDir, paths.RenderCacheDirectory);
        Assert.Equal(cacheDir, paths.RenderStoreDirectory);
        // Settings, the Library index and the other caches stay put.
        Assert.StartsWith(baseDir, paths.SettingsFilePath, StringComparison.Ordinal);
        Assert.StartsWith(baseDir, paths.CollectionIndexCacheDirectory, StringComparison.Ordinal);
        Assert.StartsWith(baseDir, paths.DatasetMetadataCacheDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheDir_UnsetLeavesDefaults()
    {
        var paths = new ViewerDataPaths();
        Assert.Null(paths.RenderCacheDirectory);
        Assert.Equal(paths.CacheStoreDirectory, paths.RenderStoreDirectory);
    }

    [Fact]
    public async Task SetViewport_MarksTheTriggerOnlyWhenApplied()
    {
        var marks = 0;
        var accessor = new MapCapabilityAccessor<IMapViewportController> { Current = new NullViewport() };
        var tool = new SetViewportTool(accessor, () => marks++);

        await tool.InvokeAsync(new SetViewportRequest(CenterLat: 47.6, CenterLon: -122.35, ScaleDenominator: 44_300), TestContext.Current.CancellationToken);
        await tool.InvokeAsync(new SetViewportRequest(CenterLat: 47.6), TestContext.Current.CancellationToken);

        Assert.Equal(1, marks);
    }

    // ── Wire contract with PerfRunner ────────────────────────────────

    [Fact]
    public void Replies_UseTheFieldNamesTheHarnessReads()
    {
        // PerfRunner's SettleReport.Flatten reads these names; renaming one
        // here silently empties a column in every settle manifest.
        var settle = AwaitSettledTool.ToDto(new SettleOutcome(
            true, false, 1234.5, 1500, 2, true,
            new SettleSample(1, LoadsDone, TilesSettled)));
        var settleJson = Reply(ToolResultOk(settle));
        foreach (var field in new[] { "settled", "timedOut", "settleMs", "waitedMs" })
            Assert.NotNull(settleJson[field]);
        Assert.NotNull(settleJson["loads"]!["loaded"]);

        using var sampler = new ProcessMetricsSampler(new FakeSource(), interval: TimeSpan.FromHours(1), processorCount: 4, uiStalls: null);
        var tool = new GetProcessMetricsTool(sampler, renderMonitor: null, () => Environment(), tileCount: null, schedulerStats: null, predictionStats: null);
        Assert.True(tool.Invoke("start").TryGetValue(out var metrics));
        var metricsJson = Reply(ToolResultOk(metrics!));
        foreach (var field in new[]
        {
            "wallMs", "cpuMs", "coresBusy", "cpuUtilisation", "gcPauseMs", "gcPauseShare", "gen0Collections",
            "gen1Collections", "gen2Collections", "allocatedBytes", "peakWorkingSetBytes", "peakGcHeapBytes",
            "peakGcCommittedBytes", "peakTileNativeBytes",
        })
        {
            Assert.NotNull(metricsJson["window"]![field]);
        }

        foreach (var field in new[] { "workingSetBytes", "gcHeapBytes", "gcCommittedBytes", "tileNativeBytes" })
            Assert.NotNull(metricsJson["memory"]![field]);
        foreach (var field in new[] { "luaEngine", "serverGc", "processorCount", "os", "architecture", "runtime", "optimized" })
            Assert.NotNull(metricsJson["environment"]![field]);
    }

    private static EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<T> ToolResultOk<T>(T value) =>
        EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<T>.Ok(value);

    private static System.Text.Json.Nodes.JsonNode Reply<T>(EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<T> result)
    {
        var call = McpAdapterShared.Translate(result);
        var text = call.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Single().Text;
        return System.Text.Json.Nodes.JsonNode.Parse(text)!;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static PerfEnvironmentDto Environment() =>
        new("luacsharp", false, true, true, "Interactive", 16, "test", "Arm64", ".NET", null, true, true);

    private static ProcessMetricsReading Reading(
        long timestamp, double cpuMs, double pauseMs, (int, int, int) gen, long allocated, long workingSet) =>
        new(timestamp, TimeSpan.FromMilliseconds(cpuMs), TimeSpan.FromMilliseconds(pauseMs),
            gen.Item1, gen.Item2, gen.Item3, allocated, 10, 20, workingSet, 30, 40);

    private sealed class FakeSource : IProcessMetricsSource
    {
        public long WorkingSet { get; set; } = 100;
        public long Allocated { get; set; }

        public ProcessMetricsReading Read() =>
            new(Stopwatch.GetTimestamp(), TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0, Allocated, 1, 2, WorkingSet, 3, 4);
    }

    private sealed class NullViewport : IMapViewportController
    {
        public void ZoomToExtent(MRect extent) { }
        public void SetViewportToExtent(MRect mercatorExtent) { }
        public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) { }
        public void SetRotation(double degrees) { }
        public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300) { }
        public EncDotNet.S100.DataModel.GeoPosition? TryGetViewportCenterWgs84() => null;
    }

    private sealed class FakeNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current { get; private set; }
        public event EventHandler<MapViewportSnapshot>? ViewportChanged;

        public void Publish(MapViewportSnapshot snapshot)
        {
            Current = snapshot;
            ViewportChanged?.Invoke(this, snapshot);
        }
    }

    private static DatasetEntry Cell(string name, double south, double west, double north, double east) => new(
        filePath: name + ".000",
        productSpec: "S-57",
        source: null,
        relativePath: name + ".000",
        displayName: name,
        geographicBounds: new BoundingBox
        {
            WestBoundLongitude = west,
            EastBoundLongitude = east,
            SouthBoundLatitude = south,
            NorthBoundLatitude = north,
        });

    private static MapViewportSnapshot Viewport(double south, double west, double north, double east) => new()
    {
        MinLatitude = south,
        MinLongitude = west,
        MaxLatitude = north,
        MaxLongitude = east,
        MercatorResolution = 5.0,
    };

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(10);
        }

        return condition();
    }
}
