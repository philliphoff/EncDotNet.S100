namespace EncDotNet.S100.Core.Scheduling;

/// <summary>A job's state inside the scheduler. Guarded by the scheduler's lock.</summary>
internal abstract class WorkJob
{
    protected WorkJob(WorkKey key, WorkStage stage, bool allocationHeavy)
    {
        Key = key;
        Stage = stage;
        AllocationHeavy = allocationHeavy;
    }

    public WorkKey Key { get; }

    public WorkStage Stage { get; }

    public bool AllocationHeavy { get; }

    public readonly CancellationTokenSource Cancellation = new();

    public readonly List<WorkInterest> Interests = new(1);

    /// <summary>Interests this job holds on jobs it spawned or depends on.</summary>
    public List<WorkInterest>? Dependencies;

    /// <summary>Continuations posted by the job's awaits, waiting for a worker.</summary>
    public Queue<(SendOrPostCallback Callback, object? State)>? Continuations;

    public WorkPriority Priority;

    public bool HasStarted;

    public bool IsDone;

    /// <summary>The scheduler's key map no longer points here (cancelled or finished).</summary>
    public bool Detached;

    /// <summary>A worker is running this job's start or a continuation now.</summary>
    public bool OnWorker;

    /// <summary>An entry with <see cref="Version"/> is in a queue.</summary>
    public bool Enqueued;

    public int Version;

    public WorkClass QueuedClass;

    public long QueuedAtTicks;

    /// <summary>Counted against the GC pause governor's limit until it finishes.</summary>
    public bool Admitted;

    public bool Runnable => !IsDone && (!HasStarted || (Continuations is { Count: > 0 } && !OnWorker));

    /// <summary>Runs before the result is published, so the scheduler's books are straight first.</summary>
    public Action<WorkJob>? Finishing;

    public abstract ValueTask StartAsync(WorkContext context);

    public abstract void Cancel();

    public abstract Task CompletionTask { get; }
}

/// <summary>A job with a result.</summary>
internal sealed class WorkJob<T> : WorkJob
{
    private readonly Func<WorkContext, ValueTask<T>> _run;

    public WorkJob(WorkKey key, WorkStage stage, bool allocationHeavy, Func<WorkContext, ValueTask<T>> run)
        : base(key, stage, allocationHeavy)
    {
        _run = run;
    }

    public readonly TaskCompletionSource<T> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override Task CompletionTask => Completion.Task;

    public override ValueTask StartAsync(WorkContext context)
    {
        ValueTask<T> pending;
        try
        {
            pending = _run(context);
        }
        catch (OperationCanceledException) when (Cancellation.IsCancellationRequested)
        {
            Finish(c => c.TrySetCanceled(Cancellation.Token));
            return ValueTask.CompletedTask;
        }
        catch (Exception ex)
        {
            Finish(c => c.TrySetException(ex));
            return ValueTask.CompletedTask;
        }

        if (pending.IsCompletedSuccessfully)
        {
            var result = pending.Result;
            Finish(c => c.TrySetResult(result));
            return ValueTask.CompletedTask;
        }

        return new ValueTask(FinishAsync(pending));
    }

    private async Task FinishAsync(ValueTask<T> pending)
    {
        try
        {
            var result = await pending.ConfigureAwait(false);
            Finish(c => c.TrySetResult(result));
        }
        catch (OperationCanceledException) when (Cancellation.IsCancellationRequested)
        {
            Finish(c => c.TrySetCanceled(Cancellation.Token));
        }
        catch (Exception ex)
        {
            Finish(c => c.TrySetException(ex));
        }
    }

    public override void Cancel() => Finish(static c => c.TrySetCanceled());

    private void Finish(Action<TaskCompletionSource<T>> publish)
    {
        Finishing?.Invoke(this);
        publish(Completion);
    }
}

/// <summary>
/// Routes a job's <c>await</c> continuations back to the scheduler's queue at
/// the job's current priority.
/// </summary>
internal sealed class WorkSynchronizationContext(WorkScheduler scheduler, WorkJob job) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => scheduler.PostContinuation(job, d, state);

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    public override SynchronizationContext CreateCopy() => this;
}
