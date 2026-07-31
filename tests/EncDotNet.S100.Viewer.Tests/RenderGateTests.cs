using EncDotNet.S100.Viewer.Diagnostics;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="RenderGate"/>, the mutual-exclusion gate that
/// serialises the live on-screen paint against the offscreen
/// <c>render_to_image</c> readback (issue #337).
/// </summary>
public class RenderGateTests
{
    [Fact]
    public void RunCapture_returns_capture_result()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var result = RenderGate.RunCapture(() => bytes, TimeSpan.FromSeconds(1));
        Assert.Same(bytes, result);
    }

    [Fact]
    public void RunCapture_propagates_capture_exception_and_releases_gate()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RenderGate.RunCapture(
                () => throw new InvalidOperationException("boom"),
                TimeSpan.FromSeconds(1)));

        // Gate must have been released despite the throw: a subsequent
        // live paint can take and release it without blocking.
        RenderGate.EnterLivePaint();
        RenderGate.ExitLivePaint();
    }

    [Fact]
    public async Task RunCapture_waits_while_live_paint_holds_the_gate()
    {
        var captureStarted = new ManualResetEventSlim(false);
        var releaseLivePaint = new ManualResetEventSlim(false);

        // Hold the gate as the live paint would, on another thread.
        var paintThread = new Thread(() =>
        {
            RenderGate.EnterLivePaint();
            try
            {
                releaseLivePaint.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                RenderGate.ExitLivePaint();
            }
        });
        paintThread.Start();

        // Give the paint thread a moment to take the gate.
        Thread.Sleep(50);

        var captureTask = Task.Run(() => RenderGate.RunCapture(
            () =>
            {
                captureStarted.Set();
                return Array.Empty<byte>();
            },
            TimeSpan.FromSeconds(5)));

        // The capture must NOT start while the live paint holds the gate.
        Assert.False(captureStarted.Wait(TimeSpan.FromMilliseconds(200)));

        // Releasing the live paint lets the capture proceed.
        releaseLivePaint.Set();
        Assert.True(captureStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotNull(await captureTask);

        paintThread.Join(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RunCapture_runs_unsynchronised_after_timeout()
    {
        var releaseLivePaint = new ManualResetEventSlim(false);

        var paintThread = new Thread(() =>
        {
            RenderGate.EnterLivePaint();
            try
            {
                releaseLivePaint.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                RenderGate.ExitLivePaint();
            }
        });
        paintThread.Start();
        Thread.Sleep(50);

        // With a zero timeout the capture cannot take the gate but must
        // still run rather than hang.
        var ran = false;
        var result = RenderGate.RunCapture(
            () => { ran = true; return new byte[] { 9 }; },
            TimeSpan.Zero);

        Assert.True(ran);
        Assert.NotNull(result);

        releaseLivePaint.Set();
        paintThread.Join(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RunCapture_null_capture_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RenderGate.RunCapture(null!, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void CaptureActive_is_false_at_rest()
    {
        Assert.False(RenderGate.CaptureActive);
    }

    [Fact]
    public void CaptureActive_is_true_only_for_the_duration_of_a_capture()
    {
        Assert.False(RenderGate.CaptureActive);

        var observedInside = false;
        RenderGate.RunCapture(
            () =>
            {
                observedInside = RenderGate.CaptureActive;
                return Array.Empty<byte>();
            },
            TimeSpan.FromSeconds(1));

        // While a capture is pending the live paint's end marker drains the
        // GPU before releasing the gate (issue #337); outside a capture it
        // pays nothing.
        Assert.True(observedInside);
        Assert.False(RenderGate.CaptureActive);
    }

    [Fact]
    public void CaptureActive_is_cleared_when_the_capture_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RenderGate.RunCapture(
                byte[]? () => throw new InvalidOperationException("boom"),
                TimeSpan.FromSeconds(1)));

        Assert.False(RenderGate.CaptureActive);
    }

    [Fact]
    public async Task CaptureActive_signals_before_the_gate_is_acquired()
    {
        // The flag must be set *before* contending for the gate so the live
        // frame currently holding it sees the pending capture at its end
        // marker and drains the GPU. Hold the gate as the live paint would
        // and confirm the flag flips while the capture is still blocked.
        var releaseLivePaint = new ManualResetEventSlim(false);
        var paintThread = new Thread(() =>
        {
            RenderGate.EnterLivePaint();
            try
            {
                releaseLivePaint.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                RenderGate.ExitLivePaint();
            }
        });
        paintThread.Start();
        Thread.Sleep(50);

        var captureTask = Task.Run(() => RenderGate.RunCapture(
            () => Array.Empty<byte>(),
            TimeSpan.FromSeconds(5)));

        // The capture is blocked on the gate but must already advertise
        // itself as active to the live paint.
        SpinWait.SpinUntil(() => RenderGate.CaptureActive, TimeSpan.FromSeconds(2));
        Assert.True(RenderGate.CaptureActive);

        releaseLivePaint.Set();
        await captureTask.WaitAsync(TimeSpan.FromSeconds(5));
        paintThread.Join(TimeSpan.FromSeconds(5));

        Assert.False(RenderGate.CaptureActive);
    }

    [Fact]
    public void CaptureActive_stays_set_across_nested_captures()
    {
        var innerObservedActive = false;
        RenderGate.RunCapture(
            () =>
            {
                RenderGate.RunCapture(
                    () =>
                    {
                        innerObservedActive = RenderGate.CaptureActive;
                        return Array.Empty<byte>();
                    },
                    TimeSpan.FromSeconds(1));

                // The inner capture finishing must not clear the flag while
                // the outer capture is still running (depth-counted).
                Assert.True(RenderGate.CaptureActive);
                return Array.Empty<byte>();
            },
            TimeSpan.FromSeconds(1));

        Assert.True(innerObservedActive);
        Assert.False(RenderGate.CaptureActive);
    }

    [Fact]
    public void BeginCapture_EndCapture_toggle_CaptureActive_with_depth()
    {
        Assert.False(RenderGate.CaptureActive);

        RenderGate.BeginCapture();
        Assert.True(RenderGate.CaptureActive);

        RenderGate.BeginCapture();
        RenderGate.EndCapture();
        // Still one outstanding BeginCapture.
        Assert.True(RenderGate.CaptureActive);

        RenderGate.EndCapture();
        Assert.False(RenderGate.CaptureActive);
    }

    [Fact]
    public void WaitForFreshDrain_returns_false_when_no_drain_occurs()
    {
        // No NotifyDrained → the wait must time out (and not block forever),
        // letting the capture proceed in the degraded path.
        var drained = RenderGate.WaitForFreshDrain(TimeSpan.FromMilliseconds(50));
        Assert.False(drained);
    }

    [Fact]
    public async Task WaitForFreshDrain_returns_true_when_a_drain_is_notified()
    {
        // WaitForFreshDrain resets the signal first, so only a NotifyDrained
        // raised *after* the wait begins counts — mirroring a forced live
        // frame draining the GPU while the capture waits.
        var waitTask = Task.Run(() =>
            RenderGate.WaitForFreshDrain(TimeSpan.FromSeconds(5)));

        await Task.Delay(50);
        RenderGate.NotifyDrained();

        Assert.True(await waitTask);
    }

    [Fact]
    public void WaitForFreshDrain_ignores_a_stale_drain_signal()
    {
        // A drain signalled before the wait must not satisfy it: the capture
        // needs a *fresh* drain that reflects the current frame's uploads.
        RenderGate.NotifyDrained();
        var drained = RenderGate.WaitForFreshDrain(TimeSpan.FromMilliseconds(50));
        Assert.False(drained);
    }

    [Fact]
    public void CaptureDrained_requests_repaint_before_capturing_and_returns_result()
    {
        var order = new System.Collections.Generic.List<string>();
        var expected = new byte[] { 7, 8, 9 };

        var result = RenderGate.CaptureDrained(
            requestRepaint: () => order.Add("repaint"),
            capture: () =>
            {
                order.Add("capture");
                Assert.True(RenderGate.CaptureActive);
                return expected;
            });

        Assert.Equal(expected, result);
        Assert.Equal(new[] { "repaint", "capture" }, order);
        Assert.False(RenderGate.CaptureActive);
    }

    [Fact]
    public void CaptureDrained_clears_CaptureActive_when_the_capture_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RenderGate.CaptureDrained(
                requestRepaint: () => { },
                capture: () => throw new InvalidOperationException("boom")));

        Assert.False(RenderGate.CaptureActive);
    }

    [Fact]
    public void CaptureDrained_null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RenderGate.CaptureDrained(null!, () => Array.Empty<byte>()));
        Assert.Throws<ArgumentNullException>(() =>
            RenderGate.CaptureDrained(() => { }, null!));
    }
}
