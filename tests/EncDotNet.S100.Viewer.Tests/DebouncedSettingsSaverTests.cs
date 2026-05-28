using System;
using System.Threading;
using EncDotNet.S100.Viewer.Services;
using Xunit;

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
        using var saver = new DebouncedSettingsSaver(
            () => { Interlocked.Increment(ref saves); fired.Set(); },
            delayMilliseconds: 100);

        for (int i = 0; i < 10; i++)
        {
            saver.RequestSave();
            Thread.Sleep(10);
        }

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5)), "Saver did not fire within 5 s");
        // Give any (incorrectly) scheduled extra fires a chance to land
        // before asserting coalescence. The debounce window is 100 ms,
        // so 300 ms is plenty without being painfully slow.
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
