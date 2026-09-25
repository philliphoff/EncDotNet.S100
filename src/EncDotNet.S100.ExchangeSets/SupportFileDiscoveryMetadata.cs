namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Discovery metadata for one support file (e.g. a text or picture file
/// referenced by datasets) in an exchange set — an
/// <c>S100_SupportFileDiscoveryMetadata</c> record, or an inline
/// <c>supportFileDiscoveryMetadata</c> element, from the exchange catalogue.
/// </summary>
/// <remarks>
/// S-100 Part 17. Instances are listed in
/// <see cref="ExchangeCatalogue.SupportFileDiscoveryMetadata"/>. Use
/// <see cref="RelativePath"/> (or <see cref="ExchangeSet.FetchSupportFileAsync"/>)
/// to locate the file. Dates are kept verbatim and are not parsed.
/// </remarks>
public sealed class SupportFileDiscoveryMetadata
{
    /// <summary>
    /// The support file name as declared by <c>fileName</c>, kept verbatim;
    /// see <see cref="RelativePath"/>.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The directory of the support file relative to the exchange set
    /// root, as declared by the catalogue's <c>fileLocation</c> element
    /// (or, from some producers, a dataset-style <c>filePath</c>, which takes
    /// precedence when both are present). May be <see langword="null"/>.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 17.</remarks>
    public string? FilePath { get; init; }

    /// <summary>
    /// The source-relative path of the support file, combining
    /// <see cref="FilePath"/> and <see cref="FileName"/> and normalizing
    /// separators, via <see cref="ExchangeSet.ResolveRelativePath"/>. This is
    /// the path to pass to an <see cref="EncDotNet.S100.Core.IAssetSource"/>.
    /// </summary>
    public string RelativePath => ExchangeSet.ResolveRelativePath(FilePath, FileName);

    /// <summary>
    /// The revision status (<c>revisionStatus</c>, e.g. <c>new</c>,
    /// <c>replacement</c>, <c>deletion</c>), kept verbatim; <see langword="null"/>
    /// when absent.
    /// </summary>
    public string? RevisionStatus { get; init; }

    /// <summary>
    /// The support file edition number (<c>editionNumber</c>), or
    /// <see langword="null"/> when absent or not an integer.
    /// </summary>
    public int? EditionNumber { get; init; }

    /// <summary>
    /// The issue date (<c>issueDate</c>), kept verbatim; <see langword="null"/>
    /// when absent.
    /// </summary>
    public string? IssueDate { get; init; }

    /// <summary>
    /// The name of the specification the support file conforms to
    /// (<c>supportFileSpecification/name</c>), or <see langword="null"/> when absent.
    /// </summary>
    public string? SupportFileSpecificationName { get; init; }

    /// <summary>
    /// The support file data type (<c>dataType</c>, e.g. <c>TXT_UTF-8</c>, <c>ASCII</c>,
    /// <c>TIFF</c> or <c>XML</c>), kept verbatim; <see langword="null"/> when absent.
    /// </summary>
    public string? DataType { get; init; }

    /// <summary>
    /// <see langword="true"/> when <c>compressionFlag</c> is <c>true</c>, i.e. the
    /// stored file is ZIP-compressed; <see langword="false"/> when absent or any
    /// other value.
    /// </summary>
    public bool CompressionFlag { get; init; }

    /// <summary>
    /// The raw <c>digitalSignatureReference</c> value naming the signature
    /// algorithm (e.g. <c>ECDSA</c>, <c>ECDSA-384-SHA2</c>), or
    /// <see langword="null"/> when absent. The parsed form is
    /// <see cref="DigitalSignatureAlgorithm"/>.
    /// </summary>
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

    /// <summary>
    /// All digital signatures declared for this support file, in catalogue order.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.8.</remarks>
    public IReadOnlyList<DigitalSignatureValue> DigitalSignatures { get; init; } = [];

    /// <summary>
    /// The declared cryptographic hash for this support file, if the catalogue
    /// carries one. Used to integrity-check the file independently of any
    /// digital signature.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10, Table 15-12.</remarks>
    public CryptographicHash? ExpectedHash { get; init; }

    /// <summary>
    /// The trimmed text of each <c>supportedResource</c> element — the resources
    /// (typically dataset file names) this support file applies to; empty when
    /// there are none.
    /// </summary>
    public IReadOnlyList<string> SupportedResources { get; init; } = [];

    /// <summary>
    /// The purpose of the support file (<c>resourcePurpose</c>), kept verbatim;
    /// <see langword="null"/> when absent.
    /// </summary>
    public string? ResourcePurpose { get; init; }
}
