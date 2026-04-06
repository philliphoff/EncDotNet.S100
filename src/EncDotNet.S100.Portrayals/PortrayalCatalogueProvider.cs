using EncDotNet.S100.Core;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// A portrayal catalogue provider backed by an <see cref="IAssetSource"/>.
/// </summary>
public sealed class PortrayalCatalogueProvider : IDisposable
{
    private readonly IAssetSource _source;

    /// <summary>
    /// Initializes a new instance of <see cref="PortrayalCatalogueProvider"/> with the given source and catalogue.
    /// </summary>
    /// <param name="source">The asset source used to fetch referenced assets.</param>
    /// <param name="catalogue">The parsed portrayal catalogue.</param>
    public PortrayalCatalogueProvider(IAssetSource source, PortrayalCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        _source = source;
        Catalogue = catalogue;
    }

    /// <summary>
    /// Gets the parsed portrayal catalogue metadata.
    /// </summary>
    public PortrayalCatalogue Catalogue { get; }

    /// <summary>
    /// Opens a <see cref="PortrayalCatalogueProvider"/> by reading the catalogue from the given source.
    /// </summary>
    /// <param name="source">The asset source containing the portrayal catalogue and assets.</param>
    /// <param name="cataloguePath">The relative path to the catalogue XML file within the source.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task<PortrayalCatalogueProvider> OpenAsync(IAssetSource source, string cataloguePath = "portrayal_catalogue.xml", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);

        await using var stream = await source.OpenAsync(cataloguePath, cancellationToken);
        var catalogue = PortrayalCatalogueReader.Read(stream);
        return new PortrayalCatalogueProvider(source, catalogue);
    }

    /// <summary>
    /// Fetches the content of a portrayal asset referenced by a catalogue item.
    /// </summary>
    public Task<Stream> FetchAssetAsync(CatalogItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _source.OpenAsync(item.FileName, cancellationToken);
    }

    /// <summary>
    /// Fetches the content of a portrayal asset referenced by a rule file.
    /// </summary>
    public Task<Stream> FetchAssetAsync(RuleFile ruleFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ruleFile);
        return _source.OpenAsync(ruleFile.FileName, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _source.Dispose();
}
