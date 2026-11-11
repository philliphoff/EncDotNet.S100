namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// A <c>productSpecification</c> reference from the exchange catalogue,
/// identifying the S-100 product specification (e.g. S-101, S-102) that a
/// catalogue, dataset or sub-catalogue conforms to.
/// </summary>
/// <remarks>
/// Values are kept as declared and are not validated. Use
/// <see cref="ProductSpecificationExtensions.TryToSpecRef"/> to derive a
/// strongly-typed <see cref="EncDotNet.S100.Core.SpecRef"/>. Referenced from
/// <see cref="ExchangeCatalogue.ProductSpecification"/>,
/// <see cref="DatasetDiscoveryMetadata.ProductSpecification"/> and
/// <see cref="CatalogueDiscoveryMetadata.ProductSpecification"/>.
/// </remarks>
public sealed class ProductSpecification
{
    /// <summary>The product specification name (<c>name</c>), e.g. <c>S-101</c>.</summary>
    public string? Name { get; init; }

    /// <summary>The product specification edition/version (<c>version</c>), e.g. <c>1.0.0</c>.</summary>
    public string? Version { get; init; }

    /// <summary>The product specification publication date (<c>date</c>), kept verbatim.</summary>
    public string? Date { get; init; }

    /// <summary>
    /// The product identifier (<c>productIdentifier</c>), e.g. the long form
    /// <c>INT.IHO.S-101.1.0.0</c>, which carries both name and version.
    /// </summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>
    /// The IHO product specification number (<c>number</c>), e.g. <c>101</c>;
    /// <see langword="null"/> when absent or not an integer.
    /// </summary>
    public int? Number { get; init; }

    /// <summary>The compliancy category (<c>compliancyCategory</c>), kept verbatim.</summary>
    public string? CompliancyCategory { get; init; }
}
