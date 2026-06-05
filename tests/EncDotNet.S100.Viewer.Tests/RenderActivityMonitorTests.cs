using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Unit tests for <see cref="RenderActivityMonitor"/>: the pure idle
/// decision boundaries, stats capture, and the async wait loop.
/// </summary>
public class RenderActivityMonitorTests
{
    private static IReadOnlyList<RenderStyleStat> Styles(params (string name, long calls, double ms)[] s)
    {
        var list = new List<RenderStyleStat>();
        foreach (var (name, calls, ms) in s) list.Add(new RenderStyleStat(name, calls, ms));
        return list;
    }

    // ---- EvaluateIdle (pure) -------------------------------------------

    [Fact]
    public void EvaluateIdle_reports_idle_once_quiet_period_elapsed()
    {
        // callStart=0, no later activity, quiet=100, timeout=1000.
        var d = RenderActivityMonitor.EvaluateIdle(
            nowTicks: 100, callStartTicks: 0, lastActivityTicks: 0,
            quietTicks: 100, timeoutTicks: 1000);
        Assert.True(d.Idle);
        Assert.False(d.TimedOut);
    }

    [Fact]
    public void EvaluateIdle_keeps_waiting_before_quiet_period()
    {
        var d = RenderActivityMonitor.EvaluateIdle(
            nowTicks: 40, callStartTicks: 0, lastActivityTicks: 0,
            quietTicks: 100, timeoutTicks: 1000);
        Assert.False(d.Idle);
        Assert.False(d.TimedOut);
        Assert.Equal(60, d.WaitTicks); // idleAt(100) - now(40)
    }

    [Fact]
    public void EvaluateIdle_minimum_wait_floored_at_call_start()
    {
        // lastActivity is far in the past, but the call just started:
        // idle must not be reported until quiet elapses from callStart.
        var d = RenderActivityMonitor.EvaluateIdle(
            nowTicks: 1000, callStartTicks: 1000, lastActivityTicks: 0,
            quietTicks: 100, timeoutTicks: 5000);
        Assert.False(d.Idle);
        Assert.False(d.TimedOut);
        Assert.Equal(100, d.WaitTicks);
    }

    [Fact]
    public void EvaluateIdle_recent_activity_extends_wait()
    {
        // Activity at tick 500 pushes idleAt to 600.
        var d = RenderActivityMonitor.EvaluateIdle(
            nowTicks: 550, callStartTicks: 0, lastActivityTicks: 500,
            quietTicks: 100, timeoutTicks: 5000);
        Assert.False(d.Idle);
        Assert.Equal(50, d.WaitTicks);
    }

    [Fact]
    public void EvaluateIdle_times_out_when_deadline_precedes_idle()
    {
        // quietPeriod (1000) longer than timeout (200): the deadline at
        // 200 must win and never falsely report idle afterwards.
        var d = RenderActivityMonitor.EvaluateIdle(
            nowTicks: 250, callStartTicks: 0, lastActivityTicks: 0,
            quietTicks: 1000, timeoutTicks: 200);
        Assert.False(d.Idle);
        Assert.True(d.TimedOut);
    }

    [Fact]
    public void EvaluateIdle_idle_wins_when_thresholds_coincide()
    {
        // idleAt == deadline == 100: success boundary wins.
        var d = RenderActivityMonitor.EvaluateIdle(
            nowTicks: 100, callStartTicks: 0, lastActivityTicks: 0,
            quietTicks: 100, timeoutTicks: 100);
        Assert.True(d.Idle);
        Assert.False(d.TimedOut);
    }

    // ---- Stats capture -------------------------------------------------

