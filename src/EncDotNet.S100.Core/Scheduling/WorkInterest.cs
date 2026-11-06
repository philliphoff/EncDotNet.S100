namespace EncDotNet.S100.Core.Scheduling;

/// <summary>
/// One holder's interest in a scheduled job. The job runs at the best
/// priority of its interests and is cancelled when the last one is released.
/// </summary>
public abstract class WorkInterest : IDisposable
{
    private protected WorkInterest(WorkScheduler scheduler, WorkJob job, WorkPriority priority)
    {
        Scheduler = scheduler;
        Job = job;
        Priority = priority;
    }

    internal WorkScheduler Scheduler { get; }

    internal WorkJob Job { get; }

    /// <summary>This holder's priority for the job.</summary>
    public WorkPriority Priority { get; internal set; }

    /// <summary>Whether <see cref="Dispose"/> has run.</summary>
    public bool IsReleased { get; internal set; }

    /// <summary>The job's completion, without its result.</summary>
    public abstract Task Completion { get; }

    /// <summary>Whether the job has finished, failed or been cancelled.</summary>
    public bool IsCompleted => Completion.IsCompleted;

    /// <summary>Whether the job has started on a worker.</summary>
    public bool HasStarted => Job.HasStarted;

    /// <summary>
    /// Changes this holder's priority. The job moves in the queue, or runs on
    /// at its new priority, at once. Does nothing once released.
    /// </summary>
    /// <param name="priority">The new priority.</param>
    public void Update(WorkPriority priority) => Scheduler.UpdateInterest(this, priority);

    /// <summary>
    /// Releases the interest. When it was the last, a queued job is dropped
    /// and a running one sees <see cref="WorkContext.Cancelled"/>.
    /// </summary>
    public void Dispose()
    {
        Scheduler.ReleaseInterest(this);
        GC.SuppressFinalize(this);
    }
}

/// <summary>An interest in a job with a result.</summary>
/// <typeparam name="T">The result type.</typeparam>
public sealed class WorkInterest<T> : WorkInterest
{
    internal WorkInterest(WorkScheduler scheduler, WorkJob<T> job, WorkPriority priority)
        : base(scheduler, job, priority)
    {
        Handle = new WorkHandle<T>(scheduler, job);
    }

    /// <summary>The shared job.</summary>
    public WorkHandle<T> Handle { get; }

    /// <summary>The job's result. Cancelled when the job is dropped or cancelled.</summary>
    public Task<T> Result => Handle.Result;

    /// <inheritdoc />
    public override Task Completion => Handle.Result;
}

/// <summary>A scheduled job that several holders can share.</summary>
/// <typeparam name="T">The result type.</typeparam>
public sealed class WorkHandle<T>
{
    private readonly WorkScheduler _scheduler;
    private readonly WorkJob<T> _job;

    internal WorkHandle(WorkScheduler scheduler, WorkJob<T> job)
    {
        _scheduler = scheduler;
        _job = job;
    }

    /// <summary>The job's result. Cancelled when the job is dropped or cancelled.</summary>
    public Task<T> Result => _job.Completion.Task;

    /// <summary>
    /// Adds another holder. Fails with a cancelled interest when the job has
    /// already been dropped.
    /// </summary>
    /// <param name="priority">The new holder's priority.</param>
    /// <returns>The interest.</returns>
    public WorkInterest<T> AddInterest(WorkPriority priority) => _scheduler.AddInterest(_job, priority);
}
