using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Datasets.S57;

namespace EncDotNet.S100.Viewer.Services.Caching;

/// <summary>
/// Disk-backed <see cref="IS57CatalogCache"/>: persists each S-57 /S-63
/// exchange-set catalogue's base-cell descriptor list as a small sidecar file
/// so a later session can recover it without re-parsing the binary
/// <c>CATALOG.031</c> (issue #467 WS3 Slice 2).
/// </summary>
/// <remarks>
/// Mechanics mirror <c>DiskDatasetMetadataCache</c>: each sidecar stores the
/// catalogue file's last-write time and length; a read is a hit only when
/// both still match, so regenerating the catalogue invalidates the entry. Any
/// IO error, truncated / corrupt file, envelope-version mismatch, or
/// <see cref="S57CatalogCacheSerializer.FormatVersion"/> mismatch is a miss and
/// never propagates. Writes are atomic (temp file + move), the store is bounded
/// by a total-bytes cap with least-recently-used eviction, and all members are
/// thread-safe (one lock).
/// </remarks>
internal sealed class DiskS57CatalogCache : IS57CatalogCache
{
    /// <summary>File extension for persisted catalogue-descriptor sidecar files.</summary>
    private const string FileExtension = ".s57cat";

    /// <summary>
    /// Envelope schema version for the sidecar header (source identity +
    /// payload framing). Independent of
    /// <see cref="S57CatalogCacheSerializer.FormatVersion"/>; bump when the
    /// header layout below changes.
    /// </summary>
    private const int EnvelopeVersion = 1;

    private readonly string _cacheDirectory;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    private long _hits;
    private long _misses;

    /// <summary>
    /// Creates a disk-backed catalogue cache rooted at
    /// <paramref name="cacheDirectory"/>.
    /// </summary>
    /// <param name="cacheDirectory">
    /// Directory under which sidecar files are stored. Created on first write
    /// if absent. Assumed private to this cache (the LRU sweep enumerates
    /// every <c>*.s57cat</c> file in it).
    /// </param>
    /// <param name="maxBytes">
    /// Soft upper bound, in bytes, on the total size of all persisted files.
    /// After each write the least-recently-accessed files are evicted until
    /// the total is at or below this cap. Must be positive.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="cacheDirectory"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is not positive.</exception>
    public DiskS57CatalogCache(string cacheDirectory, long maxBytes)
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
    public IReadOnlyList<S57ExchangeSetCell> GetOrRead(
        string cataloguePath,
        Func<string, IReadOnlyList<S57ExchangeSetCell>> producer)
    {
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);
        ArgumentNullException.ThrowIfNull(producer);

        var identity = TryStat(cataloguePath);
        var path = GetEntryPath(cataloguePath);

        lock (_gate)
        {
            var cached = identity is { } id ? TryRead(path, id) : null;
            if (cached is not null)
            {
                _hits++;
                TouchAccessTime(path);
                return cached;
            }

            _misses++;
        }

        var produced = producer(cataloguePath);

        // Only persist when the source could be stat'd (a stable identity is
        // required to validate the entry on a later read).
        if (identity is { } writeIdentity)
        {
            lock (_gate)
            {
                TryWrite(path, writeIdentity, produced);
            }
        }

        return produced;
    }

    /// <summary>
    /// Reads the catalogue file's validity identity (last-write time +
    /// length), or <see langword="null"/> when it cannot be stat'd.
    /// </summary>
    private static SourceIdentity? TryStat(string sourcePath)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists)
                return null;

            return new SourceIdentity(info.LastWriteTimeUtc.Ticks, info.Length);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a catalogue path to its sidecar file path. The filename is the
    /// lowercase hex SHA-256 of the UTF-8 path, so arbitrary path content maps
    /// to a single safe filename.
    /// </summary>
    private string GetEntryPath(string sourcePath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath));
        return Path.Combine(_cacheDirectory, Convert.ToHexString(hash).ToLowerInvariant() + FileExtension);
    }

    /// <summary>
    /// Attempts to read a persisted entry and validate it against the current
    /// catalogue identity. Returns <see langword="null"/> (a miss) when the
    /// file is absent, unreadable, of a mismatched envelope version, stale
    /// (the catalogue changed), or otherwise corrupt.
    /// </summary>
    private static IReadOnlyList<S57ExchangeSetCell>? TryRead(string path, SourceIdentity current)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            if (r.ReadInt32() != EnvelopeVersion)
                return null;

            var mtimeTicks = r.ReadInt64();
            var length = r.ReadInt64();
            if (mtimeTicks != current.MtimeUtcTicks || length != current.Length)
                return null;

            var payloadLength = r.ReadInt32();
            if (payloadLength < 0)
                return null;

            var payload = r.ReadBytes(payloadLength);
            if (payload.Length != payloadLength)
                return null;

            return S57CatalogCacheSerializer.TryDeserialize(payload);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes and atomically writes an entry, then enforces the LRU size
    /// cap. All failures are swallowed: an unwritable cache must never break
    /// loading (the freshly produced value is still returned to the caller).
    /// </summary>
    private void TryWrite(string path, SourceIdentity identity, IReadOnlyList<S57ExchangeSetCell> cells)
    {
        var temp = Path.Combine(_cacheDirectory, Path.GetRandomFileName() + ".tmp");
        try
        {
            Directory.CreateDirectory(_cacheDirectory);

            var payload = S57CatalogCacheSerializer.Serialize(cells);
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(EnvelopeVersion);
                w.Write(identity.MtimeUtcTicks);
                w.Write(identity.Length);
                w.Write(payload.Length);
                w.Write(payload);
            }

            // Temp file in the same directory so File.Move is an atomic rename.
            File.WriteAllBytes(temp, ms.ToArray());
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
    /// Stamps a cache file's last-access time to "now" so the LRU sweep treats
    /// a just-written or just-read entry as most-recently-used. Explicitly
    /// setting the timestamp avoids relying on filesystem access-time
    /// tracking, which may be disabled (e.g. <c>noatime</c>).
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

        // Oldest access time first (least-recently-used); the just-written
        // entry is sorted last so it survives unless it is the only thing left.
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
    /// The catalogue file's validity key: its last-write time (UTC ticks) and
    /// length in bytes. An entry is valid only while both are unchanged.
    /// </summary>
    private readonly record struct SourceIdentity(long MtimeUtcTicks, long Length);
}