    [Fact]
    public void NotifyPaint_captures_latest_stats_and_counts()
    {
        var m = new RenderActivityMonitor();
        Assert.Null(m.LatestStats);
        Assert.Equal(0, m.PaintCount);

        m.NotifyPaint(12.5, Styles(("VectorStyle", 3, 8.0), ("LabelStyle", 2, 4.0)));

        Assert.Equal(1, m.PaintCount);
        var s = m.LatestStats;
        Assert.NotNull(s);
        Assert.Equal(12.5, s!.FrameDurationMs);
        Assert.Null(s.IntervalMs); // first paint has no interval
        Assert.Equal(5, s.TotalDrawCalls);
        Assert.Equal(1, s.PaintSequence);
        Assert.Equal(2, s.Styles.Count);
    }

    [Fact]
    public void NotifyPaint_second_paint_has_interval_and_sequence()
    {
        var m = new RenderActivityMonitor();
        m.NotifyPaint(10.0, Styles(("VectorStyle", 1, 1.0)));
        m.NotifyPaint(11.0, Styles(("VectorStyle", 1, 1.0)));

        Assert.Equal(2, m.PaintCount);
        var s = m.LatestStats!;
        Assert.Equal(2, s.PaintSequence);
        Assert.NotNull(s.IntervalMs);
        Assert.True(s.IntervalMs >= 0);
    }

    // ---- WaitForIdleAsync ----------------------------------------------

    [Fact]
    public async Task WaitForIdleAsync_returns_idle_when_quiet()
    {
        var m = new RenderActivityMonitor();
        var result = await m.WaitForIdleAsync(
            TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(2));

        Assert.True(result.WentIdle);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.PaintsObserved);
        Assert.True(result.WaitedMs >= 0);
    }

    [Fact]
    public async Task WaitForIdleAsync_observes_paint_during_wait()
    {
        // A paint that lands while the call is waiting must be counted in
        // PaintsObserved, and the monitor must still settle afterwards.
        //
        // Driving the paint off a wall-clock delay races the quiet timer
        // (under load the delayed paint can slip past the quiet window, so
        // the monitor settles first and observes zero paints). Instead,
        // inject the paint synchronously the first time the wait loop polls
        // BusyProbe: that poll happens on the first iteration, before any
        // idle can be declared, so the paint is deterministically observed
        // exactly once while the probe stays "not busy".
        RenderActivityMonitor m = null!;
        var painted = false;
        m = new RenderActivityMonitor
        {
            BusyProbe = () =>
            {
                if (!painted)
                {
                    painted = true;
                    m.NotifyPaint(5.0, Styles(("VectorStyle", 1, 2.0)));
                }
                return false;
            },
        };

        var result = await m.WaitForIdleAsync(
            TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(5));

        Assert.True(result.WentIdle);
        Assert.Equal(1, result.PaintsObserved);
    }

    [Fact]
    public async Task WaitForIdleAsync_times_out_while_busy()
    {
        var m = new RenderActivityMonitor { BusyProbe = () => true };
        var result = await m.WaitForIdleAsync(
            TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(150));

        Assert.False(result.WentIdle);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task WaitForIdleAsync_recovers_when_busy_clears()
    {
        var busy = true;
        // ReSharper disable once AccessToModifiedClosure
        var m = new RenderActivityMonitor { BusyProbe = () => Volatile.Read(ref busy) };
        var waitTask = m.WaitForIdleAsync(
            TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(5));

        await Task.Delay(60);
        Volatile.Write(ref busy, false);
        m.NotifyActivity(); // wake the waiter promptly

        var result = await waitTask;
        Assert.True(result.WentIdle);
    }

    [Fact]
    public async Task WaitForIdleAsync_honours_cancellation()
    {
        var m = new RenderActivityMonitor { BusyProbe = () => true };
        using var cts = new CancellationTokenSource();
        var waitTask = m.WaitForIdleAsync(
            TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(30), cts.Token);

        await Task.Delay(20);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }

    [Fact]
    public async Task WaitForIdleAsync_busy_probe_exception_is_swallowed()
    {
        var m = new RenderActivityMonitor { BusyProbe = () => throw new InvalidOperationException() };
        var result = await m.WaitForIdleAsync(
            TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(2));
        Assert.True(result.WentIdle);
    }
}
