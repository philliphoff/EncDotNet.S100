using EncDotNet.S100.Core.Caching;
using EncDotNet.S100.Core.Scheduling;

namespace EncDotNet.S100.Core.Tests;

/// <summary>Runs alone: its tests keep the thread pool busy on purpose.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ThreadPoolStarvationCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Thread pool starvation";
}

/// <summary>
/// A <see cref="ContentStore"/> goes idle as soon as its background jobs
/// finish, even when the thread pool is too busy to run continuations: on a
/// slow CI runner, waiting for the pool once kept
/// <see cref="ContentStore.WaitForWritesIdle"/> from returning for over ten
/// seconds after the work was done.
/// </summary>
[Collection(ThreadPoolStarvationCollection.Name)]
public sealed class ContentStoreIdleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "content-store-idle-" + Guid.NewGuid().ToString("N"));
    private readonly WorkScheduler _scheduler = new(new WorkSchedulerOptions { WorkerCount = 2 });

    public void Dispose()
    {
        _scheduler.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_store_goes_idle_when_its_jobs_finish_however_busy_the_thread_pool()
    {
        // Opening a versioned kind deletes other versions' folders in a background job.
        var stale = Path.Combine(_root, "tiles", "v1");
        Directory.CreateDirectory(stale);
        File.WriteAllBytes(Path.Combine(stale, "a.bin"), [1, 2, 3]);

        // Every pool thread, and those the pool adds over the next seconds, waits here.
        using var release = new ManualResetEventSlim();
        var blockers = (Environment.ProcessorCount * 16) + 64;
        for (var i = 0; i < blockers; i++)
        {
            ThreadPool.UnsafeQueueUserWorkItem(static gate => gate.Wait(TimeSpan.FromSeconds(30)), release, preferLocal: false);
        }

        try
        {
            var store = new ContentStore(_root, new ContentStoreOptions { Scheduler = _scheduler });
            store.Open("tiles", 1 << 20, new ContentKindOptions { FormatVersion = 2 });

            Assert.True(store.WaitForWritesIdle(TimeSpan.FromSeconds(3)), "The store didn't go idle with the thread pool busy.");
            Assert.False(Directory.Exists(stale));
        }
        finally
        {
            release.Set();
        }
    }
}
