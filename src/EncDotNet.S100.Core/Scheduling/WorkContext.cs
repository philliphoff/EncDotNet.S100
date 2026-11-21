namespace EncDotNet.S100.Core.Scheduling;

/// <summary>What a running job can ask its scheduler.</summary>
public sealed class WorkContext
{
    private readonly WorkScheduler _scheduler;

    internal WorkContext(WorkScheduler scheduler, WorkJob job)
    {
        _scheduler = scheduler;
        Job = job;
    }

    internal WorkJob Job { get; }

    /// <summary>The job's key.</summary>
    public WorkKey Key => Job.Key;

    /// <summary>Signalled when no one wants the result any more.</summary>
    public CancellationToken Cancelled => Job.Cancellation.Token;

    /// <summary>The job's priority now. It may rise while the job runs.</summary>
    public WorkPriority Priority => _scheduler.PriorityOf(Job);

    /// <summary>
    /// Whether a job of a better class is waiting and no worker is free, so
    /// this one should yield (<see cref="YieldAsync"/>) at its next safe point.
    /// </summary>
    public bool YieldRequested => _scheduler.IsYieldRequested(Job);

    /// <summary>
    /// Gives the worker to a better job when <see cref="YieldRequested"/>,
    /// and resumes this job once a worker takes it again at its priority.
    /// Completes at once otherwise.
    /// </summary>
    /// <returns>A task to await.</returns>
    public async ValueTask YieldAsync()
    {
        if (YieldRequested)
        {
            _scheduler.RecordYield();
            await Task.Yield();
        }
    }

    /// <summary>
    /// Schedules (or joins) a child job that inherits this job's priority for
    /// as long as this job runs. The interest is released when this job
    /// finishes.
    /// </summary>
    /// <typeparam name="T">The child's result type.</typeparam>
    /// <param name="key">The child's key.</param>
    /// <param name="stage">The child's stage.</param>
    /// <param name="run">The child's work.</param>
    /// <returns>This job's interest in the child.</returns>
    public WorkInterest<T> Spawn<T>(WorkKey key, WorkStage stage, Func<WorkContext, ValueTask<T>> run) =>
        _scheduler.ScheduleDependency(Job, key, stage, run);

    /// <summary>
    /// Makes <paramref name="handle"/>'s job inherit this job's priority for
    /// as long as this job runs: a visible job that needs a background job's
    /// result promotes it.
    /// </summary>
    /// <typeparam name="T">The other job's result type.</typeparam>
    /// <param name="handle">The job this one needs.</param>
    /// <returns>This job's interest in it.</returns>
    public WorkInterest<T> DependOn<T>(WorkHandle<T> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return _scheduler.AddDependency(Job, handle);
    }
}
