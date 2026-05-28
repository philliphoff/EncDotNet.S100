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
        using var saver = new DebouncedSettingsSaver(() => Interlocked.Increment(ref saves), delayMilliseconds: 50);

        saver.RequestSave();
        Thread.Sleep(200);

        Assert.Equal(1, saves);
    }

    [Fact]
    public void RequestSave_MultipleCallsCoalesceIntoOneFire()
    {
        var saves = 0;
        using var saver = new DebouncedSettingsSaver(() => Interlocked.Increment(ref saves), delayMilliseconds: 100);

        for (int i = 0; i < 10; i++)
        {
            saver.RequestSave();
            Thread.Sleep(10);
        }
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
