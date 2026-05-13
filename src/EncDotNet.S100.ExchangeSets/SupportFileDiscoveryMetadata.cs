namespace EncDotNet.S100.ExchangeSets;

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

    /// <summary>
    /// The parsed digital signature algorithm, derived from <see cref="DigitalSignatureReference"/>.
    /// </summary>
    public DigitalSignatureAlgorithm DigitalSignatureAlgorithm { get; init; }

    /// <summary>
    /// The digital signature value for this support file, if present.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-4.2.</remarks>
    public DigitalSignatureValue? DigitalSignatureValue { get; init; }

    public IReadOnlyList<string> SupportedResources { get; init; } = [];

    public string? ResourcePurpose { get; init; }
}
