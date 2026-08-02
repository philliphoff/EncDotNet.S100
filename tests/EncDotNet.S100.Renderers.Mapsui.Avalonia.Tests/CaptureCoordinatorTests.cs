namespace EncDotNet.S100.Renderers.Mapsui.Avalonia.Tests;

public class CaptureCoordinatorTests
{
    [Fact]
    public void RunCapture_returns_capture_result()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var result = CaptureCoordinator.RunCapture(() => bytes, TimeSpan.FromSeconds(1));
        Assert.Same(bytes, result);
    }

    [Fact]
    public void RunCapture_propagates_exception_and_releases_gate()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CaptureCoordinator.RunCapture(
                () => throw new InvalidOperationException("boom"),
                TimeSpan.FromSeconds(1)));

        var lease = CaptureCoordinator.EnterLivePaint();
        CaptureCoordinator.ExitGate(lease);
    }

    [Fact]
    public async Task RunCapture_waits_while_live_paint_holds_gate()
    {
        var captureStarted = new ManualResetEventSlim(false);
        var releaseLivePaint = new ManualResetEventSlim(false);
        var paintThread = new Thread(() =>
        {
            var lease = CaptureCoordinator.EnterLivePaint();
            try
            {
                releaseLivePaint.Wait(TimeSpan.FromSeconds(5));
            }
            finally
            {
                CaptureCoordinator.ExitGate(lease);
            }
        });
        paintThread.Start();
        Thread.Sleep(50);

        var captureTask = Task.Run(() => CaptureCoordinator.RunCapture(
            () =>
            {
                captureStarted.Set();
                return Array.Empty<byte>();
            },
            TimeSpan.FromSeconds(5)));

        Assert.False(captureStarted.Wait(TimeSpan.FromMilliseconds(200)));
        releaseLivePaint.Set();
        Assert.True(captureStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotNull(await captureTask);
        paintThread.Join(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Capture_active_is_depth_counted_and_cleared_after_exception()
    {
        Assert.False(CaptureCoordinator.CaptureActive);
        CaptureCoordinator.BeginCapture();
        CaptureCoordinator.BeginCapture();
        CaptureCoordinator.EndCapture();
        Assert.True(CaptureCoordinator.CaptureActive);
        CaptureCoordinator.EndCapture();
        Assert.False(CaptureCoordinator.CaptureActive);

        Assert.Throws<InvalidOperationException>(() =>
            CaptureCoordinator.RunCapture(
                byte[]? () => throw new InvalidOperationException("boom"),
                TimeSpan.FromSeconds(1)));
        Assert.False(CaptureCoordinator.CaptureActive);
    }

    [Fact]
    public async Task Fresh_drain_wait_ignores_stale_signal_and_accepts_new_signal()
    {
        CaptureCoordinator.NotifyDrained();
        Assert.False(
            CaptureCoordinator.WaitForFreshDrain(TimeSpan.FromMilliseconds(50)));

        var waitTask = Task.Run(() =>
            CaptureCoordinator.WaitForFreshDrain(TimeSpan.FromSeconds(5)));
        await Task.Delay(50);
        CaptureCoordinator.NotifyDrained();
        Assert.True(await waitTask);
    }

    [Fact]
    public async Task CaptureDrained_requests_repaint_before_capture()
    {
        var order = new List<string>();
        var result = await CaptureCoordinator.CaptureDrainedAsync(
            () =>
            {
                order.Add("repaint");
                CaptureCoordinator.NotifyDrained();
                return Task.CompletedTask;
            },
            () =>
            {
                order.Add("capture");
                Assert.True(CaptureCoordinator.CaptureActive);
                return Task.FromResult<byte[]?>([7]);
            },
            CancellationToken.None);

        Assert.Equal(new byte[] { 7 }, result);
        Assert.Equal(["repaint", "capture"], order);
        Assert.False(CaptureCoordinator.CaptureActive);
    }

    [Fact]
    public void Live_paint_reentry_recovers_abandoned_gate()
    {
        var abandonedLease = CaptureCoordinator.EnterLivePaint();
        var replacementLease = CaptureCoordinator.EnterLivePaint();

        CaptureCoordinator.ExitGate(abandonedLease);
        var result = CaptureCoordinator.RunCapture(
            () => new byte[] { 9 },
            TimeSpan.FromMilliseconds(100));

        Assert.Equal(new byte[] { 9 }, result);
        CaptureCoordinator.ExitGate(replacementLease);
    }
}
