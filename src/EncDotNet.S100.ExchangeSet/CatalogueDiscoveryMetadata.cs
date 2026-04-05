namespace EncDotNet.S100.ExchangeSet;

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

    public bool CompressionFlag { get; init; }

    public string? DefaultLocaleLanguage { get; init; }

    public string? DefaultLocaleCharacterEncoding { get; init; }
}
