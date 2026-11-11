using System.Diagnostics;
using System.Globalization;

namespace EncDotNet.S100.Core.Scheduling;

/// <summary>Settings for a <see cref="WorkScheduler"/>.</summary>
public sealed record WorkSchedulerOptions
{
    /// <summary>The environment variable that pins <see cref="WorkerCount"/> for the shared scheduler.</summary>
    public const string WorkerCountVariable = "S100_WORK_THREADS";

    /// <summary>Dedicated worker threads, at least 1.</summary>
    public required int WorkerCount { get; init; }

    /// <summary>
    /// The stages whose background jobs the GC pause governor admits. By
    /// default portrayal and pattern clipping, the largest allocators.
    /// </summary>
    public IReadOnlySet<WorkStage> AllocationHeavyStages { get; init; } =
        new HashSet<WorkStage> { WorkStage.Portray, WorkStage.Clip };

    /// <summary>
    /// The GC pause governor's settings: by default
    /// <see cref="GcPauseGovernorOptions.Resolve"/> for this machine, so
    /// <see cref="GcPauseGovernorOptions.FixedConcurrencyVariable"/> pins it.
    /// </summary>
    public GcPauseGovernorOptions Admission { get; init; } = GcPauseGovernorOptions.Resolve(
        Environment.ProcessorCount,
        Environment.GetEnvironmentVariable(GcPauseGovernorOptions.FixedConcurrencyVariable));

    /// <summary>The worker threads' name prefix.</summary>
    public string ThreadName { get; init; } = "S100 work";

    /// <summary>
    /// Options for an interactive host: one worker per logical processor but
    /// one, leaving a core for the UI and render threads, unless
    /// <see cref="WorkerCountVariable"/> pins the count.
    /// </summary>
    /// <returns>The options.</returns>
    public static WorkSchedulerOptions ForInteractive() =>
        new() { WorkerCount = PinnedWorkers() ?? Math.Max(1, Environment.ProcessorCount - 1) };

    /// <summary>
    /// Options for a batch host such as the CLI: one worker per logical
    /// processor, unless <see cref="WorkerCountVariable"/> pins the count.
    /// </summary>
    /// <returns>The options.</returns>
    public static WorkSchedulerOptions ForBatch() =>
        new() { WorkerCount = PinnedWorkers() ?? Math.Max(1, Environment.ProcessorCount) };

    private static int? PinnedWorkers() =>
        int.TryParse(Environment.GetEnvironmentVariable(WorkerCountVariable), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;
}

/// <summary>
/// The work scheduler: dedicated worker threads that always take the best
/// queued job. See <see cref="IWorkScheduler"/> for the rules.
/// </summary>
/// <remarks>
/// Jobs never run on the .NET thread pool, so pool starvation can't hold
/// them back and they can't starve async continuations elsewhere.
/// </remarks>
public sealed class WorkScheduler : IWorkScheduler, IDisposable
{
    private const int ClassCount = 4;

    /// <summary>How long a freed admission slot counts as busy, for its producer to refill it.</summary>
    private static readonly TimeSpan RefillGrace = TimeSpan.FromMilliseconds(250);

    private static readonly object SharedSync = new();
    private static WorkScheduler? _shared;
    private static WorkSchedulerOptions? _sharedOptions;

    private readonly object _sync = new();
    private readonly WorkSchedulerOptions _options;
    private readonly Dictionary<WorkKey, WorkJob> _jobs = new();
    private readonly PriorityQueue<Entry, (double Urgency, long Sequence)>[] _queues = NewQueues();
    private readonly PriorityQueue<Entry, (double Urgency, long Sequence)>[] _heavyQueues = NewQueues();
    private readonly int[] _queuedByClass = new int[ClassCount];
    private readonly LatencyHistogram[] _waits = NewHistograms();
    private readonly LatencyHistogram[] _runs = NewHistograms();
    private readonly GcPauseGovernor _governor;
    private readonly Thread[] _threads;
    private long _sequence;
    private int _idleWorkers;
    private int _busyWorkers;
    private int _suspended;
    private int _heavyRunning;
    private long _lastHeavyFinishTicks;
    private long _completed;
    private long _cancelled;
    private long _busyTicks;
    private long _yields;
    private bool _disposed;

    /// <summary>Starts a scheduler and its worker threads.</summary>
    /// <param name="options">The settings.</param>
    public WorkScheduler(WorkSchedulerOptions options)
        : this(options, () => GC.GetTotalPauseDuration(), TimeProvider.System)
    {
    }

