using EncDotNet.S100.Core.Scheduling;

namespace EncDotNet.S100.Core.Caching;

/// <summary>
/// One content-addressed disk store for every kind of cached asset: a folder
/// per kind under <see cref="RootDirectory"/>, a file per key, one byte budget
/// split into per-kind shares, and a running size index in place of directory
/// scans (map rendering slice 11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout.</b> A kind opened as <c>tiles</c> with format version 8 keeps
/// its entries under <c>&lt;root&gt;/tiles/v8/</c>; a kind without a format
/// version keeps them directly under <c>&lt;root&gt;/&lt;name&gt;/</c>. Opening
/// a versioned kind deletes its other <c>v&lt;n&gt;</c> folders in the
/// background, so a format change is ignored at once and reclaimed soon after.
/// The store only ever reads, counts or deletes inside the folders of the kinds
/// it has opened, so its root may hold other files.
/// </para>
/// <para>
/// <b>Budget.</b> Each kind has a share of the budget, in bytes, and
/// <see cref="LimitBytes"/> is their sum. A kind may grow past its share while
/// the store as a whole is within its limit. When a write takes the store past
/// the limit, entries are evicted, least recently used first, from the kinds
/// furthest over their share, until the store is back below 95% of the limit
/// (so eviction sorts an index once per few percent written, not per write).
/// </para>
/// <para>
/// <b>Index.</b> Each kind's folder is scanned once, the first time the store
/// needs its size; after that writes and reads update an in-memory index of
/// sizes and recency. A kind is rescanned when an eviction finds a file already
/// gone (another process, or a "clear caches" sweep) and at most once per
/// <see cref="ContentStoreOptions.ReconcileInterval"/>.
/// </para>
/// <para>
/// <b>IO.</b> Writes go to a temporary file in the entry's folder and are
/// moved into place, so a reader never sees half an entry. Queued writes run
/// as <see cref="WorkClass.Background"/> jobs (<see cref="WorkStage.Io"/>) on
/// the work scheduler. <see cref="ContentKind.ReadAsync"/> awaits the file
/// read, so a scheduler job that awaits it gives its worker back meanwhile.
/// </para>
/// <para>
/// <b>Empty entries.</b> A zero-byte file is an empty marker: the producer
/// found nothing to store (a tile slot with no pixels), and a read reports
/// <see cref="ContentState.Empty"/> without reading or decoding anything.
/// </para>
/// <para>All members are thread-safe. IO failures are never thrown: a failed
/// read is a miss and a failed write is dropped.</para>
/// </remarks>
public sealed class ContentStore : IDisposable
{
    /// <summary>Default interval between reconciling rescans of a kind's folder.</summary>
    public static readonly TimeSpan DefaultReconcileInterval = TimeSpan.FromMinutes(10);

    // Eviction frees down to this fraction of the limit (and of a kind's
    // share), so the index is sorted once per few percent written.
    internal const double LowWater = 0.95;

    private readonly object _gate = new();
    private readonly List<ContentKind> _kinds = [];
    private readonly ManualResetEventSlim _idle = new(initialState: true);
    private int _inFlight;
    private volatile bool _disposed;

    /// <summary>Creates a store rooted at <paramref name="rootDirectory"/>. Nothing is read or created on disk until a kind is used.</summary>
    /// <param name="rootDirectory">The folder kind folders live under.</param>
    /// <param name="options">Store options; <see langword="null"/> uses the defaults.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is null or empty.</exception>
    public ContentStore(string rootDirectory, ContentStoreOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
        Options = options ?? new ContentStoreOptions();
    }

    /// <summary>The folder kind folders live under.</summary>
    public string RootDirectory { get; }

    /// <summary>The options the store was created with.</summary>
    public ContentStoreOptions Options { get; }

    /// <summary>The store's byte budget: the sum of its kinds' shares.</summary>
    public long LimitBytes
    {
        get
        {
            lock (_gate)
            {
                return LimitLocked();
            }
        }
    }

    /// <summary>The indexed size of every kind's entries, in bytes. A kind not yet indexed counts zero.</summary>
    public long TotalBytes
    {
        get
        {
            lock (_gate)
            {
                return TotalLocked();
            }
        }
    }

