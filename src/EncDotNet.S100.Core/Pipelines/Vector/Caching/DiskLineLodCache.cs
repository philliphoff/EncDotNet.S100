using System.Security.Cryptography;
using System.Text;

namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// In-memory <see cref="ILineLodCache"/> used by tests and single-process
/// runs. A dictionary keyed by the caller's opaque key with no eviction —
/// entries live until the process exits or <see cref="Clear"/> is called.
/// </summary>
/// <remarks>
/// Mirrors <see cref="InMemoryPortrayalInstructionCache"/> in shape: cheap
/// to instantiate, thread-safe, and useful as a fake in tests where a disk
/// path would be an over-share.
/// </remarks>
public sealed class InMemoryLineLodCache : ILineLodCache
{
    private readonly Dictionary<string, LineLodPyramid> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private long _hits;
    private long _misses;

    /// <inheritdoc />
    public long Hits { get { lock (_gate) { return _hits; } } }

    /// <inheritdoc />
    public long Misses { get { lock (_gate) { return _misses; } } }

    /// <inheritdoc />
    public LineLodPyramid GetOrCompute(string key, Func<LineLodPyramid> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                _hits++;
                return cached;
            }

            _misses++;
        }

        var produced = factory();

        lock (_gate)
        {
            _entries[key] = produced;
        }

        return produced;
    }

    /// <summary>
    /// Removes every cached entry. Not required for correctness — the cache
    /// is a soft accelerator only — but useful in tests that share one cache
    /// across cases.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}

/// <summary>
/// Disk-backed <see cref="ILineLodCache"/>: persists each precomputed
/// <see cref="LineLodPyramid"/> as a sidecar file so cold first-paints on a
/// previously-processed dataset — including after a process restart — skip
/// the Douglas-Peucker pass. Mirrors <see cref="DiskPortrayalInstructionCache"/>
/// in shape (atomic writes, corruption-tolerant reads, LRU eviction under a
/// total-bytes budget) so operators can reason about both caches with one
/// mental model.
/// </summary>
public sealed class DiskLineLodCache : ILineLodCache
{
    /// <summary>File extension for persisted pyramid sidecar files.</summary>
    private const string FileExtension = ".llod";

    private readonly string _cacheDirectory;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    private long _hits;
    private long _misses;

    /// <summary>
    /// Creates a disk-backed line-LOD cache rooted at
    /// <paramref name="cacheDirectory"/>.
    /// </summary>
    /// <param name="cacheDirectory">
    /// Directory under which sidecar files are stored. Created on first
    /// write if absent. Assumed private to this cache (the LRU sweep
    /// enumerates every <c>*.llod</c> file in it).
    /// </param>
    /// <param name="maxBytes">
    /// Soft upper bound, in bytes, on the total size of all persisted
    /// files. After each write the least-recently-accessed files are
    /// evicted until the total is at or below this cap. Must be positive.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="cacheDirectory"/> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxBytes"/> is not positive.
    /// </exception>
    public DiskLineLodCache(string cacheDirectory, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        _cacheDirectory = cacheDirectory;
        _maxBytes = maxBytes;
    }

    /// <inheritdoc />
    public long Hits { get { lock (_gate) { return _hits; } } }

    /// <inheritdoc />
    public long Misses { get { lock (_gate) { return _misses; } } }

    /// <inheritdoc />
    public LineLodPyramid GetOrCompute(string key, Func<LineLodPyramid> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        var path = GetEntryPath(key);

        lock (_gate)
        {
            var cached = TryRead(path);
            if (cached is not null)
            {
                _hits++;
                TouchAccessTime(path);
                return cached;
            }

            _misses++;
        }

        var produced = factory();

        lock (_gate)
        {
            TryWrite(path, produced);
        }

        return produced;
    }

    /// <summary>
    /// Maps a cache key to its sidecar file path. The filename is the
    /// lowercase hex SHA-256 of the UTF-8 key, so arbitrary key content
    /// maps to a single safe filename.
    /// </summary>
    private string GetEntryPath(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_cacheDirectory, Convert.ToHexString(hash).ToLowerInvariant() + FileExtension);
    }

    /// <summary>
    /// Attempts to read and deserialise a persisted entry. Returns
    /// <see langword="null"/> (a miss) when the file is absent, unreadable,
    /// has a mismatched format version, or is otherwise corrupt / truncated.
    /// </summary>
    private static LineLodPyramid? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return LineLodPyramidSerializer.TryDeserialize(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serialises and atomically writes an entry, then enforces the LRU
    /// size cap. All failures are swallowed: an unwritable cache must
    /// never break a render (the freshly computed value is still returned
    /// to the caller).
    /// </summary>
    private void TryWrite(string path, LineLodPyramid pyramid)
    {
        var temp = Path.Combine(_cacheDirectory, Path.GetRandomFileName() + ".tmp");
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var bytes = LineLodPyramidSerializer.Serialize(pyramid);

            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            TouchAccessTime(path);

            EnforceSizeCap(path);
        }
        catch
        {
            // Best-effort persistence only — a render must never fail
            // because a cache write did.
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>Deletes a file if present, swallowing any IO error.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Non-fatal.
        }
    }

    /// <summary>
    /// Stamps a cache file's last-access time to "now" so the LRU sweep
    /// treats a just-written or just-read entry as most-recently-used.
    /// Explicitly setting the timestamp avoids relying on filesystem
    /// access-time tracking, which may be disabled (e.g. <c>noatime</c>
    /// mounts).
    /// </summary>
    private static void TouchAccessTime(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Non-fatal.
        }
    }

    /// <summary>
    /// Evicts least-recently-accessed sidecar files until the total size
    /// is at or below <see cref="_maxBytes"/>. The just-written
    /// <paramref name="freshPath"/> is evicted only as a last resort. Also
    /// sweeps orphaned <c>*.tmp</c> files left by interrupted writes.
    /// </summary>
    private void EnforceSizeCap(string freshPath)
    {
        FileInfo[] files;
        try
        {
            var dir = new DirectoryInfo(_cacheDirectory);

            foreach (var tmp in dir.GetFiles("*.tmp", SearchOption.TopDirectoryOnly))
            {
                TryDelete(tmp.FullName);
            }

            files = dir.GetFiles("*" + FileExtension, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return;
        }

        long total = 0;
        foreach (var f in files)
        {
            total += f.Length;
        }

        if (total <= _maxBytes)
        {
            return;
        }

        Array.Sort(files, (a, b) =>
        {
            var aFresh = string.Equals(a.FullName, freshPath, StringComparison.Ordinal);
            var bFresh = string.Equals(b.FullName, freshPath, StringComparison.Ordinal);
            if (aFresh != bFresh)
            {
                return aFresh ? 1 : -1;
            }

            return a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc);
        });

        foreach (var f in files)
        {
            if (total <= _maxBytes)
            {
                break;
            }

            var len = f.Length;
            try
            {
                f.Delete();
                total -= len;
            }
            catch
            {
                // Skip files we cannot delete (e.g. transient lock).
            }
        }
    }
}
