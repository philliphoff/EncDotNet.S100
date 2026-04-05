namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Represents a container (e.g. a directory or archive) holding an S-100 exchange set
/// and its referenced datasets, support files, and catalogues.
/// </summary>
public interface IExchangeSetProvider : IDisposable
{
    /// <summary>
    /// Gets the parsed exchange catalogue metadata.
    /// </summary>
    ExchangeCatalogue Catalogue { get; }

    /// <summary>
    /// Fetches the content of a dataset file referenced by the catalogue.
    /// </summary>
    Task<Stream> FetchDatasetAsync(DatasetDiscoveryMetadata dataset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the content of a support file referenced by the catalogue.
    /// </summary>
    Task<Stream> FetchSupportFileAsync(SupportFileDiscoveryMetadata supportFile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the content of a sub-catalogue file referenced by the catalogue.
    /// </summary>
    Task<Stream> FetchCatalogueFileAsync(CatalogueDiscoveryMetadata catalogueFile, CancellationToken cancellationToken = default);
}
