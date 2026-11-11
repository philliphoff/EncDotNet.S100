using System.Collections.Concurrent;
using System.Globalization;
using EncDotNet.S100.Core.Scheduling;

namespace EncDotNet.S100.Core.Caching;

/// <summary>
/// One kind of asset in a <see cref="ContentStore"/>: a folder of entries, a
/// file per key, with a share of the store's byte budget.
/// </summary>
/// <remarks>
/// Keys are relative file names without the extension: letters, digits,
/// <c>.</c>, <c>-</c> and <c>_</c>, and with
/// <see cref="ContentKindOptions.NestedKeys"/> also <c>/</c> between segments.
/// Hash arbitrary keys first (see <see cref="HashKey"/>).
/// </remarks>
public sealed class ContentKind
{
    private const string TempExtension = ".tmp";

    // A temp file younger than this may belong to a write still in flight
    // (on this or another thread or process), so a scan leaves it alone.
    private static readonly TimeSpan OrphanTempAge = TimeSpan.FromMinutes(1);

    private readonly ContentStore _store;
    private readonly ContentKindOptions _options;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private readonly ManualResetEventSlim _idle = new(initialState: true);
    private readonly string _kindDirectory;
    private long _clock;
    private long _totalBytes;
    private long _nextReconcileMs;
    private bool _loaded;
    private volatile bool _reconcileRequested;
    private int _inFlight;

    internal ContentKind(ContentStore store, string name, long shareBytes, ContentKindOptions options)
    {
        _store = store;
        _options = options;
        Name = name;
        ShareBytes = shareBytes;
        _kindDirectory = Path.Combine(store.RootDirectory, name);
        Directory = options.FormatVersion is { } version
            ? Path.Combine(_kindDirectory, VersionFolder(version))
            : _kindDirectory;
    }

    /// <summary>The kind's name, which is also its folder name under the store root.</summary>
    public string Name { get; }

    /// <summary>The folder this kind's entries live in (including its version folder, if any).</summary>
    public string Directory { get; }

    /// <summary>The kind's share of the store's budget, in bytes.</summary>
    public long ShareBytes { get; }

    /// <summary>The kind's format version, or <see langword="null"/>.</summary>
    public int? FormatVersion => _options.FormatVersion;

    /// <summary>The store this kind belongs to.</summary>
    public ContentStore Store => _store;

    /// <summary>
    /// The indexed size of the kind's entries, in bytes, including
    /// <see cref="ContentKindOptions.EntryOverheadBytes"/>. Zero until the
    /// kind is indexed (see <see cref="EnsureIndexed"/>).
    /// </summary>
    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    /// <summary>The number of indexed entries. Zero until the kind is indexed.</summary>
    public int Count => _entries.Count;

    /// <summary>The number of queued writes not yet committed.</summary>
    public int QueuedWrites => _pending.Count;

    internal long TotalBytesLocked => _totalBytes;

    /// <summary>Indexes the kind's folder now if it hasn't been, so <see cref="TotalBytes"/> and <see cref="Count"/> are real.</summary>
    public void EnsureIndexed()
    {
        lock (_store.Gate)
        {
            EnsureIndexedLocked();
        }
    }

