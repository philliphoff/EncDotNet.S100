using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Default <see cref="IRenderActivityMonitor"/> / <see cref="IRenderActivitySink"/>
/// implementation. Tracks paint completions and other render activity
/// reported from the compositor render thread, and lets off-thread
/// callers (MCP tool handlers) wait for the map to settle and read the
/// last paint's cost.
/// </summary>
/// <remarks>
/// <para>
/// State shared across threads (<see cref="PaintCount"/>,
/// <see cref="LatestStats"/>, the last-activity timestamp, and the
/// wake-up pulse) is guarded by a single lock. The pulse is a
/// <see cref="TaskCompletionSource"/> created with
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> and
/// completed outside the lock so waiter continuations never run inline
/// on the render thread.
/// </para>
/// <para>
/// Timing uses <see cref="Stopwatch.GetTimestamp"/> (monotonic). The
/// idle decision is factored into the pure, side-effect-free
/// <see cref="EvaluateIdle"/> so its boundary behaviour can be unit
/// tested without timing dependence.
/// </para>
/// </remarks>
internal sealed class RenderActivityMonitor : IRenderActivityMonitor, IRenderActivitySink
{
    private readonly object _gate = new();

    private long _paintCount;
    private long _lastActivityTimestamp = Stopwatch.GetTimestamp();
    private RenderStatsSnapshot? _latestStats;
    private TaskCompletionSource _pulse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Capacity of the rolling paint window.</summary>
    private const int WindowCapacity = 4096;
    private readonly double[] _winFrameMs = new double[WindowCapacity];
    private readonly double[] _winVectorMs = new double[WindowCapacity];
    private readonly long[] _winDrawCalls = new long[WindowCapacity];
    private readonly long[] _winSequence = new long[WindowCapacity];
    private int _winHead;
    private int _winCount;

    /// <inheritdoc />
    public long PaintCount
    {
        get { lock (_gate) { return _paintCount; } }
    }

    /// <inheritdoc />
    public RenderStatsSnapshot? LatestStats
    {
        get { lock (_gate) { return _latestStats; } }
    }

    /// <inheritdoc />
    public Func<bool>? BusyProbe { get; set; }

    /// <inheritdoc />
    public void NotifyPaint(double frameDurationMs, IReadOnlyList<RenderStyleStat> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);

        long total = 0;
        double vectorMs = 0;
        foreach (var s in styles)
        {
            total += s.Calls;
            if (string.Equals(s.Style, "VectorStyle", StringComparison.Ordinal))
            {
                vectorMs += s.DurationMs;
            }
        }

        TaskCompletionSource toSignal;
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            double? intervalMs = _paintCount == 0
                ? null
                : Stopwatch.GetElapsedTime(_lastActivityTimestamp, now).TotalMilliseconds;

            _paintCount++;
            _lastActivityTimestamp = now;
            _latestStats = new RenderStatsSnapshot(
                FrameDurationMs: frameDurationMs,
                IntervalMs: intervalMs,
                TotalDrawCalls: total,
                Styles: styles,
                PaintSequence: _paintCount,
                CapturedAtUtc: DateTimeOffset.UtcNow);

            var slot = (_winHead + _winCount) % WindowCapacity;
            if (_winCount == WindowCapacity)
            {
                _winHead = (_winHead + 1) % WindowCapacity;
                slot = (_winHead + _winCount - 1 + WindowCapacity) % WindowCapacity;
            }
            else
            {
                _winCount++;
            }
            _winFrameMs[slot] = frameDurationMs;
            _winVectorMs[slot] = vectorMs;
            _winDrawCalls[slot] = total;
            _winSequence[slot] = _paintCount;

