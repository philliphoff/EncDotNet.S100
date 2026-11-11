namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Discovery metadata for one catalogue file (e.g. a feature or portrayal
/// catalogue) delivered in an exchange set — an
/// <c>S100_CatalogueDiscoveryMetadata</c> record from the exchange catalogue.
/// </summary>
/// <remarks>
/// S-100 Part 17. Instances are listed in
/// <see cref="ExchangeCatalogue.CatalogueDiscoveryMetadata"/>. Use
/// <see cref="RelativePath"/> (or <see cref="ExchangeSet.FetchCatalogueFileAsync"/>)
/// to locate the file. Dates are kept verbatim and are not parsed.
/// </remarks>
public sealed class CatalogueDiscoveryMetadata
{
    /// <summary>
    /// The catalogue file name as declared by <c>fileName</c>, kept verbatim;
    /// see <see cref="RelativePath"/>.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The directory of the catalogue file relative to the exchange set
    /// root, as declared by the catalogue's <c>filePath</c> element.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 17.</remarks>
    public string? FilePath { get; init; }

    /// <summary>
    /// The source-relative path of the catalogue file, combining
    /// <see cref="FilePath"/> and <see cref="FileName"/> and normalizing
    /// separators, via <see cref="ExchangeSet.ResolveRelativePath"/>. This is
    /// the path to pass to an <see cref="EncDotNet.S100.Core.IAssetSource"/>.
    /// </summary>
    public string RelativePath => ExchangeSet.ResolveRelativePath(FilePath, FileName);

    /// <summary>
    /// The purpose (<c>purpose</c>), kept verbatim; <see langword="null"/> when absent.
    /// </summary>
    public string? Purpose { get; init; }

    /// <summary>
    /// The edition number (<c>editionNumber</c>), or <see langword="null"/> when
    /// absent or not an integer.
    /// </summary>
    public int? EditionNumber { get; init; }

    /// <summary>
    /// The catalogue scope (<c>scope</c>, e.g. <c>featureCatalogue</c> or
    /// <c>portrayalCatalogue</c>), kept verbatim; <see langword="null"/> when absent.
    /// </summary>
    public string? Scope { get; init; }

    /// <summary>
    /// The catalogue version (<c>versionNumber</c>), kept verbatim;
    /// <see langword="null"/> when absent.
    /// </summary>
    public string? VersionNumber { get; init; }

    /// <summary>
    /// The issue date (<c>issueDate</c>), kept verbatim; <see langword="null"/>
    /// when absent.
    /// </summary>
    public string? IssueDate { get; init; }

    /// <summary>
    /// The product specification the catalogue belongs to
    /// (<c>productSpecification</c>), or <see langword="null"/> when absent.
    /// </summary>
    public ProductSpecification? ProductSpecification { get; init; }

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
    /// The digital signature value for this catalogue file, if present.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-4.2.</remarks>
    public DigitalSignatureValue? DigitalSignatureValue { get; init; }

    /// <summary>
    /// All digital signatures declared for this catalogue file, in catalogue order.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.8.</remarks>
    public IReadOnlyList<DigitalSignatureValue> DigitalSignatures { get; init; } = [];

    /// <summary>
    /// The declared cryptographic hash for this catalogue file, if the
    /// catalogue carries one. Used to integrity-check the file independently
    /// of any digital signature.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10, Table 15-12.</remarks>
    public CryptographicHash? ExpectedHash { get; init; }

    /// <summary>
    /// <see langword="true"/> when <c>compressionFlag</c> is <c>true</c>, i.e. the
    /// stored file is ZIP-compressed; <see langword="false"/> when absent or any
    /// other value.
    /// </summary>
    public bool CompressionFlag { get; init; }

    /// <summary>
    /// The <c>codeListValue</c> of the <c>lan:LanguageCode</c> under the record's
    /// <c>defaultLocale</c>, or <see langword="null"/> when absent.
    /// </summary>
    public string? DefaultLocaleLanguage { get; init; }

    /// <summary>
    /// The <c>codeListValue</c> of the <c>lan:MD_CharacterSetCode</c> under the
    /// record's <c>defaultLocale</c>, or <see langword="null"/> when absent.
    /// </summary>
    public string? DefaultLocaleCharacterEncoding { get; init; }
}
