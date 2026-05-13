namespace EncDotNet.S100.ExchangeSets;

public sealed class CatalogueDiscoveryMetadata
{
    public required string FileName { get; init; }

    public string? Purpose { get; init; }

    public int? EditionNumber { get; init; }

    public string? Scope { get; init; }

    public string? VersionNumber { get; init; }

    public string? IssueDate { get; init; }

    public ProductSpecification? ProductSpecification { get; init; }

    public string? DigitalSignatureReference { get; init; }

    /// <summary>
    /// The parsed digital signature algorithm, derived from <see cref="DigitalSignatureReference"/>.
    /// </summary>
    public DigitalSignatureAlgorithm DigitalSignatureAlgorithm { get; init; }

    /// <summary>
    /// The digital signature value for this catalogue file, if present.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-4.2.</remarks>
    public DigitalSignatureValue? DigitalSignatureValue { get; init; }

    public bool CompressionFlag { get; init; }

    public string? DefaultLocaleLanguage { get; init; }

    public string? DefaultLocaleCharacterEncoding { get; init; }
}
