using System.Collections.Concurrent;

namespace EncDotNet.S100.Core.Caching;

/// <summary>
/// Byte-budget and least-recently-used bookkeeping for a directory of cache
/// files, plus the atomic read/write primitives every disk cache shares.
/// </summary>
/// <remarks>
/// <para>
/// The budget keeps an in-memory index of the entries under
/// <see cref="Directory"/> (path, size, recency) and a running total. The
/// directory is scanned once, lazily, on the first write. After that, writes
/// and reads update the index in memory, and eviction picks victims from it
/// without enumerating or <c>stat</c>-ing the directory. Cost per write is
/// therefore independent of how many files the cache holds.
/// </para>
/// <para>
/// The index can drift when something else changes the directory (a host
/// "clear caches" sweep, or another process sharing the cache). It is
/// reconciled with a rescan when an eviction finds its victim already gone,
/// and otherwise at most once per reconcile interval. Between rescans the cap
/// is soft: another process's writes can push the directory past it.
/// </para>
/// <para>
/// Recency is an in-process sequence number, so eviction order is exact even
/// when several entries are written within the file system's timestamp
/// resolution. Entries found by a scan are ordered by their last-access time
/// and rank older than anything this process has written or read. Reads and
/// writes stamp the file's last-access time, so the order survives a restart
/// even on <c>noatime</c> mounts.
/// </para>
/// <para>
/// All members are thread-safe. Reads never take the lock; a write takes it
/// only to update the index and, when over budget, to evict.
/// </para>
/// </remarks>
public sealed class DiskCacheBudget
{
    /// <summary>Default interval between reconciling rescans of the directory.</summary>
    public static readonly TimeSpan DefaultReconcileInterval = TimeSpan.FromMinutes(10);

    private const string TempExtension = ".tmp";

    // A temp file younger than this may belong to a write still in flight
    // (on this or another thread or process), so a scan leaves it alone.
    private static readonly TimeSpan OrphanTempAge = TimeSpan.FromMinutes(1);

    private readonly string _searchPattern;
    private readonly SearchOption _searchOption;
    private readonly long _reconcileIntervalMs;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private long _clock;
    private long _totalBytes;
    private long _nextReconcileMs;
    private bool _loaded;
    private volatile bool _reconcileRequested;

    /// <summary>
    /// Creates a budget over the <paramref name="fileExtension"/> files under
    /// <paramref name="directory"/>. Nothing is read from disk until the first
    /// <see cref="TryWrite(string, ReadOnlySpan{byte}, Func{bool}?)"/> or
    /// <see cref="RecordWrite"/>.
    /// </summary>
    /// <param name="directory">
    /// Cache directory. Assumed private to the cache: every
    /// <paramref name="fileExtension"/> file under it counts toward the budget
    /// and may be evicted, and stale <c>*.tmp</c> files are removed.
    /// </param>
    /// <param name="fileExtension">Extension of entry files, including the leading dot (for example <c>.dlist</c>).</param>
    /// <param name="maxBytes">Soft upper bound on the total size of entry files. Must be positive.</param>
    /// <param name="recursive">Whether entries live in subdirectories of <paramref name="directory"/>.</param>
    /// <param name="reconcileInterval">
    /// Minimum time between reconciling rescans; <see langword="null"/> uses
    /// <see cref="DefaultReconcileInterval"/>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> or <paramref name="fileExtension"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is not positive.</exception>
    public DiskCacheBudget(
        string directory,
        string fileExtension,
        long maxBytes,
        bool recursive = false,
        TimeSpan? reconcileInterval = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentException.ThrowIfNullOrEmpty(fileExtension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        Directory = directory;
        MaxBytes = maxBytes;
        _searchPattern = "*" + fileExtension;
        _searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        _reconcileIntervalMs = (long)(reconcileInterval ?? DefaultReconcileInterval).TotalMilliseconds;
    }

    /// <summary>The cache directory this budget governs.</summary>
    public string Directory { get; }

    /// <summary>Soft upper bound, in bytes, on the total size of entry files.</summary>
    public long MaxBytes { get; }

    /// <summary>
    /// The indexed total size of entry files, in bytes. Zero until the first
    /// write loads the index.
    /// </summary>
    public long TotalBytes => Interlocked.Read(ref _totalBytes);

    /// <summary>The number of indexed entry files. Zero until the first write loads the index.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Reads the entry at <paramref name="path"/> and marks it most recently
    /// used, or returns <see langword="null"/> when it is absent or unreadable.
    /// Never throws for IO errors.
    /// </summary>
    /// <param name="path">Full path of the entry file.</param>
    public byte[]? TryRead(string path)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            bytes = File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }

        TouchAccessTime(path);
        RecordAccess(path);
        return bytes;
    }

    /// <summary>
    /// Atomically writes <paramref name="bytes"/> as the entry at
    /// <paramref name="path"/> (temp file in the same directory, then rename),
    /// records it, and evicts least-recently-used entries if the budget is
    /// exceeded. Never throws for IO errors.
    /// </summary>
    /// <param name="path">Full path of the entry file; its directory is created if absent.</param>
    /// <param name="bytes">The entry content.</param>
    /// <param name="beforeCommit">
    /// Optional check run after the temp file is written and before it is
    /// renamed into place; returning <see langword="false"/> abandons the write.
    /// </param>
    /// <returns><see langword="true"/> when the entry was committed.</returns>
    public bool TryWrite(string path, ReadOnlySpan<byte> bytes, Func<bool>? beforeCommit = null)
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
        RecordWrite(path, bytes.Length);
        return true;
    }

    /// <summary>
    /// Marks the indexed entry at <paramref name="path"/> most recently used.
    /// A path the index does not hold yet is ignored; it is picked up by the
    /// next scan. Lock-free.
    /// </summary>
    /// <param name="path">Full path of the entry file.</param>
    public void RecordAccess(string path)
    {
        if (_entries.TryGetValue(path, out var entry))
        {
            Volatile.Write(ref entry.Stamp, Interlocked.Increment(ref _clock));
        }
    }

    /// <summary>
    /// Records that the entry at <paramref name="path"/> now holds
    /// <paramref name="length"/> bytes and is the most recently used, then
    /// evicts least-recently-used entries until the total is within
    /// <see cref="MaxBytes"/>. The first call loads the index from disk.
    /// </summary>
    /// <param name="path">Full path of the entry file that was just written.</param>
    /// <param name="length">Size of the entry file in bytes.</param>
    public void RecordWrite(string path, long length)
    {
        lock (_gate)
        {
            if (!_loaded || _reconcileRequested || Environment.TickCount64 >= _nextReconcileMs)
            {
                Scan();
            }

            var stamp = Interlocked.Increment(ref _clock);
            if (_entries.TryGetValue(path, out var existing))
            {
                AddTotal(length - existing.Length);
                existing.Length = length;
                Volatile.Write(ref existing.Stamp, stamp);
            }
            else
            {
                _entries[path] = new Entry(length, stamp);
                AddTotal(length);
            }

            if (_totalBytes > MaxBytes)
            {
                Evict(path);
            }
        }
    }

    /// <summary>
    /// Sets a file's last-access time to now, so a later scan (after a
    /// restart) orders it as recently used without relying on file-system
    /// access-time tracking. Never throws.
    /// </summary>
    /// <param name="path">The file to stamp.</param>
    public static void TouchAccessTime(string path)
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

    /// <summary>Deletes a file if present, ignoring any IO error.</summary>
    /// <param name="path">The file to delete.</param>
    public static void TryDelete(string path)
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

    /// <summary>
    /// Evicts least-recently-used entries until the total is within budget.
    /// <paramref name="freshPath"/> (the entry just written) goes only when it
    /// alone still exceeds the budget. Called under the lock.
    /// </summary>
    private void Evict(string freshPath)
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
        victims.Add((freshPath, long.MaxValue));

        foreach (var (path, _) in victims)
        {
            if (_totalBytes <= MaxBytes)
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
                    // next write re-reads the directory.
                    _reconcileRequested = true;
                }
            }
            catch
            {
                // In use or not deletable right now; keep it indexed and retry
                // on a later eviction.
                continue;
            }

            if (_entries.TryRemove(path, out var removed))
            {
                AddTotal(-removed.Length);
            }
        }
    }

    /// <summary>
    /// Reconciles the index with the directory: adds entry files it does not
    /// hold, drops entries whose files are gone, refreshes sizes, and removes
    /// stale temp files. Entries touched in memory since the scan began are
    /// kept regardless. Called under the lock.
    /// </summary>
    private void Scan()
    {
        _loaded = true;
        _reconcileRequested = false;
        _nextReconcileMs = Environment.TickCount64 + _reconcileIntervalMs;

        var scanStamp = Interlocked.Read(ref _clock);
        var found = new List<FileInfo>();
        try
        {
            var dir = new DirectoryInfo(Directory);
            if (dir.Exists)
            {
                var tempCutoff = DateTime.UtcNow - OrphanTempAge;
                foreach (var tmp in dir.EnumerateFiles("*" + TempExtension, _searchOption))
                {
                    if (tmp.LastWriteTimeUtc < tempCutoff)
                    {
                        TryDelete(tmp.FullName);
                    }
                }

                found.AddRange(dir.EnumerateFiles(_searchPattern, _searchOption));
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
            long length;
            try
            {
                length = file.Length;
            }
            catch
            {
                continue;
            }

            var entry = _entries.GetOrAdd(file.FullName, static (_, s) => new Entry(0, s), seed++);
            entry.Length = length;
        }

        long total = 0;
        foreach (var (_, entry) in _entries)
        {
            total += entry.Length;
        }

        Interlocked.Exchange(ref _totalBytes, total);
    }

    private void AddTotal(long delta) => Interlocked.Add(ref _totalBytes, delta);

    private sealed class Entry(long length, long stamp)
    {
        // Written only under the budget lock.
        public long Length = length;

        // Written lock-free by reads; the latest stamp wins.
        public long Stamp = stamp;
    }
}
