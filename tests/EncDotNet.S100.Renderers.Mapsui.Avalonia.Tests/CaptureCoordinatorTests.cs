namespace EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests;

public class CaptureCoordinatorTests
{
    private readonly CaptureCoordinator _coordinator = new();

    [Fact]
    public async Task Drain_on_another_control_does_not_release_a_capture()
    {
        // Split map (#1728): map B finishing a live paint must not release a
        // capture of map A that waits for A's drain.
        var mapB = new CaptureCoordinator();
        var captured = new TaskCompletionSource();
        var capture = _coordinator.CaptureDrainedAsync(
            () =>
            {
                Assert.False(mapB.CaptureActive);
                mapB.NotifyDrained();
                return Task.CompletedTask;
            },
            () =>
            {
                captured.SetResult();
                return Task.FromResult<byte[]?>([1]);
            },
            TestContext.Current.CancellationToken);

        // B's drain is not A's: the capture waits for A (here, until its
        // drain timeout) rather than running on B's signal.
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.False(captured.Task.IsCompleted);
        _coordinator.NotifyDrained();
        Assert.Equal(new byte[] { 1 }, await capture);
        Assert.False(_coordinator.CaptureActive);
    }

    [Fact]
    public async Task Captures_of_two_controls_do_not_serialize()
    {
        var mapB = new CaptureCoordinator();
        var releaseA = new TaskCompletionSource<byte[]?>();
        var captureA = _coordinator.CaptureDrainedAsync(
            () => Task.CompletedTask,
            () => releaseA.Task,
            TestContext.Current.CancellationToken,
            synchronized: false);

        // A is mid-capture; B's capture still runs.
        var captureB = await mapB.CaptureDrainedAsync(
            () => Task.CompletedTask,
            () => Task.FromResult<byte[]?>([2]),
            TestContext.Current.CancellationToken,
            synchronized: false);
        Assert.Equal(new byte[] { 2 }, captureB);
        Assert.True(_coordinator.CaptureActive);
        Assert.False(mapB.CaptureActive);

        releaseA.SetResult([1]);
        Assert.Equal(new byte[] { 1 }, await captureA);
    }

    [Fact]
    public async Task Capture_of_several_controls_waits_for_every_drain()
    {
        var mapB = new CaptureCoordinator();
        var captured = false;
        var capture = CaptureCoordinator.CaptureDrainedAsync(
            [mapB, _coordinator],
            () =>
            {
                Assert.True(_coordinator.CaptureActive);
                Assert.True(mapB.CaptureActive);
                _coordinator.NotifyDrained();
                return Task.CompletedTask;
            },
            () =>
            {
                captured = true;
                return Task.FromResult<byte[]?>([3]);
            },
            TestContext.Current.CancellationToken,
            acquireGate: false);

        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.False(captured);
        mapB.NotifyDrained();
        Assert.Equal(new byte[] { 3 }, await capture);
        Assert.False(_coordinator.CaptureActive);
        Assert.False(mapB.CaptureActive);
    }