    /// <summary>
    /// The lowercase hexadecimal SHA-256 of <paramref name="text"/>: a safe,
    /// fixed-length key for arbitrary key text.
    /// </summary>
    /// <param name="text">The text to hash.</param>
    /// <returns>A 64-character key.</returns>
    public static string HashKey(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>The full path of the file that holds <paramref name="key"/>.</summary>
    /// <param name="key">The entry's key.</param>
    /// <returns>The file path.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> isn't a valid key for this kind.</exception>
    public string PathFor(string key)
    {
        ValidateKey(key);
        var relative = _options.NestedKeys && key.Contains('/', StringComparison.Ordinal)
            ? key.Replace('/', Path.DirectorySeparatorChar)
            : key;
        return Path.Combine(Directory, relative + _options.Extension);
    }

    /// <summary>
    /// Whether the kind holds <paramref name="key"/>, as an entry, an empty
    /// marker or a queued write. Checks only that the file exists; it neither
    /// reads it nor marks it used.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <returns><see langword="true"/> when the key is held.</returns>
    public bool Contains(string key)
    {
        var path = PathFor(key);
        if (_pending.TryGetValue(path, out var pending) && pending.Bytes is not null)
        {
            return true;
        }

        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads <paramref name="key"/> and marks it most recently used. A queued
    /// write's bytes are returned before it reaches the disk.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <returns>The entry, an empty marker, or a miss.</returns>
    public ContentEntry Read(string key)
    {
        var path = PathFor(key);
        if (TryReadPending(path, out var queued))
        {
            return queued;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch
        {
            return ContentEntry.Missing;
        }

        return Accessed(path, bytes);
    }

    /// <summary>
    /// Reads <paramref name="key"/> without blocking the caller's thread on the
    /// file read, and marks it most recently used. A queued write's bytes are
    /// returned before it reaches the disk.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="cancellationToken">Cancels the read; a cancelled read is a miss.</param>
    /// <returns>The entry, an empty marker, or a miss.</returns>
    public async ValueTask<ContentEntry> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (TryReadPending(path, out var queued))
        {
            return queued;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return ContentEntry.Missing;
        }

        return Accessed(path, bytes);
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> as <paramref name="key"/> now, on the
    /// caller's thread: a temporary file, then an atomic move. An empty span
    /// writes an empty marker. Evicts across the store when it is over budget.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="bytes">The entry's content.</param>
    /// <param name="beforeCommit">
    /// Optional check run after the temporary file is written and before it is
    /// moved into place; <see langword="false"/> abandons the write.
    /// </param>
    /// <returns><see langword="true"/> when the entry was committed.</returns>
    public bool Write(string key, ReadOnlySpan<byte> bytes, Func<bool>? beforeCommit = null) =>
        WritePath(PathFor(key), bytes, beforeCommit);

    /// <summary>
    /// Queues <paramref name="bytes"/> to be written as <paramref name="key"/>
    /// by a background job. Until it is committed, <see cref="Read"/> and
    /// <see cref="Contains"/> see the queued bytes.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="bytes">The entry's content; empty for an empty marker. Must not change afterwards.</param>
    /// <returns>Whether the write was queued.</returns>
    public ContentWriteResult QueueWrite(string key, ReadOnlyMemory<byte> bytes) =>
        Enqueue(PathFor(key), new Pending(bytes, Produce: null, IsRelevant: null, Completed: null));

    /// <summary>
    /// Queues a write of <paramref name="key"/> whose content
    /// <paramref name="produce"/> makes on a background job (for example, by
    /// encoding an image). The job skips the write when
    /// <paramref name="isRelevant"/> says the entry is no longer wanted, before
    /// producing it and again before committing it.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="produce">Makes the content: empty for an empty marker, <see langword="null"/> to skip the write.</param>
    /// <param name="isRelevant">Optional check that the entry is still wanted.</param>
    /// <param name="completed">Optional callback with what the job did.</param>
    /// <returns>Whether the write was queued.</returns>
    public ContentWriteResult QueueWrite(
        string key,
        Func<byte[]?> produce,
        Func<bool>? isRelevant = null,
        Action<ContentWriteOutcome>? completed = null)
    {
        ArgumentNullException.ThrowIfNull(produce);
        return Enqueue(PathFor(key), new Pending(null, produce, isRelevant, completed));
    }

    /// <summary>Waits until none of this kind's queued writes is pending or running.</summary>
    /// <param name="timeout">The longest time to wait.</param>
    /// <returns><see langword="true"/> when the kind went idle within <paramref name="timeout"/>.</returns>
    public bool WaitForWritesIdle(TimeSpan timeout) => _idle.Wait(timeout);

    internal static bool IsSafeSegment(string segment)
    {
        if (segment.Length == 0 || segment.Length > 200 || segment[0] == '.')
        {
            return false;
        }

        foreach (var c in segment)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_options.NestedKeys)
        {
            foreach (var segment in key.Split('/'))
            {
                if (!IsSafeSegment(segment))
                {
                    throw new ArgumentException($"'{key}' isn't a valid key.", nameof(key));
                }
            }
        }
        else if (!IsSafeSegment(key))
        {
            throw new ArgumentException($"'{key}' isn't a valid key.", nameof(key));
        }
    }

    private bool TryReadPending(string path, out ContentEntry entry)
    {
        if (_pending.TryGetValue(path, out var pending) && pending.Bytes is { } bytes)
        {
            entry = bytes.Length == 0
                ? ContentEntry.Empty
                : new ContentEntry(ContentState.Present, bytes.ToArray());
            return true;
        }

        entry = default;
        return false;
    }

    private ContentEntry Accessed(string path, byte[] bytes)
    {
        TouchAccessTime(path);
        if (_entries.TryGetValue(path, out var entry))
        {
            Volatile.Write(ref entry.Stamp, Interlocked.Increment(ref _clock));
        }

        return bytes.Length == 0 ? ContentEntry.Empty : new ContentEntry(ContentState.Present, bytes);
    }

    private ContentWriteResult Enqueue(string path, Pending pending)
    {
        if (_store.IsDisposed)
        {
            return ContentWriteResult.Stopped;
        }

        if (_pending.Count >= _store.Options.MaxQueuedWritesPerKind)
        {
            return ContentWriteResult.Full;
        }

        if (!_pending.TryAdd(path, pending))
        {
            return ContentWriteResult.Duplicate;
        }

        if (Interlocked.Increment(ref _inFlight) == 1)
        {
            _idle.Reset();
        }

        if (!_store.RunBackground(new WorkKey(this, path), () => RunQueued(path, pending)))
        {
            Finish(path, pending);
            return ContentWriteResult.Stopped;
        }

        return ContentWriteResult.Queued;
    }

    private void RunQueued(string path, Pending pending)
    {
        var outcome = ContentWriteOutcome.Failed;
        try
        {
            if (pending.Bytes is { } bytes)
            {
                outcome = WritePath(path, bytes.Span, beforeCommit: null)
                    ? ContentWriteOutcome.Written
                    : ContentWriteOutcome.Failed;
                return;
            }

            if (!IsRelevant(pending))
            {
                outcome = ContentWriteOutcome.Stale;
                return;
            }

            var produced = pending.Produce!();
            if (produced is null)
            {
                outcome = ContentWriteOutcome.NotProduced;
                return;
            }

            if (!IsRelevant(pending))
            {
                outcome = ContentWriteOutcome.Stale;
                return;
            }

            outcome = WritePath(path, produced, () => IsRelevant(pending))
                ? ContentWriteOutcome.Written
                : IsRelevant(pending) ? ContentWriteOutcome.Failed : ContentWriteOutcome.Stale;
        }
        catch
        {
            outcome = ContentWriteOutcome.Failed;
        }
        finally
        {
            try
            {
                pending.Completed?.Invoke(outcome);
            }
            catch
            {
                // A telemetry callback must not break the writer.
            }

            Finish(path, pending);
        }
    }

    private static bool IsRelevant(Pending pending) => pending.IsRelevant?.Invoke() ?? true;

    private void Finish(string path, Pending pending)
    {
        if (!pending.Finished)
        {
            pending.Finished = true;
            _pending.TryRemove(new KeyValuePair<string, Pending>(path, pending));
            if (Interlocked.Decrement(ref _inFlight) == 0)
            {
                _idle.Set();
            }
        }
    }

    private bool WritePath(string path, ReadOnlySpan<byte> bytes, Func<bool>? beforeCommit)
    {
        var dir = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir))
        {
            return false;
        }

        var temp = Path.Combine(dir, Path.GetRandomFileName() + TempExtension);
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
            }

