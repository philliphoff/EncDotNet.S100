using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Collections.Library;

/// <summary>Options for <see cref="LibrarySync"/>.</summary>
public sealed record LibrarySyncOptions
{
    /// <summary>
    /// The most a sync downloads in one go (bytes, by the sizes the source
    /// lists). A sync that needs more downloads nothing and reports
    /// <see cref="LibrarySyncStatus.NeededBytes"/>; narrow the source to an area.
    /// </summary>
    public long MaxBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>
    /// Says whether a downloaded copy (its folder) is in use, for example open
    /// in a viewer; a copy in use is not pruned until a later sync. <see langword="null"/>
    /// when nothing is ever in use.
    /// </summary>
    public Func<string, bool>? IsInUse { get; init; }
}

/// <summary>Where a synced source stands after its last sync.</summary>
/// <param name="SyncedAt">When the sync finished.</param>
/// <param name="Local">How many of the source's objects have a current local copy.</param>
/// <param name="Listed">How many objects the source lists (not cancelled).</param>
/// <param name="Downloaded">How many objects this sync downloaded.</param>
/// <param name="Pruned">How many local copies this sync deleted (no longer listed, or cancelled).</param>
/// <param name="Failed">How many downloads failed.</param>
/// <param name="NeededBytes">
/// When the sync stopped because it would download more than
/// <see cref="LibrarySyncOptions.MaxBytes"/>, how much it needed; otherwise <see langword="null"/>.
/// </param>
/// <param name="PruneSkipped">
/// True when copies were not pruned because the listing was not complete and
/// fresh (the service was unreachable, or the source was capped).
/// </param>
public sealed record LibrarySyncStatus(
    DateTimeOffset SyncedAt,
    int Local,
    int Listed,
    int Downloaded,
    int Pruned,
    int Failed,
    long? NeededBytes = null,
    bool PruneSkipped = false);

/// <summary>Describes a <see cref="LibrarySync.Synced"/> event.</summary>
/// <param name="SourceId">The synced source.</param>
/// <param name="Status">Where it stands now.</param>
public sealed class LibrarySyncedEventArgs(Guid SourceId, LibrarySyncStatus Status) : EventArgs
{
    /// <summary>The synced source.</summary>
    public Guid SourceId { get; } = SourceId;

    /// <summary>Where it stands now.</summary>
    public LibrarySyncStatus Status { get; } = Status;
}

/// <summary>
/// Keeps synced sources' local copies current (issue #807): after a synced
/// source is indexed, the items its <see cref="ILibrarySyncPolicy"/> wants are
/// downloaded when missing or outdated, and copies in its managed folder that
/// no source lists any more (or lists as cancelled) are deleted. The source is
/// then re-indexed once, so downloaded items get their bounds. Synced SECOM
/// sources (<see cref="SecomSyncPolicy"/>) are the first kind; others opt in
/// with a policy of their own.
/// </summary>
/// <remarks>
/// <para>
/// UI-free and shared by the viewer and headless hosts (#792). Downloads go
/// through <see cref="LibraryDownloads"/> without a progress notification:
/// the objects are small and the sync runs in the background.
/// </para>
/// <para>
/// Pruning is conservative: it runs only after a complete, fresh listing (an
/// index with a fingerprint and no warnings, so neither a stale copy served
/// while the service was down nor a capped listing), and keeps anything any
/// source of the same managed folder still lists. A sync that downloads or
/// deletes nothing does not re-index, so the index-sync cycle settles.
/// </para>
/// </remarks>
public sealed class LibrarySync : IDisposable
{
    private readonly CollectionLibrary _library;
    private readonly LibraryDownloads _downloads;
    private readonly LibrarySyncOptions _options;
    private readonly IReadOnlyList<ILibrarySyncPolicy> _policies;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, LibrarySyncStatus> _status = new();
    private readonly ConcurrentDictionary<Guid, Task> _running = new();
    private readonly ConcurrentDictionary<Guid, (CollectionSource Source, SourceIndex Index)> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Creates a sync over <paramref name="library"/>, downloading through <paramref name="downloads"/>.</summary>
    /// <param name="library">The library whose synced sources are kept current.</param>
    /// <param name="downloads">The downloads that hold the local copies.</param>
    /// <param name="options">Options; defaults when <see langword="null"/>.</param>
    /// <param name="logger">A logger, or <see langword="null"/>.</param>
    /// <param name="timeProvider">The clock; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="policies">The sync rules per source kind; defaults to <see cref="SecomSyncPolicy"/>.</param>
    public LibrarySync(
        CollectionLibrary library,
        LibraryDownloads downloads,
        LibrarySyncOptions? options = null,
        ILogger<LibrarySync>? logger = null,
        TimeProvider? timeProvider = null,
        IEnumerable<ILibrarySyncPolicy>? policies = null)
    {
        _policies = policies?.ToArray() ?? [new SecomSyncPolicy()];
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(downloads);
        _library = library;
        _downloads = downloads;
        _options = options ?? new LibrarySyncOptions();
        _logger = logger ?? NullLogger<LibrarySync>.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _library.SourceIndexed += OnSourceIndexed;
    }

