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
        foreach (var s in styles) total += s.Calls;

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

            toSignal = _pulse;
            _pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        toSignal.TrySetResult();
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
            var decision = EvaluateIdle(now, callStart, lastActivity, quietTicks, timeoutTicks);

            if (decision.Idle && !busy)
            {
                return Done(wentIdle: true, callStart, now, startPaintCount, lastActivity);
            }

            // The timeout always wins once the deadline passes — including
            // the case where the quiet timer elapsed (decision.Idle) but a
            // layer is still busy. Without this guard a map that stays busy
            // forever (an async fetch that never produces a paint) would
            // spin here past the requested timeout, because EvaluateIdle
            // reports Idle as soon as the quiet period elapses whenever the
            // quiet period is shorter than the timeout.
            var deadlinePassed = (now - callStart) >= timeoutTicks;
            if (decision.TimedOut || deadlinePassed)
            {
                return Done(wentIdle: false, callStart, now, startPaintCount, lastActivity);
            }

            // Wait either for the next activity pulse or until the next
            // decision boundary (idle-at or deadline). When the map is
            // busy we cannot trust the quiet timer, so poll on a short
            // cap so the busy flag is re-checked promptly.
            var waitTicks = decision.WaitTicks;
            var waitMs = waitTicks <= 0 ? 0.0 : TicksToMilliseconds(waitTicks);
            if (busy)
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
