using EncDotNet.S100.Core.Caching;
using EncDotNet.S100.Diagnostics;

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
/// never propagate. Entries live in a <see cref="ContentKind"/> of the host's
/// <see cref="ContentStore"/>, which bounds them by its byte budget with
/// least-recently-used eviction. A miss serializes the fresh list on the
/// caller and queues the file write as a background job; until it lands,
/// reads see the queued bytes. The kind is shared across every processor, so
/// all members are thread-safe, and hits read and deserialize without a lock.
/// </para>
/// </remarks>
public sealed class DiskPortrayalInstructionCache : IPortrayalInstructionCache
{
    /// <summary>The kind name hosts open for this cache (also its folder name).</summary>
    public const string KindName = "PortrayalInstructionCache";

    /// <summary>The options hosts open this cache's kind with.</summary>
    public static ContentKindOptions KindOptions { get; } = new() { Extension = ".dlist" };

    private readonly ContentKind _kind;

    private long _hits;
    private long _misses;

    /// <summary>
    /// Creates a disk-backed display-list cache over <paramref name="kind"/>,
    /// normally opened as <see cref="KindName"/> with <see cref="KindOptions"/>.
    /// </summary>
    /// <param name="kind">The store kind the entries live in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
    public DiskPortrayalInstructionCache(ContentKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        _kind = kind;
    }

    /// <inheritdoc />
    public long Hits => Interlocked.Read(ref _hits);

    /// <inheritdoc />
    public long Misses => Interlocked.Read(ref _misses);

    /// <summary>The store kind the entries live in.</summary>
    public ContentKind Kind => _kind;

    /// <summary>
    /// The total size, in bytes, of the persisted entries. The first call reads
    /// the kind's folder if nothing has indexed it yet.
    /// </summary>
    public long TotalBytes
    {
        get
        {
            _kind.EnsureIndexed();
            return _kind.TotalBytes;
        }
    }

    /// <summary>
    /// Whether an entry is persisted (or queued) for <paramref name="key"/>.
    /// Checks only that it exists: it neither reads nor validates the entry,
    /// and does not mark it used.
    /// </summary>
    /// <param name="key">A key as passed to <see cref="GetOrCompute"/>.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _kind.Contains(ContentKind.HashKey(key));
    }

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> GetOrCompute(
        string key,
        Func<IReadOnlyList<DrawingInstruction>> factory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        var entryKey = ContentKind.HashKey(key);
        IReadOnlyList<DrawingInstruction>? cached;
        using (var activity = Telemetry.ActivitySource.StartActivity("s100.portrayal.cache.read"))
        {
            cached = Deserialize(_kind.Read(entryKey));
            activity?.SetTag(TelemetryTags.CacheResult, cached is null ? "miss" : "hit");
        }

        if (cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);
        var produced = factory();
        QueueWrite(entryKey, produced);
        return produced;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DrawingInstruction>> GetOrComputeAsync(
        string key,
        Func<CancellationToken, ValueTask<IReadOnlyList<DrawingInstruction>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        var entryKey = ContentKind.HashKey(key);
        IReadOnlyList<DrawingInstruction>? cached;
        using (var activity = Telemetry.ActivitySource.StartActivity("s100.portrayal.cache.read"))
        {
            // The file read is awaited, so a scheduler job gives its worker
            // back while it waits.
            cached = Deserialize(await _kind.ReadAsync(entryKey, cancellationToken).ConfigureAwait(false));
            activity?.SetTag(TelemetryTags.CacheResult, cached is null ? "miss" : "hit");
        }

        if (cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);
        var produced = await factory(cancellationToken).ConfigureAwait(false);
        QueueWrite(entryKey, produced);
        return produced;
    }

    /// <summary>
    /// Deserializes a read entry, or returns <see langword="null"/> (a miss)
    /// when it is absent, an empty marker, of another format version, or
    /// corrupt.
    /// </summary>
    private static IReadOnlyList<DrawingInstruction>? Deserialize(ContentEntry entry)
    {
        if (entry.Bytes is not { } bytes)
            return null;

        try
        {
            return DrawingInstructionSerializer.TryDeserialize(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes an entry and queues its write. All failures are swallowed:
    /// an unwritable cache must never break a render (the freshly computed
    /// value is still returned to the caller).
    /// </summary>
    private void QueueWrite(string entryKey, IReadOnlyList<DrawingInstruction> instructions)
    {
        byte[] bytes;
        try
        {
            bytes = DrawingInstructionSerializer.Serialize(instructions);
        }
        catch
        {
            return;
        }

        // A dropped entry costs a portrayal run later, so a full queue (or a
        // store shutting down) writes it here instead.
        if (_kind.QueueWrite(entryKey, bytes) is ContentWriteResult.Full or ContentWriteResult.Stopped)
            _kind.Write(entryKey, bytes);
    }
}