            if (beforeCommit is not null && !beforeCommit())
            {
                return false;
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            return false;
        }
        finally
        {
            TryDelete(temp);
        }

        TouchAccessTime(path);
        _store.RecordWrite(this, path, bytes.Length + _options.EntryOverheadBytes);
        return true;
    }

    internal void EnsureIndexedLocked()
    {
        if (!_loaded || _reconcileRequested || Environment.TickCount64 >= _nextReconcileMs)
        {
            Scan();
        }
    }

    internal void RecordWriteLocked(string path, long cost)
    {
        var stamp = Interlocked.Increment(ref _clock);
        if (_entries.TryGetValue(path, out var existing))
        {
            AddTotal(cost - existing.Length);
            existing.Length = cost;
            Volatile.Write(ref existing.Stamp, stamp);
        }
        else
        {
            _entries[path] = new Entry(cost, stamp);
            AddTotal(cost);
        }
    }

    /// <summary>
    /// Deletes least-recently-used entries until at least
    /// <paramref name="bytesToFree"/> are freed. <paramref name="freshPath"/>
    /// (the entry just written) goes last. Called under the store lock.
    /// </summary>
    /// <returns>The bytes freed.</returns>
    internal long EvictLocked(long bytesToFree, string freshPath)
    {
        var victims = new List<(string Path, long Stamp)>(_entries.Count);
        foreach (var (path, entry) in _entries)
        {
            if (!string.Equals(path, freshPath, StringComparison.Ordinal))
            {
                victims.Add((path, Volatile.Read(ref entry.Stamp)));
            }
        }

        victims.Sort(static (a, b) => a.Stamp.CompareTo(b.Stamp));
        if (_entries.ContainsKey(freshPath))
        {
            victims.Add((freshPath, long.MaxValue));
        }

        long freed = 0;
        foreach (var (path, _) in victims)
        {
            if (freed >= bytesToFree)
            {
                break;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else
                {
                    // Deleted behind our back: the index has drifted, so the
                    // next write re-reads the folder.
                    _reconcileRequested = true;
                }
            }
            catch
            {
                // In use or not deletable right now; keep it and retry later.
                continue;
            }

            if (_entries.TryRemove(path, out var removed))
            {
                AddTotal(-removed.Length);
                freed += removed.Length;
            }
        }

        return freed;
    }

    /// <summary>Deletes the kind's <c>v&lt;n&gt;</c> folders of other format versions. Best-effort.</summary>
    internal void DeleteOtherVersions()
    {
        if (_options.FormatVersion is not { } version)
        {
            return;
        }

        var current = VersionFolder(version);
        try
        {
            foreach (var dir in System.IO.Directory.EnumerateDirectories(_kindDirectory, "v*"))
            {
                var name = Path.GetFileName(dir);
                if (name.Length > 1
                    && name.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0
                    && !string.Equals(name, current, StringComparison.Ordinal))
                {
                    try
                    {
                        System.IO.Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // Retried on the next open.
                    }
                }
            }
        }
        catch
        {
            // The kind folder may not exist yet; nothing to reclaim.
        }
    }

    /// <summary>
    /// Reconciles the index with the folder: adds entry files it doesn't hold,
    /// drops entries whose files are gone, refreshes sizes, and removes stale
    /// temporary files. Entries touched since the scan began are kept. Called
    /// under the store lock.
    /// </summary>
    private void Scan()
    {
        _loaded = true;
        _reconcileRequested = false;
        _nextReconcileMs = Environment.TickCount64 + (long)_store.Options.ReconcileInterval.TotalMilliseconds;

        var scanStamp = Interlocked.Read(ref _clock);
        var found = new List<FileInfo>();
        var searchOption = _options.NestedKeys ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        try
        {
            var dir = new DirectoryInfo(Directory);
            if (dir.Exists)
            {
                var tempCutoff = DateTime.UtcNow - OrphanTempAge;
                foreach (var file in dir.EnumerateFiles("*", searchOption))
                {
                    if (file.Name.EndsWith(TempExtension, StringComparison.Ordinal))
                    {
                        if (file.LastWriteTimeUtc < tempCutoff)
                        {
                            TryDelete(file.FullName);
                        }
                    }
                    else if (_options.Extension.Length == 0
                        || file.Name.EndsWith(_options.Extension, StringComparison.Ordinal))
                    {
                        found.Add(file);
                    }
                }
            }
        }
        catch
        {
            // Keep whatever the index already holds; the next reconcile retries.
            return;
        }

        var onDisk = new HashSet<string>(found.Count, StringComparer.Ordinal);
        foreach (var file in found)
        {
            onDisk.Add(file.FullName);
        }

        foreach (var (path, entry) in _entries)
        {
            if (!onDisk.Contains(path) && Volatile.Read(ref entry.Stamp) <= scanStamp)
            {
                _entries.TryRemove(path, out _);
            }
        }

        // Files new to the index rank older than anything this process has
        // touched, ordered among themselves by last-access time.
        found.Sort(static (a, b) => a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc));
        var seed = long.MinValue / 2;
        foreach (var file in found)
        {
            long cost;
            try
            {
                cost = file.Length + _options.EntryOverheadBytes;
            }
            catch
            {
                continue;
            }

            var entry = _entries.GetOrAdd(file.FullName, static (_, s) => new Entry(0, s), seed++);
            entry.Length = cost;
        }

        long total = 0;
        foreach (var (_, entry) in _entries)
        {
            total += entry.Length;
        }

        Interlocked.Exchange(ref _totalBytes, total);
    }

    private void AddTotal(long delta) => Interlocked.Add(ref _totalBytes, delta);

    private static string VersionFolder(int version) =>
        "v" + version.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Sets a file's last-access time to now, so a later scan (after a restart)
    /// orders it as recently used without relying on file-system access-time
    /// tracking. Never throws.
    /// </summary>
    private static void TouchAccessTime(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Non-fatal: a missed touch only affects eviction order after a restart.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Non-fatal.
        }
    }

    private sealed class Entry(long length, long stamp)
    {
        // Written only under the store lock.
        public long Length = length;

        // Written lock-free by reads; the latest stamp wins.
        public long Stamp = stamp;
    }

    private sealed record Pending(
        ReadOnlyMemory<byte>? Bytes,
        Func<byte[]?>? Produce,
        Func<bool>? IsRelevant,
        Action<ContentWriteOutcome>? Completed)
    {
        public bool Finished;
    }
}

/// <summary>What a queued write's background job did.</summary>
public enum ContentWriteOutcome
{
    /// <summary>The entry (or empty marker) was committed.</summary>
    Written,

    /// <summary>The entry was no longer relevant, so it wasn't written.</summary>
    Stale,

    /// <summary>The producer made nothing to write.</summary>
    NotProduced,

    /// <summary>Producing or writing the entry failed.</summary>
    Failed,
}
