namespace EncDotNet.S100.Portrayals;

/// <summary>
/// An asset provider that resolves portrayal assets from a local directory.
/// </summary>
public sealed class FileSystemPortrayalAssetProvider : IPortrayalAssetProvider
{
    private readonly string _basePath;

    public FileSystemPortrayalAssetProvider(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    }

    public Task<Stream> FetchAssetAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        string fullPath = Path.GetFullPath(Path.Combine(_basePath, fileName));

        // Prevent path traversal
        if (!fullPath.StartsWith(Path.GetFullPath(_basePath), StringComparison.Ordinal))
        {
            throw new ArgumentException("File name must not navigate outside the base path.", nameof(fileName));
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }
}
