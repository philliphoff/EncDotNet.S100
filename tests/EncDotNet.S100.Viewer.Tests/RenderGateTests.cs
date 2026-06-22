using System;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Viewer.Diagnostics;
using Xunit;

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
}
