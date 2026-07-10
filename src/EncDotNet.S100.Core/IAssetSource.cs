namespace EncDotNet.S100.Core;

/// <summary>
/// Provides read access to assets within a source (e.g. a file system directory or ZIP archive).
/// </summary>
public interface IAssetSource : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Opens a readable stream for the asset at the given relative path within the source.
    /// </summary>
    /// <param name="relativePath">A forward-slash separated relative path to the asset.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the asset at <paramref name="relativePath"/> fully into memory and
    /// returns an <see cref="AssetBytes"/> view over its contents.
    /// </summary>
    /// <param name="relativePath">A forward-slash separated relative path to the asset.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="AssetBytes"/> containing the asset contents.</returns>
    /// <remarks>
    /// The default implementation opens the asset via <see cref="OpenAsync"/>
    /// and copies the stream into a fresh buffer, allocating one array per call
    /// (with a fast path for exposable <see cref="MemoryStream"/> sources).
    /// A source that already holds the bytes in memory — for example
    /// <c>CachingAssetSource</c> — should override this to serve them directly
    /// and avoid the stream round-trip, so the acceleration applies even when
    /// the source is referenced through <see cref="IAssetSource"/>.
    /// </remarks>
    async Task<AssetBytes> ReadAllBytesAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        await using Stream stream = await OpenAsync(relativePath, cancellationToken).ConfigureAwait(false);

        // Fast path: if the underlying stream is an exposable MemoryStream,
        // avoid the intermediate copy.
        if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> segment))
        {
            byte[] copy = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array!, segment.Offset, copy, 0, segment.Count);
            return new AssetBytes(copy, relativePath);
        }

        using var buffer = new MemoryStream(
            capacity: stream.CanSeek ? (int)Math.Min(stream.Length, int.MaxValue) : 4096);
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return new AssetBytes(buffer.ToArray(), relativePath);
    }

    /// <summary>
    /// Asynchronously releases the resources held by the source.
    /// </summary>
    /// <remarks>
    /// The default implementation forwards to <see cref="IDisposable.Dispose"/>
    /// and completes synchronously, which is correct for sources whose
    /// resources dispose synchronously (a <see cref="System.IO.Compression.ZipArchive"/>,
    /// a directory root, embedded resources). A source that owns
    /// async-disposable resources — or that wraps another
    /// <see cref="IAssetSource"/> it owns — should override this to release
    /// them without blocking.
    /// </remarks>
    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
