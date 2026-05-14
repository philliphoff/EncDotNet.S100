namespace EncDotNet.S100.Core;

/// <summary>
/// Extension methods over <see cref="IAssetSource"/> that provide a
/// bytes-level read surface above the existing <see cref="Stream"/>-based
/// <see cref="IAssetSource.OpenAsync"/>.
/// </summary>
/// <remarks>
/// The default implementation allocates one <see cref="byte"/> array
/// per call. When the underlying source is wrapped in a
/// <see cref="CachingAssetSource"/>, repeat reads of the same path are
/// served from the in-memory cache without re-opening the underlying
/// stream.
/// </remarks>
public static class AssetSourceExtensions
{
    /// <summary>
    /// Reads the asset at <paramref name="relativePath"/> fully into
    /// memory and returns an <see cref="AssetBytes"/> view.
    /// </summary>
    /// <param name="source">The asset source to read from.</param>
    /// <param name="relativePath">A forward-slash relative path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="AssetBytes"/> containing the asset contents.</returns>
    public static async Task<AssetBytes> ReadAllBytesAsync(
        this IAssetSource source,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        await using Stream stream = await source.OpenAsync(relativePath, cancellationToken).ConfigureAwait(false);

        // Fast path: if the underlying stream is an exposable MemoryStream,
        // avoid the intermediate copy.
        if (stream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> segment))
        {
            byte[] copy = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array!, segment.Offset, copy, 0, segment.Count);
            return new AssetBytes(copy, relativePath);
        }

        using var buffer = new MemoryStream(capacity: stream.CanSeek ? (int)Math.Min(stream.Length, int.MaxValue) : 4096);
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return new AssetBytes(buffer.ToArray(), relativePath);
    }
}
