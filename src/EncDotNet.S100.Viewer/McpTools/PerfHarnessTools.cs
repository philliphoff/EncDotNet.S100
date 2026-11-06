using System.ComponentModel;
using System.Runtime;
using System.Runtime.InteropServices;
using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>The lazy-load half of a settle reading.</summary>
[Description("What the lazy-load coordinator still has to load for the view.")]
internal sealed record SettleLoadsDto(
    [property: Description("A viewport change is still inside the load debounce, so the queue doesn't reflect the view yet.")] bool EvaluationPending,
    [property: Description("Cells in view waiting to load.")] int QueuedVisible,
    [property: Description("Cells in view loading now.")] int RunningVisible,
    [property: Description("Covered, ahead and unbounded cells waiting to load; they don't hold the view unsettled.")] int QueuedOther,
    [property: Description("Covered, ahead and unbounded cells loading now; they don't hold the view unsettled.")] int RunningOther,
    [property: Description("Cells held loaded.")] int Loaded,
    [property: Description("Registered cells not loaded.")] int Deferred);

/// <summary>The tile half of a settle reading.</summary>
[Description("What the tiled chart layers showed in their latest frames.")]
internal sealed record SettleTilesDto(
    [property: Description("Enabled tiled layers in range at the latest frame's scale.")] int LayersConsidered,
    [property: Description("Of those, layers that haven't painted the latest view yet.")] int LayersNotPainted,
    [property: Description("Of those, layers that painted it showing something other than their own target tiles (a fallback band, predecessor, held tile, other scale class or gap), or with visible tiles still queued.")] int LayersIncomplete,
    [property: Description("Visible target tiles not yet resident.")] int MissingTiles,
    [property: Description("Visible tiles queued or rasterising.")] int PendingVisibleTiles);

/// <summary>Result of <see cref="AwaitSettledTool"/>.</summary>
[Description("Whether the view settled: every cell in view loaded and every visible tile slot showing its own target tile.")]
internal sealed record SettleStateDto(
    [property: Description("The view settled and stayed settled for confirmMs.")] bool Settled,
    [property: Description("timeoutMs elapsed first.")] bool TimedOut,
    [property: Description("Milliseconds from the last set_viewport (or from this call, when there was none) to the frame that completed the settled view; null on a timeout.")] double? SettleMs,
    [property: Description("Milliseconds this call waited, including confirmMs.")] double WaitedMs,
    [property: Description("Milliseconds from the trigger to this call.")] double TriggerAgeMs,
    [property: Description("Whether a set_viewport was recorded as the trigger.")] bool HadTrigger,
    [property: Description("The coordinator's work at the last reading, or null when nothing loads as you pan.")] SettleLoadsDto? Loads,
    [property: Description("The tile layers at the last reading.")] SettleTilesDto Tiles);

/// <summary>Memory figures in a <see cref="ProcessMetricsDto"/>.</summary>
[Description("Process memory: managed (GC heap and committed), native tile caches and resident set.")]
internal sealed record MemoryDto(
    [property: Description("GC heap size at the last collection.")] long GcHeapBytes,
    [property: Description("Memory the GC has committed (GCMemoryInfo.TotalCommittedBytes).")] long GcCommittedBytes,
    [property: Description("Resident set (working set).")] long WorkingSetBytes,
    [property: Description("Private memory.")] long PrivateBytes,
    [property: Description("Native bytes held by the tile raster and GPU caches.")] long TileNativeBytes,
    [property: Description("Raster tiles resident across layers.")] int TileCount);