    internal WorkScheduler(WorkSchedulerOptions options, Func<TimeSpan> gcPauseTotal, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.WorkerCount, 1, nameof(options));
        _options = options;
        _governor = new GcPauseGovernor(options.Admission, gcPauseTotal, time);
        _threads = new Thread[options.WorkerCount];
        for (var i = 0; i < _threads.Length; i++)
        {
            _threads[i] = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"{options.ThreadName} {i + 1}",
            };
            _threads[i].Start();
        }
    }

    /// <summary>
    /// The process-wide scheduler, created on first use with the options from
    /// <see cref="ConfigureShared"/>, or <see cref="WorkSchedulerOptions.ForInteractive"/>.
    /// </summary>
    public static WorkScheduler Shared
    {
        get
        {
            var shared = Volatile.Read(ref _shared);
            if (shared is not null)
                return shared;
            lock (SharedSync)
            {
                return _shared ??= new WorkScheduler(_sharedOptions ?? WorkSchedulerOptions.ForInteractive());
            }
        }
    }

    /// <summary>
    /// Sets the options <see cref="Shared"/> is created with. A host calls it
    /// at start-up, before anything schedules work.
    /// </summary>
    /// <param name="options">The settings.</param>
    /// <returns><see langword="false"/> when the shared scheduler already exists, so the options weren't applied.</returns>
    public static bool ConfigureShared(WorkSchedulerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (SharedSync)
        {
            if (_shared is not null)
                return false;
            _sharedOptions = options;
            return true;
        }
    }

    /// <summary>The number of worker threads.</summary>
    public int WorkerCount => _threads.Length;

    /// <summary>
    /// How many allocation-heavy background jobs the GC pause governor admits
    /// now. Cheaper than <see cref="GetStats"/> for a producer that sizes how
    /// much it queues, such as the portrayal pre-warm.
    /// </summary>
    public int AllocationHeavyLimit
    {
        get
        {
            lock (_sync)
            {
                return _governor.Limit;
            }
        }
    }

    /// <inheritdoc />
    public WorkInterest<T> Schedule<T>(WorkKey key, WorkStage stage, WorkPriority priority, Func<WorkContext, ValueTask<T>> run)
    {
        ArgumentNullException.ThrowIfNull(key.Owner, nameof(key));
        ArgumentNullException.ThrowIfNull(run);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_jobs.TryGetValue(key, out var existing) && !existing.Detached && !existing.CompletionTask.IsCompleted)
            {
                if (existing is not WorkJob<T> typed)
                {
                    throw new InvalidOperationException(
                        $"A job with key {key} is already scheduled with another result type.");
                }

                return AddInterestLocked(typed, priority);
            }

            var job = new WorkJob<T>(key, stage, _options.AllocationHeavyStages.Contains(stage), run);
            _jobs[key] = job;
            job.Priority = priority;
            var interest = new WorkInterest<T>(this, job, priority);
            job.Interests.Add(interest);
            job.Finishing = OnFinished;
            EnqueueLocked(job);
            return interest;
        }
    }

    /// <inheritdoc />
    public WorkSchedulerStats GetStats()
    {
        lock (_sync)
        {
            return new WorkSchedulerStats(
                _threads.Length,
                _busyWorkers,
                (int[])_queuedByClass.Clone(),
                _suspended,
                _heavyRunning,
                _governor.Limit,
                _governor.LastPauseShare,
                Interlocked.Read(ref _completed),
                Interlocked.Read(ref _cancelled),
                Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _busyTicks)).TotalMilliseconds,
                Array.ConvertAll(_waits, h => h.Snapshot()),
                Array.ConvertAll(_runs, h => h.Snapshot()));
        }
    }

    /// <summary>Times jobs gave their worker to a better one (<see cref="WorkContext.YieldAsync"/>).</summary>
    public long Yields => Interlocked.Read(ref _yields);

    /// <summary>
    /// Stops the workers once they finish their current job. Queued jobs are
    /// cancelled. Blocks for up to five seconds.
    /// </summary>
    public void Dispose()
    {
        List<WorkJob> dropped;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            dropped = [.. _jobs.Values];
            _jobs.Clear();
            Monitor.PulseAll(_sync);
        }

        foreach (var job in dropped)
        {
            if (!job.HasStarted)
                job.Cancel();
            else
                job.Cancellation.Cancel();
        }

        foreach (var thread in _threads)
        {
            if (thread != Thread.CurrentThread)
                thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    internal WorkInterest<T> AddInterest<T>(WorkJob<T> job, WorkPriority priority)
    {
        lock (_sync)
        {
            return AddInterestLocked(job, priority);
        }
    }

    internal WorkInterest<T> ScheduleDependency<T>(WorkJob parent, WorkKey key, WorkStage stage, Func<WorkContext, ValueTask<T>> run)
    {
        lock (_sync)
        {
            var interest = Schedule(key, stage, parent.Priority, run);
            TrackDependencyLocked(parent, interest);
            return interest;
        }
    }

    internal WorkInterest<T> AddDependency<T>(WorkJob parent, WorkHandle<T> handle)
    {
        lock (_sync)
        {
            var interest = handle.AddInterest(parent.Priority);
            TrackDependencyLocked(parent, interest);
            return interest;
        }
    }

    internal WorkPriority PriorityOf(WorkJob job)
    {
        lock (_sync)
        {
            return job.Priority;
        }
    }

    internal bool IsYieldRequested(WorkJob job)
    {
        if (Volatile.Read(ref _idleWorkers) > 0)
            return false;
        var cls = (int)job.Priority.Class;
        for (var c = 0; c < cls; c++)
        {
            if (Volatile.Read(ref _queuedByClass[c]) > 0)
                return true;
        }

        return false;
    }

    internal void RecordYield() => Interlocked.Increment(ref _yields);

    internal void UpdateInterest(WorkInterest interest, WorkPriority priority)
    {
        lock (_sync)
        {
            if (interest.IsReleased || interest.Priority == priority)
                return;
            interest.Priority = priority;
            RecomputePriorityLocked(interest.Job);
        }
    }

    internal void ReleaseInterest(WorkInterest interest)
    {
        WorkJob? cancelQueued = null;
        WorkJob? cancelRunning = null;
        lock (_sync)
        {
            if (interest.IsReleased)
                return;
            interest.IsReleased = true;
            var job = interest.Job;
            job.Interests.Remove(interest);
            if (job.IsDone)
                return;
            if (job.Interests.Count > 0)
            {
                RecomputePriorityLocked(job);
                return;
            }

            // No one wants it: drop it if it hasn't started, else tell it.
            DetachLocked(job);
            if (!job.HasStarted)
            {
                job.IsDone = true;
                DequeueLocked(job);
                _cancelled++;
                cancelQueued = job;
            }
            else
            {
                cancelRunning = job;
            }
        }

        // Outside the lock: cancellation callbacks and completion run user code.
        cancelQueued?.Cancel();
        cancelRunning?.Cancellation.Cancel();
    }

    internal void PostContinuation(WorkJob job, SendOrPostCallback callback, object? state)
    {
        lock (_sync)
        {
            if (!_disposed && !job.IsDone)
            {
                (job.Continuations ??= new()).Enqueue((callback, state));
                if (job.Runnable && !job.Enqueued)
                {
                    // Started, off the workers and not queued: it was suspended.
                    _suspended--;
                    EnqueueLocked(job);
                }

                return;
            }
        }

        // Shutting down, or the job has finished (an await inside it outlived
        // it, such as fire-and-forget work): finish on the pool so it unwinds.
        ThreadPool.UnsafeQueueUserWorkItem(s => callback(s), state, preferLocal: false);
    }

    private WorkInterest<T> AddInterestLocked<T>(WorkJob<T> job, WorkPriority priority)
    {
        var interest = new WorkInterest<T>(this, job, priority);
        if (job.Detached || job.IsDone)
        {
            // Too late to join: hand back a released interest on the finished job.
            interest.IsReleased = true;
            return interest;
        }

        job.Interests.Add(interest);
        RecomputePriorityLocked(job);
        return interest;
    }

    private void TrackDependencyLocked(WorkJob parent, WorkInterest interest)
    {
        if (parent.IsDone)
        {
            ReleaseInterest(interest);
            return;
        }

        (parent.Dependencies ??= []).Add(interest);
    }

    private void RecomputePriorityLocked(WorkJob job)
    {
        if (job.IsDone || job.Interests.Count == 0)
            return;
        var best = job.Interests[0].Priority;
        for (var i = 1; i < job.Interests.Count; i++)
            best = WorkPriority.Best(best, job.Interests[i].Priority);
        if (best == job.Priority)
            return;

        var promoted = best.Class < job.Priority.Class;
        job.Priority = best;
        if (job.Enqueued)
        {
            DequeueLocked(job);
            EnqueueLocked(job, resetWait: promoted);
        }

        if (job.Dependencies is { } dependencies)
        {
            foreach (var dependency in dependencies)
            {
                if (!dependency.IsReleased && dependency.Priority != best)
                {
                    dependency.Priority = best;
                    RecomputePriorityLocked(dependency.Job);
                }
            }
        }
    }

    private void EnqueueLocked(WorkJob job, bool resetWait = true)
    {
        var cls = job.Priority.Class;
        job.Version++;
        job.Enqueued = true;
        if (resetWait || job.QueuedAtTicks == 0)
            job.QueuedAtTicks = Stopwatch.GetTimestamp();
        job.QueuedClass = cls;
        _queuedByClass[(int)cls]++;
        var queue = (IsGoverned(job) ? _heavyQueues : _queues)[(int)cls];
        queue.Enqueue(new Entry(job, job.Version), (job.Priority.Urgency, _sequence++));
        if (queue.Count > 4 * (_queuedByClass[(int)cls] + 64))
            Compact(queue);
        if (_idleWorkers > 0)
            Monitor.Pulse(_sync);
    }

    private void DequeueLocked(WorkJob job)
    {
        if (!job.Enqueued)
            return;
        job.Enqueued = false;
        job.Version++;
        _queuedByClass[(int)job.QueuedClass]--;
    }

    private void DetachLocked(WorkJob job)
    {
        if (job.Detached)
            return;
        job.Detached = true;
        if (_jobs.TryGetValue(job.Key, out var mapped) && ReferenceEquals(mapped, job))
            _jobs.Remove(job.Key);
    }

    /// <summary>Background and validation jobs of an allocation-heavy stage go through admission.</summary>
    private static bool IsGoverned(WorkJob job) =>
        job.AllocationHeavy && job.Priority.Class >= WorkClass.Background && !job.HasStarted;

    private bool TryTakeLocked(out WorkJob job, out bool starting, out int dispatchClass)
    {
        // A slot freed moments ago still counts as busy: its producer (the
        // pre-warm) refills it on its next pass, and the governor only grows
        // after windows in which every slot was used.
        var refilling = Stopwatch.GetElapsedTime(_lastHeavyFinishTicks) < RefillGrace;
        if (_heavyRunning > 0 || refilling || HasHeavyQueuedLocked())
        {
            _governor.Observe(refilling ? Math.Max(_heavyRunning, _governor.Limit) : _heavyRunning);
        }
        else
        {
            _governor.Restart();
        }

        var heavyAllowed = _heavyRunning < _governor.Limit;
        for (var c = 0; c < ClassCount; c++)
        {
            var hasNormal = TryPeekLocked(_queues[c], out var normal, out var normalPriority);
            Entry heavy = default;
            (double Urgency, long Sequence) heavyPriority = default;
            var hasHeavy = heavyAllowed && TryPeekLocked(_heavyQueues[c], out heavy, out heavyPriority);
            if (!hasNormal && !hasHeavy)
                continue;

            Entry chosen;
            if (hasNormal && (!hasHeavy || normalPriority.CompareTo(heavyPriority) <= 0))
            {
                _queues[c].Dequeue();
                chosen = normal;
            }
            else
            {
                _heavyQueues[c].Dequeue();
                chosen = heavy;
            }

            job = chosen.Job;
            DequeueLocked(job);
            starting = !job.HasStarted;
            if (starting)
            {
                job.HasStarted = true;
                if (IsGovernedClass(job))
                {
                    job.Admitted = true;
                    _heavyRunning++;
                }

                _waits[c].Record(Stopwatch.GetElapsedTime(job.QueuedAtTicks).TotalMilliseconds);
            }

            job.OnWorker = true;
            dispatchClass = c;
            return true;
        }

        job = null!;
        starting = false;
        dispatchClass = 0;
        return false;

        static bool IsGovernedClass(WorkJob j) => j.AllocationHeavy && j.Priority.Class >= WorkClass.Background;
    }

    /// <summary>
    /// Drops stale entries (jobs re-queued at another priority, or dropped):
    /// re-prioritising leaves the old entry behind until it reaches the top,
    /// and a busy queue that keeps re-sorting would otherwise grow without end.
    /// </summary>
    private static void Compact(PriorityQueue<Entry, (double Urgency, long Sequence)> queue)
    {
        var live = new List<(Entry, (double, long))>(queue.Count / 2);
        foreach (var (entry, priority) in queue.UnorderedItems)
        {
            if (entry.Job.Enqueued && entry.Job.Version == entry.Version)
                live.Add((entry, priority));
        }

        queue.Clear();
        queue.EnqueueRange(live);
    }

    private bool HasHeavyQueuedLocked()
    {
        for (var c = 0; c < ClassCount; c++)
        {
            if (TryPeekLocked(_heavyQueues[c], out _, out _))
                return true;
        }

        return false;
    }

    /// <summary>Peeks the best live entry, dropping stale ones (re-queued at another priority, or dropped).</summary>
    private static bool TryPeekLocked(
        PriorityQueue<Entry, (double Urgency, long Sequence)> queue,
        out Entry entry,
        out (double Urgency, long Sequence) priority)
    {
        while (queue.TryPeek(out entry, out priority))
        {
            if (entry.Job.Enqueued && entry.Job.Version == entry.Version)
                return true;
            queue.Dequeue();
        }

        return false;
    }

    private void WorkerLoop()
    {
        while (true)
        {
            WorkJob job;
            bool starting;
            int cls;
            lock (_sync)
            {
                while (true)
                {
                    if (_disposed)
                        return;
                    if (TryTakeLocked(out job, out starting, out cls))
                        break;
                    _idleWorkers++;
                    // A timed wait so the governor can re-open admission when a
                    // window passes with no job finishing to wake anyone.
                    Monitor.Wait(_sync, HasHeavyQueuedLocked() ? 250 : Timeout.Infinite);
                    _idleWorkers--;
                }

                _busyWorkers++;
            }

            var started = Stopwatch.GetTimestamp();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new WorkSynchronizationContext(this, job));
            try
            {
                if (starting)
                {
                    var context = new WorkContext(this, job);
                    _ = job.StartAsync(context);
                }
                else
                {
                    (SendOrPostCallback Callback, object? State) next;
                    lock (_sync)
                    {
                        next = job.Continuations!.Dequeue();
                    }

                    next.Callback(next.State);
                }
            }
            catch (Exception ex)
            {
                // A continuation callback throwing escapes the job's own
                // exception handling; keep the worker alive.
                Trace.TraceError($"Work scheduler job {job.Key} threw: {ex}");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
            }

            var elapsed = Stopwatch.GetTimestamp() - started;
            Interlocked.Add(ref _busyTicks, elapsed);
            _runs[cls].Record(Stopwatch.GetElapsedTime(started, started + elapsed).TotalMilliseconds);
            lock (_sync)
            {
                _busyWorkers--;
                job.OnWorker = false;
                if (job.IsDone)
                    continue;
                if (job.Runnable)
                    EnqueueLocked(job);
                else
                    _suspended++;
            }
        }
    }

    private void OnFinished(WorkJob job)
    {
        List<WorkInterest>? dependencies;
        lock (_sync)
        {
            if (!job.IsDone)
            {
                if (job.HasStarted)
                {
                    _completed++;
                    if (!job.OnWorker && !job.Enqueued)
                        _suspended--;
                }

                job.IsDone = true;
                DequeueLocked(job);
            }

            DetachLocked(job);
            if (job.Admitted)
            {
                job.Admitted = false;
                _heavyRunning--;
                _lastHeavyFinishTicks = Stopwatch.GetTimestamp();
                if (_idleWorkers > 0)
                    Monitor.Pulse(_sync);
            }

            dependencies = job.Dependencies;
            job.Dependencies = null;
        }

        if (dependencies is not null)
        {
            foreach (var dependency in dependencies)
                dependency.Dispose();
        }

    }

    private static PriorityQueue<Entry, (double, long)>[] NewQueues()
    {
        var queues = new PriorityQueue<Entry, (double, long)>[ClassCount];
        for (var i = 0; i < queues.Length; i++)
            queues[i] = new PriorityQueue<Entry, (double, long)>();
        return queues;
    }

    private static LatencyHistogram[] NewHistograms()
    {
        var histograms = new LatencyHistogram[ClassCount];
        for (var i = 0; i < histograms.Length; i++)
            histograms[i] = new LatencyHistogram();
        return histograms;
    }

    private readonly record struct Entry(WorkJob Job, int Version);
}
