namespace EncDotNet.S100.ExchangeSet;

public sealed class SupportFileDiscoveryMetadata
{
    public required string FileName { get; init; }

    public string? RevisionStatus { get; init; }

    public int? EditionNumber { get; init; }

    public string? IssueDate { get; init; }

    public string? SupportFileSpecificationName { get; init; }

    public string? DataType { get; init; }

    public bool CompressionFlag { get; init; }

    public string? DigitalSignatureReference { get; init; }

    public IReadOnlyList<string> SupportedResources { get; init; } = [];

    public string? ResourcePurpose { get; init; }
}