    [Fact]
    public void RunCapture_returns_capture_result()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var result = _coordinator.RunCapture(() => bytes, TimeSpan.FromSeconds(1));
        Assert.Same(bytes, result);
    }

    [Fact]
    public void RunCapture_propagates_exception_and_releases_gate()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _coordinator.RunCapture(
                () => throw new InvalidOperationException("boom"),
                TimeSpan.FromSeconds(1)));

        var lease = CaptureCoordinator.EnterLivePaint();
        CaptureCoordinator.ExitGate(lease);
    }

    [Fact]
    public async Task RunCapture_waits_while_live_paint_holds_gate()
    {
        var captureStarted = new ManualResetEventSlim(false);
        var gateHeld = new ManualResetEventSlim(false);
        var releaseLivePaint = new ManualResetEventSlim(false);
        var paintThread = new Thread(() =>
        {
            var lease = CaptureCoordinator.EnterLivePaint();
            try
            {
                gateHeld.Set();
                releaseLivePaint.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                CaptureCoordinator.ExitGate(lease);
            }
        });
        paintThread.Start();
        // Start the capture only once the paint thread holds the gate; a fixed
        // sleep let the capture win the race on slow runners.
        Assert.True(gateHeld.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        var captureTask = Task.Run(() => _coordinator.RunCapture(
            () =>
            {
                captureStarted.Set();
                return Array.Empty<byte>();
            },
            TimeSpan.FromSeconds(5)));

        Assert.False(captureStarted.Wait(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));
        releaseLivePaint.Set();
        Assert.True(captureStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.NotNull(await captureTask);
        paintThread.Join(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Capture_active_is_depth_counted_and_cleared_after_exception()
    {
        Assert.False(_coordinator.CaptureActive);
        _coordinator.BeginCapture();
        _coordinator.BeginCapture();
        _coordinator.EndCapture();
        Assert.True(_coordinator.CaptureActive);
        _coordinator.EndCapture();
        Assert.False(_coordinator.CaptureActive);

        Assert.Throws<InvalidOperationException>(() =>
            _coordinator.RunCapture(
                byte[]? () => throw new InvalidOperationException("boom"),
                TimeSpan.FromSeconds(1)));
        Assert.False(_coordinator.CaptureActive);
    }

    [Fact]
    public async Task Fresh_drain_wait_ignores_stale_signal_and_accepts_new_signal()
    {
        _coordinator.NotifyDrained();
        Assert.False(
            _coordinator.WaitForFreshDrain(TimeSpan.FromMilliseconds(50)));

        var waitTask = Task.Run(() =>
            _coordinator.WaitForFreshDrain(TimeSpan.FromSeconds(5)));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _coordinator.NotifyDrained();
        Assert.True(await waitTask);
    }

    [Fact]
    public async Task CaptureDrained_requests_repaint_before_capture()
    {
        var order = new List<string>();
        var result = await _coordinator.CaptureDrainedAsync(
            () =>
            {
                order.Add("repaint");
                _coordinator.NotifyDrained();
                return Task.CompletedTask;
            },
            () =>
            {
                order.Add("capture");
                Assert.True(_coordinator.CaptureActive);
                return Task.FromResult<byte[]?>([7]);
            },
            CancellationToken.None);

        Assert.Equal(new byte[] { 7 }, result);
        Assert.Equal(["repaint", "capture"], order);
        Assert.False(_coordinator.CaptureActive);
    }

    [Fact]
    public async Task CaptureDrained_unsynchronized_skips_the_repaint_drain_handshake()
    {
        // A plain (non-capture-synchronized) control never signals a drain, so
        // synchronized: false must skip the repaint + drain wait entirely and
        // capture directly - never stalling on a signal that can never arrive.
        var order = new List<string>();
        var result = await _coordinator.CaptureDrainedAsync(
            () =>
            {
                order.Add("repaint");
                return Task.CompletedTask;
            },
            () =>
            {
                order.Add("capture");
                Assert.True(_coordinator.CaptureActive);
                return Task.FromResult<byte[]?>([9]);
            },
            CancellationToken.None,
            synchronized: false);

        Assert.Equal(new byte[] { 9 }, result);
        // The repaint request was skipped: only the capture ran.
        Assert.Equal(["capture"], order);
        Assert.False(_coordinator.CaptureActive);
    }

    [Fact]
    public async Task CaptureDrained_without_gate_lets_capture_reenter_live_paint()
    {
        // Reproduces the whole-control capture path: the capture callback renders
        // the control tree, whose start/end markers re-enter the live-paint gate
        // on the capturing thread. With acquireGate: true, CaptureDrainedAsync
        // holds the gate on a *worker* thread, so this re-entry from another
        // thread would block forever (the classic capture_app_screenshot
        // deadlock). acquireGate: false relies on the markers to serialize and
        // must not deadlock.
        var reentered = false;
        var capture = _coordinator.CaptureDrainedAsync(
            () =>
            {
                _coordinator.NotifyDrained();
                return Task.CompletedTask;
            },
            () =>
            {
                // Stand in for StartMarkerOperation/EndMarkerOperation re-entering
                // the gate during the on-UI-thread render.
                var lease = CaptureCoordinator.EnterLivePaint();
                CaptureCoordinator.ExitGate(lease);
                reentered = true;
                return Task.FromResult<byte[]?>([42]);
            },
            CancellationToken.None,
            acquireGate: false);

        var finished = await Task.WhenAny(
            capture,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(capture, finished); // did not deadlock
        Assert.True(reentered);
        Assert.Equal(new byte[] { 42 }, await capture);
        Assert.False(_coordinator.CaptureActive);
    }

    [Fact]
    public void Live_paint_reentry_recovers_abandoned_gate()
    {
        var abandonedLease = CaptureCoordinator.EnterLivePaint();
        var replacementLease = CaptureCoordinator.EnterLivePaint();

        CaptureCoordinator.ExitGate(abandonedLease);
        var result = _coordinator.RunCapture(
            () => new byte[] { 9 },
            TimeSpan.FromMilliseconds(100));

        Assert.Equal(new byte[] { 9 }, result);
        CaptureCoordinator.ExitGate(replacementLease);
    }
}
