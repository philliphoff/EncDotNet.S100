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
