using System.Security.Cryptography;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Disk-backed <see cref="IPatternClipCache"/>: persists each computed S-101
/// pattern-fill priority clip result (see <see cref="MapsuiDisplayListRenderer"/>'s
/// <c>ClipPatternsByPriority</c>) as a WKB sidecar file so that the
/// <em>cold</em> first open of a previously-seen cell — including after a
/// process restart — skips the multi-second NetTopologySuite overlay.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory <see cref="InMemoryPatternClipCache"/> (step 1) only eliminates
/// re-clip cost for re-renders of the <em>same already-open</em> dataset (e.g.
/// Day/Dusk/Night palette switches). It cannot help the very first open of a
/// cell — nothing is cached yet — and its state is lost on close/restart. This
/// implementation closes that gap by persisting the clip geometry to disk.
/// </para>
/// <para>
/// The clip geometry is <em>palette-independent</em> (the palette only recolours
/// the raster tiles applied <em>after</em> clipping), so it is safe to persist
/// and reuse across palette/display re-renders. To make the on-disk key globally
/// unique — the <see cref="GetOrCompute"/> contract's <c>key</c> is only unique
/// within one processor's single in-memory slot — the S-101 processor composes a
/// fully-qualified key <c>{datasetScope}|{portrayalKey}</c> whose
/// <c>datasetScope</c> deterministically encodes the dataset content hash, the
/// clip parameters, the CRS, and the cache <see cref="FormatVersion"/>. Any
/// change to dataset content, clip parameters, or the serialization format
/// therefore yields a different filename and recomputes (auto-invalidation).
/// </para>
/// <para>
/// Robustness: any IO error, truncated/corrupt file, or
/// <see cref="FormatVersion"/> mismatch is treated as a miss (the factory runs
/// and overwrites the entry); failures never propagate to the caller. Writes are
/// atomic (temp file + move) so a crash mid-write cannot leave a half-written
/// entry that later deserializes incorrectly.
/// </para>
/// <para>
/// The cache is bounded by a total-bytes cap enforced with a least-recently-used
/// eviction policy. The cache directory is shared across every S-101 processor,
/// so all instance methods are thread-safe (guarded by a single lock).
/// </para>
/// </remarks>
public sealed class DiskPatternClipCache : IPatternClipCache
{
    /// <summary>
    /// Version stamp for the on-disk serialization frame. It is written into
    /// every cache file and verified on read; a mismatch is treated as a miss.
    /// The S-101 processor also folds this value into the <c>datasetScope</c>
    /// component of the cache key, so bumping it both renames future files and
    /// rejects stale ones. Increment whenever the serialization frame or the
    /// clip algorithm changes in a way that invalidates persisted geometry.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>File extension for persisted clip sidecar files.</summary>
    private const string FileExtension = ".clip";

    private readonly string _cacheDirectory;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private readonly WKBWriter _wkbWriter = new();
    private readonly WKBReader _wkbReader = new();

    private long _hits;
    private long _misses;

    /// <summary>
    /// Creates a disk-backed pattern-clip cache rooted at
    /// <paramref name="cacheDirectory"/>.
    /// </summary>
    /// <param name="cacheDirectory">
    /// Directory under which clip sidecar files are stored. Created on first
    /// write if it does not exist. The directory is assumed to be private to
    /// this cache (the LRU sweep enumerates every <c>*.clip</c> file in it).
    /// </param>
    /// <param name="maxBytes">
    /// Soft upper bound, in bytes, on the total size of all persisted clip
    /// files. After each write the least-recently-accessed files are evicted
    /// until the total is at or below this cap. Must be positive.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="cacheDirectory"/> is null or empty.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxBytes"/> is not positive.
    /// </exception>
    public DiskPatternClipCache(string cacheDirectory, long maxBytes)
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
    public IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> GetOrCompute(
        string key,
        Func<IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>> factory)
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

        // Run the expensive clip OUTSIDE the lock so a single ~multi-second miss
        // does not stall hits / unrelated computes on other processors sharing
        // this cache. Concurrent misses on the same key merely duplicate work
        // (rare) and the last writer wins; the result is identical either way.
        var produced = factory();

        lock (_gate)
        {
            TryWrite(path, produced);
        }