/// <summary>Window metrics in a <see cref="ProcessMetricsDto"/>.</summary>
[Description("CPU, GC and allocation over the window since the last start or restart.")]
internal sealed record MetricsWindowDto(
    [property: Description("Wall time of the window.")] double WallMs,
    [property: Description("Process CPU time in the window, all threads.")] double CpuMs,
    [property: Description("cpuMs ÷ wallMs: the average number of cores kept busy.")] double CoresBusy,
    [property: Description("coresBusy ÷ processorCount, 0 to 1.")] double CpuUtilisation,
    [property: Description("GC pause time in the window (GC.GetTotalPauseDuration).")] double GcPauseMs,
    [property: Description("gcPauseMs ÷ wallMs, 0 to 1.")] double GcPauseShare,
    [property: Description("Gen 0 collections in the window.")] int Gen0Collections,
    [property: Description("Gen 1 collections in the window.")] int Gen1Collections,
    [property: Description("Gen 2 collections in the window.")] int Gen2Collections,
    [property: Description("Bytes allocated in the window (GC.GetTotalAllocatedBytes).")] long AllocatedBytes,
    [property: Description("allocatedBytes per wall second.")] double AllocationRateBytesPerSecond,
    [property: Description("Peak working set sampled through the window.")] long PeakWorkingSetBytes,
    [property: Description("Peak GC heap size sampled through the window.")] long PeakGcHeapBytes,
    [property: Description("Peak GC committed bytes sampled through the window.")] long PeakGcCommittedBytes,
    [property: Description("Peak tile-cache native bytes sampled through the window.")] long PeakTileNativeBytes,
    [property: Description("Samples behind the peaks.")] int PeakSamples,
    [property: Description("How long the UI thread took to pick up a probe posted about every millisecond, or null when no probe runs. maxMs is, to within about a millisecond, the longest single UI-thread task.")] UiThreadDelayDto? UiThread = null);

/// <summary>UI-thread probe delays in a <see cref="MetricsWindowDto"/>.</summary>
[Description("Delays of UI-thread probes in milliseconds (map rendering slice 6).")]
internal sealed record UiThreadDelayDto(
    [property: Description("Probes measured.")] long Count,
    [property: Description("95th percentile.")] double P95Ms,
    [property: Description("99th percentile.")] double P99Ms,
    [property: Description("Longest (to within one bucket, 10%): about the longest UI-thread task.")] double MaxMs,
    [property: Description("Probes that waited more than 8 ms.")] long Over8Ms,
    [property: Description("Probes that waited more than 50 ms.")] long Over50Ms)
{
    public static UiThreadDelayDto Of(LatencySnapshot s) =>
        new(s.Count, s.Percentile(0.95), s.Percentile(0.99), s.MaxMs, s.CountAbove(8), s.CountAbove(50));
}

/// <summary>Map paint times in a <see cref="ProcessMetricsDto"/>.</summary>
[Description("On-screen map paint times over get_render_stats' rolling window.")]
internal sealed record FrameTimesDto(
    [property: Description("Paints in the window.")] long Count,
    [property: Description("Median paint time.")] double P50Ms,
    [property: Description("95th-percentile paint time.")] double P95Ms,
    [property: Description("Longest paint.")] double MaxMs);

/// <summary>Run environment in a <see cref="ProcessMetricsDto"/>.</summary>
[Description("What the run is measuring on.")]
internal sealed record PerfEnvironmentDto(
    [property: Description("The Lua engine portrayal runs on: 'luacsharp' or 'moonsharp'.")] string LuaEngine,
    [property: Description("Whether S100_LUA_ENGINE pins it.")] bool LuaEnginePinned,
    [property: Description("Server GC.")] bool ServerGc,
    [property: Description("Concurrent (background) GC.")] bool ConcurrentGc,
    [property: Description("GC latency mode.")] string GcLatencyMode,
    [property: Description("Logical processors.")] int ProcessorCount,
    [property: Description("Operating system.")] string Os,
    [property: Description("Process architecture.")] string Architecture,
    [property: Description(".NET runtime.")] string Runtime,
    [property: Description("Where the render caches live when --cache-dir or S100_CACHE_DIR moved them, else null.")] string? RenderCacheDirectory,
    [property: Description("Whether the build is optimised (Release).")] bool Optimized,
    [property: Description("Whether macOS App Nap is off for the process. The perf-harness tools turn it off on first use, so a hidden or covered window doesn't throttle the run; false off macOS.")] bool AppNapDisabled);

