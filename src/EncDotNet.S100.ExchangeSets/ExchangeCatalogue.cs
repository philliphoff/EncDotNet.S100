namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The parsed S-100 Part 17 exchange catalogue (<c>CATALOG.XML</c>) of an
/// exchange set: catalogue-level metadata plus discovery metadata for every
/// dataset, support file and sub-catalogue in the set.
/// </summary>
/// <remarks>
/// Produced by <see cref="ExchangeCatalogueReader"/>; wrapped together with an
/// asset source by <see cref="ExchangeSet"/>. Element names below are relative
/// to the catalogue root, in the root element's namespace.
/// </remarks>
public sealed class ExchangeCatalogue
{
    /// <summary>The catalogue identifier and creation date-time (<c>identifier</c>).</summary>
    public required ExchangeCatalogueIdentifier Identifier { get; init; }

    /// <summary>
    /// The producer contact (<c>contact</c>), or <see langword="null"/> when absent.
    /// </summary>
    public ExchangeCatalogueContact? Contact { get; init; }

    /// <summary>
    /// The catalogue-level product specification (<c>productSpecification</c>),
    /// or <see langword="null"/> when absent. Individual datasets carry their own in
    /// <see cref="DatasetDiscoveryMetadata.ProductSpecification"/>.
    /// </summary>
    public ProductSpecification? ProductSpecification { get; init; }

    /// <summary>
    /// The <c>codeListValue</c> of the <c>lan:LanguageCode</c> under
    /// <c>defaultLocale</c> (e.g. <c>eng</c>), or <see langword="null"/> when absent.
    /// </summary>
    public string? DefaultLocaleLanguage { get; init; }

    /// <summary>
    /// The <c>codeListValue</c> of the <c>lan:MD_CharacterSetCode</c> under
    /// <c>defaultLocale</c> (e.g. <c>utf8</c>), or <see langword="null"/> when absent.
    /// </summary>
    public string? DefaultLocaleCharacterEncoding { get; init; }

    /// <summary>
    /// The free-text catalogue description (<c>exchangeCatalogueDescription</c>),
    /// or <see langword="null"/> when absent.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// The free-text catalogue comment (<c>exchangeCatalogueComment</c>),
    /// or <see langword="null"/> when absent.
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// The identifier of the data server that produced the exchange set
    /// (<c>dataServerIdentifier</c>), or <see langword="null"/> when absent.
    /// </summary>
    public string? DataServerIdentifier { get; init; }

    /// <summary>
    /// Discovery metadata for each dataset in the exchange set, in catalogue order;
    /// empty when there are none. Both the wrapped
    /// (<c>datasetDiscoveryMetadata/*_DatasetDiscoveryMetadata</c>) and the legacy
    /// <c>S100EC</c> unwrapped layouts are recognized.
    /// </summary>
    public IReadOnlyList<DatasetDiscoveryMetadata> DatasetDiscoveryMetadata { get; init; } = [];

    /// <summary>
    /// Discovery metadata for each support file in the exchange set, in catalogue
    /// order; empty when there are none. Both typed
    /// <c>*_SupportFileDiscoveryMetadata</c> children and inline
    /// <c>supportFileDiscoveryMetadata</c> records are recognized.
    /// </summary>
    public IReadOnlyList<SupportFileDiscoveryMetadata> SupportFileDiscoveryMetadata { get; init; } = [];

    /// <summary>
    /// Discovery metadata for each catalogue file (e.g. feature or portrayal
    /// catalogue) in the exchange set, in catalogue order; empty when there are none.
    /// </summary>
    public IReadOnlyList<CatalogueDiscoveryMetadata> CatalogueDiscoveryMetadata { get; init; } = [];

    /// <summary>
    /// The certificate block from the catalogue, containing the scheme administrator
    /// identifier and embedded X.509 certificates used to verify per-file signatures.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-5.</remarks>
    public CertificateBlock? Certificates { get; init; }
}
