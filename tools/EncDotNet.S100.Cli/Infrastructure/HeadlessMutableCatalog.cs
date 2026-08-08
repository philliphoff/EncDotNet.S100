using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Mcp.Tools.Mutable;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// The CLI's in-process <see cref="IMutableDatasetCatalog"/>: the single source
/// of truth for the datasets a <c>s100 mcp serve</c> session holds. Each dataset
/// is kept in two forms — a projected <see cref="LoadedDataset"/> for the
/// read-only query tools, and an open <see cref="S100Dataset"/> render handle for
/// the headless renderer (see <see cref="RenderHandles"/>). Loading and removing
/// keep both in step and publish <see cref="Changed"/>.
/// </summary>
/// <remarks>
/// <para>
/// v1 known gaps (tracked for the unification follow-up): each dataset is parsed
/// twice (projection + render handle); and the composite renderer re-reads the
/// path on each render, so any extracted exchange-set resources are held for the
/// whole session and released only on <see cref="Dispose"/>.
/// </para>
/// </remarks>
internal sealed class HeadlessMutableCatalog : IMutableDatasetCatalog, IDisposable
{
    private readonly ICrsTransformFactory? _transforms;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _resolutions = [];

    // Serialises render use of the S100Dataset handles against their disposal, so
    // a close_dataset / close_all_datasets cannot dispose a handle a render is
    // using mid-flight (which would surface as an internal_error). Renders run
    // one at a time; disposal waits for the in-flight render.
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    private IReadOnlyList<LoadedDataset> _snapshot = [];
    private bool _disposed;

    private sealed record Entry(LoadedDataset Loaded, S100Dataset Render);

    /// <summary>Creates the catalog with an optional CRS transform factory.</summary>
    public HeadlessMutableCatalog(ICrsTransformFactory? transforms = null)
    {
        _transforms = transforms;
    }

    /// <inheritdoc />
    public IReadOnlyList<LoadedDataset> Datasets => Volatile.Read(ref _snapshot);