/// <summary>Wait or run times of one work class in a <see cref="SchedulerWindowDto"/>.</summary>
[Description("A distribution of times in milliseconds.")]
internal sealed record LatencyDto(
    [property: Description("Jobs measured.")] long Count,
    [property: Description("Median.")] double P50Ms,
    [property: Description("95th percentile.")] double P95Ms,
    [property: Description("Longest (to within one bucket, 10%).")] double MaxMs)
{
    public static LatencyDto Of(LatencySnapshot s) => new(s.Count, s.Percentile(0.5), s.Percentile(0.95), s.MaxMs);
}

/// <summary>Work scheduler figures in a <see cref="ProcessMetricsDto"/>.</summary>
[Description("The work scheduler (map rendering slice 5) over the window: how long jobs waited for a worker and how long they ran.")]
internal sealed record SchedulerWindowDto(
    [property: Description("Dedicated worker threads.")] int Workers,
    [property: Description("Worker time spent running jobs in the window.")] double BusyMs,
    [property: Description("busyMs ÷ the window's wall time: the average number of workers kept busy.")] double CoresBusy,
    [property: Description("Visible jobs: time from queued (or promoted to visible) to a worker starting them.")] LatencyDto VisibleWait,
    [property: Description("Predicted jobs' wait.")] LatencyDto PredictedWait,
    [property: Description("Background jobs' wait.")] LatencyDto BackgroundWait,
    [property: Description("How long a job held a worker per run, visible class.")] LatencyDto VisibleRun,
    [property: Description("How long a job held a worker per run, every class.")] LatencyDto Run,
    [property: Description("Jobs finished in the window.")] long Completed,
    [property: Description("Jobs dropped before they started in the window (no one wanted them).")] long Cancelled,
    [property: Description("Jobs waiting for a worker now, by class: visible, predicted, background, validation.")] IReadOnlyList<int> QueuedNow,
    [property: Description("Allocation-heavy background jobs the GC pause governor admits now.")] int AllocationHeavyLimit);

/// <summary>Result of <see cref="GetProcessMetricsTool"/>.</summary>
[Description("Process CPU, GC and memory metrics for the perf harness.")]
internal sealed record ProcessMetricsDto(
    [property: Description("The window up to now, or null when none was started.")] MetricsWindowDto? Window,
    [property: Description("Memory now.")] MemoryDto Memory,
    [property: Description("Map paint times over the render-stats window.")] FrameTimesDto Frames,
    [property: Description("The run environment.")] PerfEnvironmentDto Environment,
    [property: Description("The work scheduler over the window, or null when no window is open.")] SchedulerWindowDto? Scheduler = null);

/// <summary>Result of <see cref="PortrayalPrewarmTool"/>.</summary>
[Description("The background portrayal pre-warm's progress.")]
internal sealed record PortrayalPrewarmDto(
    [property: Description("'off', 'idle', 'working', 'paused', 'cache_full' or 'complete'.")] string State,
    [property: Description("Registered cells already in the portrayal cache.")] int Warm,
    [property: Description("Registered cells it considers.")] int Total,
    [property: Description("Whether it may run: the session override if set, else the setting.")] bool Enabled,
    [property: Description("The session override (not saved), or null when the setting decides.")] bool? Override,
    [property: Description("How many cells it may portray at once now; it adapts to the GC pause share.")] int ConcurrencyLimit);

