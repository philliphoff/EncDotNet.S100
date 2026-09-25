namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Discovery metadata for one dataset in an exchange set — an
/// <c>S100_DatasetDiscoveryMetadata</c> record (or a product-specific equivalent
/// such as <c>S102_DatasetDiscoveryMetadata</c>) from the exchange catalogue.
/// </summary>
/// <remarks>
/// S-100 Part 17. Instances are listed in
/// <see cref="ExchangeCatalogue.DatasetDiscoveryMetadata"/>. Use
/// <see cref="RelativePath"/> (or <see cref="ExchangeSet.FetchDatasetAsync"/>)
/// to locate the file. Element names below are children of the record.
/// Dates are kept verbatim as the XML text and are not parsed.
/// </remarks>
public sealed class DatasetDiscoveryMetadata
{
    /// <summary>
    /// The dataset file name as declared by <c>fileName</c>. Kept verbatim: it may be
    /// a bare name, a full path, or a <c>file:/</c> URI; see <see cref="RelativePath"/>
    /// and <see cref="ExchangeSet.NormalizeFileName"/>.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The directory of the dataset file relative to the exchange set
    /// root, as declared by the catalogue's <c>filePath</c> element.
    /// May be <see langword="null"/> when the file lives at the root or
    /// the path is folded into <see cref="FileName"/>.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 17.</remarks>
    public string? FilePath { get; init; }

    /// <summary>
    /// The source-relative path of the dataset file, combining
    /// <see cref="FilePath"/> and <see cref="FileName"/> and normalizing
    /// separators. This is the path to pass to an
    /// <see cref="EncDotNet.S100.Core.IAssetSource"/>.
    /// Computed by <see cref="ExchangeSet.ResolveRelativePath"/>.
    /// </summary>
    public string RelativePath => ExchangeSet.ResolveRelativePath(FilePath, FileName);

    /// <summary>
    /// The free-text dataset description (<c>description</c>), or
    /// <see langword="null"/> when absent.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// <see langword="true"/> when <c>compressionFlag</c> is <c>true</c>, i.e. the
    /// stored file is ZIP-compressed (S-100 Part 15 §15-5.2); <see langword="false"/>
    /// when absent or any other value.
    /// </summary>
    public bool CompressionFlag { get; init; }

    /// <summary>
    /// <see langword="true"/> when <c>dataProtection</c> is <c>true</c>, i.e. the
    /// stored file is encrypted under the Part 15 protection scheme and needs a
    /// permit key to read (see <see cref="Protection.DecryptingAssetSource"/>);
    /// <see langword="false"/> when absent or any other value.
    /// </summary>
    public bool DataProtection { get; init; }

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
    /// The digital signature value for this dataset file, if present.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-4.2.</remarks>
    public DigitalSignatureValue? DigitalSignatureValue { get; init; }

    /// <summary>
    /// All digital signatures declared for this dataset, in catalogue order.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.8.</remarks>
    public IReadOnlyList<DigitalSignatureValue> DigitalSignatures { get; init; } = [];

    /// <summary>
    /// The declared cryptographic hash for this dataset file, if the catalogue
    /// carries one. Used to integrity-check the file independently of any
    /// digital signature.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10, Table 15-12.</remarks>
    public CryptographicHash? ExpectedHash { get; init; }

    /// <summary>
    /// <see langword="true"/> when <c>copyright</c> is <c>true</c>;
    /// <see langword="false"/> when absent or any other value.
    /// </summary>
    public bool Copyright { get; init; }

    /// <summary>
    /// The security classification (<c>classification</c>): the
    /// <c>codeListValue</c> attribute of its code-list child when present
    /// (e.g. <c>unclassified</c>), otherwise the element text;
    /// <see langword="null"/> when absent.
    /// </summary>
    public string? Classification { get; init; }

    /// <summary>
    /// The dataset purpose (<c>purpose</c>, e.g. <c>newDataset</c>, <c>newEdition</c>,
    /// <c>update</c>), kept verbatim; <see langword="null"/> when absent.
    /// Spelling varies between producers (e.g. <c>new</c>, <c>new edition</c>),
    /// so compare tolerantly.
    /// </summary>
    public string? Purpose { get; init; }

    /// <summary>
    /// <see langword="true"/> when <c>notForNavigation</c> is <c>true</c>;
    /// <see langword="false"/> when absent or any other value.
    /// </summary>
    public bool NotForNavigation { get; init; }

    /// <summary>
    /// The intended usage text from <c>specificUsage/mri:MD_Usage/mri:specificUsage</c>,
    /// or <see langword="null"/> when absent.
    /// </summary>
    public string? SpecificUsage { get; init; }