    /// <inheritdoc />
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    /// <summary>The open render handles for the currently-loaded datasets.</summary>
    public IReadOnlyList<S100Dataset> RenderHandles
    {
        get
        {
            lock (_gate)
            {
                return _entries.Select(e => e.Render).ToList();
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="render"/> against a snapshot of the current render
    /// handles while holding the render gate, so none of those handles can be
    /// disposed by a concurrent <see cref="Remove"/> / <see cref="RemoveAll"/>
    /// until the render completes. Renders are serialised with respect to each
    /// other and to disposal.
    /// </summary>
    public async Task<byte[]?> RenderAsync(
        Func<IReadOnlyList<S100Dataset>, CancellationToken, Task<byte[]?>> render,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(render);

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<S100Dataset> handles;
            lock (_gate)
            {
                handles = _entries.Select(e => e.Render).ToList();
            }
            return await render(handles, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    /// <summary>Disposes a render handle while holding the render gate.</summary>
    private void DisposeHandle(S100Dataset handle)
    {
        _renderGate.Wait();
        try
        {
            handle.Dispose();
        }
        finally
        {
            _renderGate.Release();
        }
    }

    /// <summary>
    /// Seeds the catalog with datasets resolved up front. Takes ownership of
    /// <paramref name="resolution"/> (e.g. an exchange-set temp extraction),
    /// disposing it with the catalog.
    /// </summary>
    public void Seed(IEnumerable<FileDatasetInput> inputs, IDisposable? resolution)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        bool changed;
        bool retainedResolution;
        lock (_gate)
        {
            var added = AddInputsLocked(inputs);
            changed = added.Count > 0;
            // Only keep the extraction alive if it actually contributed a
            // dataset; otherwise release it now instead of holding it for the
            // whole session.
            retainedResolution = changed && resolution is not null;
            if (retainedResolution)
            {
                _resolutions.Add(resolution!);
            }
            if (changed)
            {
                UpdateSnapshotLocked();
            }
        }

        if (!retainedResolution)
        {
            resolution?.Dispose();
        }
        if (changed)
        {
            RaiseChanged(DatasetCatalogChangeKind.Batch, id: null);
        }
    }

    /// <inheritdoc />
    public Task<DatasetLoadOutcome> LoadAsync(
        string path, string? specHint = null, CancellationToken cancellationToken = default)
    {
        // Fail fast before any exchange-set resolution / dataset projection work.
        cancellationToken.ThrowIfCancellationRequested();

        // specHint forces the product spec for a single-file load (ignored for
        // exchange sets); the resolver otherwise auto-detects.
        var warnings = new List<string>();
        var inputs = DatasetInputResolver.Resolve(
            path, [], exchangeSet: null, only: null, warnings, out var resolution, specHint);

        foreach (var warning in warnings)
        {
            Console.Error.WriteLine(warning);
        }

        var kind = ExchangeSetInput.LooksLikeExchangeSet(path)
            ? DatasetSourceKind.ExchangeSet
            : DatasetSourceKind.File;

        List<DatasetId> added;
        bool retainedResolution;
        lock (_gate)
        {
            added = AddInputsLocked(inputs);
            // Only keep the extraction alive if it actually contributed a
            // dataset; a failed / empty load must not leak temp resources for
            // the whole session.
            retainedResolution = added.Count > 0 && resolution is not null;
            if (retainedResolution)
            {
                _resolutions.Add(resolution!);
            }
            if (added.Count > 0)
            {
                UpdateSnapshotLocked();
            }
        }

        if (!retainedResolution)
        {
            resolution?.Dispose();
        }
        if (added.Count > 0)
        {
            RaiseChanged(DatasetCatalogChangeKind.Batch, id: null);
        }

        return Task.FromResult(new DatasetLoadOutcome(path, kind, added, TimedOut: false));
    }

    /// <inheritdoc />
    public bool Remove(DatasetId id)
    {
        Entry entry;
        lock (_gate)
        {
            var index = _entries.FindIndex(e => e.Loaded.Id.Equals(id));
            if (index < 0)
            {
                return false;
            }

            entry = _entries[index];
            _entries.RemoveAt(index);
            _usedIds.Remove(id.Value);
            UpdateSnapshotLocked();
        }

        DisposeHandle(entry.Render);
        RaiseChanged(DatasetCatalogChangeKind.Removed, id);
        return true;
    }

    /// <inheritdoc />
    public int RemoveAll()
    {
        List<Entry> removed;
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return 0;
            }

            removed = [.. _entries];
            _entries.Clear();
            _usedIds.Clear();
            UpdateSnapshotLocked();
        }

        _renderGate.Wait();
        try
        {
            foreach (var entry in removed)
            {
                entry.Render.Dispose();
            }
        }
        finally
        {
            _renderGate.Release();
        }
        RaiseChanged(DatasetCatalogChangeKind.Batch, id: null);
        return removed.Count;
    }

    private List<DatasetId> AddInputsLocked(IEnumerable<FileDatasetInput> inputs)
    {
        var added = new List<DatasetId>();
        foreach (var input in inputs)
        {
            if (input is null || string.IsNullOrEmpty(input.Path) || !File.Exists(input.Path))
            {
                continue;
            }

            var id = new DatasetId(UniqueIdLocked(input.Id.Value));

            LoadedDataset? projected;
            try
            {
                using var stream = File.OpenRead(input.Path);
                projected = LoadedDatasetProjector.Project(
                    id, input.Spec, stream, input.ExternalTextResolver, _transforms);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidOperationException or FormatException or NotSupportedException)
            {
                Console.Error.WriteLine($"Skipped unreadable dataset '{input.Path}': {ex.Message}");
                _usedIds.Remove(id.Value);
                continue;
            }

            if (projected is null)
            {
                Console.Error.WriteLine(
                    $"Skipped unsupported dataset (no known product specification '{input.Spec}'): {input.Path}");
                _usedIds.Remove(id.Value);
                continue;
            }

            S100Dataset render;
            try
            {
                render = S100Dataset.Open(input.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidOperationException or FormatException or NotSupportedException
                or ArgumentException)
            {
                // Treat an expected render-handle failure as a load failure so the
                // session invariant holds: every catalog dataset is renderable.
                // Adding a query-only entry would make open_dataset succeed while
                // the dataset is silently absent from render_to_image. Unexpected
                // exceptions (bugs) are left to propagate as internal_error.
                Console.Error.WriteLine(
                    $"Skipped dataset '{input.Path}' (render handle failed): {ex.Message}");
                _usedIds.Remove(id.Value);
                continue;
            }

            _entries.Add(new Entry(projected, render));
            added.Add(id);
        }

        return added;
    }

    private string UniqueIdLocked(string candidate)
    {
        var id = string.IsNullOrEmpty(candidate) ? "dataset" : candidate;
        if (_usedIds.Add(id))
        {
            return id;
        }
        for (var i = 2; ; i++)
        {
            var next = $"{id}#{i}";
            if (_usedIds.Add(next))
            {
                return next;
            }
        }
    }

    /// <summary>
    /// Refreshes the published <see cref="Datasets"/> snapshot from the current
    /// entries. Call under <see cref="_gate"/>; raise <see cref="Changed"/> with
    /// <see cref="RaiseChanged"/> after releasing the lock.
    /// </summary>
    private void UpdateSnapshotLocked()
    {
        Volatile.Write(ref _snapshot, _entries.Select(e => e.Loaded).ToList());
    }

    /// <summary>
    /// Raises <see cref="Changed"/>. Invoked <b>outside</b> <see cref="_gate"/>
    /// so a subscriber that re-enters the catalog cannot deadlock.
    /// </summary>
    private void RaiseChanged(DatasetCatalogChangeKind kind, DatasetId? id)
    {
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs { Kind = kind, DatasetId = id });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Wait for any in-flight render before disposing the handles it uses.
        _renderGate.Wait();
        try
        {
            lock (_gate)
            {
                foreach (var entry in _entries)
                {
                    entry.Render.Dispose();
                }
                _entries.Clear();

                foreach (var resolution in _resolutions)
                {
                    resolution.Dispose();
                }
                _resolutions.Clear();
            }
        }
        finally
        {
            _renderGate.Release();
        }

        _renderGate.Dispose();
    }
}
