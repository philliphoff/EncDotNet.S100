using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// PR-M3: covers the debounced settings-saver primitive used to coalesce
/// rapid splitter drags into a single disk write.
/// </summary>
public sealed class DebouncedSettingsSaverTests
{
    [Fact]
    public void RequestSave_FiresAfterDelay()
    {
        var saves = 0;
        using var fired = new ManualResetEventSlim();
        using var saver = new DebouncedSettingsSaver(
            () => { Interlocked.Increment(ref saves); fired.Set(); },
            delayMilliseconds: 50);

        saver.RequestSave();

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5)), "Saver did not fire within 5 s");
        Assert.Equal(1, saves);
    }

    [Fact]
    public void RequestSave_MultipleCallsCoalesceIntoOneFire()
    {
        var saves = 0;
        using var fired = new ManualResetEventSlim();
        // A wide window so a rapid synchronous burst of requests cannot
        // realistically straddle it, even under heavy CI scheduler load.
        const int delayMilliseconds = 1000;
        using var saver = new DebouncedSettingsSaver(
            () => { Interlocked.Increment(ref saves); fired.Set(); },
            delayMilliseconds: delayMilliseconds);

        // Issue the requests as a rapid synchronous burst (no sleeps): each
        // RequestSave restarts the single debounce timer, so only the final
        // one survives and fires exactly once. The previous version spaced
        // the calls with Thread.Sleep(10); under CI starvation a sleep could
        // exceed the 100 ms window, letting the timer fire mid-burst and then
        // re-arm — producing a second save (flaky, issue #215).
        for (int i = 0; i < 10; i++)
        {
            saver.RequestSave();
        }

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5)), "Saver did not fire within 5 s");
        // Give any (incorrectly) scheduled extra fire a chance to land before
        // asserting coalescence. A spurious second timer would fire at roughly
        // the same instant as the first, so a short margin is sufficient.
        Thread.Sleep(300);

        Assert.Equal(1, saves);
    }

    [Fact]
    public void Flush_WritesImmediatelyAndCancelsPendingTimer()
    {
        var saves = 0;
        using var saver = new DebouncedSettingsSaver(() => Interlocked.Increment(ref saves), delayMilliseconds: 5000);

        saver.RequestSave();
        saver.Flush();

        Assert.Equal(1, saves);

        Thread.Sleep(50);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void Flush_WithoutPendingSave_IsNoOp()
    {
        var saves = 0;
        using var saver = new DebouncedSettingsSaver(() => Interlocked.Increment(ref saves), delayMilliseconds: 50);

        saver.Flush();

        Assert.Equal(0, saves);
    }

    [Fact]
    public void Dispose_FlushesPendingSave()
    {
        var saves = 0;
        var saver = new DebouncedSettingsSaver(() => Interlocked.Increment(ref saves), delayMilliseconds: 5000);

        saver.RequestSave();
        saver.Dispose();

        Assert.Equal(1, saves);
    }

    [Fact]
    public void RunSave_SwallowsExceptions()
    {
        using var saver = new DebouncedSettingsSaver(() => throw new InvalidOperationException("boom"), delayMilliseconds: 5000);

        saver.RequestSave();
        var ex = Record.Exception(() => saver.Flush());

        Assert.Null(ex);
    }
}
