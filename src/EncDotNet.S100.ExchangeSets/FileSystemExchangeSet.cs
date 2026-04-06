namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// An exchange set provider backed by a local file system directory.
/// </summary>
public sealed class FileSystemExchangeSet : ExchangeSetBase
{
    private readonly string _basePath;

    private FileSystemExchangeSet(string basePath, ExchangeCatalogue catalogue)
        : base(catalogue)
    {
        _basePath = basePath;
    }

    /// <summary>
    /// Creates a <see cref="FileSystemExchangeSet"/> from a directory containing a
    /// <c>CATALOG.XML</c> file and its referenced assets.
    /// </summary>
    /// <param name="directoryPath">The path to the exchange set root directory.</param>
    /// <param name="catalogueFileName">The name of the catalogue XML file. Defaults to <c>CATALOG.XML</c>.</param>
    public static FileSystemExchangeSet Create(string directoryPath, string catalogueFileName = "CATALOG.XML")
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);

        string fullDir = Path.GetFullPath(directoryPath);
        string cataloguePath = Path.Combine(fullDir, catalogueFileName);
        var catalogue = ExchangeCatalogueReader.Read(cataloguePath);

        return new FileSystemExchangeSet(fullDir, catalogue);
    }

    /// <inheritdoc />
    protected override Task<Stream> OpenFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));

        // Prevent path traversal
        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("File name must not navigate outside the exchange set directory.", nameof(relativePath));
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }
}