        return produced;
    }

    /// <summary>
    /// Maps a cache key to its sidecar file path. The filename is the lowercase
    /// hex SHA-256 of the UTF-8 key, so arbitrary key content (including path
    /// separators) maps to a single safe filename.
    /// </summary>
    private string GetEntryPath(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_cacheDirectory, Convert.ToHexString(hash).ToLowerInvariant() + FileExtension);
    }

    /// <summary>
    /// Attempts to read and deserialize a persisted clip entry. Returns
    /// <see langword="null"/> (a miss) when the file is absent, unreadable, has a
    /// mismatched <see cref="FormatVersion"/>, or is otherwise corrupt/truncated.
    /// </summary>
    private IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            return Deserialize(bytes);
        }
        catch
        {
            // Any IO/parse failure is a miss: recompute and overwrite.
            return null;
        }
    }

    /// <summary>
    /// Serializes and atomically writes a clip entry, then enforces the LRU size
    /// cap. All failures are swallowed: an unwritable cache must never break a
    /// render (the freshly computed value is still returned to the caller).
    /// </summary>
    private void TryWrite(
        string path,
        IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> entries)
    {
        var temp = Path.Combine(
            _cacheDirectory,
            Path.GetRandomFileName() + ".tmp");
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var bytes = Serialize(entries);

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
            // A crash/throw before File.Move can orphan the temp file; remove it.
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
    /// Evicts least-recently-accessed sidecar files until the total size of all
    /// persisted entries is at or below <see cref="_maxBytes"/>. The
    /// just-written <paramref name="freshPath"/> is evicted only as a last
    /// resort (when it alone still exceeds the cap), so a normal write is never
    /// immediately discarded due to coarse access-time resolution. Also sweeps
    /// orphaned <c>*.tmp</c> files left by interrupted writes.
    /// </summary>
    private void EnforceSizeCap(string freshPath)
    {
        FileInfo[] files;
        try
        {
            var dir = new DirectoryInfo(_cacheDirectory);

            // Remove orphaned temp files from interrupted writes; they are not
            // counted toward the cap but would otherwise accumulate unbounded.
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

    /// <summary>
    /// Serializes clip entries into the on-disk frame:
    /// <c>[FormatVersion:int][count:int]</c> then, per entry,
    /// <c>[patternRefUtf8Len:int][patternRefUtf8 bytes][priority:int][wkbLen:int][wkb bytes]</c>.
    /// All integers are written little-endian via <see cref="BinaryWriter"/>.
    /// </summary>
    private byte[] Serialize(
        IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> entries)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(FormatVersion);
            writer.Write(entries.Count);

            foreach (var (patternRef, priority, geometry) in entries)
            {
                var refBytes = Encoding.UTF8.GetBytes(patternRef);
                writer.Write(refBytes.Length);
                writer.Write(refBytes);
                writer.Write(priority);

                var wkb = _wkbWriter.Write(geometry);
                writer.Write(wkb.Length);
                writer.Write(wkb);
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Deserializes the frame produced by <see cref="Serialize"/>. Returns
    /// <see langword="null"/> when the leading version does not match
    /// <see cref="FormatVersion"/>. Throws on truncation/corruption, which the
    /// caller treats as a miss.
    /// </summary>
    private IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>? Deserialize(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var version = reader.ReadInt32();
        if (version != FormatVersion)
            return null;

        var count = reader.ReadInt32();
        // Guard against corrupt/hostile lengths driving huge allocations: each
        // entry needs at least three ints (refLen, priority, wkbLen) = 12 bytes,
        // so a valid count cannot exceed the remaining byte budget.
        var remaining = ms.Length - ms.Position;
        if (count < 0 || count > remaining / 12)
            return null;

        var result = new List<(string PatternRef, int Priority, Geometry Geometry)>(count);
        for (var i = 0; i < count; i++)
        {
            var refLen = reader.ReadInt32();
            if (refLen < 0 || refLen > ms.Length - ms.Position)
                return null;
            var refBytes = reader.ReadBytes(refLen);
            if (refBytes.Length != refLen)
                return null;
            var patternRef = Encoding.UTF8.GetString(refBytes);

            var priority = reader.ReadInt32();

            var wkbLen = reader.ReadInt32();
            if (wkbLen < 0 || wkbLen > ms.Length - ms.Position)
                return null;
            var wkb = reader.ReadBytes(wkbLen);
            if (wkb.Length != wkbLen)
                return null;
            var geometry = _wkbReader.Read(wkb);

            result.Add((patternRef, priority, geometry));
        }

        return result;
    }
}
