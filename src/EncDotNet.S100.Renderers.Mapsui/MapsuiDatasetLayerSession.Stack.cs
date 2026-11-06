using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

// The chart stack off the map thread (map rendering slice 6). A compose is
// captured (cheap, under _sync), computed (the S-98 projection and overlap
// suppression, on the work scheduler) and applied (display state, clips and
// the layer diff, in one post to the map thread). Versions order them: every
// capture takes the next version, and a computed stack whose version is not
// newer than the one on the map is dropped.
public sealed partial class MapsuiDatasetLayerSession
{
    /// <summary>
    /// How long a stack job waits before it captures, so cells that finish
    /// together compose once. <c>S100_STACK_BATCH_MS</c> overrides it.
    /// Arrivals while a compose is computing or applying always join the next
    /// one, whatever the window.
    /// </summary>
    internal static TimeSpan StackBatchWindow { get; set; } = TimeSpan.FromMilliseconds(
        int.TryParse(Environment.GetEnvironmentVariable("S100_STACK_BATCH_MS"), out var ms) && ms >= 0 ? ms : 16);

    private readonly Action<Action>? _stackMarshal;
    private readonly CoverageOverlapIndex _overlapIndex = new();
    private readonly List<StackWaiter> _stackWaiters = [];

    // All under _sync.
    private long _stackRequested;
    private long _stackApplied;
    private long _stackFailed;
    private Exception? _stackFailure;
    private bool _stackJobRunning;

    /// <summary>Stack versions asked for so far (a test seam).</summary>
    internal long StackRequestedVersion
    {
        get { lock (_sync) return _stackRequested; }
    }

    /// <summary>The newest stack version on the map (a test seam).</summary>
    internal long StackAppliedVersion
    {
        get { lock (_sync) return _stackApplied; }
    }

    /// <summary>Stack composes computed so far, applied or dropped (a test seam).</summary>
    internal long StackComputeCount => Interlocked.Read(ref _stackComputes);

    private long _stackComputes;

    /// <summary>Exact coverage intersection tests run by the overlap index (a test seam).</summary>
    internal long OverlapIntersectionTests
    {
        get { lock (_overlapIndex) return _overlapIndex.IntersectionTests; }
    }

    /// <summary>One dataset's composition inputs, captured under <c>_sync</c>.</summary>
    private sealed record StackEntryInfo(
        Entry Entry,
        MapsuiMapDatasetSnapshot Snapshot,
        CoverageLayoutCache CoverageLayout,
        int? RankingScale);

    /// <summary>Everything a compose reads, captured under <c>_sync</c>.</summary>
    private sealed record StackInput(
        long Version,
        IReadOnlyList<MapsuiMapDatasetSnapshot> Snapshots,
        IReadOnlyDictionary<MapDatasetId, StackEntryInfo> Entries,
        IInteroperabilityAuthority Authority,
        MarinerSettings Mariner,
        bool IgnoreScaleMinimum);

    /// <summary>A computed stack, ready to apply.</summary>
    private sealed record StackPlan(
        StackInput Input,
        IReadOnlyList<MapsuiProjectedDatasetLayer> Projected,
        IReadOnlyList<LayerStackEntry> StackEntries,
        IReadOnlyList<ILayer> StackedLayers,
        IReadOnlyList<(IReadOnlyList<ILayer> Layers, FinerCoverage[]? Finer)> Suppression);

    private sealed record StackWaiter(long Version, TaskCompletionSource Completion);

    private StackInput CaptureStackInput(long version)
    {
        var snapshots = CreateOrderedSnapshots();
        var entries = new Dictionary<MapDatasetId, StackEntryInfo>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var entry = _entries[snapshot.Dataset.Id];
            entries[snapshot.Dataset.Id] = new StackEntryInfo(entry, snapshot, entry.CoverageLayout, RankingScale(entry));
        }

