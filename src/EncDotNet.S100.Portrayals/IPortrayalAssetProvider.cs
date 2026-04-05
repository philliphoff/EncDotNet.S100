namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Represents a container (e.g. a directory or archive) holding a portrayal catalogue and its assets.
/// </summary>
public interface IPortrayalCatalogueProvider : IDisposable
{
    /// <summary>
    /// Gets the parsed portrayal catalogue metadata.
    /// </summary>
    PortrayalCatalogue Catalogue { get; }

    /// <summary>
    /// Fetches the content of a portrayal asset referenced by a catalogue item.
    /// </summary>
    Task<Stream> FetchAssetAsync(CatalogItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the content of a portrayal asset referenced by a rule file.
    /// </summary>
    Task<Stream> FetchAssetAsync(RuleFile ruleFile, CancellationToken cancellationToken = default);
}
