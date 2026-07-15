namespace EncDotNet.S100.ExchangeSets;

public sealed class DatasetDiscoveryMetadata
{
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
    /// </summary>
    public string RelativePath => ExchangeSet.ResolveRelativePath(FilePath, FileName);

    public string? Description { get; init; }

    public string? DatasetId { get; init; }

    public bool CompressionFlag { get; init; }

    public bool DataProtection { get; init; }

    public string? ProtectionScheme { get; init; }

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
    /// The declared cryptographic hash for this dataset file, if the catalogue
    /// carries one. Used to integrity-check the file independently of any
    /// digital signature.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10, Table 15-12.</remarks>
    public CryptographicHash? ExpectedHash { get; init; }

    public bool Copyright { get; init; }

    public string? Classification { get; init; }

    public Purpose? Purpose { get; init; }

    public bool NotForNavigation { get; init; }

    public string? SpecificUsage { get; init; }

    public int? EditionNumber { get; init; }

    public int? UpdateNumber { get; init; }

    public DateOnly? UpdateApplicationDate { get; init; }

    public string? ReferenceId { get; init; }

    public DateOnly? IssueDate { get; init; }

    public TimeOnly? IssueTime { get; init; }

    public BoundingBox? BoundingBox { get; init; }

    public TemporalExtent? TemporalExtent { get; init; }

    public ProductSpecification? ProductSpecification { get; init; }

    public string? ProducingAgency { get; init; }

    public string? EncodingFormat { get; init; }

    public IReadOnlyList<DataCoverage> DataCoverages { get; init; } = [];

    public string? Comment { get; init; }

    public PT_Locale? DefaultLocale { get; init; }

    public IReadOnlyList<PT_Locale> OtherLocales { get; init; } = [];


    public string? MetadataPointOfContact { get; init; }
    public DateOnly? MetadataDateStamp { get; init; }

    public bool? ReplaceData { get; set; }
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

    public string? DefaultLocaleLanguage { get; init; }

    public string? DataReplacement { get; init; }

    public NavigationPurpose? NavigationPurpose { get; init; }

    public MaintenanceInformation? ResourceMaintenance { get; init; }
}