/// <summary>
/// Waits for the view to settle (MCP <c>await_settled</c>, map rendering slice 1).
/// </summary>
/// <remarks>
/// Unlike <c>await_render_idle</c>, which waits for paints to stop, this waits
/// for the right picture: every cell in view loaded and every visible tile
/// slot showing its own target tile. It measures from the last
/// <c>set_viewport</c>, so a harness calls <c>set_viewport</c> then this.
/// </remarks>
internal sealed class AwaitSettledTool(ViewSettleMonitor monitor)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "await_settled";

    internal const int DefaultTimeoutMs = 60_000;
    internal const int MaxTimeoutMs = 600_000;
    internal const int DefaultConfirmMs = 250;
    internal const int MaxConfirmMs = 10_000;
    internal const int DefaultPollMs = 10;

    private readonly ViewSettleMonitor _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));

    /// <summary>Waits.</summary>
    public async Task<ToolResult<SettleStateDto>> InvokeAsync(int? timeoutMs, int? confirmMs, int? pollMs, CancellationToken ct)
    {
        var timeout = Math.Clamp(timeoutMs ?? DefaultTimeoutMs, 50, MaxTimeoutMs);
        var confirm = Math.Clamp(confirmMs ?? DefaultConfirmMs, 0, MaxConfirmMs);
        var poll = Math.Clamp(pollMs ?? DefaultPollMs, 1, 1000);
        var outcome = await _monitor.WaitForSettledAsync(
            TimeSpan.FromMilliseconds(confirm),
            TimeSpan.FromMilliseconds(timeout),
            TimeSpan.FromMilliseconds(poll),
            ct).ConfigureAwait(false);
        return ToolResult<SettleStateDto>.Ok(ToDto(outcome));
    }

    internal static SettleStateDto ToDto(SettleOutcome outcome)
    {
        var loads = outcome.Last.Loads is { } l
            ? new SettleLoadsDto(l.EvaluationPending, l.QueuedVisible, l.RunningVisible, l.QueuedOther, l.RunningOther, l.Loaded, l.Deferred)
            : null;
        var t = outcome.Last.Tiles;
        return new SettleStateDto(
            outcome.Settled,
            outcome.TimedOut,
            outcome.SettleMs,
            outcome.WaitedMs,
            outcome.TriggerAgeMs,
            outcome.HadTrigger,
            loads,
            new SettleTilesDto(t.LayersConsidered, t.LayersNotPainted, t.LayersIncomplete, t.MissingTiles, t.PendingVisibleTiles));
    }
}

