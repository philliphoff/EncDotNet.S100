using System.Text;
using EncDotNet.S100.Core.Caching;

namespace EncDotNet.S100.Core.Metadata;

/// <summary>
/// Disk-backed <see cref="IDatasetMetadataCache"/>: persists each dataset's
/// <see cref="DatasetMetadata"/> as a small sidecar file so a later session
/// can recover it without re-parsing the dataset (issue #467 WS3).
/// </summary>
/// <remarks>
/// <para>
/// Each sidecar stores the source file's last-write time and length
/// alongside the serialized metadata; a read is a hit only when both still
/// match the current file, so editing or replacing the dataset silently
/// invalidates the entry. Robustness mirrors
/// <c>DiskPortrayalInstructionCache</c>: any IO error, truncated / corrupt
/// file, envelope-version mismatch, or
/// <see cref="DatasetMetadataSerializer.FormatVersion"/> mismatch is
/// treated as a miss; failures never propagate. Writes are atomic
/// (temp file + move), entries live in a <see cref="ContentKind"/> of the
/// host's <see cref="ContentStore"/>, which bounds them by its byte budget, and all
/// members are thread-safe; hits are served without a lock.
/// </para>
/// </remarks>
public sealed class DiskDatasetMetadataCache : IDatasetMetadataCache
{
    /// <summary>The kind name hosts open for this cache (also its folder name).</summary>
    public const string KindName = "DatasetMetadataCache";

    /// <summary>The options hosts open this cache's kind with.</summary>
    public static ContentKindOptions KindOptions { get; } = new() { Extension = ".dmeta" };

    /// <summary>
    /// Envelope schema version for the sidecar header (source identity +
    /// payload framing). Independent of
    /// <see cref="DatasetMetadataSerializer.FormatVersion"/>; bump when the
    /// header layout below changes.
    /// </summary>
    private const int EnvelopeVersion = 1;

    private readonly ContentKind _kind;

    private long _hits;
    private long _misses;

    /// <summary>
    /// Creates a disk-backed metadata cache over <paramref name="kind"/>,
    /// normally opened as <see cref="KindName"/> with <see cref="KindOptions"/>.
    /// </summary>
    /// <param name="kind">The store kind the entries live in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
    public DiskDatasetMetadataCache(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        _kind = kind;
    }

    /// <inheritdoc />
    public long Hits => Interlocked.Read(ref _hits);

    /// <inheritdoc />
    public long Misses => Interlocked.Read(ref _misses);

    /// <inheritdoc />
    public bool TryGet(string sourcePath, out DatasetMetadata metadata)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);

        var identity = TryStat(sourcePath);
        var path = GetEntryPath(sourcePath);

        var cached = identity is { } id ? TryRead(path, id) : null;
        if (cached is not null)
        {
            Interlocked.Increment(ref _hits);
            metadata = cached;
            return true;
        }

        Interlocked.Increment(ref _misses);

        metadata = null!;
        return false;
    }

    /// <inheritdoc />
    public DatasetMetadata GetOrRead(string sourcePath, Func<string, DatasetMetadata> producer)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentNullException.ThrowIfNull(producer);

        var identity = TryStat(sourcePath);
        var path = GetEntryPath(sourcePath);

        var cached = identity is { } id ? TryRead(path, id) : null;
        if (cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);

        var produced = producer(sourcePath);

        // Only persist when the source could be stat'd (a stable identity is
        // required to validate the entry on a later read).
        if (identity is { } writeIdentity)
        {
            TryWrite(path, writeIdentity, produced);
        }

        return produced;
    }

    /// <summary>
    /// Reads the source file's validity identity (last-write time + length),
    /// or <see langword="null"/> when it cannot be stat'd.
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
    /// Maps a source path to its sidecar file path. The filename is the
    /// lowercase hex SHA-256 of the UTF-8 path, so arbitrary path content
    /// maps to a single safe filename.
    /// </summary>
    private static string GetEntryPath(string sourcePath) => ContentKind.HashKey(sourcePath);

    /// <summary>
    /// Attempts to read a persisted entry and validate it against the current
    /// source identity. Returns <see langword="null"/> (a miss) when the file
    /// is absent, unreadable, of a mismatched envelope version, stale (the
    /// source changed), or otherwise corrupt.
    /// </summary>
    private DatasetMetadata? TryRead(string path, SourceIdentity current)
    {
        var bytes = _kind.Read(path).Bytes;
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

            return DatasetMetadataSerializer.TryDeserialize(payload);
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
    private void TryWrite(string path, SourceIdentity identity, DatasetMetadata metadata)
    {
        byte[] bytes;
        try
        {
            var payload = DatasetMetadataSerializer.Serialize(metadata);
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

        _kind.Write(path, bytes);
    }

    /// <summary>
    /// The source file's validity key: its last-write time (UTC ticks) and
    /// length in bytes. An entry is valid only while both are unchanged.
    /// </summary>
    private readonly record struct SourceIdentity(long MtimeUtcTicks, long Length);
}