    /// <summary>Raised on the syncing thread after a source has been synced.</summary>
    public event EventHandler<LibrarySyncedEventArgs>? Synced;

    /// <summary>True when <paramref name="source"/> is kept in sync.</summary>
    public bool IsSynced(CollectionSource source) => PolicyFor(source) is not null;

    private ILibrarySyncPolicy? PolicyFor(CollectionSource source) => _policies.FirstOrDefault(p => p.IsSynced(source));

    /// <summary>Where <paramref name="sourceId"/> stood after its last sync, or <see langword="null"/> before one.</summary>
    public LibrarySyncStatus? StatusOf(Guid sourceId) => _status.TryGetValue(sourceId, out var status) ? status : null;

    /// <summary>True while any source is syncing.</summary>
    public bool IsSyncing => !_running.IsEmpty;

    /// <summary>Completes when no source is syncing.</summary>
    public async Task WhenIdle()
    {
        while (_running.Values.ToArray() is { Length: > 0 } running)
            await Task.WhenAll(running).ConfigureAwait(false);
    }

    /// <summary>
    /// Syncs <paramref name="sourceId"/> against its current index now (it is
    /// otherwise synced after each index).
    /// </summary>
    /// <returns>Where it stands, or <see langword="null"/> when it is not a synced, indexed source.</returns>
    public async Task<LibrarySyncStatus?> SyncAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = _library.Collections.SelectMany(c => c.Sources).FirstOrDefault(s => s.Id == sourceId);
        if (source is not { Index: { } index } || !IsSynced(source.Definition))
            return null;
        return await SyncCoreAsync(source.Definition, index, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _library.SourceIndexed -= OnSourceIndexed;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private void OnSourceIndexed(object? sender, LibrarySourceIndexedEventArgs e)
    {
        if (_shutdown.IsCancellationRequested)
            return;

        if (IsSynced(e.Source))
        {
            Schedule(e.Source, e.Index);
            return;
        }

        // Another source with items in a synced source's folder changed what it
        // lists, and so what that source may prune: sync it again.
        var folders = e.Index.Items
            .Select(i => i.Location).OfType<RemoteItemLocation>()
            .Select(_downloads.FolderOf).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (folders.Count == 0)
            return;
        foreach (var sibling in _library.Collections.SelectMany(c => c.Sources))
        {
            if (sibling is { Index: { } index } && PolicyFor(sibling.Definition) is { } policy
                && policy.PruneFolder(sibling.Definition, index) is { } folder
                && _downloads.FolderOf(folder) is { } root && folders.Contains(root))
            {
                Schedule(sibling.Definition, index);
            }
        }
    }

    /// <summary>
    /// Syncs the source in the background, one sync per source at a time: an
    /// index that arrives during a sync is synced right after it (only the
    /// newest is kept).
    /// </summary>
    private void Schedule(CollectionSource source, SourceIndex index)
    {
        var id = source.Id;
        _pending[id] = (source, index);

        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? task = null;
        task = Task.Run(async () =>
        {
            if (!await start.Task.ConfigureAwait(false))
                return;

            var token = _shutdown.Token;
            try
            {
                while (_pending.TryRemove(id, out var next) && !token.IsCancellationRequested)
                {
                    try
                    {
                        await SyncCoreAsync(next.Source, next.Index, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Syncing collection source {SourceId} failed", id);
                    }
                }
            }
            finally
            {
                _running.TryRemove(KeyValuePair.Create(id, task!));
            }

            // An index that arrived after the loop's last check.
            if (_pending.TryGetValue(id, out var late) && !token.IsCancellationRequested)
                Schedule(late.Source, late.Index);
        }, CancellationToken.None);

        start.SetResult(_running.TryAdd(id, task));
    }

    private async Task<LibrarySyncStatus> SyncCoreAsync(CollectionSource source, SourceIndex index, CancellationToken cancellationToken)
    {
        var policy = PolicyFor(source)
            ?? throw new InvalidOperationException($"No sync policy keeps {source.GetType().Name} in sync.");
        var listed = policy.Wanted(index);
        var needed = listed
            .Where(_downloads.CanDownload)
            .Where(i => _downloads.Localize(i).Location is RemoteItemLocation || _downloads.IsOutdated(i))
            .ToArray();

        var downloaded = 0;
        var failed = 0;
        long? neededBytes = null;
        var bytes = needed.Sum(i => (i.Location as RemoteItemLocation)?.SizeBytes ?? 0);
        if (bytes > _options.MaxBytes)
        {
            neededBytes = bytes;
        }
        else if (needed.Length > 0)
        {
            var result = await _downloads.DownloadAsync(needed, null, cancellationToken).ConfigureAwait(false);
            downloaded = result.Downloaded;
            failed = result.Failed;
        }

        var (pruned, pruneSkipped) = Prune(policy, source, index);

        var local = listed.Count(i => _downloads.Localize(i).Location is LocalItemLocation && !_downloads.IsOutdated(i));
        var status = new LibrarySyncStatus(_time.GetUtcNow(), local, listed.Count, downloaded, pruned, failed, neededBytes, pruneSkipped);
        _status[source.Id] = status;

        // Downloaded objects get their bounds, and pruned ones go, on re-index.
        if (downloaded > 0 || pruned > 0)
            _library.Refresh(sourceId: source.Id);

        Synced?.Invoke(this, new LibrarySyncedEventArgs(source.Id, status));
        return status;
    }

    /// <summary>
    /// Deletes copies in the source's managed folder that no source with
    /// items in that folder lists as current; only after a complete, fresh listing.
    /// </summary>
    private (int Pruned, bool Skipped) Prune(ILibrarySyncPolicy policy, CollectionSource source, SourceIndex index)
    {
        if (policy.PruneFolder(source, index) is not { } probe || _downloads.FolderOf(probe) is not { } root)
            return (0, false);
        if (!policy.CanPrune(index))
            return (0, true);

        // Anything any source lists in this folder as current is kept, synced or not.
        var keep = _library.Collections
            .SelectMany(c => c.Sources)
            .Where(s => s.Id != source.Id)
            .SelectMany(s => s.Index?.Items ?? [])
            .Concat(index.Items)
            .Where(i => i.Status != CollectionItemStatus.Cancelled
                && i.Location is RemoteItemLocation remote && _downloads.FolderOf(remote) == root)
            .Select(i => (i.Location as RemoteItemLocation)?.Package ?? i.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pruned = 0;
        foreach (var name in _downloads.DownloadedNames(probe))
        {
            if (keep.Contains(name) || _options.IsInUse?.Invoke(Path.Combine(root, name)) == true)
                continue;
            try
            {
                if (_downloads.Delete(probe, name))
                    pruned++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.LogWarning(ex, "Could not delete the synced copy {Name} in {Folder}", name, root);
            }
        }

        return (pruned, false);
    }
}