            toSignal = _pulse;
            _pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        toSignal.TrySetResult();
    }

    /// <inheritdoc />
    public RenderWindowStats GetWindowStats()
    {
        double[] frames;
        double[] vectors;
        long maxCalls = 0;
        long firstSeq, lastSeq;
        int n;
        lock (_gate)
        {
            n = _winCount;
            if (n == 0) return RenderWindowStats.Empty;
            frames = new double[n];
            vectors = new double[n];
            firstSeq = _winSequence[_winHead];
            lastSeq = _winSequence[(_winHead + n - 1) % WindowCapacity];
            for (var i = 0; i < n; i++)
            {
                var idx = (_winHead + i) % WindowCapacity;
                frames[i] = _winFrameMs[idx];
                vectors[i] = _winVectorMs[idx];
                if (_winDrawCalls[idx] > maxCalls) maxCalls = _winDrawCalls[idx];
            }
        }

        return new RenderWindowStats(
            Count: n,
            FirstSequence: firstSeq,
            LastSequence: lastSeq,
            FrameMaxMs: Max(frames),
            FrameMeanMs: Mean(frames),
            FrameP95Ms: Percentile(frames, 0.95),
            VectorMaxMs: Max(vectors),
            VectorMeanMs: Mean(vectors),
            VectorP95Ms: Percentile(vectors, 0.95),
            MaxTotalDrawCalls: maxCalls);
    }

    /// <inheritdoc />
    public void ResetWindow()
    {
        lock (_gate)
        {
            _winHead = 0;
            _winCount = 0;
        }
    }

    private static double Max(double[] values)
    {
        var m = 0.0;
        foreach (var v in values) if (v > m) m = v;
        return m;
    }

    private static double Mean(double[] values)
    {
        if (values.Length == 0) return 0;
        var sum = 0.0;
        foreach (var v in values) sum += v;
        return sum / values.Length;
    }

    /// <summary>
    /// Nearest-rank percentile over an unsorted copy. The input array is
    /// sorted in place, so callers must pass a throwaway copy.
    /// </summary>
    private static double Percentile(double[] values, double p)
    {
        if (values.Length == 0) return 0;
        Array.Sort(values);
        var rank = (int)Math.Ceiling(p * values.Length) - 1;
        if (rank < 0) rank = 0;
        if (rank >= values.Length) rank = values.Length - 1;
        return values[rank];
    }

    /// <inheritdoc />
    public void NotifyActivity()
    {
        TaskCompletionSource toSignal;
        lock (_gate)
        {
            _lastActivityTimestamp = Stopwatch.GetTimestamp();
            toSignal = _pulse;
            _pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        toSignal.TrySetResult();
    }

    /// <inheritdoc />
    public async Task<RenderIdleResult> WaitForIdleAsync(
        TimeSpan quietPeriod,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (quietPeriod < TimeSpan.Zero) quietPeriod = TimeSpan.Zero;
        if (timeout < TimeSpan.Zero) timeout = TimeSpan.Zero;

        cancellationToken.ThrowIfCancellationRequested();

        var quietTicks = ToStopwatchTicks(quietPeriod);
        var timeoutTicks = ToStopwatchTicks(timeout);
        var callStart = Stopwatch.GetTimestamp();
        var startPaintCount = PaintCount;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long lastActivity;
            Task pulseTask;
            lock (_gate)
            {
                lastActivity = _lastActivityTimestamp;
                pulseTask = _pulse.Task;
            }

            var now = Stopwatch.GetTimestamp();
            var busy = SafeBusy();
            // Recency-gate the busy flag: a busy layer only vetoes idle
            // while it is still producing render activity. A live fetch
            // keeps firing graphics-refresh signals (NotifyActivity), which
            // resets the quiet timer and keeps the map non-idle on its own;
            // a *stale* busy flag (a layer whose Busy never clears though no
            // paint or activity has occurred for the whole quiet period) is
            // ignored, so a settled map is not held open until the timeout.
            var busyVetoes = busy && !IsBusyStale(now, lastActivity, quietTicks);
            var decision = EvaluateIdle(now, callStart, lastActivity, quietTicks, timeoutTicks);

            if (decision.Idle && !busyVetoes)
            {
                return Done(wentIdle: true, callStart, now, startPaintCount, lastActivity);
            }

            // The timeout always wins once the deadline passes — including
            // the case where a live-busy layer (one still emitting activity)
            // never lets the quiet timer elapse. Without this guard such a
            // map would spin here past the requested timeout.
            var deadlinePassed = (now - callStart) >= timeoutTicks;
            if (decision.TimedOut || deadlinePassed)
            {
                return Done(wentIdle: false, callStart, now, startPaintCount, lastActivity);
            }

            // Wait either for the next activity pulse or until the next
            // decision boundary (idle-at or deadline). While a live-busy
            // layer is emitting activity we poll on a short cap so the busy
            // flag is re-checked promptly.
            var waitTicks = decision.WaitTicks;
            var waitMs = waitTicks <= 0 ? 0.0 : TicksToMilliseconds(waitTicks);
            if (busyVetoes)
            {
                waitMs = waitMs <= 0 ? BusyPollMs : Math.Min(waitMs, BusyPollMs);

                // Never poll past the deadline, so the timeout guard above
                // fires promptly once the busy map exhausts its timeout.
                var remainingToDeadlineMs = TicksToMilliseconds(callStart + timeoutTicks - now);
                if (remainingToDeadlineMs > 0 && remainingToDeadlineMs < waitMs)
                {
                    waitMs = remainingToDeadlineMs;
                }
            }
            if (waitMs <= 0) waitMs = 1;

            var delayTask = Task.Delay(TimeSpan.FromMilliseconds(waitMs), cancellationToken);
            var completed = await Task.WhenAny(delayTask, pulseTask).ConfigureAwait(false);
            if (completed == delayTask)
            {
                // Observe cancellation surfaced through the delay.
                await delayTask.ConfigureAwait(false);
            }
        }
    }

    private RenderIdleResult Done(
        bool wentIdle, long callStart, long now, long startPaintCount, long lastActivity)
    {
        var waitedMs = Stopwatch.GetElapsedTime(callStart, now).TotalMilliseconds;
        var quietForMs = Stopwatch.GetElapsedTime(lastActivity, now).TotalMilliseconds;
        if (quietForMs < 0) quietForMs = 0;
        var paintsObserved = PaintCount - startPaintCount;
        if (paintsObserved < 0) paintsObserved = 0;
        return new RenderIdleResult(
            WentIdle: wentIdle,
            TimedOut: !wentIdle,
            WaitedMs: waitedMs,
            PaintsObserved: paintsObserved,
            QuietForMs: quietForMs);
    }

    private bool SafeBusy()
    {
        var probe = BusyProbe;
        if (probe is null) return false;
        try
        {
            return probe();
        }
        catch
        {
            // A probe that throws (e.g. the layer collection mutated
            // mid-enumeration) must not abort the wait; treat as not busy.
            return false;
        }
    }

    /// <summary>Short re-check cadence (ms) used while a layer reports busy.</summary>
    private const double BusyPollMs = 50.0;

    /// <summary>
    /// Pure idle decision. Given monotonic timestamps (in
    /// <see cref="Stopwatch"/> ticks), decides whether the wait should
    /// report idle, time out, or keep waiting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>idleAt = max(lastActivity, callStart) + quiet</c>. Flooring the
    /// reference at <paramref name="callStartTicks"/> guarantees a minimum
    /// wait of the quiet period from the call's start, which closes the
    /// race where a viewport change scheduled a paint that has not begun
    /// when the wait starts.
    /// </para>
    /// <para>
    /// Timeout precedence is resolved against the idle threshold so a
    /// late wake-up cannot report idle after the deadline — important
    /// because the caller may request a quiet period longer than the
    /// timeout.
    /// </para>
    /// </remarks>
    internal static IdleDecision EvaluateIdle(
        long nowTicks,
        long callStartTicks,
        long lastActivityTicks,
        long quietTicks,
        long timeoutTicks)
    {
        var reference = Math.Max(lastActivityTicks, callStartTicks);
        var idleAt = reference + quietTicks;
        var deadline = callStartTicks + timeoutTicks;

        // If the deadline arrives strictly before the idle threshold, the
        // timeout wins once reached. When the two coincide (e.g. a quiet
        // period equal to the timeout with no activity), idle wins via the
        // check below — a map that stayed quiet for the full period is a
        // success, not a timeout.
        if (deadline < idleAt && nowTicks >= deadline)
        {
            return new IdleDecision(false, true, 0);
        }
        if (nowTicks >= idleAt)
        {
            return new IdleDecision(true, false, 0);
        }
        if (nowTicks >= deadline)
        {
            return new IdleDecision(false, true, 0);
        }

        var nextBoundary = Math.Min(idleAt, deadline);
        return new IdleDecision(false, false, nextBoundary - nowTicks);
    }

    /// <summary>
    /// Pure recency gate for the layer-busy veto. Returns <see langword="true"/>
    /// when the most recent render activity is at least the quiet period in
    /// the past — i.e. the busy flag is <em>stale</em> (no paint or
    /// graphics-refresh signal has arrived for the whole quiet window) and
    /// must not veto idle. A live fetch keeps emitting activity, so its busy
    /// flag never goes stale and continues to (indirectly) keep the map
    /// non-idle via the quiet timer.
    /// </summary>
    internal static bool IsBusyStale(long nowTicks, long lastActivityTicks, long quietTicks)
        => (nowTicks - lastActivityTicks) >= quietTicks;

    private static long ToStopwatchTicks(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return 0;
        return (long)(span.TotalSeconds * Stopwatch.Frequency);
    }

    private static double TicksToMilliseconds(long ticks)
        => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>Result of <see cref="EvaluateIdle"/>.</summary>
    /// <param name="Idle">The quiet period elapsed with no further activity.</param>
    /// <param name="TimedOut">The overall timeout elapsed first.</param>
    /// <param name="WaitTicks">Stopwatch ticks until the next decision boundary (only meaningful when neither flag is set).</param>
    internal readonly record struct IdleDecision(bool Idle, bool TimedOut, long WaitTicks);
}

/// <summary>
/// Static bridge that hands the render thread a reference to the live
/// <see cref="IRenderActivitySink"/>. Needed because
/// <see cref="InstrumentedMapControl"/> and the static
/// <see cref="MapPaintInstrumentation"/> are not resolved from DI; the
/// host assigns <see cref="Sink"/> from the DI singleton once it is
/// available, mirroring the late-bound accessor pattern used elsewhere.
/// </summary>
internal static class RenderActivityHub
{
    /// <summary>
    /// The live sink, or <see langword="null"/> before the host wires it
    /// (or after teardown). Render-path code must null-check before use
    /// and skip per-paint snapshot work when this is unset.
    /// </summary>
    public static volatile IRenderActivitySink? Sink;
}
