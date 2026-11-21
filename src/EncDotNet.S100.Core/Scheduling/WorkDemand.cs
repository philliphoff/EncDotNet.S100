namespace EncDotNet.S100.Core.Scheduling;

/// <summary>
/// The priority of a piece of work that spans several scheduled jobs, such as
/// loading one cell (parse, then portray). Every job scheduled through
/// <see cref="WorkSchedulerExtensions.RunAsync{T}(IWorkScheduler, WorkStage, Func{CancellationToken, T}, CancellationToken, WorkPriority?)"/>
/// while the demand is current runs at its priority, and follows it when it
/// changes: a cell that a pan brings into view is promoted mid-load.
/// </summary>
/// <remarks>
/// The demand flows with the async call it is entered around
/// (<see cref="Enter"/>), like any <see cref="AsyncLocal{T}"/>, so code deep
/// in a load picks it up without a parameter.
/// </remarks>
public sealed class WorkDemand
{
    private static readonly AsyncLocal<WorkDemand?> CurrentDemand = new();

    private readonly object _sync = new();
    private readonly List<WorkInterest> _interests = new();
    private WorkPriority _priority;

    /// <summary>Creates a demand.</summary>
    /// <param name="priority">Its starting priority.</param>
    public WorkDemand(WorkPriority priority)
    {
        _priority = priority;
    }

    /// <summary>The demand flowing with the current async call, or null.</summary>
    public static WorkDemand? Current => CurrentDemand.Value;

    /// <summary>The demand's priority now.</summary>
    public WorkPriority Priority
    {
        get
        {
            lock (_sync)
            {
                return _priority;
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="demand"/> current until the returned scope is
    /// disposed. An async method called inside the scope keeps it for its
    /// whole run.
    /// </summary>
    /// <param name="demand">The demand, or null for none.</param>
    /// <returns>A scope that restores the previous demand.</returns>
    public static Scope Enter(WorkDemand? demand)
    {
        var previous = CurrentDemand.Value;
        CurrentDemand.Value = demand;
        return new Scope(previous);
    }

    /// <summary>Changes the priority of the demand and of every job it holds now.</summary>
    /// <param name="priority">The new priority.</param>
    public void Update(WorkPriority priority)
    {
        WorkInterest[] interests;
        lock (_sync)
        {
            if (_priority == priority)
                return;
            _priority = priority;
            interests = [.. _interests];
        }

        foreach (var interest in interests)
            interest.Update(priority);
    }

    internal void Track(WorkInterest interest)
    {
        WorkPriority priority;
        lock (_sync)
        {
            _interests.Add(interest);
            priority = _priority;
        }

        // The demand may have moved between scheduling and tracking.
        if (interest.Priority != priority)
            interest.Update(priority);
    }

    internal void Untrack(WorkInterest interest)
    {
        lock (_sync)
        {
            _interests.Remove(interest);
        }
    }

    /// <summary>Restores the previous demand when disposed.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly WorkDemand? _previous;

        internal Scope(WorkDemand? previous)
        {
            _previous = previous;
        }

        /// <inheritdoc />
        public void Dispose() => CurrentDemand.Value = _previous;
    }
}

/// <summary>Helpers for running ordinary code on an <see cref="IWorkScheduler"/>.</summary>
public static class WorkSchedulerExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> as one scheduler job, in place of
    /// <see cref="Task.Run(Func{Task})"/>, at the priority of the current
    /// <see cref="WorkDemand"/> (following it as it changes), else at
    /// <paramref name="fallback"/>, else as <see cref="WorkClass.Visible"/>.
    /// The job has its own key, so it never joins another.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="scheduler">The scheduler.</param>
    /// <param name="stage">The stage, for admission and statistics.</param>
    /// <param name="work">The work. Its token is cancelled when <paramref name="cancellationToken"/> is.</param>
    /// <param name="cancellationToken">Cancels the job: drops it if it hasn't started.</param>
    /// <param name="fallback">The priority when no demand is current.</param>
    /// <returns>The result.</returns>
    public static Task<T> RunAsync<T>(
        this IWorkScheduler scheduler,
        WorkStage stage,
        Func<CancellationToken, T> work,
        CancellationToken cancellationToken,
        WorkPriority? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunCoreAsync(scheduler, stage, token => ValueTask.FromResult(work(token)), cancellationToken, fallback);
    }

    /// <summary>
    /// Runs asynchronous <paramref name="work"/> as one scheduler job: like
    /// <see cref="RunAsync{T}(IWorkScheduler, WorkStage, Func{CancellationToken, T}, CancellationToken, WorkPriority?)"/>.
    /// The job lasts until the work completes, and its awaits resume on the
    /// scheduler's workers at the job's priority unless they use
    /// <c>ConfigureAwait(false)</c>.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="scheduler">The scheduler.</param>
    /// <param name="stage">The stage, for admission and statistics.</param>
    /// <param name="work">The work.</param>
    /// <param name="cancellationToken">Cancels the job: drops it if it hasn't started.</param>
    /// <param name="fallback">The priority when no demand is current.</param>
    /// <returns>The result.</returns>
    public static Task<T> RunAsync<T>(
        this IWorkScheduler scheduler,
        WorkStage stage,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken,
        WorkPriority? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunCoreAsync(scheduler, stage, token => new ValueTask<T>(work(token)), cancellationToken, fallback);
    }

    private static async Task<T> RunCoreAsync<T>(
        IWorkScheduler scheduler,
        WorkStage stage,
        Func<CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken,
        WorkPriority? fallback)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        cancellationToken.ThrowIfCancellationRequested();
        var demand = WorkDemand.Current;
        var priority = demand?.Priority ?? fallback ?? WorkPriority.Visible();
        var interest = scheduler.Schedule(
            new WorkKey(new object()),
            stage,
            priority,
            async ctx =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctx.Cancelled);
                return await work(linked.Token);
            });
        demand?.Track(interest);
        try
        {
            using (cancellationToken.Register(static i => ((WorkInterest)i!).Dispose(), interest))
            {
                return await interest.Result.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            demand?.Untrack(interest);
            interest.Dispose();
        }
    }
}