/// <summary>
/// Reads process CPU, GC and memory metrics over a window (MCP
/// <c>get_process_metrics</c>, map rendering slice 1 and #1276 item 3).
/// </summary>
internal sealed class GetProcessMetricsTool(
    ProcessMetricsSampler sampler,
    IRenderActivityMonitor? renderMonitor,
    Func<PerfEnvironmentDto> environment,
    Func<int>? tileCount = null,
    Func<WorkSchedulerStats>? schedulerStats = null)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "get_process_metrics";

    private readonly ProcessMetricsSampler _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
    private readonly Func<PerfEnvironmentDto> _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IRenderActivityMonitor? _renderMonitor = renderMonitor;
    private readonly Func<int>? _tileCount = tileCount;
    private readonly Func<WorkSchedulerStats>? _schedulerStats = schedulerStats;
    private WorkSchedulerStats? _schedulerStart;

    /// <summary>Reads, and optionally starts, restarts or stops the window.</summary>
    /// <param name="window">'read' (default), 'start', 'restart' (read, then start the next window here) or 'stop'.</param>
    public ToolResult<ProcessMetricsDto> Invoke(string? window)
    {
        var action = (window ?? "read").Trim().ToLowerInvariant();
        ProcessMetricsWindow? measured;
        var schedulerNow = _schedulerStats?.Invoke();
        var schedulerStart = _schedulerStart;
        switch (action)
        {
            case "read":
                measured = _sampler.ReadWindow(restart: false);
                break;
            case "start":
                _sampler.StartWindow();
                measured = _sampler.ReadWindow(restart: false);
                schedulerStart = _schedulerStart = schedulerNow;
                break;
            case "restart":
                measured = _sampler.ReadWindow(restart: true);
                if (measured is null)
                    _sampler.StartWindow();
                _schedulerStart = schedulerNow;
                break;
            case "stop":
                measured = _sampler.ReadWindow(restart: false);
                _sampler.StopWindow();
                _schedulerStart = null;
                break;
            default:
                return ToolResult<ProcessMetricsDto>.Err(new InvalidArgument("window", "one of 'read', 'start', 'restart' or 'stop'"));
        }

        var now = measured?.End ?? _sampler.ReadNow();
        var frames = _renderMonitor?.GetWindowStats() ?? RenderWindowStats.Empty;
        return ToolResult<ProcessMetricsDto>.Ok(new ProcessMetricsDto(
            measured is null ? null : ToDto(measured),
            new MemoryDto(now.GcHeapBytes, now.GcCommittedBytes, now.WorkingSetBytes, now.PrivateBytes, now.TileNativeBytes, SafeTileCount()),
            new FrameTimesDto(frames.Count, frames.FrameP50Ms, frames.FrameP95Ms, frames.FrameMaxMs),
            _environment(),
            measured is not null && schedulerNow is not null && schedulerStart is not null
                ? ToDto(schedulerStart, schedulerNow, measured.WallMs)
                : null));
    }

    internal static SchedulerWindowDto ToDto(WorkSchedulerStats start, WorkSchedulerStats end, double wallMs)
    {
        LatencySnapshot Wait(WorkClass c) => end.WaitByClass[(int)c].Since(start.WaitByClass[(int)c]);
        LatencySnapshot RunOf(WorkClass c) => end.RunByClass[(int)c].Since(start.RunByClass[(int)c]);
        var allRuns = RunOf(WorkClass.Visible);
        foreach (var c in new[] { WorkClass.Predicted, WorkClass.Background, WorkClass.Validation })
            allRuns = allRuns.Plus(RunOf(c));
        var busy = Math.Max(0, end.BusyMilliseconds - start.BusyMilliseconds);
        return new SchedulerWindowDto(
            end.Workers,
            busy,
            wallMs > 0 ? busy / wallMs : 0,
            LatencyDto.Of(Wait(WorkClass.Visible)),
            LatencyDto.Of(Wait(WorkClass.Predicted)),
            LatencyDto.Of(Wait(WorkClass.Background)),
            LatencyDto.Of(RunOf(WorkClass.Visible)),
            LatencyDto.Of(allRuns),
            end.Completed - start.Completed,
            end.Cancelled - start.Cancelled,
            end.QueuedByClass,
            end.AllocationHeavyLimit);
    }

    private int SafeTileCount()
    {
        try
        {
            return _tileCount?.Invoke() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    internal static MetricsWindowDto ToDto(ProcessMetricsWindow w) => new(
        w.WallMs,
        w.CpuMs,
        w.CoresBusy,
        w.CpuUtilisation,
        w.GcPauseMs,
        w.GcPauseShare,
        w.Gen0Collections,
        w.Gen1Collections,
        w.Gen2Collections,
        w.AllocatedBytes,
        w.AllocationRateBytesPerSecond,
        w.Peaks.WorkingSetBytes,
        w.Peaks.GcHeapBytes,
        w.Peaks.GcCommittedBytes,
        w.Peaks.TileNativeBytes,
        w.Peaks.Samples,
        w.UiDelays is { } ui ? UiThreadDelayDto.Of(ui) : null);

    /// <summary>The environment of this process.</summary>
    internal static PerfEnvironmentDto DescribeEnvironment(ViewerSettings settings, string? renderCacheDirectory) => new(
        Scripting.LuaEngineKinds.GetName(settings.ResolveLuaEngine()),
        ViewerLuaEngine.IsPinnedByEnvironment,
        GCSettings.IsServerGC,
        AppContext.TryGetSwitch("System.GC.Concurrent", out var concurrent) ? concurrent : true,
        GCSettings.LatencyMode.ToString(),
        Environment.ProcessorCount,
        RuntimeInformation.OSDescription,
        RuntimeInformation.ProcessArchitecture.ToString(),
        RuntimeInformation.FrameworkDescription,
        renderCacheDirectory,
        IsOptimized(),
        MacAppNap.IsDisabled);

    private static bool IsOptimized()
    {
        var debuggable = typeof(GetProcessMetricsTool).Assembly
            .GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false)
            .OfType<System.Diagnostics.DebuggableAttribute>()
            .FirstOrDefault();
        return debuggable is null || !debuggable.IsJITOptimizerDisabled;
    }
}

