using System.Text.Json;
using System.Threading.Channels;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Persistence;
using EncDotNet.S100.Datasets.S128;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Owns the user's dataset collections (issue #655): persists their
/// definitions to <c>collections.json</c>, keeps each source's index cached
/// on disk, and re-indexes sources in the background without loading any
/// dataset.
/// </summary>
/// <remarks>
/// <para>
/// State is exposed as immutable <see cref="Collections"/> snapshots plus a
/// coarse <see cref="Changed"/> event, raised on whichever thread made the
/// change; UI consumers marshal to the UI thread themselves.
/// </para>
/// <para>
/// Indexing runs one source at a time on a background worker. At start-up the
/// cached indexes are loaded first (so the library is populated immediately)
/// and every source is then refreshed; an unchanged source costs only a
/// fingerprint check.
/// </para>
/// <para>
/// S-128 datasets loaded this session appear in a transient "Session"
/// collection (it replaces the former Catalog panel); <see cref="KeepSessionCatalogue"/>
/// turns one into a persisted collection.
/// </para>
/// </remarks>
internal sealed class LibraryService : IDisposable
{
    /// <summary>The fixed id of the transient session collection.</summary>
    public static readonly Guid SessionCollectionId = new("5e5510a0-0000-4000-8000-000000000128");

    private readonly CollectionIndexer _indexer;
    private readonly string _storePath;
    private readonly string _indexCacheDirectory;
    private readonly bool _readOnly;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    private readonly object _gate = new();
    private readonly List<DatasetCollection> _collections = [];
    private readonly Dictionary<Guid, SourceState> _states = [];
    private readonly Dictionary<string, SessionCatalogue> _session = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<Guid> _queued = [];
    private readonly CancellationTokenSource _shutdown = new();

    private IReadOnlyList<LibraryCollection>? _snapshot;
    private Task? _worker;
    private int _pending;
    private TaskCompletionSource _idle = CreateIdleSource(completed: true);

    public LibraryService(
        CollectionIndexer indexer,
        ViewerDataPaths paths,
        ViewerSettings settings,
        ILogger<LibraryService>? logger = null)
        : this(indexer, paths.CollectionsFilePath, paths.CollectionIndexCacheDirectory, settings.IsReadOnly, logger, null)
    {
    }

    internal LibraryService(
        CollectionIndexer indexer,
        string storePath,
        string indexCacheDirectory,
        bool readOnly,
        ILogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(indexer);
        ArgumentException.ThrowIfNullOrEmpty(storePath);
        ArgumentException.ThrowIfNullOrEmpty(indexCacheDirectory);

        _indexer = indexer;
        _storePath = storePath;
        _indexCacheDirectory = indexCacheDirectory;
        _readOnly = readOnly;
        _logger = logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised after any change to <see cref="Collections"/>, on the changing thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The library: persisted collections in order, followed by the session
    /// collection when it has any catalogue.
    /// </summary>
    public IReadOnlyList<LibraryCollection> Collections
    {
        get
        {
            lock (_gate)
                return _snapshot ??= BuildSnapshot();
        }
    }

    /// <summary>Finds a collection by id in the current snapshot.</summary>
    public LibraryCollection? Find(Guid collectionId) =>
        Collections.FirstOrDefault(c => c.Id == collectionId);

    /// <summary>
    /// Loads <c>collections.json</c> and starts the background worker, which
    /// loads cached indexes and then refreshes every source. Idempotent.
    /// </summary>
    public void Initialize()
    {
        lock (_gate)
        {
            if (_worker is not null)
                return;

            foreach (var collection in LoadStore())
            {
                _collections.Add(collection);
                foreach (var source in collection.Sources)
                    _states[source.Id] = new SourceState();
            }

            _snapshot = null;
            _worker = Task.Run(() => RunWorkerAsync(_shutdown.Token));
        }

        RaiseChanged();

        // Queue every source now (the worker loads cached indexes before it
        // starts on the queue), so WhenIdle covers the start-up refresh.
        Refresh();
    }

    /// <summary>A task that completes when no indexing is queued or running (for tests and shutdown).</summary>
    public Task WhenIdle()
    {
        lock (_gate)
            return _idle.Task;
    }

    /// <summary>Creates a collection and queues its sources for indexing.</summary>
    public DatasetCollection AddCollection(string name, IEnumerable<CollectionSource> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sources);

        var collection = new DatasetCollection(Guid.NewGuid(), name.Trim(), sources.ToArray(), _time.GetUtcNow());
        lock (_gate)
        {
            _collections.Add(collection);
            foreach (var source in collection.Sources)
                _states[source.Id] = new SourceState();
            _snapshot = null;
        }

        SaveStore();
        RaiseChanged();
        Enqueue(collection.Sources.Select(s => s.Id));
        return collection;
    }

