using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// An exchange set backed by an <see cref="IAssetSource"/>.
/// </summary>
public sealed class ExchangeSet : IDisposable
{
    private readonly IAssetSource _source;

    /// <summary>
    /// Initializes a new instance of <see cref="ExchangeSet"/> with the given source and catalogue.
    /// </summary>
    /// <param name="source">The asset source used to fetch referenced files.</param>
    /// <param name="catalogue">The parsed exchange catalogue.</param>
    public ExchangeSet(IAssetSource source, ExchangeCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        _source = source;
        Catalogue = catalogue;
    }

    /// <summary>
    /// Gets the parsed exchange catalogue metadata.
    /// </summary>
    public ExchangeCatalogue Catalogue { get; }

    /// <summary>
    /// Opens an <see cref="ExchangeSet"/> by reading the catalogue from the given source.
    /// </summary>
    /// <param name="source">The asset source containing the exchange set.</param>
    /// <param name="cataloguePath">The relative path to the catalogue XML file within the source.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task<ExchangeSet> OpenAsync(IAssetSource source, string cataloguePath = "CATALOG.XML", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);

        await using var stream = await source.OpenAsync(cataloguePath, cancellationToken);
        var catalogue = ExchangeCatalogueReader.Read(stream);
        return new ExchangeSet(source, catalogue);
    }

    /// <summary>
    /// Fetches the content of a dataset file referenced by the catalogue.
    /// </summary>
    public Task<Stream> FetchDatasetAsync(DatasetDiscoveryMetadata dataset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return _source.OpenAsync(NormalizeFileName(dataset.FileName), cancellationToken);
    }

    /// <summary>
    /// Fetches the content of a support file referenced by the catalogue.
    /// </summary>
    public Task<Stream> FetchSupportFileAsync(SupportFileDiscoveryMetadata supportFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supportFile);
        return _source.OpenAsync(NormalizeFileName(supportFile.FileName), cancellationToken);
    }

    /// <summary>
    /// Fetches the content of a sub-catalogue file referenced by the catalogue.
    /// </summary>
    public Task<Stream> FetchCatalogueFileAsync(CatalogueDiscoveryMetadata catalogueFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogueFile);
        return _source.OpenAsync(NormalizeFileName(catalogueFile.FileName), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _source.Dispose();

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
