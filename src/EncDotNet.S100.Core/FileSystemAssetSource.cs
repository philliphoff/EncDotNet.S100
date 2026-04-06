namespace EncDotNet.S100.Core;

/// <summary>
/// An asset source backed by a local file system directory.
/// </summary>
public sealed class FileSystemAssetSource : IAssetSource
{
    private readonly string _basePath;

    /// <summary>
    /// Initializes a new instance of <see cref="FileSystemAssetSource"/> rooted at the given directory.
    /// </summary>
    /// <param name="directoryPath">The path to the root directory.</param>
    public FileSystemAssetSource(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        _basePath = Path.GetFullPath(directoryPath);
    }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        string fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));

        // Prevent path traversal
        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("Path must not navigate outside the base directory.", nameof(relativePath));
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources to release.
    }
}