/// <summary>Holds the session override of the portrayal pre-warm setting (never saved).</summary>
internal sealed class PortrayalPrewarmOverride
{
    private volatile object? _enabled;

    /// <summary>The override, or null when the setting decides.</summary>
    public bool? Enabled
    {
        get => _enabled is bool b ? b : null;
        set => _enabled = value;
    }
}

/// <summary>
/// Reads the background portrayal pre-warm's progress and, for this session
/// only, switches it on or off (MCP <c>portrayal_prewarm</c>). The perf
/// harness switches it off for cold runs, so cells aren't warmed between jumps,
/// and waits for <c>complete</c> before after-pre-warm runs.
/// </summary>
internal sealed class PortrayalPrewarmTool(
    Func<PortrayalWarmStatus> status,
    PortrayalPrewarmOverride overrideHolder,
    Func<bool> settingEnabled,
    Func<int> concurrencyLimit)
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "portrayal_prewarm";

    private readonly Func<PortrayalWarmStatus> _status = status ?? throw new ArgumentNullException(nameof(status));
    private readonly PortrayalPrewarmOverride _override = overrideHolder ?? throw new ArgumentNullException(nameof(overrideHolder));
    private readonly Func<bool> _settingEnabled = settingEnabled ?? throw new ArgumentNullException(nameof(settingEnabled));
    private readonly Func<int> _concurrencyLimit = concurrencyLimit ?? throw new ArgumentNullException(nameof(concurrencyLimit));

    /// <summary>Reads the progress, after applying <paramref name="enabled"/> when given.</summary>
    /// <param name="enabled">'on' or 'off' to override the setting for this session, 'setting' to drop the override, or null to leave it.</param>
    public ToolResult<PortrayalPrewarmDto> Invoke(string? enabled)
    {
        switch (enabled?.Trim().ToLowerInvariant())
        {
            case null or "":
                break;
            case "on" or "true":
                _override.Enabled = true;
                break;
            case "off" or "false":
                _override.Enabled = false;
                break;
            case "setting":
                _override.Enabled = null;
                break;
            default:
                return ToolResult<PortrayalPrewarmDto>.Err(new InvalidArgument("enabled", "one of 'on', 'off' or 'setting'"));
        }

        var s = _status();
        var effective = _override.Enabled ?? _settingEnabled();
        return ToolResult<PortrayalPrewarmDto>.Ok(new PortrayalPrewarmDto(StateName(s.State), s.Warm, s.Total, effective, _override.Enabled, _concurrencyLimit()));
    }

    internal static string StateName(PortrayalWarmState state) => state switch
    {
        PortrayalWarmState.Off => "off",
        PortrayalWarmState.Idle => "idle",
        PortrayalWarmState.Working => "working",
        PortrayalWarmState.Paused => "paused",
        PortrayalWarmState.CacheFull => "cache_full",
        PortrayalWarmState.Complete => "complete",
        _ => state.ToString().ToLowerInvariant(),
    };
}

/// <summary>What the viewer hands the MCP host for the perf-harness tools.</summary>
/// <param name="Settle">The settled signal.</param>
/// <param name="Metrics">The process metrics sampler.</param>
/// <param name="PrewarmStatus">Reads the portrayal pre-warm's progress.</param>
/// <param name="PrewarmOverride">The session override of the pre-warm setting.</param>
/// <param name="PrewarmConcurrency">Reads how many cells the pre-warm may portray at once.</param>
/// <param name="RenderCacheDirectory">Where <c>--cache-dir</c> moved the render caches, or null.</param>
internal sealed record PerfHarnessServices(
    ViewSettleMonitor Settle,
    ProcessMetricsSampler Metrics,
    Func<PortrayalWarmStatus> PrewarmStatus,
    PortrayalPrewarmOverride PrewarmOverride,
    Func<int> PrewarmConcurrency,
    string? RenderCacheDirectory);

