namespace EncDotNet.S100.ExchangeSets;

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

    /// <summary>
    /// The certificate block from the catalogue, containing the scheme administrator
    /// identifier and embedded X.509 certificates used to verify per-file signatures.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-5.</remarks>
    public CertificateBlock? Certificates { get; init; }
}
