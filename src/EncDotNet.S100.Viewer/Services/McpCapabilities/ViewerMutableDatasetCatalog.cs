using System.Diagnostics;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Viewer.Services.McpCapabilities;

/// <summary>
/// Adapts the viewer's read-only <see cref="IDatasetCatalog"/> plus its
/// UI-thread <see cref="IDatasetLoadGateway"/> to the shared
/// <see cref="IMutableDatasetCatalog"/> that backs the <c>open_dataset</c>,
/// <c>close_dataset</c>, and <c>close_all_datasets</c> tools.
/// </summary>
/// <remarks>
/// <para>
/// The read side (<see cref="Datasets"/> / <see cref="Changed"/>) is delegated
/// straight to the wrapped catalog, which already observes the loader. The
/// write side owns the orchestration the viewer's bespoke <c>open_dataset</c>
/// tool used to carry: classify the path, snapshot the catalog, trigger the
/// GUI load path, wait for an exchange set to quiesce, then diff the catalog to
/// report exactly which datasets were added. That logic lives here so the
/// shared tool stays a thin, renderer-neutral shell.
/// </para>
/// <para>
/// <see cref="Remove"/> and <see cref="RemoveAll"/> are synchronous on the
/// shared contract but the viewer's unload path is async and UI-thread-bound,
/// so they block on the gateway. That is safe because MCP tools run on
/// threadpool threads, never the UI thread — the gateway marshals to the UI
/// thread internally and the blocked threadpool thread cannot deadlock it.
/// </para>
/// </remarks>
internal sealed class ViewerMutableDatasetCatalog : IMutableDatasetCatalog
{
    private readonly IDatasetCatalog _inner;
    private readonly IDatasetLoadGateway _gateway;
    private readonly int _quietMs;
    private readonly int _maxWaitMs;

    /// <summary>Creates the adapter with production quiescence timings.</summary>
    public ViewerMutableDatasetCatalog(IDatasetCatalog inner, IDatasetLoadGateway gateway)
        : this(inner, gateway, quietMs: 600, maxWaitMs: 30_000)
    {
    }

