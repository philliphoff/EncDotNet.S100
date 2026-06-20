using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit coverage for <see cref="PaletteLoadCoordinator"/>, the shared
/// concurrency-safe palette-load protocol introduced for issue #321. These
/// tests exercise the coordinator directly against a real
/// <see cref="PortrayalAssetCache"/> so the guarantees hold independently of
/// any single product spec's catalogue.
/// </summary>
public class PaletteLoadCoordinatorTests
{
    [Fact]
    public async Task EnsureLoaded_RunsLoadExactlyOnce_ThenUsesFastPath()
    {
        var cache = new PortrayalAssetCache();
        var loadCount = 0;
        var appliedCount = 0;

        ValueTask Load(CancellationToken _)
        {
            loadCount++;
            return ValueTask.CompletedTask;
        }

        for (var i = 0; i < 5; i++)
        {
            await PaletteLoadCoordinator.EnsureLoadedAsync(cache, Load, () => appliedCount++);
        }

        Assert.Equal(1, loadCount);
        Assert.Equal(5, appliedCount);
        Assert.True(cache.PalettesLoaded);
    }

    [Fact]
    public async Task EnsureLoaded_WhenLoadCancelled_DoesNotPoisonCache()
    {
        var cache = new PortrayalAssetCache();
        var attempts = 0;
        using var cts = new CancellationTokenSource();

        ValueTask Load(CancellationToken ct)
        {
            attempts++;
            // Simulate the in-flight load being cancelled partway through
            // (e.g. the viewer aborts a render on pan/zoom/reload).
            if (attempts == 1)
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            return ValueTask.CompletedTask;
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await PaletteLoadCoordinator.EnsureLoadedAsync(cache, Load, () => { }, cts.Token));

        // The cancelled attempt must NOT have committed the loaded flag.
        Assert.False(cache.PalettesLoaded);

        // A subsequent (uncancelled) load succeeds and commits.
        await PaletteLoadCoordinator.EnsureLoadedAsync(cache, Load, () => { });

        Assert.True(cache.PalettesLoaded);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task EnsureLoaded_UnderConcurrency_LoadsOnlyOnce()
    {
        var cache = new PortrayalAssetCache();
        var loadCount = 0;

        async ValueTask SlowLoad(CancellationToken ct)
        {
            Interlocked.Increment(ref loadCount);
            await Task.Delay(50, ct);
        }

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () =>
                await PaletteLoadCoordinator.EnsureLoadedAsync(cache, SlowLoad, () => { })))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, loadCount);
        Assert.True(cache.PalettesLoaded);
    }
}