/// <summary>MCP adapters for the perf-harness tools.</summary>
internal static class PerfHarnessMcpAdapters
{
    /// <summary>
    /// Runs on every perf-harness tool call. The first call turns macOS App
    /// Nap off for the rest of the process: App Nap throttles a viewer whose
    /// window is hidden or covered (lower CPU priority, coalesced timers), which
    /// made about half of the harness's cold runs several times slower,
    /// depending only on whether the window happened to be visible.
    /// </summary>
    private static void Attach() => MacAppNap.Disable("SoundCharts performance measurement");

    /// <summary>Creates <c>await_settled</c>.</summary>
    public static McpServerTool Create(AwaitSettledTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("Longest wait in ms; default 60000, 50 to 600000.")] int? timeoutMs = null,
            [Description("How long the view must stay settled before the call returns, in ms; default 250, 0 to 10000. The reported settleMs doesn't include it.")] int? confirmMs = null,
            [Description("Sampling interval in ms; default 10, 1 to 1000.")] int? pollMs = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() =>
            {
                Attach();
                return inner.InvokeAsync(timeoutMs, confirmMs, pollMs, ct);
            });

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = AwaitSettledTool.Name,
            Description =
                "Waits until the view is settled: every chart cell the load-as-you-pan coordinator classifies as visible "
                + "for the current viewport is loaded (ahead, covered and speculative cells don't count), and every visible "
                + "tile slot of every visible tiled layer shows its own target tile (not a fallback band, predecessor, held "
                + "tile or other scale class) with no visible tile work pending. Returns settleMs, the time from the last "
                + "set_viewport to the frame that completed the settled view, or timedOut. Call set_viewport, then this. "
                + "Measurement only: it doesn't change loading or rendering. Read-only; viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    /// <summary>Creates <c>get_process_metrics</c>.</summary>
    public static McpServerTool Create(GetProcessMetricsTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("'read' (default) reports the open window; 'start' opens a window here and samples peaks every 100 ms; 'restart' reports the window and opens the next one at the same reading; 'stop' reports it and stops sampling.")] string? window = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                Attach();
                return Task.FromResult(inner.Invoke(window));
            });

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = GetProcessMetricsTool.Name,
            Description =
                "Reports process CPU, GC and memory for the perf harness. Over a window: CPU and wall time, coresBusy "
                + "(CPU ÷ wall) and cpuUtilisation (÷ processor count), GC pause time and its share of wall time, "
                + "collections by generation, bytes allocated, and peak working set, GC heap, GC committed and tile-cache "
                + "native bytes sampled every 100 ms. Now: GC heap, GC committed, working set, private bytes and tile-cache "
                + "native bytes. Also map paint p50/p95/max over get_render_stats' window, and the run environment (Lua "
                + "engine, GC mode, processors, OS, render cache folder). Sampling runs only while a window is open. "
                + "Viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }

    /// <summary>Creates <c>portrayal_prewarm</c>.</summary>
    public static McpServerTool Create(PortrayalPrewarmTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("'on' or 'off' overrides the pre-warm setting for this session (not saved); 'setting' drops the override; omit to only read.")] string? enabled = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                Attach();
                return Task.FromResult(inner.Invoke(enabled));
            });

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = PortrayalPrewarmTool.Name,
            Description =
                "Reads the background portrayal pre-warm, which portrays load-as-you-pan cells into the portrayal cache "
                + "while the map is idle: its state (off, idle, working, paused, cache_full, complete) and how many "
                + "registered cells are warm of the total, and how many cells it may portray at once (it adapts to the GC pause share). 'enabled' switches it on or off for this session without "
                + "changing the saved setting. Viewer-injected tool.",
            SerializerOptions = McpAdapterShared.Options,
        });
    }
}
