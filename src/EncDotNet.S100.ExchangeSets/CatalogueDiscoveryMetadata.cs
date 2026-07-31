namespace EncDotNet.S100.ExchangeSets;

public sealed class CatalogueDiscoveryMetadata
{
    public required string FileName { get; init; }

    /// <summary>
    /// The directory of the catalogue file relative to the exchange set
    /// root, as declared by the catalogue's <c>filePath</c> element.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 17.</remarks>
    public string? FilePath { get; init; }

    /// <summary>
    /// The source-relative path of the catalogue file, combining
    /// <see cref="FilePath"/> and <see cref="FileName"/>.
    /// </summary>
    public string RelativePath => ExchangeSet.ResolveRelativePath(FilePath, FileName);

    public string? Purpose { get; init; }

    public int? EditionNumber { get; init; }

    public string? Scope { get; init; }

    public string? VersionNumber { get; init; }

    public DateOnly? IssueDate { get; init; }

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

    /// <summary>
    /// The declared cryptographic hash for this catalogue file, if the
    /// catalogue carries one. Used to integrity-check the file independently
    /// of any digital signature.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10, Table 15-12.</remarks>
    public CryptographicHash? ExpectedHash { get; init; }

    public bool CompressionFlag { get; init; }

    public PtLocale? DefaultLocale { get; init; }

    public IReadOnlyList<PtLocale> OtherLocales { get; init; } = [];
}
