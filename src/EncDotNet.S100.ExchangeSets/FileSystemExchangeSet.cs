namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// An exchange set provider backed by a local file system directory.
/// </summary>
public sealed class FileSystemExchangeSet : IExchangeSetProvider
{
    private readonly string _basePath;

    private FileSystemExchangeSet(string basePath, ExchangeCatalogue catalogue)
    {
        _basePath = basePath;
        Catalogue = catalogue;
    }

    /// <inheritdoc />
    public ExchangeCatalogue Catalogue { get; }

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
    public Task<Stream> FetchDatasetAsync(DatasetDiscoveryMetadata dataset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return FetchFileAsync(dataset.FileName);
    }

    /// <inheritdoc />
    public Task<Stream> FetchSupportFileAsync(SupportFileDiscoveryMetadata supportFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supportFile);
        return FetchFileAsync(supportFile.FileName);
    }

    /// <inheritdoc />
    public Task<Stream> FetchCatalogueFileAsync(CatalogueDiscoveryMetadata catalogueFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogueFile);
        return FetchFileAsync(catalogueFile.FileName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources; present for future extensibility.
    }

    private Task<Stream> FetchFileAsync(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        // Strip the file:/ URI prefix that S-100 exchange catalogues use
        string relativePath = fileName;
        if (relativePath.StartsWith("file:/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = relativePath["file:/".Length..];
        }

        string fullPath = Path.GetFullPath(Path.Combine(_basePath, relativePath));

        // Prevent path traversal
        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("File name must not navigate outside the exchange set directory.", nameof(fileName));
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }
}
