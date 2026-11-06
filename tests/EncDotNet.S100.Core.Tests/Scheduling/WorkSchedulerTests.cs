using System.Collections.Concurrent;
using EncDotNet.S100.Core.Scheduling;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Core.Tests.Scheduling;

public sealed class WorkSchedulerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static WorkScheduler Create(int workers, GcPauseGovernorOptions? admission = null, Func<TimeSpan>? pause = null) =>
        new(
            new WorkSchedulerOptions
            {
                WorkerCount = workers,
                Admission = admission ?? GcPauseGovernorOptions.ForProcessors(8),
                ThreadName = "S100 test work",
            },
            pause ?? (() => TimeSpan.Zero),
            TimeProvider.System);

    /// <summary>Occupies every worker until released, so the test can line up a queue.</summary>
    private static (List<WorkInterest<bool>> Blockers, TaskCompletionSource Release) Block(WorkScheduler scheduler)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new CountdownEvent(scheduler.WorkerCount);
        var blockers = new List<WorkInterest<bool>>();
        for (var i = 0; i < scheduler.WorkerCount; i++)
        {
            blockers.Add(scheduler.Schedule(new WorkKey("block", i), WorkStage.Raster, WorkPriority.Visible(-1), _ =>
            {
                started.Signal();
                release.Task.Wait(Timeout);
                return ValueTask.FromResult(true);
            }));
        }

        Assert.True(started.Wait(Timeout));
        return (blockers, release);
    }

    private static Func<WorkContext, ValueTask<bool>> Record(ConcurrentQueue<string> order, string name) => _ =>
    {
        order.Enqueue(name);
        return ValueTask.FromResult(true);
    };

    [Fact]
    public async Task Runs_by_class_then_urgency()
    {
        using var scheduler = Create(1);
        var (_, release) = Block(scheduler);
        var order = new ConcurrentQueue<string>();
        var jobs = new[]
        {
            scheduler.Schedule(new WorkKey("v", 1), WorkStage.Raster, WorkPriority.Validation(), Record(order, "validation")),
            scheduler.Schedule(new WorkKey("v", 2), WorkStage.Raster, WorkPriority.Background(), Record(order, "background")),
            scheduler.Schedule(new WorkKey("v", 3), WorkStage.Raster, WorkPriority.Predicted(), Record(order, "predicted")),
            scheduler.Schedule(new WorkKey("v", 4), WorkStage.Raster, WorkPriority.Visible(2), Record(order, "visible far")),
            scheduler.Schedule(new WorkKey("v", 5), WorkStage.Raster, WorkPriority.Visible(1), Record(order, "visible near")),
        };

        release.SetResult();
        await Task.WhenAll(jobs.Select(j => j.Result)).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["visible near", "visible far", "predicted", "background", "validation"], order);
    }

    [Fact]
    public async Task Same_key_joins_the_queued_job()
    {
        using var scheduler = Create(1);
        var (_, release) = Block(scheduler);
        var runs = 0;
        Func<WorkContext, ValueTask<int>> run = _ => ValueTask.FromResult(Interlocked.Increment(ref runs));
        var first = scheduler.Schedule(new WorkKey("cell", "a"), WorkStage.Portray, WorkPriority.Background(), run);
        var second = scheduler.Schedule(new WorkKey("cell", "a"), WorkStage.Portray, WorkPriority.Visible(), run);

        Assert.Same(first.Result, second.Result);
        release.SetResult();
        Assert.Equal(1, await first.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Joining_raises_the_job_to_the_best_interest()
    {
        using var scheduler = Create(1);
        var (_, release) = Block(scheduler);
        var order = new ConcurrentQueue<string>();
        var predicted = scheduler.Schedule(new WorkKey("p"), WorkStage.Raster, WorkPriority.Predicted(), Record(order, "predicted"));
        var shared = scheduler.Schedule(new WorkKey("s"), WorkStage.Raster, WorkPriority.Background(), Record(order, "shared"));
        using var visibleInterest = shared.Handle.AddInterest(WorkPriority.Visible());

        release.SetResult();
        await Task.WhenAll(predicted.Result, shared.Result).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["shared", "predicted"], order);
    }

    [Fact]
    public async Task Update_moves_a_queued_job()
    {
        using var scheduler = Create(1);
        var (_, release) = Block(scheduler);
        var order = new ConcurrentQueue<string>();
        var a = scheduler.Schedule(new WorkKey("a"), WorkStage.Raster, WorkPriority.Background(1), Record(order, "a"));
        var b = scheduler.Schedule(new WorkKey("b"), WorkStage.Raster, WorkPriority.Background(2), Record(order, "b"));
        b.Update(WorkPriority.Visible());

        release.SetResult();
        await Task.WhenAll(a.Result, b.Result).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["b", "a"], order);
    }

    [Fact]
    public async Task Releasing_the_last_interest_drops_a_queued_job()
    {
        using var scheduler = Create(1);
        var (_, release) = Block(scheduler);
        var ran = false;
        var first = scheduler.Schedule(new WorkKey("x"), WorkStage.Raster, WorkPriority.Visible(), _ =>
        {
            ran = true;
            return ValueTask.FromResult(true);
        });
        var second = first.Handle.AddInterest(WorkPriority.Predicted());

        first.Dispose();
        Assert.False(first.Result.IsCompleted);
        second.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken));

        release.SetResult();
        var after = scheduler.Schedule(new WorkKey("y"), WorkStage.Raster, WorkPriority.Background(), _ => ValueTask.FromResult(true));
        await after.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.False(ran);
        Assert.Equal(1, scheduler.GetStats().Cancelled);
    }

    [Fact]
    public async Task Releasing_the_last_interest_cancels_a_running_job()
    {
        using var scheduler = Create(1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var interest = scheduler.Schedule(new WorkKey("running"), WorkStage.Raster, WorkPriority.Visible(), ctx =>
        {
            started.SetResult();
            Assert.True(ctx.Cancelled.WaitHandle.WaitOne(Timeout));
            ctx.Cancelled.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        });

        await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        interest.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interest.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_cancelled_running_key_can_be_scheduled_again()
    {
        using var scheduler = Create(2);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = scheduler.Schedule(new WorkKey("k"), WorkStage.Raster, WorkPriority.Visible(), async ctx =>
        {
            await gate.Task;
            return 1;
        });
        while (!first.HasStarted)
            await Task.Delay(1, TestContext.Current.CancellationToken);
        first.Dispose();

        var second = scheduler.Schedule(new WorkKey("k"), WorkStage.Raster, WorkPriority.Visible(), _ => ValueTask.FromResult(2));
        Assert.Equal(2, await second.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        gate.SetResult();
    }

    [Fact]
    public async Task Await_resumes_on_a_worker_and_frees_it_meanwhile()
    {
        using var scheduler = Create(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? resumedOn = null;
        var waiting = scheduler.Schedule(new WorkKey("await"), WorkStage.Io, WorkPriority.Visible(), async _ =>
        {
            await gate.Task;
            resumedOn = Thread.CurrentThread.Name;
            return true;
        });

        // The only worker is free while the first job awaits.
        var other = scheduler.Schedule(new WorkKey("other"), WorkStage.Raster, WorkPriority.Background(), _ => ValueTask.FromResult(true));
        await other.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, scheduler.GetStats().Suspended);

        gate.SetResult();
        await waiting.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.StartsWith("S100 test work", resumedOn);
    }

    [Fact]
    public async Task A_dependency_inherits_the_dependent_priority()
    {
        using var scheduler = Create(1);
        var (_, release) = Block(scheduler);
        var order = new ConcurrentQueue<string>();
        var portrayal = scheduler.Schedule(new WorkKey("portray"), WorkStage.Portray, WorkPriority.Background(), _ =>
        {
            order.Enqueue("portray");
            return ValueTask.FromResult(42);
        });
        var predicted = scheduler.Schedule(new WorkKey("predicted"), WorkStage.Raster, WorkPriority.Predicted(), Record(order, "predicted"));
        var tile = scheduler.Schedule(new WorkKey("tile"), WorkStage.Raster, WorkPriority.Visible(), async ctx =>
        {
            order.Enqueue("tile start");
            var value = await ctx.DependOn(portrayal.Handle).Result;
            order.Enqueue("tile end");
            return value;
        });

        release.SetResult();
        Assert.Equal(42, await tile.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        await predicted.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["tile start", "portray", "tile end", "predicted"], order);
    }

    [Fact]
    public async Task A_spawned_child_follows_its_parent_when_promoted()
    {
        using var scheduler = Create(1);
        var order = new ConcurrentQueue<string>();
        var childGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkInterest<bool>? child = null;
        var spawned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = scheduler.Schedule(new WorkKey("parent"), WorkStage.Portray, WorkPriority.Background(), async ctx =>
        {
            child = ctx.Spawn(new WorkKey("child"), WorkStage.Portray, Record(order, "child"));
            spawned.SetResult();
            await childGate.Task;
            return await child.Result;
        });
        await spawned.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        parent.Update(WorkPriority.Visible(3));
        Assert.Equal(WorkPriority.Visible(3), child!.Priority);

        childGate.SetResult();
        Assert.True(await parent.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken));

        // The child's interest goes with the parent.
        Assert.True(child.IsReleased);
    }

    [Fact]
    public async Task Admission_caps_allocation_heavy_background_jobs()
    {
        using var scheduler = Create(3, GcPauseGovernorOptions.Fixed(1));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var peak = 0;
        Func<WorkContext, ValueTask<bool>> heavy = _ =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            release.Task.Wait(Timeout);
            Interlocked.Decrement(ref running);
            return ValueTask.FromResult(true);
        };
        var heavyJobs = Enumerable.Range(0, 3)
            .Select(i => scheduler.Schedule(new WorkKey("heavy", i), WorkStage.Portray, WorkPriority.Background(), heavy))
            .ToList();
        // A visible portrayal is never capped, and a background raster isn't allocation-heavy.
        var visible = scheduler.Schedule(new WorkKey("visible portrayal"), WorkStage.Portray, WorkPriority.Visible(), _ => ValueTask.FromResult(true));
        var raster = scheduler.Schedule(new WorkKey("raster"), WorkStage.Raster, WorkPriority.Background(), _ => ValueTask.FromResult(true));

        await Task.WhenAll(visible.Result, raster.Result).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref running));
        Assert.Equal(1, scheduler.GetStats().AllocationHeavyRunning);

        release.SetResult();
        await Task.WhenAll(heavyJobs.Select(j => j.Result)).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task Yield_gives_the_worker_to_a_better_job()
    {
        using var scheduler = Create(1);
        var order = new ConcurrentQueue<string>();
        var looping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var background = scheduler.Schedule(new WorkKey("long"), WorkStage.Clip, WorkPriority.Background(), async ctx =>
        {
            looping.SetResult();
            var deadline = DateTime.UtcNow + Timeout;
            while (!ctx.YieldRequested && DateTime.UtcNow < deadline)
                Thread.SpinWait(100);
            order.Enqueue("background yields");
            await ctx.YieldAsync();
            order.Enqueue("background resumes");
            return true;
        });

        await looping.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        var visible = scheduler.Schedule(new WorkKey("visible"), WorkStage.Raster, WorkPriority.Visible(), Record(order, "visible"));
        await Task.WhenAll(background.Result, visible.Result).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal(["background yields", "visible", "background resumes"], order);
        Assert.Equal(1, scheduler.Yields);
    }

    [Fact]
    public async Task Records_the_wait_of_each_start_by_class()
    {
        using var scheduler = Create(2);
        var before = scheduler.GetStats();
        var jobs = Enumerable.Range(0, 5)
            .Select(i => scheduler.Schedule(new WorkKey("w", i), WorkStage.Raster, WorkPriority.Visible(), _ => ValueTask.FromResult(true)))
            .ToList();
        await Task.WhenAll(jobs.Select(j => j.Result)).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        var after = scheduler.GetStats();

        Assert.Equal(5, after.WaitByClass[(int)WorkClass.Visible].Since(before.WaitByClass[(int)WorkClass.Visible]).Count);
        Assert.Equal(0, after.WaitByClass[(int)WorkClass.Background].Count);
        Assert.Equal(5, after.Completed - before.Completed);
    }

    [Fact]
    public async Task A_faulting_job_faults_its_result_and_keeps_the_worker()
    {
        using var scheduler = Create(1);
        var bad = scheduler.Schedule<bool>(new WorkKey("bad"), WorkStage.Raster, WorkPriority.Visible(), _ => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bad.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        var good = scheduler.Schedule(new WorkKey("good"), WorkStage.Raster, WorkPriority.Visible(), _ => ValueTask.FromResult(true));
        Assert.True(await good.Result.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Joining_with_another_result_type_throws()
    {
        using var scheduler = Create(1);
        var (_, release) = Block(scheduler);
        scheduler.Schedule(new WorkKey("typed"), WorkStage.Raster, WorkPriority.Visible(), _ => ValueTask.FromResult(1));
        Assert.Throws<InvalidOperationException>(() =>
            scheduler.Schedule(new WorkKey("typed"), WorkStage.Raster, WorkPriority.Visible(), _ => ValueTask.FromResult("x")));
        release.SetResult();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value
            && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}

public sealed class GcPauseGovernorTests
{
    [Fact]
    public void Halves_on_high_pause_share_and_grows_when_saturated_and_calm()
    {
        var time = new FakeTimeProvider();
        var pause = TimeSpan.Zero;
        var governor = new GcPauseGovernor(
            new GcPauseGovernorOptions
            {
                MaxConcurrency = 8,
                InitialConcurrency = 4,
                Window = TimeSpan.FromSeconds(2),
                BackOffPauseShare = 0.15,
                GrowPauseShare = 0.05,
            },
            () => pause,
            time);

        governor.Observe(4);
        time.Advance(TimeSpan.FromSeconds(2));
        pause += TimeSpan.FromMilliseconds(600); // 30%
        Assert.True(governor.Observe(4));
        Assert.Equal(2, governor.Limit);
        Assert.Equal(0.3, governor.LastPauseShare!.Value, 3);

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(governor.Observe(2));
        Assert.Equal(3, governor.Limit);

        // Not saturated: it holds.
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(governor.Observe(1));
        Assert.Equal(3, governor.Limit);
    }

    [Fact]
    public void Fixed_never_moves()
    {
        var time = new FakeTimeProvider();
        var pause = TimeSpan.Zero;
        var governor = new GcPauseGovernor(GcPauseGovernorOptions.Fixed(3), () => pause, time);
        governor.Observe(3);
        time.Advance(TimeSpan.FromSeconds(2));
        pause += TimeSpan.FromSeconds(1.9);
        Assert.False(governor.Observe(3));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(governor.Observe(3));
        Assert.Equal(3, governor.Limit);
    }
}

public sealed class LatencyHistogramTests
{
    [Fact]
    public void Percentiles_fall_within_a_bucket()
    {
        var histogram = new LatencyHistogram();
        var start = histogram.Snapshot();
        for (var i = 1; i <= 100; i++)
            histogram.Record(i);
        var window = histogram.Snapshot().Since(start);

        Assert.Equal(100, window.Count);
        Assert.InRange(window.Percentile(0.95), 95, 95 * 1.11);
        Assert.InRange(window.Percentile(0.5), 50, 50 * 1.11);
        Assert.InRange(window.MaxMs, 100, 111);
        Assert.Equal(0, LatencySnapshot.Empty.Percentile(0.95));
    }
}
