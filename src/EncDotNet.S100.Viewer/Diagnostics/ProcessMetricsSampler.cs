using System.Diagnostics;
using EncDotNet.S100.Core.Scheduling;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>A point-in-time reading of the process's CPU, GC and memory counters.</summary>
/// <param name="Timestamp">When it was read (<see cref="Stopwatch"/> timestamp).</param>
/// <param name="CpuTime">Process CPU time, user plus kernel, all threads.</param>
/// <param name="GcPause">Total GC pause time since start (<see cref="GC.GetTotalPauseDuration"/>).</param>
/// <param name="Gen0Collections">Gen 0 collections since start.</param>
/// <param name="Gen1Collections">Gen 1 collections since start.</param>
/// <param name="Gen2Collections">Gen 2 collections since start.</param>
/// <param name="AllocatedBytes">Bytes allocated since start (<see cref="GC.GetTotalAllocatedBytes"/>).</param>
/// <param name="GcHeapBytes">GC heap size at the last collection (<see cref="GCMemoryInfo.HeapSizeBytes"/>).</param>
/// <param name="GcCommittedBytes">Memory the GC has committed (<see cref="GCMemoryInfo.TotalCommittedBytes"/>).</param>
/// <param name="WorkingSetBytes">Resident set (<see cref="Process.WorkingSet64"/>).</param>
/// <param name="PrivateBytes">Private memory (<see cref="Process.PrivateMemorySize64"/>).</param>
/// <param name="TileNativeBytes">Native bytes the tile raster and GPU caches hold.</param>
internal readonly record struct ProcessMetricsReading(
    long Timestamp,
    TimeSpan CpuTime,
    TimeSpan GcPause,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long AllocatedBytes,
    long GcHeapBytes,
    long GcCommittedBytes,
    long WorkingSetBytes,
    long PrivateBytes,
    long TileNativeBytes);

/// <summary>Reads <see cref="ProcessMetricsReading"/>s; a seam so the window arithmetic is testable.</summary>
internal interface IProcessMetricsSource
{
    /// <summary>Reads the counters now.</summary>
    ProcessMetricsReading Read();
}

/// <summary>Reads the counters from the runtime and the current process.</summary>
/// <param name="tileNativeBytes">Reads the tile caches' native bytes, or null for 0.</param>
internal sealed class RuntimeProcessMetricsSource(Func<long>? tileNativeBytes) : IProcessMetricsSource
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly object _gate = new();

    /// <inheritdoc />
    public ProcessMetricsReading Read()
    {
        long workingSet;
        long privateBytes;
        TimeSpan cpu;
        lock (_gate)
        {
            _process.Refresh();
            workingSet = _process.WorkingSet64;
            privateBytes = _process.PrivateMemorySize64;
            cpu = _process.TotalProcessorTime;
        }

        var info = GC.GetGCMemoryInfo(GCKind.Any);
        long tiles = 0;
        try
        {
            tiles = tileNativeBytes?.Invoke() ?? 0;
        }
        catch
        {
            // A cache racing teardown reads as empty.
        }

        return new ProcessMetricsReading(
            Stopwatch.GetTimestamp(),
            cpu,
            GC.GetTotalPauseDuration(),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            GC.GetTotalAllocatedBytes(precise: false),
            info.HeapSizeBytes,
            info.TotalCommittedBytes,
            workingSet,
            privateBytes,
            tiles);
    }
}

/// <summary>Peaks seen by the sampler over a window.</summary>
internal readonly record struct ProcessMetricsPeaks(
    long WorkingSetBytes,
    long GcHeapBytes,
    long GcCommittedBytes,
    long TileNativeBytes,
    int Samples)
{
    /// <summary>The peaks of one reading.</summary>
    public static ProcessMetricsPeaks Of(ProcessMetricsReading r) =>
        new(r.WorkingSetBytes, r.GcHeapBytes, r.GcCommittedBytes, r.TileNativeBytes, 1);

    /// <summary>These peaks raised to cover <paramref name="r"/>.</summary>
    public ProcessMetricsPeaks With(ProcessMetricsReading r) => new(
        Math.Max(WorkingSetBytes, r.WorkingSetBytes),
        Math.Max(GcHeapBytes, r.GcHeapBytes),
        Math.Max(GcCommittedBytes, r.GcCommittedBytes),
        Math.Max(TileNativeBytes, r.TileNativeBytes),
        Samples + 1);
}

