namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Base class for exchange set providers that supplies catalogue access
/// and delegates storage-specific file operations to derived types.
/// </summary>
public abstract class ExchangeSetBase : IExchangeSetProvider
{
    /// <summary>
    /// Initializes a new instance of <see cref="ExchangeSetBase"/> with the given catalogue.
    /// </summary>
    protected ExchangeSetBase(ExchangeCatalogue catalogue)
    {
        Catalogue = catalogue;
    }

    /// <inheritdoc />
    public ExchangeCatalogue Catalogue { get; }

    /// <inheritdoc />
    public Task<Stream> FetchDatasetAsync(DatasetDiscoveryMetadata dataset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return OpenFileAsync(NormalizeFileName(dataset.FileName), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Stream> FetchSupportFileAsync(SupportFileDiscoveryMetadata supportFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supportFile);
        return OpenFileAsync(NormalizeFileName(supportFile.FileName), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Stream> FetchCatalogueFileAsync(CatalogueDiscoveryMetadata catalogueFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogueFile);
        return OpenFileAsync(NormalizeFileName(catalogueFile.FileName), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources used by this provider.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>
    /// Opens a readable stream for the file at the given normalized relative path
    /// within the exchange set.
    /// </summary>
    /// <param name="relativePath">
    /// A forward-slash separated relative path with the <c>file:/</c> prefix already removed.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    protected abstract Task<Stream> OpenFileAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Strips the <c>file:/</c> URI prefix that S-100 exchange catalogues may use on file names.
    /// </summary>
    private static string NormalizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        if (fileName.StartsWith("file:/", StringComparison.OrdinalIgnoreCase))
        {
            return fileName["file:/".Length..];
        }

        return fileName;
    }
}
