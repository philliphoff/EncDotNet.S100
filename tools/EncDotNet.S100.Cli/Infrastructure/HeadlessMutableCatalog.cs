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
/// twice (projection + render handle); the composite renderer re-reads the path
/// on each render, so any extracted exchange-set resources are held for the whole
/// session and released only on <see cref="Dispose"/>; and the <c>spec</c> hint on
/// <see cref="LoadAsync"/> is currently ignored (the product is auto-detected).
/// </para>
/// </remarks>
internal sealed class HeadlessMutableCatalog : IMutableDatasetCatalog, IDisposable
{
    private readonly ICrsTransformFactory? _transforms;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _resolutions = [];

    private IReadOnlyList<LoadedDataset> _snapshot = [];
    private bool _disposed;

    private sealed record Entry(LoadedDataset Loaded, S100Dataset? Render);

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
                return _entries.Where(e => e.Render is not null).Select(e => e.Render!).ToList();
            }
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
        lock (_gate)
        {
            if (resolution is not null)
            {
                _resolutions.Add(resolution);
            }
            var added = AddInputsLocked(inputs);
            if (added.Count > 0)
            {
                PublishLocked(DatasetCatalogChangeKind.Batch, id: null);
            }
        }
    }

    /// <inheritdoc />
    public Task<DatasetLoadOutcome> LoadAsync(
        string path, string? specHint = null, CancellationToken cancellationToken = default)
    {
        // specHint is currently ignored: DatasetInputResolver detects the product
        // specification from the file. Tracked as a v1 gap.
        var warnings = new List<string>();
        var inputs = DatasetInputResolver.Resolve(
            path, [], exchangeSet: null, only: null, warnings, out var resolution);

        foreach (var warning in warnings)
        {
            Console.Error.WriteLine(warning);
        }

        var kind = ExchangeSetInput.LooksLikeExchangeSet(path)
            ? DatasetSourceKind.ExchangeSet
            : DatasetSourceKind.File;

        List<DatasetId> added;
        lock (_gate)
        {
            if (resolution is not null)
            {
                _resolutions.Add(resolution);
            }
            added = AddInputsLocked(inputs);
            if (added.Count > 0)
            {
                PublishLocked(DatasetCatalogChangeKind.Batch, id: null);
            }
        }

        return Task.FromResult(new DatasetLoadOutcome(path, kind, added, TimedOut: false));
    }

    /// <inheritdoc />
    public bool Remove(DatasetId id)
    {
        lock (_gate)
        {
            var index = _entries.FindIndex(e => e.Loaded.Id.Equals(id));
            if (index < 0)
            {
                return false;
            }

            var entry = _entries[index];
            _entries.RemoveAt(index);
            _usedIds.Remove(id.Value);
            entry.Render?.Dispose();
            PublishLocked(DatasetCatalogChangeKind.Removed, id);
            return true;
        }
    }

    /// <inheritdoc />
    public int RemoveAll()
    {
        lock (_gate)
        {
            var count = _entries.Count;
            if (count == 0)
            {
                return 0;
            }

            foreach (var entry in _entries)
            {
                entry.Render?.Dispose();
            }
            _entries.Clear();
            _usedIds.Clear();
            PublishLocked(DatasetCatalogChangeKind.Batch, id: null);
            return count;
        }
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

            S100Dataset? render = null;
            try
            {
                render = S100Dataset.Open(input.Path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Skipped render handle for '{input.Path}': {ex.Message}");
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

    private void PublishLocked(DatasetCatalogChangeKind kind, DatasetId? id)
    {
        Volatile.Write(ref _snapshot, _entries.Select(e => e.Loaded).ToList());
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs { Kind = kind, DatasetId = id });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_gate)
        {
            foreach (var entry in _entries)
            {
                entry.Render?.Dispose();
            }
            _entries.Clear();

            foreach (var resolution in _resolutions)
            {
                resolution.Dispose();
            }
            _resolutions.Clear();
        }
    }
}
