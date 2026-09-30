using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Core.Caching;
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
/// by a total-bytes cap with least-recently-used eviction (see
/// <see cref="DiskCacheBudget"/>), and all members are thread-safe; hits are
/// served without a lock.
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
    private readonly DiskCacheBudget _budget;

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
    /// After a write that exceeds it, the least-recently-used files are
    /// evicted until the total is at or below this cap. Must be positive.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="cacheDirectory"/> is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytes"/> is not positive.</exception>
    public DiskS57CatalogCache(string cacheDirectory, long maxBytes)
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
    public IReadOnlyList<S57ExchangeSetCell> GetOrRead(
        string cataloguePath,
        Func<string, IReadOnlyList<S57ExchangeSetCell>> producer)
    {
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);
        ArgumentNullException.ThrowIfNull(producer);

        var identity = TryStat(cataloguePath);
        var path = GetEntryPath(cataloguePath);

        var cached = identity is { } id ? TryRead(path, id) : null;
        if (cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);

        var produced = producer(cataloguePath);

        // Only persist when the source could be stat'd (a stable identity is
        // required to validate the entry on a later read).
        if (identity is { } writeIdentity)
        {
            TryWrite(path, writeIdentity, produced);
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
    private IReadOnlyList<S57ExchangeSetCell>? TryRead(string path, SourceIdentity current)
    {
        var bytes = _budget.TryRead(path);
        if (bytes is null)
            return null;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            if (r.ReadInt32() != EnvelopeVersion)
                return null;

            var mtimeTicks = r.ReadInt64();
            var length = r.ReadInt64();
            if (mtimeTicks != current.MtimeUtcTicks || length != current.Length)
                return null;

            var payloadLength = r.ReadInt32();
            if (payloadLength < 0 || payloadLength > ms.Length - ms.Position)
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
    /// Serializes and atomically writes an entry, evicting least-recently-used
    /// entries if the cap is exceeded. All failures are swallowed: an unwritable cache must never break
    /// loading (the freshly produced value is still returned to the caller).
    /// </summary>
    private void TryWrite(string path, SourceIdentity identity, IReadOnlyList<S57ExchangeSetCell> cells)
    {
        byte[] bytes;
        try
        {
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

            bytes = ms.ToArray();
        }
        catch
        {
            return;
        }

        _budget.TryWrite(path, bytes);
    }

    /// <summary>
    /// The catalogue file's validity key: its last-write time (UTC ticks) and
    /// length in bytes. An entry is valid only while both are unchanged.
    /// </summary>
    private readonly record struct SourceIdentity(long MtimeUtcTicks, long Length);
}