    /// <summary>The kinds opened so far, in the order they were opened.</summary>
    public IReadOnlyList<ContentKind> Kinds
    {
        get
        {
            lock (_gate)
            {
                return _kinds.ToArray();
            }
        }
    }

    internal IWorkScheduler Scheduler => Options.Scheduler ?? WorkScheduler.Shared;

    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Opens the kind <paramref name="name"/> with a share of
    /// <paramref name="shareBytes"/> of the store's budget.
    /// </summary>
    /// <param name="name">The kind's folder name: letters, digits, <c>.</c>, <c>-</c> or <c>_</c>.</param>
    /// <param name="shareBytes">The kind's share of the budget, in bytes. Must be positive.</param>
    /// <param name="options">Kind options; <see langword="null"/> uses the defaults.</param>
    /// <returns>The opened kind.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> isn't a valid folder name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shareBytes"/> isn't positive.</exception>
    /// <exception cref="InvalidOperationException">A kind of that name is already open.</exception>
    /// <exception cref="ObjectDisposedException">The store is disposed.</exception>
    public ContentKind Open(string name, long shareBytes, ContentKindOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!ContentKind.IsSafeSegment(name))
        {
            throw new ArgumentException($"'{name}' isn't a valid kind name.", nameof(name));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shareBytes);
        ObjectDisposedException.ThrowIf(_disposed, this);
        options ??= new ContentKindOptions();

        ContentKind kind;
        lock (_gate)
        {
            if (_kinds.Exists(k => string.Equals(k.Name, name, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"The kind '{name}' is already open.");
            }

            kind = new ContentKind(this, name, shareBytes, options);
            _kinds.Add(kind);
        }

        if (options.FormatVersion is not null)
        {
            _ = RunBackground(new WorkKey(kind, "cleanup"), kind.DeleteOtherVersions);
        }

        return kind;
    }

    /// <summary>
    /// Waits until no queued write of any kind is pending or running.
    /// </summary>
    /// <param name="timeout">The longest time to wait.</param>
    /// <returns><see langword="true"/> when the store went idle within <paramref name="timeout"/>.</returns>
    public bool WaitForWritesIdle(TimeSpan timeout) => _idle.Wait(timeout);

    /// <summary>
    /// Stops accepting queued writes and waits briefly (up to two seconds) for
    /// those already queued, so a clean exit keeps them.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idle.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Records that a kind's entry was written, then evicts across kinds when
    /// the store is over its limit. Every kind is indexed first, so the total
    /// is real.
    /// </summary>
    internal void RecordWrite(ContentKind kind, string path, long cost)
    {
        lock (_gate)
        {
            foreach (var k in _kinds)
            {
                k.EnsureIndexedLocked();
            }

            kind.RecordWriteLocked(path, cost);
            EnforceLocked(path);
        }
    }

    /// <summary>Takes the store lock for a kind's index work.</summary>
    internal object Gate => _gate;

    internal void BeginWrite()
    {
        if (Interlocked.Increment(ref _inFlight) == 1)
        {
            _idle.Reset();
        }
    }

    internal void EndWrite()
    {
        if (Interlocked.Decrement(ref _inFlight) == 0)
        {
            _idle.Set();
        }
    }