        return new StackInput(
            version,
            snapshots,
            entries,
            _authorityProvider.Current,
            _mariner,
            _ignoreScaleMinimum);
    }

    /// <summary>
    /// Computes a stack from captured inputs. Reads no mutable session state
    /// and changes no layer, so it may run on any thread.
    /// </summary>
    private StackPlan ComputeStack(StackInput input)
    {
        using var activity = Diagnostics.Telemetry.ActivitySource.StartActivity("s100.cell.compose");
        Interlocked.Increment(ref _stackComputes);
        var (projected, stackEntries, stackedLayers) = ProjectLayerStack(input);
        ValidateProjection(projected, input.Snapshots);
        var suppression = ComputeOverlapSuppression(input, projected);
        return new StackPlan(input, projected, stackEntries, stackedLayers, suppression);
    }

    /// <summary>
    /// Asks for a background compose that includes the current state, and
    /// returns the version it will carry. Call under <c>_sync</c>.
    /// </summary>
    private long RequestStackCompose()
    {
        var version = ++_stackRequested;
        if (!_stackJobRunning)
        {
            _stackJobRunning = true;
            // The job must not inherit the caller's WorkDemand: it composes
            // for every cell, at visible priority.
            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(RunStackJobAsync);
            }
        }

        return version;
    }

    private async Task RunStackJobAsync()
    {
        while (true)
        {
            if (StackBatchWindow > TimeSpan.Zero)
                await Task.Delay(StackBatchWindow).ConfigureAwait(false);

            StackInput input;
            lock (_sync)
            {
                // A failed version isn't retried until something new asks.
                if (_disposed || Math.Max(_stackApplied, _stackFailed) >= _stackRequested)
                {
                    _stackJobRunning = false;
                    return;
                }

                input = CaptureStackInput(_stackRequested);
            }

            StackPlan? plan = null;
            Exception? error = null;
            try
            {
                using var interest = WorkScheduler.Shared.Schedule(
                    new WorkKey(new object()),
                    WorkStage.Stack,
                    WorkPriority.Visible(),
                    _ => ValueTask.FromResult(ComputeStack(input)));
                plan = await interest.Result.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            // Publish and apply in one post; the next compose captures only
            // after this one is on the map, so arrivals in between batch.
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _stackMarshal!(() =>
                {
                    try
                    {
                        ApplyPublishedStack(input.Version, plan, error);
                    }
                    finally
                    {
                        applied.TrySetResult();
                    }
                });
            }
            catch (Exception ex)
            {
                // The map thread is gone (host shutting down).
                FailStackWaiters(input.Version, ex);
                lock (_sync)
                    _stackJobRunning = false;
                return;
            }

            await applied.Task.ConfigureAwait(false);
        }
    }

    // On the map thread.
    private void ApplyPublishedStack(long version, StackPlan? plan, Exception? error)
    {
        var changed = false;
        lock (_sync)
        {
            if (_disposed)
                return;

            if (error is null && plan is not null && IsCurrent(plan))
            {
                try
                {
                    ApplyStack(plan);
                    changed = true;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }

            if (error is not null)
                FailStackWaitersCore(version, error);
        }

        if (changed)
            LayersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether a computed stack may go on the map: it's newer than the one
    /// there, and every dataset it was built from is still in the session as
    /// the same entry. Every change that removes a dataset, reorders the stack
    /// or changes display state composes synchronously, as a newer version, so
    /// such a stack is dropped. A cell that landed again since the capture
    /// keeps its captured rendering in this stack (it was current a moment
    /// ago, and layers are never disposed); its landing asked for the next
    /// compose, which brings the new one. Dropping the stack for that instead
    /// would hold back every reveal while a wave of cells keeps landing.
    /// </summary>
    private bool IsCurrent(StackPlan plan)
    {
        if (plan.Input.Version <= _stackApplied)
            return false;
        foreach (var (id, info) in plan.Input.Entries)
        {
            if (!_entries.TryGetValue(id, out var entry) || !ReferenceEquals(entry, info.Entry))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Waits until a stack of <paramref name="version"/> or newer is on the
    /// map. When composing it failed, puts the cell's previous rendering back
    /// (unless it has rendered again since), recomposes without it and
    /// rethrows, as a synchronous compose would.
    /// </summary>
    private async Task AwaitStackAsync(
        MapDatasetId datasetId,
        Entry renderEntry,
        long generation,
        long version,
        RenderingState previous)
    {
        Task wait;
        lock (_sync)
        {
            if (_stackApplied >= version)
                return;

            if (_stackFailed >= version && _stackFailure is { } failure)
            {
                // The compose failed before this waiter arrived.
                wait = Task.FromException(failure);
            }
            else
            {
                var waiter = new StackWaiter(version, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                _stackWaiters.Add(waiter);
                wait = waiter.Completion.Task;
            }
        }

        try
        {
            await wait.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var recomposed = false;
            lock (_sync)
            {
                if (!_disposed
                    && _entries.TryGetValue(datasetId, out var entry)
                    && ReferenceEquals(entry, renderEntry)
                    && entry.Generation == generation)
                {
                    entry.RestoreRendering(previous);
                    try
                    {
                        ComposeLayers();
                        recomposed = true;
                    }
                    catch
                    {
                        // The original failure is the one to report.
                    }
                }
            }

            if (recomposed)
                LayersChanged?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }

    // Under _sync.
    private void CompleteStackWaiters()
    {
        for (var i = _stackWaiters.Count - 1; i >= 0; i--)
        {
            if (_stackWaiters[i].Version <= _stackApplied)
            {
                _stackWaiters[i].Completion.TrySetResult();
                _stackWaiters.RemoveAt(i);
            }
        }
    }

    private void FailStackWaiters(long version, Exception error)
    {
        lock (_sync)
            FailStackWaitersCore(version, error);
    }

    // Under _sync. Fails the waiters the failed compose would have satisfied.
    private void FailStackWaitersCore(long version, Exception error)
    {
        if (version >= _stackFailed)
        {
            _stackFailed = version;
            _stackFailure = error;
        }

        for (var i = _stackWaiters.Count - 1; i >= 0; i--)
        {
            if (_stackWaiters[i].Version <= version)
            {
                _stackWaiters[i].Completion.TrySetException(error);
                _stackWaiters.RemoveAt(i);
            }
        }
    }

    // Under _sync, on dispose.
    private void CancelStackWaiters()
    {
        foreach (var waiter in _stackWaiters)
            waiter.Completion.TrySetCanceled();
        _stackWaiters.Clear();
    }
}