    /// <summary>Adds sources to an existing collection and queues them for indexing.</summary>
    /// <returns>False when the collection does not exist.</returns>
    public bool AddSources(Guid collectionId, IEnumerable<CollectionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var added = sources.ToArray();

        lock (_gate)
        {
            var index = _collections.FindIndex(c => c.Id == collectionId);
            if (index < 0)
                return false;

            _collections[index] = _collections[index] with { Sources = [.. _collections[index].Sources, .. added] };
            foreach (var source in added)
                _states[source.Id] = new SourceState();
            _snapshot = null;
        }

        SaveStore();
        RaiseChanged();
        Enqueue(added.Select(s => s.Id));
        return true;
    }

    /// <summary>Renames a collection.</summary>
    public bool RenameCollection(Guid collectionId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (_gate)
        {
            var index = _collections.FindIndex(c => c.Id == collectionId);
            if (index < 0)
                return false;
            _collections[index] = _collections[index] with { Name = name.Trim() };
            _snapshot = null;
        }

        SaveStore();
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Removes a collection and its cached indexes. The referenced data is
    /// never touched.
    /// </summary>
    public bool RemoveCollection(Guid collectionId)
    {
        DatasetCollection? removed;
        lock (_gate)
        {
            removed = _collections.FirstOrDefault(c => c.Id == collectionId);
            if (removed is null)
                return false;
            _collections.Remove(removed);
            foreach (var source in removed.Sources)
                _states.Remove(source.Id);
            _snapshot = null;
        }

        foreach (var source in removed.Sources)
            DeleteCachedIndex(source.Id);

        SaveStore();
        RaiseChanged();
        return true;
    }

    /// <summary>Removes one source from a collection, with its cached index.</summary>
    public bool RemoveSource(Guid collectionId, Guid sourceId)
    {
        lock (_gate)
        {
            var index = _collections.FindIndex(c => c.Id == collectionId);
            if (index < 0 || !_collections[index].Sources.Any(s => s.Id == sourceId))
                return false;

            _collections[index] = _collections[index] with
            {
                Sources = _collections[index].Sources.Where(s => s.Id != sourceId).ToArray(),
            };
            _states.Remove(sourceId);
            _snapshot = null;
        }

        DeleteCachedIndex(sourceId);
        SaveStore();
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Queues sources for re-indexing: every source when
    /// <paramref name="collectionId"/> is null, otherwise that collection's
    /// sources (or just <paramref name="sourceId"/>).
    /// </summary>
    public void Refresh(Guid? collectionId = null, Guid? sourceId = null)
    {
        Guid[] ids;
        lock (_gate)
        {
            ids = _collections
                .Where(c => collectionId is null || c.Id == collectionId)
                .SelectMany(c => c.Sources)
                .Where(s => sourceId is null || s.Id == sourceId)
                .Select(s => s.Id)
                .ToArray();
        }

        Enqueue(ids);
    }

    /// <summary>
    /// Shows a loaded S-128 dataset's product entries in the session
    /// collection under <paramref name="label"/> (replacing any previous
    /// dataset with that label).
    /// </summary>
    public void AddSessionCatalogue(string label, string? path, S128Dataset dataset)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentNullException.ThrowIfNull(dataset);

        var sourceId = Guid.NewGuid();
        var index = new SourceIndex(
            sourceId, _time.GetUtcNow(), null, S128CatalogueIndexer.MapEntries(dataset), []);
        lock (_gate)
        {
            _session[label] = new SessionCatalogue(
                new S128CatalogueSource(sourceId, label, path ?? label), index);
            _snapshot = null;
        }

        RaiseChanged();
    }

    /// <summary>Removes a session catalogue added with <see cref="AddSessionCatalogue"/>.</summary>
    public bool RemoveSessionCatalogue(string label)
    {
        bool removed;
        lock (_gate)
        {
            removed = _session.Remove(label);
            if (removed)
                _snapshot = null;
        }

        if (removed)
            RaiseChanged();
        return removed;
    }

    /// <summary>Removes every session catalogue.</summary>
    public void ClearSessionCatalogues()
    {
        bool changed;
        lock (_gate)
        {
            changed = _session.Count > 0;
            _session.Clear();
            _snapshot = null;
        }

        if (changed)
            RaiseChanged();
    }

    /// <summary>
    /// Persists the session catalogue with source id <paramref name="sourceId"/>
    /// as a new collection (named after its file) and removes it from the
    /// session. Returns the new collection, or <see langword="null"/> when the
    /// catalogue is unknown or has no file on disk.
    /// </summary>
    public DatasetCollection? KeepSessionCatalogue(Guid sourceId)
    {
        SessionCatalogue? catalogue;
        string? label;
        lock (_gate)
        {
            (label, catalogue) = _session.FirstOrDefault(p => p.Value.Source.Id == sourceId);
        }

        if (catalogue is null || label is null || !File.Exists(catalogue.Source.Path))
            return null;

        RemoveSessionCatalogue(label);
        var name = Path.GetFileNameWithoutExtension(catalogue.Source.Path);
        return AddCollection(name, [new S128CatalogueSource(Guid.NewGuid(), null, catalogue.Source.Path)]);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _queue.Writer.TryComplete();
    }

    private void Enqueue(IEnumerable<Guid> sourceIds)
    {
        foreach (var id in sourceIds)
        {
            lock (_gate)
            {
                if (!_queued.Add(id))
                    continue;
                if (_pending++ == 0)
                    _idle = CreateIdleSource(completed: false);
            }

            _queue.Writer.TryWrite(id);
        }
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        LoadCachedIndexes();

        try
        {
            await foreach (var sourceId in _queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                lock (_gate)
                    _queued.Remove(sourceId);

                try
                {
                    await IndexSourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate)
                    {
                        if (--_pending == 0)
                            _idle.TrySetResult();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task IndexSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        CollectionSource? source;
        SourceIndex? previous;
        lock (_gate)
        {
            source = _collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
            if (source is null || !_states.TryGetValue(sourceId, out var state))
                return;

            previous = state.Index;
            state.State = LibrarySourceState.Indexing;
            _snapshot = null;
        }

        RaiseChanged();

        SourceIndex? index = null;
        string? error = null;
        try
        {
            index = await _indexer.IndexAsync(source, previous, progress: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogWarning(ex, "Indexing collection source {SourceId} failed", sourceId);
        }

        lock (_gate)
        {
            if (!_states.TryGetValue(sourceId, out var state))
                return;  // removed while indexing

            state.Index = index ?? state.Index;
            state.State = index is null ? LibrarySourceState.Failed : LibrarySourceState.Ready;
            state.Error = error;
            _snapshot = null;
        }

        if (index is not null && !ReferenceEquals(index, previous))
            SaveCachedIndex(index);

        RaiseChanged();
    }

    private IReadOnlyList<LibraryCollection> BuildSnapshot()
    {
        var result = new List<LibraryCollection>(_collections.Count + 1);
        foreach (var collection in _collections)
        {
            result.Add(new LibraryCollection(
                collection,
                collection.Sources
                    .Select(s => _states.TryGetValue(s.Id, out var st)
                        ? new LibrarySource(s, st.Index, st.State, st.Error)
                        : new LibrarySource(s, null, LibrarySourceState.Pending))
                    .ToArray()));
        }

        if (_session.Count > 0)
        {
            var sources = _session.Values
                .Select(c => new LibrarySource(c.Source, c.Index, LibrarySourceState.Ready))
                .ToArray();
            result.Add(new LibraryCollection(
                new DatasetCollection(SessionCollectionId, Resources.Strings.Library_SessionCollectionName,
                    sources.Select(s => s.Definition).ToArray(), DateTimeOffset.MinValue),
                sources,
                IsSession: true));
        }

        return result;
    }

    private IReadOnlyList<DatasetCollection> LoadStore()
    {
        try
        {
            if (!File.Exists(_storePath))
                return [];
            return CollectionJson.DeserializeStore(File.ReadAllText(_storePath)).Collections;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Unreadable or future-format store: start empty rather than fail
            // start-up. The file is left in place; the next save replaces it.
            _logger.LogWarning(ex, "Could not read the collection store {Path}", _storePath);
            return [];
        }
    }

    private void SaveStore()
    {
        if (_readOnly)
            return;

        CollectionStoreDocument document;
        lock (_gate)
            document = new CollectionStoreDocument(CollectionStoreDocument.CurrentVersion, _collections.ToArray());

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            var temp = _storePath + ".tmp";
            File.WriteAllText(temp, CollectionJson.SerializeStore(document));
            File.Move(temp, _storePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the collection store {Path}", _storePath);
        }
    }

    private void LoadCachedIndexes()
    {
        Guid[] ids;
        lock (_gate)
            ids = _states.Where(p => p.Value.Index is null).Select(p => p.Key).ToArray();

        var loadedAny = false;
        foreach (var id in ids)
        {
            var path = IndexPath(id);
            if (!File.Exists(path))
                continue;

            try
            {
                using var stream = File.OpenRead(path);
                var index = CollectionJson.ReadIndex(stream);
                lock (_gate)
                {
                    if (_states.TryGetValue(id, out var state) && state.Index is null && index.SourceId == id)
                    {
                        state.Index = index;
                        state.State = LibrarySourceState.Ready;
                        _snapshot = null;
                        loadedAny = true;
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or NotSupportedException)
            {
                _logger.LogInformation(ex, "Discarding unreadable cached index {Path}", path);
                DeleteCachedIndex(id);
            }
        }

        if (loadedAny)
            RaiseChanged();
    }

    private void SaveCachedIndex(SourceIndex index)
    {
        try
        {
            Directory.CreateDirectory(_indexCacheDirectory);
            var path = IndexPath(index.SourceId);
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
                CollectionJson.WriteIndex(stream, index);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogInformation(ex, "Could not cache the index of source {SourceId}", index.SourceId);
        }
    }

    private void DeleteCachedIndex(Guid sourceId)
    {
        try
        {
            File.Delete(IndexPath(sourceId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string IndexPath(Guid sourceId) =>
        Path.Combine(_indexCacheDirectory, sourceId.ToString("N") + ".index.json.gz");

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static TaskCompletionSource CreateIdleSource(bool completed)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed)
            source.SetResult();
        return source;
    }

    private sealed class SourceState
    {
        public SourceIndex? Index { get; set; }

        public LibrarySourceState State { get; set; } = LibrarySourceState.Pending;

        public string? Error { get; set; }
    }

    private sealed record SessionCatalogue(S128CatalogueSource Source, SourceIndex Index);
}