    /// <summary>Runs <paramref name="work"/> as a background IO job, never throwing.</summary>
    /// <returns>Whether the job was scheduled.</returns>
    internal bool RunBackground(WorkKey key, Action work)
    {
        BeginWrite();
        WorkInterest<bool> interest;
        try
        {
            interest = Scheduler.Schedule(
                key,
                WorkStage.Io,
                WorkPriority.Background(),
                _ =>
                {
                    try
                    {
                        work();
                    }
                    catch
                    {
                        // Best-effort: a failed cache write or cleanup is dropped.
                    }

                    return ValueTask.FromResult(true);
                });
        }
        catch
        {
            EndWrite();
            return false;
        }

        interest.Result.ContinueWith(
            static (_, state) =>
            {
                var (store, owned) = ((ContentStore, WorkInterest<bool>))state!;
                owned.Dispose();
                store.EndWrite();
            },
            (this, interest),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return true;
    }

    private void EnforceLocked(string freshPath)
    {
        var limit = LimitLocked();
        var total = TotalLocked();
        if (total <= limit)
        {
            return;
        }

        var target = (long)(limit * LowWater);
        while (total > target)
        {
            // The kind furthest over its own low-water share gives first.
            ContentKind? victim = null;
            long worst = 0;
            foreach (var k in _kinds)
            {
                var over = k.TotalBytesLocked - (long)(k.ShareBytes * LowWater);
                if (over > worst)
                {
                    worst = over;
                    victim = k;
                }
            }

            if (victim is null)
            {
                return;
            }

            var freed = victim.EvictLocked(Math.Min(total - target, worst), freshPath);
            if (freed <= 0)
            {
                return;
            }

            total -= freed;
        }
    }

    private long LimitLocked()
    {
        long limit = 0;
        foreach (var k in _kinds)
        {
            limit += k.ShareBytes;
        }

        return limit;
    }

    private long TotalLocked()
    {
        long total = 0;
        foreach (var k in _kinds)
        {
            total += k.TotalBytesLocked;
        }

        return total;
    }
}

/// <summary>Options for a <see cref="ContentStore"/>.</summary>
public sealed class ContentStoreOptions
{
    /// <summary>
    /// The scheduler queued writes and cleanup run on, or
    /// <see langword="null"/> for <see cref="WorkScheduler.Shared"/>.
    /// </summary>
    public IWorkScheduler? Scheduler { get; init; }

    /// <summary>The least time between reconciling rescans of a kind's folder.</summary>
    public TimeSpan ReconcileInterval { get; init; } = ContentStore.DefaultReconcileInterval;

    /// <summary>The most queued writes a kind holds before refusing more (<see cref="ContentWriteResult.Full"/>).</summary>
    public int MaxQueuedWritesPerKind { get; init; } = 64;
}

/// <summary>Options for one kind of a <see cref="ContentStore"/>.</summary>
public sealed class ContentKindOptions
{
    /// <summary>
    /// The extension of entry files, including the leading dot, or empty when
    /// keys carry their own. Only files with this extension are counted and
    /// evicted. Default <c>.bin</c>.
    /// </summary>
    public string Extension { get; init; } = ".bin";

    /// <summary>
    /// The kind's format version, or <see langword="null"/> for none. A
    /// versioned kind keeps its entries in a <c>v&lt;n&gt;</c> folder and
    /// deletes the folders of other versions when opened, so a format change
    /// invalidates every entry at once.
    /// </summary>
    public int? FormatVersion { get; init; }

    /// <summary>Whether keys may contain <c>/</c>, placing entries in subfolders.</summary>
    public bool NestedKeys { get; init; }

    /// <summary>
    /// Bytes counted against the budget for each entry on top of its length,
    /// for the file system's per-file cost (so a flood of empty markers is
    /// bounded too). Default 0.
    /// </summary>
    public long EntryOverheadBytes { get; init; }
}

/// <summary>What a <see cref="ContentStore"/> holds for a key.</summary>
public enum ContentState
{
    /// <summary>Nothing: the key was never written, was evicted, or couldn't be read.</summary>
    Missing,

    /// <summary>An empty marker: the producer had nothing to store.</summary>
    Empty,

    /// <summary>An entry with content.</summary>
    Present,
}

/// <summary>The result of reading a key from a <see cref="ContentKind"/>.</summary>
/// <param name="State">What the store holds for the key.</param>
/// <param name="Bytes">The entry's content when <paramref name="State"/> is <see cref="ContentState.Present"/>; otherwise <see langword="null"/>.</param>
public readonly record struct ContentEntry(ContentState State, byte[]? Bytes)
{
    /// <summary>A miss.</summary>
    public static ContentEntry Missing => default;

    /// <summary>An empty marker.</summary>
    public static ContentEntry Empty => new(ContentState.Empty, null);

    /// <summary>Whether the store held the key, as an entry or an empty marker.</summary>
    public bool IsHit => State != ContentState.Missing;
}

/// <summary>What happened to a queued write.</summary>
public enum ContentWriteResult
{
    /// <summary>The write was queued.</summary>
    Queued,

    /// <summary>A write of the same key is already queued.</summary>
    Duplicate,

    /// <summary>The kind's write queue is full; the write was dropped.</summary>
    Full,

    /// <summary>The store is disposed; the write was dropped.</summary>
    Stopped,
}
