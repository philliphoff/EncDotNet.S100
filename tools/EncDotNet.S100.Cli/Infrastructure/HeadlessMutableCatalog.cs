using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Mcp.Tools.Mutable;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// The CLI's in-process <see cref="IMutableDatasetCatalog"/>: the single source
/// of truth for the datasets a <c>s100 mcp serve</c> session holds. Each dataset
/// is parsed <b>once</b> into a resident <see cref="IDatasetProcessor"/> (built
/// from a single shared <see cref="IDatasetProcessorFactory"/> so the catalogue
/// parse caches are shared across datasets too), and that one processor feeds
/// both the read-only query tools — via a <see cref="LoadedDataset"/> projected
/// from it without a second parse — and the headless renderer, which composites
/// the resident processors directly (see <see cref="RenderProcessors"/>).
/// Loading and removing keep the projection and the processor in step and
/// publish <see cref="Changed"/>.
/// </summary>
/// <remarks>
/// The composite renderer re-reads nothing per render (it paints the resident
/// processors), but any extracted exchange-set resources are still held for the
/// whole session and released only on <see cref="Dispose"/>, because a processor
/// may lazily re-read its source bytes.
/// </remarks>
internal sealed class HeadlessMutableCatalog : IMutableDatasetCatalog, IDisposable
{
    private readonly ICrsTransformFactory? _transforms;
    private readonly IDatasetProcessorFactory _factory;
    private readonly bool _ownsFactory;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _resolutions = [];

    // Serialises render use of the resident processors against their disposal, so
    // a close_dataset / close_all_datasets cannot dispose a processor a render is
    // using mid-flight (which would surface as an internal_error). Renders run
    // one at a time; disposal waits for the in-flight render.
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    private IReadOnlyList<LoadedDataset> _snapshot = [];
    private bool _disposed;

    private sealed record Entry(LoadedDataset Loaded, IDatasetProcessor Processor);

    /// <summary>Creates the catalog with an optional CRS transform factory.</summary>
    /// <param name="transforms">
    /// CRS transform factory used to reproject projected-CRS coverage extents into
    /// the WGS-84 bounds the query tools expect; may be <c>null</c>.
    /// </param>
    /// <param name="factory">
    /// Shared processor factory the session builds every dataset processor from,
    /// so the bundled feature / portrayal catalogue parse caches are reused across
    /// datasets. When <c>null</c> a bundled all-products factory is created and
    /// owned (disposed with this catalog).
    /// </param>
    public HeadlessMutableCatalog(
        ICrsTransformFactory? transforms = null,
        IDatasetProcessorFactory? factory = null)
    {
        _transforms = transforms;
        _factory = factory ?? BundledDatasetProcessorFactory.Create();
        _ownsFactory = factory is null;
    }

    /// <inheritdoc />
    public IReadOnlyList<LoadedDataset> Datasets => Volatile.Read(ref _snapshot);

    /// <inheritdoc />
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    /// <summary>The resident processors for the currently-loaded datasets.</summary>
    public IReadOnlyList<IDatasetProcessor> RenderProcessors
    {
        get
        {
            lock (_gate)
            {
                return _entries.Select(e => e.Processor).ToList();
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="render"/> against a snapshot of the current resident
    /// processors while holding the render gate, so none of those processors can
    /// be disposed by a concurrent <see cref="Remove"/> / <see cref="RemoveAll"/>
    /// until the render completes. Renders are serialised with respect to each
    /// other and to disposal.
    /// </summary>
    public async Task<byte[]?> RenderAsync(
        Func<IReadOnlyList<IDatasetProcessor>, CancellationToken, Task<byte[]?>> render,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(render);

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<IDatasetProcessor> processors;
            lock (_gate)
            {
                processors = _entries.Select(e => e.Processor).ToList();
            }
            return await render(processors, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    /// <summary>Disposes a resident processor while holding the render gate.</summary>
    private void DisposeProcessor(IDatasetProcessor processor)
    {
        _renderGate.Wait();
        try
        {
            (processor as IDisposable)?.Dispose();
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
        // Fail fast before any exchange-set resolution / processor construction.
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

        DisposeProcessor(entry.Processor);
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
                (entry.Processor as IDisposable)?.Dispose();
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

            IDatasetProcessor processor;
            try
            {
                // Honour the resolved product spec (a --spec / specHint for a
                // single file, or an exchange-set catalogue spec) so a dataset
                // whose product cannot be sniffed from its bytes still loads with
                // the correct processor. A factory that can map a declared spec
                // (the bundled factory, or a decorator forwarding to it) does so;
                // any other factory's default falls back to file detection.
                processor = _factory.CreateProcessor(input.Path, input.Spec);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidOperationException or FormatException or NotSupportedException
                or ArgumentException)
            {
                Console.Error.WriteLine($"Skipped unreadable dataset '{input.Path}': {ex.Message}");
                _usedIds.Remove(id.Value);
                continue;
            }

            // Every catalog dataset must be renderable: the resident processor is
            // the render source, so a processor that portrays neither vector nor
            // coverage content would make open_dataset succeed while the dataset
            // is silently absent from render_to_image. Reject it up front.
            if (processor is not (IVectorPortrayalSource or ICoveragePortrayalSource))
            {
                Console.Error.WriteLine(
                    $"Skipped dataset '{input.Path}' (not renderable: {processor.Spec.Name}).");
                (processor as IDisposable)?.Dispose();
                _usedIds.Remove(id.Value);
                continue;
            }

            LoadedDataset? projected;
            try
            {
                // Parse-free projection from the resident processor; the query
                // tools and the renderer share this single parse.
                projected = LoadedDatasetProjector.Project(
                    id, processor, input.ExternalTextResolver, _transforms);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidOperationException or FormatException or NotSupportedException)
            {
                Console.Error.WriteLine($"Skipped unreadable dataset '{input.Path}': {ex.Message}");
                (processor as IDisposable)?.Dispose();
                _usedIds.Remove(id.Value);
                continue;
            }

            if (projected is null)
            {
                Console.Error.WriteLine(
                    $"Skipped unsupported dataset (no catalog projection): {input.Path}");
                (processor as IDisposable)?.Dispose();
                _usedIds.Remove(id.Value);
                continue;
            }

            _entries.Add(new Entry(projected, processor));
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

        // Wait for any in-flight render before disposing the processors it uses.
        // Acquire the render gate and dispose it while still holding the permit.
        // Releasing before Dispose() would leave a window where a waiting
        // render / processor-disposal acquires the permit and then throws when it
        // releases it on the now-disposed semaphore. Not releasing means any
        // remaining waiter simply observes ObjectDisposedException from its wait.
        _renderGate.Wait();
        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                (entry.Processor as IDisposable)?.Dispose();
            }
            _entries.Clear();

            foreach (var resolution in _resolutions)
            {
                resolution.Dispose();
            }
            _resolutions.Clear();
        }

        // Release the shared factory (and its catalogue caches) after every
        // processor built from it, but only when this catalog created it.
        if (_ownsFactory && _factory is IDisposable disposableFactory)
        {
            disposableFactory.Dispose();
        }

        _renderGate.Dispose();
    }
}
