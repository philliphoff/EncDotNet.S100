namespace EncDotNet.S100.ExchangeSets;

public sealed class DatasetDiscoveryMetadata
{
    public required string FileName { get; init; }

    public string? Description { get; init; }

    public bool CompressionFlag { get; init; }

    public bool DataProtection { get; init; }

    public string? DigitalSignatureReference { get; init; }

    public bool Copyright { get; init; }

    public string? Classification { get; init; }

    public string? Purpose { get; init; }

    public bool NotForNavigation { get; init; }

    public string? SpecificUsage { get; init; }

    public int? EditionNumber { get; init; }

    public int? UpdateNumber { get; init; }

    public string? UpdateApplicationDate { get; init; }

    public string? IssueDate { get; init; }

    public BoundingBox? BoundingBox { get; init; }

    public ProductSpecification? ProductSpecification { get; init; }

    public string? ProducingAgency { get; init; }

    public string? EncodingFormat { get; init; }

    public IReadOnlyList<DataCoverage> DataCoverages { get; init; } = [];

    public string? DefaultLocaleLanguage { get; init; }

    public string? DefaultLocaleCharacterEncoding { get; init; }

    public string? MetadataDateStamp { get; init; }

    public string? NavigationPurpose { get; init; }
}
