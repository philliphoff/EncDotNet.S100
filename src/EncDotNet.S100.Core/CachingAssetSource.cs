using System.Collections.Concurrent;

namespace EncDotNet.S100.Core;

/// <summary>
/// Wraps another <see cref="IAssetSource"/> and memoises the bytes of
/// each asset on first read. Intended for read-only sources whose
/// contents are immutable for the lifetime of the source
/// (<c>EmbeddedAssetSource</c>, packaged exchange sets opened in
/// <see cref="System.IO.Compression.ZipArchiveMode.Read"/>, on-disk
/// spec content).
/// </summary>
/// <remarks>
/// <para>
/// The cache is thread-safe; concurrent first-read storms collapse to
/// a single underlying open via <see cref="Lazy{T}"/> with
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>.
/// </para>
/// <para>
/// Cache size is bounded by the number of distinct relative paths
/// requested and is not actively evicted. For bundled S-100
/// specification content (Feature and Portrayal Catalogues —
/// see S-100 Edition 5.2.1 Part 4 §4 and Part 9 §3) the working set
/// is small (tens of paths per spec) and steady-state reads dominate.
/// </para>
/// <para>
/// On cache hits, the supplied <see cref="CancellationToken"/> is
/// observed only insofar as the call is synchronous: the cached bytes
/// are returned immediately. On a cache miss the token flows through
/// to the underlying <see cref="IAssetSource.OpenAsync"/>.
/// </para>
/// </remarks>
public sealed class CachingAssetSource : IAssetSource
{
    private readonly IAssetSource _inner;
    private readonly ConcurrentDictionary<string, Lazy<Task<AssetBytes>>> _cache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initialises a new <see cref="CachingAssetSource"/> over the given
    /// <paramref name="inner"/> source.
    /// </summary>
    /// <param name="inner">The asset source to wrap. Must not be null.</param>
    public CachingAssetSource(IAssetSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        AssetBytes bytes = await GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        return bytes.AsStream();
    }

    /// <summary>
    /// Returns the cached <see cref="AssetBytes"/> for
    /// <paramref name="relativePath"/>, reading from the underlying
    /// source on first access.
    /// </summary>
    /// <param name="relativePath">A forward-slash relative path.</param>
    /// <param name="cancellationToken">
    /// A cancellation token. Observed on cache misses (when the inner
    /// <see cref="IAssetSource.OpenAsync"/> is invoked); ignored on
    /// cache hits.
    /// </param>
    public Task<AssetBytes> GetAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        Lazy<Task<AssetBytes>> lazy = _cache.GetOrAdd(
            relativePath,
            path => new Lazy<Task<AssetBytes>>(
                () => _inner.ReadAllBytesAsync(path, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
