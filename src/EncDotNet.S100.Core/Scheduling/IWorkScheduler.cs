namespace EncDotNet.S100.Core.Scheduling;

/// <summary>
/// One scheduler for every stage of map work (map rendering design, "Work
/// scheduler"): parse, translate, portray, scene, clip, raster, IO and
/// validation share one queue and one set of worker threads, ordered by
/// <see cref="WorkPriority"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Strict priority, then urgency.</b> A free worker takes the best
/// queued job of any stage. There are no per-stage or per-owner quotas.</item>
/// <item><b>Single-flight.</b> Scheduling a key that is queued or running
/// joins that job.</item>
/// <item><b>Interest.</b> Every <see cref="Schedule{T}"/> returns an
/// interest. A job runs at the best priority of its interests and is
/// cancelled when the last one is released.</item>
/// <item><b>Inheritance.</b> Jobs a job spawns or depends on
/// (<see cref="WorkContext.Spawn{T}"/>, <see cref="WorkContext.DependOn{T}"/>)
/// run at least at its priority, which may rise while they wait or run.</item>
/// <item><b>Short jobs.</b> Keep a job to about 100 ms: a visible job waits
/// for at most one job length. Longer work splits into child jobs or yields
/// (<see cref="WorkContext.YieldAsync"/>).</item>
/// <item><b>GC-aware admission.</b> Allocation-heavy stages in the
/// <see cref="WorkClass.Background"/> and <see cref="WorkClass.Validation"/>
/// classes are capped by GC pause share (<see cref="GcPauseGovernor"/>);
/// visible and predicted work is never capped.</item>
/// </list>
/// </remarks>
public interface IWorkScheduler
{
    /// <summary>
    /// Queues a job, or joins the queued or running job with the same
    /// <paramref name="key"/>, and returns the caller's interest in it.
    /// </summary>
    /// <typeparam name="T">The result type. Joining a job of another result type throws.</typeparam>
    /// <param name="key">Identifies the job for single-flight.</param>
    /// <param name="stage">The pipeline stage.</param>
    /// <param name="priority">The caller's priority for it.</param>
    /// <param name="run">
    /// The work. It starts on a worker thread. An <c>await</c> inside it
    /// resumes on a worker at the job's current priority, unless the awaited
    /// call uses <c>ConfigureAwait(false)</c>.
    /// </param>
    /// <returns>The interest. Dispose it when the result is no longer wanted.</returns>
    WorkInterest<T> Schedule<T>(WorkKey key, WorkStage stage, WorkPriority priority, Func<WorkContext, ValueTask<T>> run);

    /// <summary>Reads the scheduler's counters.</summary>
    /// <returns>The counters now.</returns>
    WorkSchedulerStats GetStats();
}

/// <summary>A snapshot of a scheduler's counters.</summary>
/// <param name="Workers">Worker threads.</param>
/// <param name="BusyWorkers">Workers running a job now.</param>
/// <param name="QueuedByClass">Jobs waiting for a worker, indexed by <see cref="WorkClass"/>.</param>
/// <param name="Suspended">Started jobs awaiting something off the workers.</param>
/// <param name="AllocationHeavyRunning">Admitted allocation-heavy background jobs not yet finished.</param>
/// <param name="AllocationHeavyLimit">How many of those the GC pause governor allows now.</param>
/// <param name="LastGcPauseShare">The governor's last judged GC pause share, or null.</param>
/// <param name="Completed">Jobs finished (with a result, a fault or cancelled after they started).</param>
/// <param name="Cancelled">Jobs dropped before they started because no one wanted them.</param>
/// <param name="BusyMilliseconds">Total worker time spent running jobs.</param>
/// <param name="WaitByClass">
/// Time from a job being queued, or promoted to its class, to a worker
/// starting it, indexed by the <see cref="WorkClass"/> it started in.
/// </param>
/// <param name="RunByClass">Time a worker spent per run of a job, indexed by <see cref="WorkClass"/>.</param>
public sealed record WorkSchedulerStats(
    int Workers,
    int BusyWorkers,
    IReadOnlyList<int> QueuedByClass,
    int Suspended,
    int AllocationHeavyRunning,
    int AllocationHeavyLimit,
    double? LastGcPauseShare,
    long Completed,
    long Cancelled,
    double BusyMilliseconds,
    IReadOnlyList<LatencySnapshot> WaitByClass,
    IReadOnlyList<LatencySnapshot> RunByClass);
