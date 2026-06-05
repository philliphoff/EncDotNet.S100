using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// Disk-backed <see cref="IPortrayalInstructionCache"/>: persists each prepared
/// S-100 Part 9 display list as a sidecar file so the <em>cold</em> first open
/// of a previously-portrayed dataset — including after a process restart — skips
/// the portrayal run (for S-101 the ~1 s MoonSharp Lua execution).
/// </summary>
/// <remarks>
/// <para>
/// The companion <see cref="InMemoryPortrayalInstructionCache"/> only helps
/// re-opens within one session and is lost on restart; this implementation
/// closes that gap by persisting the list to disk. The cache is correct only
/// when the caller's key fully captures every portrayal input — see
/// <see cref="IPortrayalInstructionCache"/> — which the S-101 processor ensures
/// by folding the dataset content hash, the feature- and portrayal-catalogue
/// content hashes, the engine/format stamp, and the mariner + ECDIS state into
/// the key.
/// </para>
/// <para>
/// Robustness mirrors <c>DiskPatternClipCache</c>: any IO error, truncated /
/// corrupt file, or <see cref="DrawingInstructionSerializer.FormatVersion"/>
/// mismatch is treated as a miss (the factory runs and overwrites); failures
/// never propagate. Writes are atomic (temp file + move). The cache is bounded
/// by a total-bytes cap with least-recently-used eviction, and the directory is
/// shared across every processor, so all members are thread-safe (one lock).
/// </para>
/// </remarks>
public sealed class DiskPortrayalInstructionCache : IPortrayalInstructionCache
{
    /// <summary>File extension for persisted display-list sidecar files.</summary>
    private const string FileExtension = ".dlist";

    private readonly string _cacheDirectory;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    private long _hits;
    private long _misses;

    /// <summary>
    /// Creates a disk-backed display-list cache rooted at
    /// <paramref name="cacheDirectory"/>.
    /// </summary>
    /// <param name="cacheDirectory">
    /// Directory under which sidecar files are stored. Created on first write if
    /// absent. Assumed private to this cache (the LRU sweep enumerates every
    /// <c>*.dlist</c> file in it).
    /// </param>
    /// <param name="maxBytes">
    /// Soft upper bound, in bytes, on the total size of all persisted files.
    /// After each write the least-recently-accessed files are evicted until the
    /// total is at or below this cap. Must be positive.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="cacheDirectory"/> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxBytes"/> is not positive.
    /// </exception>
    public DiskPortrayalInstructionCache(string cacheDirectory, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(cacheDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        _cacheDirectory = cacheDirectory;
        _maxBytes = maxBytes;
    }

    /// <inheritdoc />
    public long Hits
    {
        get { lock (_gate) { return _hits; } }
    }

    /// <inheritdoc />
    public long Misses
    {
        get { lock (_gate) { return _misses; } }
    }

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> GetOrCompute(
        string key,
        Func<IReadOnlyList<DrawingInstruction>> factory)
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

        // Run the portrayal pipeline OUTSIDE the lock so a single multi-second
        // miss does not stall hits or unrelated computes on other processors
        // sharing this cache. Concurrent misses on the same key merely duplicate
        // work (rare); the last writer wins and the result is identical.
        var produced = factory();

        lock (_gate)
        {
            TryWrite(path, produced);
        }

        return produced;
    }

    /// <summary>
    /// Maps a cache key to its sidecar file path. The filename is the lowercase
    /// hex SHA-256 of the UTF-8 key, so arbitrary key content maps to a single
    /// safe filename.
    /// </summary>
    private string GetEntryPath(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_cacheDirectory, Convert.ToHexString(hash).ToLowerInvariant() + FileExtension);
    }

    /// <summary>
    /// Attempts to read and deserialize a persisted entry. Returns
    /// <see langword="null"/> (a miss) when the file is absent, unreadable, has a
    /// mismatched format version, or is otherwise corrupt / truncated.
    /// </summary>
    private static IReadOnlyList<DrawingInstruction>? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            return DrawingInstructionSerializer.TryDeserialize(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes and atomically writes an entry, then enforces the LRU size
    /// cap. All failures are swallowed: an unwritable cache must never break a
    /// render (the freshly computed value is still returned to the caller).
    /// </summary>
    private void TryWrite(string path, IReadOnlyList<DrawingInstruction> instructions)
    {
        var temp = Path.Combine(_cacheDirectory, Path.GetRandomFileName() + ".tmp");
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var bytes = DrawingInstructionSerializer.Serialize(instructions);

            // Temp file in the same directory so File.Move is an atomic rename.
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            TouchAccessTime(path);

            EnforceSizeCap(path);
        }
        catch
        {
            // Best-effort persistence only.
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
                File.Delete(path);
        }
        catch
        {
            // Non-fatal.
        }
    }

    /// <summary>
    /// Stamps a cache file's last-access time to "now" so the LRU sweep treats a
    /// just-written or just-read entry as most-recently-used. Explicitly setting
    /// the timestamp avoids relying on filesystem access-time tracking, which may
    /// be disabled (e.g. <c>noatime</c> mounts).
    /// </summary>
    private static void TouchAccessTime(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Non-fatal: a missed touch only affects eviction ordering.
        }
    }

    /// <summary>
    /// Evicts least-recently-accessed sidecar files until the total size is at
    /// or below <see cref="_maxBytes"/>. The just-written
    /// <paramref name="freshPath"/> is evicted only as a last resort. Also sweeps
    /// orphaned <c>*.tmp</c> files left by interrupted writes.
    /// </summary>
    private void EnforceSizeCap(string freshPath)
    {
        FileInfo[] files;
        try
        {
            var dir = new DirectoryInfo(_cacheDirectory);

            foreach (var tmp in dir.GetFiles("*.tmp", SearchOption.TopDirectoryOnly))
                TryDelete(tmp.FullName);

            files = dir.GetFiles("*" + FileExtension, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return;
        }

        long total = 0;
        foreach (var f in files)
            total += f.Length;

        if (total <= _maxBytes)
            return;

        // Oldest access time first (least-recently-used); the just-written entry
        // is sorted last so it survives unless it is the only thing left.
        Array.Sort(files, (a, b) =>
        {
            var aFresh = string.Equals(a.FullName, freshPath, StringComparison.Ordinal);
            var bFresh = string.Equals(b.FullName, freshPath, StringComparison.Ordinal);
            if (aFresh != bFresh)
                return aFresh ? 1 : -1;
            return a.LastAccessTimeUtc.CompareTo(b.LastAccessTimeUtc);
        });

        foreach (var f in files)
        {
            if (total <= _maxBytes)
                break;

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
