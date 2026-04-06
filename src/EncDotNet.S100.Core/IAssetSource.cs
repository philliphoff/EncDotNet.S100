namespace EncDotNet.S100.Core;

/// <summary>
/// Provides read access to assets within a source (e.g. a file system directory or ZIP archive).
/// </summary>
public interface IAssetSource : IDisposable
{
    /// <summary>
    /// Opens a readable stream for the asset at the given relative path within the source.
    /// </summary>
    /// <param name="relativePath">A forward-slash separated relative path to the asset.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default);
}