    /// <summary>
    /// Test seam: allows tests to shorten the exchange-set quiescence debounce
    /// so timing paths are exercised quickly.
    /// </summary>
    /// <param name="inner">The read-only catalog to diff before / after a load.</param>
    /// <param name="gateway">The UI-thread load / unload gateway.</param>
    /// <param name="quietMs">Quiet window (no new datasets) that signals quiescence.</param>
    /// <param name="maxWaitMs">Hard ceiling on the exchange-set quiescence wait.</param>
    internal ViewerMutableDatasetCatalog(
        IDatasetCatalog inner, IDatasetLoadGateway gateway, int quietMs, int maxWaitMs)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(gateway);
        _inner = inner;
        _gateway = gateway;
        _quietMs = quietMs;
        _maxWaitMs = maxWaitMs;
    }

    /// <inheritdoc />
    public IReadOnlyList<LoadedDataset> Datasets => _inner.Datasets;

    /// <inheritdoc />
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed
    {
        add => _inner.Changed += value;
        remove => _inner.Changed -= value;
    }

    /// <inheritdoc />
    public async Task<DatasetLoadOutcome> LoadAsync(
        string path, string? specHint = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_gateway.IsReady)
        {
            throw new DatasetCatalogNotReadyException(
                "the dataset loader has not been initialised yet");
        }

        using var _ = await _gateway.LockAsync(cancellationToken).ConfigureAwait(false);

        var kind = _gateway.Classify(path);
        var before = _inner.Datasets.Select(d => d.Id.Value).ToHashSet(StringComparer.Ordinal);

        // Subscribe BEFORE triggering so synchronous adds (single-file load)
        // and the first asynchronous add (exchange set) are both observed.
        var activity = new SemaphoreSlim(0);
        void OnChanged(object? sender, DatasetCatalogChangedEventArgs e)
        {
            if (e.Kind is DatasetCatalogChangeKind.Added or DatasetCatalogChangeKind.Batch)
            {
                activity.Release();
            }
        }
        _inner.Changed += OnChanged;

        var timedOut = false;
        try
        {
            if (kind == DatasetPathKind.File)
            {
                var recognised = await _gateway
                    .LoadFileAsync(path, specHint, cancellationToken).ConfigureAwait(false);
                if (!recognised)
                {
                    // Unrecognised file type: report nothing added so the
                    // open tool surfaces a dataset_load_failed error.
                    return new DatasetLoadOutcome(path, DatasetSourceKind.File, [], TimedOut: false);
                }
                // Single-file load updates the catalog synchronously during the
                // awaited LoadAsync, so no quiescence wait is needed.
            }
            else
            {
                var dispatched = await _gateway
                    .TriggerExchangeSetAsync(path, cancellationToken).ConfigureAwait(false);
                if (dispatched == 0)
                {
                    // No datasets this viewer can read — fail fast rather than
                    // waiting out the quiet window.
                    return new DatasetLoadOutcome(path, DatasetSourceKind.ExchangeSet, [], TimedOut: false);
                }
                timedOut = await WaitForQuiescenceAsync(
                    activity, dispatched, () => CountAdded(before), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _inner.Changed -= OnChanged;
            // The SemaphoreSlim is intentionally NOT disposed: a catalog event
            // could still be racing toward OnChanged after the unsubscribe
            // above, and Release on a disposed semaphore throws. It uses no wait
            // handle (only WaitAsync), so it needs no deterministic disposal.
        }

        var added = _inner.Datasets
            .Where(d => !before.Contains(d.Id.Value))
            .Select(d => d.Id)
            .ToList();

        return new DatasetLoadOutcome(
            path,
            kind == DatasetPathKind.File ? DatasetSourceKind.File : DatasetSourceKind.ExchangeSet,
            added,
            timedOut);
    }

    /// <inheritdoc />
    public bool Remove(DatasetId id) => RemoveByIdAsync(id.Value).GetAwaiter().GetResult() > 0;

    /// <inheritdoc />
    public int RemoveAll() => RemoveAllAsync().GetAwaiter().GetResult();

    private async Task<int> RemoveByIdAsync(string id)
    {
        using var _ = await _gateway.LockAsync().ConfigureAwait(false);
        return await _gateway.RemoveAsync(id).ConfigureAwait(false);
    }

    private async Task<int> RemoveAllAsync()
    {
        using var _ = await _gateway.LockAsync().ConfigureAwait(false);
        var ids = _inner.Datasets
            .Select(d => d.Id.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var total = 0;
        foreach (var id in ids)
        {
            total += await _gateway.RemoveAsync(id).ConfigureAwait(false);
        }
        return total;
    }

    private int CountAdded(HashSet<string> before)
        => _inner.Datasets.Count(d => !before.Contains(d.Id.Value));

    /// <summary>
    /// Waits for an exchange-set load (which dispatched
    /// <paramref name="expectedCount"/> datasets fire-and-forget) to settle:
    /// returns once every dispatched dataset has been added, or a full quiet
    /// window has elapsed after at least one add (some dispatched loads may have
    /// failed). Returns <see langword="true"/> when the <see cref="_maxWaitMs"/>
    /// ceiling is hit first (timed out).
    /// </summary>
    /// <remarks>
    /// A quiet window with <em>zero</em> adds does NOT resolve as quiescent:
    /// because datasets were dispatched, "no events yet" means the first load is
    /// still in flight (a slow first load must not be reported as a failure).
    /// Only the max-wait ceiling ends that case.
    /// </remarks>
    private async Task<bool> WaitForQuiescenceAsync(
        SemaphoreSlim activity, int expectedCount, Func<int> addedCount, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (addedCount() >= expectedCount)
            {
                // Every dispatched dataset arrived.
                return false;
            }

            var remaining = _maxWaitMs - (int)deadline.ElapsedMilliseconds;
            if (remaining <= 0)
            {
                return true;
            }

            var wait = Math.Min(_quietMs, remaining);
            var signalled = await activity.WaitAsync(wait, ct).ConfigureAwait(false);
            if (!signalled)
            {
                if (wait < _quietMs)
                {
                    // The wait was truncated by the max-wait deadline, not a
                    // genuine quiet window — report a timeout.
                    return true;
                }
                if (addedCount() >= 1)
                {
                    // A full quiet window elapsed after at least one add; the
                    // remaining dispatched loads must have failed. Settled.
                    return false;
                }
                // No adds yet though datasets were dispatched: the first load is
                // still in flight. Keep waiting up to the max.
                continue;
            }

            // Drain any adds that piled up so the next wait measures a fresh
            // quiet window rather than immediately returning.
            while (activity.Wait(0))
            {
            }
        }
    }
}