/// <summary>Metrics over a window, derived from its first and last readings and the sampled peaks.</summary>
/// <param name="WallMs">Wall time of the window.</param>
/// <param name="CpuMs">Process CPU time used in the window.</param>
/// <param name="CoresBusy">CPU time ÷ wall time: the average number of cores the process kept busy.</param>
/// <param name="CpuUtilisation">CPU time ÷ wall time ÷ <see cref="Environment.ProcessorCount"/>, 0 to 1.</param>
/// <param name="GcPauseMs">GC pause time in the window.</param>
/// <param name="GcPauseShare">GC pause time ÷ wall time, 0 to 1.</param>
/// <param name="Gen0Collections">Gen 0 collections in the window.</param>
/// <param name="Gen1Collections">Gen 1 collections in the window.</param>
/// <param name="Gen2Collections">Gen 2 collections in the window.</param>
/// <param name="AllocatedBytes">Bytes allocated in the window.</param>
/// <param name="AllocationRateBytesPerSecond">Bytes allocated ÷ wall seconds.</param>
/// <param name="End">The reading at the window's end.</param>
/// <param name="Peaks">Peaks sampled through the window, including both ends.</param>
/// <param name="UiDelays">How long UI-thread probes waited to run in the window, or null when no probe runs.</param>
internal sealed record ProcessMetricsWindow(
    double WallMs,
    double CpuMs,
    double CoresBusy,
    double CpuUtilisation,
    double GcPauseMs,
    double GcPauseShare,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long AllocatedBytes,
    double AllocationRateBytesPerSecond,
    ProcessMetricsReading End,
    ProcessMetricsPeaks Peaks,
    LatencySnapshot? UiDelays = null)
{
    /// <summary>Derives the window metrics. Pure.</summary>
    /// <param name="start">The reading at the window's start.</param>
    /// <param name="end">The reading at its end.</param>
    /// <param name="peaks">The sampled peaks.</param>
    /// <param name="processorCount">Logical processors.</param>
    /// <param name="uiDelays">The UI-thread probe delays in the window, or null.</param>
    /// <returns>The metrics.</returns>
    public static ProcessMetricsWindow Compute(
        ProcessMetricsReading start,
        ProcessMetricsReading end,
        ProcessMetricsPeaks peaks,
        int processorCount,
        LatencySnapshot? uiDelays = null)
    {
        var wallMs = Math.Max(0, (end.Timestamp - start.Timestamp) * 1000.0 / Stopwatch.Frequency);
        var cpuMs = Math.Max(0, (end.CpuTime - start.CpuTime).TotalMilliseconds);
        var pauseMs = Math.Max(0, (end.GcPause - start.GcPause).TotalMilliseconds);
        var allocated = Math.Max(0, end.AllocatedBytes - start.AllocatedBytes);
        var coresBusy = wallMs > 0 ? cpuMs / wallMs : 0;
        return new ProcessMetricsWindow(
            WallMs: wallMs,
            CpuMs: cpuMs,
            CoresBusy: coresBusy,
            CpuUtilisation: processorCount > 0 ? coresBusy / processorCount : 0,
            GcPauseMs: pauseMs,
            GcPauseShare: wallMs > 0 ? Math.Min(1, pauseMs / wallMs) : 0,
            Gen0Collections: end.Gen0Collections - start.Gen0Collections,
            Gen1Collections: end.Gen1Collections - start.Gen1Collections,
            Gen2Collections: end.Gen2Collections - start.Gen2Collections,
            AllocatedBytes: allocated,
            AllocationRateBytesPerSecond: wallMs > 0 ? allocated / (wallMs / 1000.0) : 0,
            End: end,
            Peaks: peaks.With(start).With(end),
            UiDelays: uiDelays);
    }
}