    /// <summary>
    /// The dataset edition number (<c>editionNumber</c>), or <see langword="null"/>
    /// when absent or not an integer.
    /// </summary>
    public int? EditionNumber { get; init; }

    /// <summary>
    /// The dataset update number (<c>updateNumber</c>), or <see langword="null"/>
    /// when absent or not an integer.
    /// </summary>
    public int? UpdateNumber { get; init; }

    /// <summary>
    /// The update application date (<c>updateApplicationDate</c>), kept verbatim;
    /// <see langword="null"/> when absent.
    /// </summary>
    public string? UpdateApplicationDate { get; init; }

    /// <summary>
    /// The dataset issue date (<c>issueDate</c>), kept verbatim (typically
    /// <c>YYYY-MM-DD</c>); <see langword="null"/> when absent.
    /// </summary>
    public string? IssueDate { get; init; }

    /// <summary>
    /// The dataset's geographic extent (<c>boundingBox</c>), or
    /// <see langword="null"/> when absent.
    /// </summary>
    public BoundingBox? BoundingBox { get; init; }

    /// <summary>
    /// The product specification the dataset conforms to
    /// (<c>productSpecification</c>), or <see langword="null"/> when absent.
    /// </summary>
    public ProductSpecification? ProductSpecification { get; init; }

    /// <summary>
    /// The producing agency name, read from
    /// <c>producingAgency/cit:CI_Responsibility/cit:party/cit:CI_Organisation/cit:name</c>;
    /// <see langword="null"/> when absent.
    /// </summary>
    public string? ProducingAgency { get; init; }

    /// <summary>
    /// The dataset encoding format (<c>encodingFormat</c>, e.g. <c>ISO/IEC 8211</c>
    /// or <c>HDF5</c>), kept verbatim; <see langword="null"/> when absent.
    /// </summary>
    public string? EncodingFormat { get; init; }

    /// <summary>
    /// The dataset's <c>dataCoverage</c> entries, in catalogue order; empty when
    /// there are none.
    /// </summary>
    public IReadOnlyList<DataCoverage> DataCoverages { get; init; } = [];

    /// <summary>
    /// The most-permissive coarsest display-scale denominator across all
    /// <see cref="DataCoverages"/> — the <em>largest</em>
    /// <see cref="DataCoverage.MinimumDisplayScale"/> present — or
    /// <see langword="null"/> when no coverage declares one.
    /// </summary>
    /// <remarks>
    /// This is the most zoomed-out edge of the cell's intended scale band
    /// (S-100 Part 17; S-101 FC §3.1.1 <c>DataCoverage.minimumDisplayScale</c>).
    /// The maximum is taken so detail remains visible wherever <em>any</em>
    /// coverage region still permits it, matching the S-101 in-file
    /// out-of-scale-band resolution. Drives the hole-safe per-cell zoom-out
    /// visibility window (issue #438).
    /// </remarks>
    public int? ResolveMinimumDisplayScale()
    {
        int? result = null;
        foreach (var coverage in DataCoverages)
        {
            if (coverage.MinimumDisplayScale is not int value || value <= 0)
                continue;
            result = result is null ? value : Math.Max(result.Value, value);
        }

        return result;
    }

    /// <summary>
    /// The most-permissive finest display-scale denominator across all
    /// <see cref="DataCoverages"/> — the <em>smallest</em>
    /// <see cref="DataCoverage.MaximumDisplayScale"/> present — or
    /// <see langword="null"/> when no coverage declares one.
    /// </summary>
    /// <remarks>
    /// This is the most zoomed-in edge of the cell's intended scale band
    /// (S-100 Part 17; S-101 FC §3.1.1 <c>DataCoverage.maximumDisplayScale</c>).
    /// Carried for completeness; the zoom-in cutoff it would drive is deferred
    /// to the coverage-clipping work (issue #438 Phase 2) because a naive
    /// whole-cell cutoff would leave holes outside finer cells' footprints.
    /// </remarks>
    public int? ResolveMaximumDisplayScale()
    {
        int? result = null;
        foreach (var coverage in DataCoverages)
        {
            if (coverage.MaximumDisplayScale is not int value || value <= 0)
                continue;
            result = result is null ? value : Math.Min(result.Value, value);
        }

        return result;
    }

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

    /// <summary>
    /// The date the discovery metadata was produced (<c>metadataDateStamp</c>),
    /// kept verbatim; <see langword="null"/> when absent.
    /// </summary>
    public string? MetadataDateStamp { get; init; }

    /// <summary>
    /// The navigation purpose (<c>navigationPurpose</c>, e.g. <c>port</c>,
    /// <c>transit</c> or <c>overview</c>), kept verbatim; <see langword="null"/> when absent.
    /// </summary>
    public string? NavigationPurpose { get; init; }
}
