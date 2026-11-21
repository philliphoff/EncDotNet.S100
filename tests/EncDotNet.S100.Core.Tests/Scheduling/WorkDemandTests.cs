using System.Collections.Concurrent;
using EncDotNet.S100.Core.Scheduling;

namespace EncDotNet.S100.Core.Tests.Scheduling;

public sealed class WorkDemandTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WorkScheduler Create(int workers) =>
        new(new WorkSchedulerOptions { WorkerCount = workers, ThreadName = "S100 demand test" });

    /// <summary>Holds the only worker until released.</summary>
    private static async Task<TaskCompletionSource> BlockAsync(WorkScheduler scheduler)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Schedule(new WorkKey("block"), WorkStage.Raster, WorkPriority.Visible(-1), _ =>
        {
            started.SetResult();
            release.Task.Wait(Timeout);
            return ValueTask.FromResult(true);
        });
        await started.Task.WaitAsync(Timeout, Ct);
        return release;
    }

    [Fact]
    public async Task RunAsync_UsesTheCurrentDemand_AndFollowsItsUpdates()
    {
        using var scheduler = Create(1);
        var release = await BlockAsync(scheduler);
        var order = new ConcurrentQueue<string>();
        var predicted = scheduler.RunAsync(WorkStage.Raster, _ => { order.Enqueue("predicted"); return 0; }, Ct, WorkPriority.Predicted());

        var demand = new WorkDemand(WorkPriority.Background());
        Task<int> portray;
        using (WorkDemand.Enter(demand))
        {
            portray = scheduler.RunAsync(WorkStage.Portray, _ => { order.Enqueue("portray"); return 1; }, Ct);
        }

        Assert.Null(WorkDemand.Current);
        demand.Update(WorkPriority.Visible());
        release.SetResult();
        await Task.WhenAll(predicted, portray).WaitAsync(Timeout, Ct);
        Assert.Equal(["portray", "predicted"], order);
    }

    [Fact]
    public async Task Demand_FlowsIntoAsyncCallsEnteredInsideIt()
    {
        var demand = new WorkDemand(WorkPriority.Predicted());
        WorkDemand? seen = null;

        async Task LoadAsync()
        {
            await Task.Yield();
            seen = WorkDemand.Current;
        }

        Task running;
        using (WorkDemand.Enter(demand))
        {
            running = LoadAsync();
        }

        await running.WaitAsync(Timeout, Ct);
        Assert.Same(demand, seen);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeItStarts_DropsTheJob()
    {
        using var scheduler = Create(1);
        var release = await BlockAsync(scheduler);
        using var cts = new CancellationTokenSource();
        var ran = false;
        var job = scheduler.RunAsync(WorkStage.Parse, _ => { ran = true; return 0; }, cts.Token);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.WaitAsync(Timeout, Ct));
        release.SetResult();
        await scheduler.RunAsync(WorkStage.Parse, _ => 0, Ct).WaitAsync(Timeout, Ct);
        Assert.False(ran);
    }

    [Fact]
    public async Task RunAsync_AsyncWork_HoldsTheJobUntilItCompletes()
    {
        using var scheduler = Create(1);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? resumedOn = null;
        var job = scheduler.RunAsync(WorkStage.Portray, async _ =>
        {
            await gate.Task;
            resumedOn = Thread.CurrentThread.Name;
            return 7;
        }, Ct);

        // The worker is free while the job awaits, and the job isn't done.
        await scheduler.RunAsync(WorkStage.Raster, _ => 0, Ct).WaitAsync(Timeout, Ct);
        Assert.False(job.IsCompleted);
        Assert.Equal(1, scheduler.GetStats().Suspended);

        gate.SetResult();
        Assert.Equal(7, await job.WaitAsync(Timeout, Ct));
        Assert.StartsWith("S100 demand test", resumedOn);
    }
}
