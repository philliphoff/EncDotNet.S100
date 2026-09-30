using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Core.Caching;

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
/// by a total-bytes cap with least-recently-used eviction (see
/// <see cref="DiskCacheBudget"/>), and the directory is shared across every
/// processor, so all members are thread-safe. Hits read and deserialize
/// without a lock, so concurrent loads do not serialize on the cache.
/// </para>
/// </remarks>
public sealed class DiskPortrayalInstructionCache : IPortrayalInstructionCache
{
    /// <summary>File extension for persisted display-list sidecar files.</summary>
    private const string FileExtension = ".dlist";

    private readonly string _cacheDirectory;
    private readonly DiskCacheBudget _budget;

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
    /// After a write that exceeds it, the least-recently-used files are evicted
    /// until the total is at or below this cap. Must be positive.
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
        _budget = new DiskCacheBudget(cacheDirectory, FileExtension, maxBytes);
    }

    /// <inheritdoc />
    public long Hits => Interlocked.Read(ref _hits);

    /// <inheritdoc />
    public long Misses => Interlocked.Read(ref _misses);

    /// <inheritdoc />
    public IReadOnlyList<DrawingInstruction> GetOrCompute(
        string key,
        Func<IReadOnlyList<DrawingInstruction>> factory)
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

        var produced = factory();

        TryWrite(path, produced);

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

        var path = GetEntryPath(key);

        var cached = TryRead(path);
        if (cached is not null)
        {
            Interlocked.Increment(ref _hits);
            return cached;
        }

        Interlocked.Increment(ref _misses);

        var produced = await factory(cancellationToken).ConfigureAwait(false);

        TryWrite(path, produced);

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
    private IReadOnlyList<DrawingInstruction>? TryRead(string path)
    {
        var bytes = _budget.TryRead(path);
        if (bytes is null)
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
    /// Serializes and atomically writes an entry, evicting least-recently-used
    /// entries if the cap is exceeded. All failures are swallowed: an
    /// unwritable cache must never break a render (the freshly computed value
    /// is still returned to the caller).
    /// </summary>
    private void TryWrite(string path, IReadOnlyList<DrawingInstruction> instructions)
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

        _budget.TryWrite(path, bytes);
    }
}
