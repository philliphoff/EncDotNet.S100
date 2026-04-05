namespace EncDotNet.S100.ExchangeSet;

public sealed class ExchangeCatalogue
{
    public required ExchangeCatalogueIdentifier Identifier { get; init; }

    public ExchangeCatalogueContact? Contact { get; init; }

    public ProductSpecification? ProductSpecification { get; init; }

    public string? DefaultLocaleLanguage { get; init; }

    public string? DefaultLocaleCharacterEncoding { get; init; }

    public string? Description { get; init; }

    public string? Comment { get; init; }

    public string? DataServerIdentifier { get; init; }

    public IReadOnlyList<DatasetDiscoveryMetadata> DatasetDiscoveryMetadata { get; init; } = [];

    public IReadOnlyList<SupportFileDiscoveryMetadata> SupportFileDiscoveryMetadata { get; init; } = [];

    public IReadOnlyList<CatalogueDiscoveryMetadata> CatalogueDiscoveryMetadata { get; init; } = [];
}
