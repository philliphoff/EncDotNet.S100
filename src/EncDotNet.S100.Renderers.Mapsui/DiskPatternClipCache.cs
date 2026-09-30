using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Core.Caching;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Disk-backed <see cref="IPatternClipCache"/>: persists each computed S-101
/// pattern-fill priority clip result (see <see cref="MapsuiDisplayListRenderer"/>'s
/// <see cref="EncDotNet.S100.Rendering.Scene.PatternPriorityClipper"/>) as a WKB sidecar file so that the
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
/// eviction policy (see <see cref="DiskCacheBudget"/>). The cache directory is
/// shared across every S-101 processor, so all instance methods are
/// thread-safe; hits read and deserialize without a lock.
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
    private readonly DiskCacheBudget _budget;

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
    /// files. After a write that exceeds it, the least-recently-used files are
    /// evicted until the total is at or below this cap. Must be positive.
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
        _budget = new DiskCacheBudget(cacheDirectory, FileExtension, maxBytes);
    }

    /// <inheritdoc />
    public long Hits => Interlocked.Read(ref _hits);

    /// <inheritdoc />
    public long Misses => Interlocked.Read(ref _misses);

    /// <inheritdoc />
    public IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> GetOrCompute(
        string key,
        Func<IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        var path = GetEntryPath(key);

        var cached = TryRead(path);
        if (cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);

        // Nothing is locked while the expensive clip runs, so a single
        // ~multi-second miss does not stall hits / unrelated computes on other
        // processors sharing this cache. Concurrent misses on the same key merely duplicate work
        // (rare) and the last writer wins; the result is identical either way.
        var produced = factory();

        TryWrite(path, produced);

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
        var bytes = _budget.TryRead(path);
        if (bytes is null)
            return null;

        try
        {
            return Deserialize(bytes);
        }
        catch
        {
            // Any parse failure is a miss: recompute and overwrite.
            return null;
        }
    }

    /// <summary>
    /// Serializes and atomically writes a clip entry, evicting
    /// least-recently-used entries if the cap is exceeded. All failures are
    /// swallowed: an unwritable cache must never break a render (the freshly
    /// computed value is still returned to the caller).
    /// </summary>
    private void TryWrite(
        string path,
        IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> entries)
    {
        byte[] bytes;
        try
        {
            bytes = Serialize(entries);
        }
        catch
        {
            return;
        }

        _budget.TryWrite(path, bytes);
    }

    /// <summary>
    /// Serializes clip entries into the on-disk frame:
    /// <c>[FormatVersion:int][count:int]</c> then, per entry,
    /// <c>[patternRefUtf8Len:int][patternRefUtf8 bytes][priority:int][wkbLen:int][wkb bytes]</c>.
    /// All integers are written little-endian via <see cref="BinaryWriter"/>.
    /// </summary>
    private static byte[] Serialize(
        IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)> entries)
    {
        // WKB readers/writers are not thread-safe, and calls run concurrently.
        var wkbWriter = new WKBWriter();
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

                var wkb = wkbWriter.Write(geometry);
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
    private static IReadOnlyList<(string PatternRef, int Priority, Geometry Geometry)>? Deserialize(byte[] bytes)
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

        var wkbReader = new WKBReader();
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
            var geometry = wkbReader.Read(wkb);

            result.Add((patternRef, priority, geometry));
        }

        return result;
    }
}