/// <summary>
/// Measures CPU, GC and memory over windows the perf harness brackets
/// (map rendering slice 1, #1276 item 3). <see cref="StartWindow"/> takes a
/// baseline and starts a background sampler for the peaks (the working set's
/// own peak counter is unreliable on macOS); <see cref="ReadWindow"/> reports
/// the deltas and peaks, and can start the next window.
/// </summary>
/// <remarks>
/// Nothing runs until a window starts, so a viewer without the harness pays
/// nothing. The sampler is a <see cref="PeriodicTimer"/> loop on the thread
/// pool; each tick reads the process counters (a <c>Process.Refresh</c>) and
/// the tile caches' byte totals.
/// </remarks>
internal sealed class ProcessMetricsSampler : IDisposable
{
    private readonly IProcessMetricsSource _source;
    private readonly UiThreadStallMonitor? _uiStalls;
    private readonly int _processorCount;
    private readonly object _gate = new();
    private ProcessMetricsReading? _start;
    private ProcessMetricsPeaks _peaks;
    private LatencySnapshot? _uiStart;
    private CancellationTokenSource? _samplerStop;

    /// <summary>Creates a sampler.</summary>
    /// <param name="source">Reads the counters.</param>
    /// <param name="interval">How often the peaks are sampled while a window is open.</param>
    /// <param name="processorCount">Logical processors (the app passes <see cref="Environment.ProcessorCount"/>).</param>
    /// <param name="uiStalls">Probes the UI thread while a window is open, or null for no probe.</param>
    public ProcessMetricsSampler(
        IProcessMetricsSource source,
        int processorCount,
        UiThreadStallMonitor? uiStalls,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _uiStalls = uiStalls;
        Interval = interval ?? TimeSpan.FromMilliseconds(100);
        _processorCount = processorCount;
    }

    /// <summary>How often peaks are sampled while a window is open.</summary>
    public TimeSpan Interval { get; }

    /// <summary>Logical processors used for <see cref="ProcessMetricsWindow.CpuUtilisation"/>.</summary>
    public int ProcessorCount => _processorCount;

    /// <summary>Whether a window is open.</summary>
    public bool IsWindowOpen
    {
        get { lock (_gate) return _start is not null; }
    }

    /// <summary>Reads the counters now, outside any window.</summary>
    public ProcessMetricsReading ReadNow() => _source.Read();

    /// <summary>Starts a window now, replacing any open one, and starts the peak sampler.</summary>
    public void StartWindow()
    {
        var reading = _source.Read();
        lock (_gate)
        {
            _start = reading;
            _peaks = ProcessMetricsPeaks.Of(reading);
            _uiStalls?.Start();
            _uiStart = _uiStalls?.Snapshot();
            if (_samplerStop is null)
            {
                _samplerStop = new CancellationTokenSource();
                _ = SampleAsync(_samplerStop.Token);
            }
        }
    }

    /// <summary>
    /// Reports the open window up to now, or null when none is open. With
    /// <paramref name="restart"/>, the next window starts at this reading.
    /// </summary>
    /// <param name="restart">Start the next window at this reading.</param>
    /// <returns>The window, or null.</returns>
    public ProcessMetricsWindow? ReadWindow(bool restart)
    {
        var end = _source.Read();
        var uiNow = _uiStalls?.Snapshot();
        lock (_gate)
        {
            if (_start is not { } start)
                return null;

            var uiDelays = uiNow is not null && _uiStart is not null ? uiNow.Since(_uiStart) : null;
            var window = ProcessMetricsWindow.Compute(start, end, _peaks, _processorCount, uiDelays);
            if (restart)
            {
                _start = end;
                _peaks = ProcessMetricsPeaks.Of(end);
                _uiStart = uiNow;
            }

            return window;
        }
    }

    /// <summary>Closes the window and stops the sampler.</summary>
    public void StopWindow()
    {
        lock (_gate)
        {
            _start = null;
            _uiStart = null;
            _uiStalls?.Stop();
            _samplerStop?.Cancel();
            _samplerStop?.Dispose();
            _samplerStop = null;
        }
    }

    /// <summary>Takes one peak sample now; the background loop calls it each interval.</summary>
    internal void SampleOnce()
    {
        var reading = _source.Read();
        lock (_gate)
        {
            if (_start is not null)
                _peaks = _peaks.With(reading);
        }
    }

    private async Task SampleAsync(CancellationToken stop)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                SampleOnce();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <inheritdoc />
    public void Dispose() => StopWindow();
}
