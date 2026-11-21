using EncDotNet.S57;
using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>Where an AML cell's protective marking came from.</summary>
public enum AmlMarkingSource
{
    /// <summary>The cell gives no marking anywhere.</summary>
    None,

    /// <summary>The <c>DSID</c> comment (<c>COMT</c>) <c>Protective Marking</c> entry.</summary>
    Header,

    /// <summary>The <c>m_clas</c> (Security Classification Information) meta feature's <c>secpmk</c>.</summary>
    SecurityMetaFeature,

    /// <summary>The cell name's classification letter, because the cell itself gives none.</summary>
    CellName,
}

/// <summary>
/// What SoundCharts knows about a NATO Additional Military Layers (AML) cell:
/// its product, version, title and protective marking, and what its name says.
/// </summary>
/// <remarks>
/// <para>
/// The header fields come from the <c>DSID</c> comment (<c>COMT</c>), read by
/// <see cref="AmlDataSetMetadata"/>; where the comment lacks one, the
/// <c>m_clas</c> meta feature fills it in. The protective marking falls back
/// further, to the cell name's classification letter, because the AML 3.0
/// trial cells have an empty <c>COMT</c> and no <c>m_clas</c>
/// (<see cref="MarkingSource"/> says which was used).
/// </para>
/// <para>
/// AML data is military overlay data, not a chart: it never feeds depth
/// answers or route checks.
/// </para>
/// </remarks>
public sealed record AmlDatasetInfo
{
    /// <summary>The spec name AML cells are registered under.</summary>
    public const string SpecName = Core.SpecName.Aml;

    private const string SecurityMetaFeatureAcronym = "m_clas";
    private const string ProtectiveMarkingAcronym = "secpmk";
    private const string IdoStatusAcronym = "secido";
    private const string OwnerAuthorityAcronym = "secown";
    private const string CaveatAcronym = "seccvt";

    /// <summary>The data set name (<c>DSNM</c>), e.g. <c>UKC6U114.000</c>.</summary>
    public required string DataSetName { get; init; }

    /// <summary>The AML product, from <c>DSID</c> <c>PRSP</c>.</summary>
    public required AmlProduct Product { get; init; }

    /// <summary>The <c>DSID</c> <c>PRSP</c> code, 51–57.</summary>
    public required int ProductSpecification { get; init; }

    /// <summary>
    /// The product code: the AML 1.0/2.1 product (<c>CLB</c>), or for the AML
    /// 3.0 integrated product the token in its cell name (<c>SBO</c>), when it
    /// has one; otherwise <see langword="null"/>.
    /// </summary>
    public string? ProductCode { get; init; }

    /// <summary>
    /// The product's full name: e.g. "Contour Line Bathymetry", or
    /// <see cref="AmlProducts.IntegratedName"/> for a 3.0 cell whose code isn't
    /// one of the six AML 1.0/2.1 products.
    /// </summary>
    public required string ProductName { get; init; }

    /// <summary>The product specification edition (<c>PRED</c>) as written, e.g. <c>1.0</c> or <c>3.0</c>.</summary>
    public string? Version { get; init; }

    /// <summary>Whether this is an update cell rather than a base cell.</summary>
    public bool IsUpdate { get; init; }

    /// <summary>The cell title from the header, or <see langword="null"/>.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// The protective marking, normalized (see
    /// <see cref="AmlProtectiveMarking.Normalize"/>), or <see langword="null"/>
    /// when the cell and its name give none.
    /// </summary>
    public string? ProtectiveMarking { get; init; }

    /// <summary>Where <see cref="ProtectiveMarking"/> came from.</summary>
    public AmlMarkingSource MarkingSource { get; init; }

    /// <summary>The International Defence Organisation (IDO) status, or <see langword="null"/>.</summary>
    public string? IdoStatus { get; init; }

    /// <summary>The owner authority, or <see langword="null"/>.</summary>
    public string? OwnerAuthority { get; init; }

    /// <summary>The handling caveat, or <see langword="null"/>.</summary>
    public string? Caveat { get; init; }

    /// <summary>
    /// The AML scale band (0–9): from <c>DSID</c> <c>INTU</c> (100 + band),
    /// else from the cell name; <see langword="null"/> when neither gives one.
    /// </summary>
    public int? ScaleBand { get; init; }

    /// <summary>What the cell name says, or <see langword="null"/> when it doesn't follow an AML pattern.</summary>
    public AmlCellName? FromName { get; init; }

    /// <summary>Whether <see cref="ProtectiveMarking"/> ranks above UNCLASSIFIED.</summary>
    public bool IsMarkedAboveUnclassified =>
        ProtectiveMarking is { } marking && AmlProtectiveMarking.IsAboveUnclassified(marking);

    /// <summary>Whether the cell follows AML 3.0 (its edition's major number is 3, or it's the integrated product).</summary>
    public bool IsAml30 =>
        Product == AmlProduct.Integrated
        || (Version is { } v && (v == "3" || v.StartsWith("3.", StringComparison.Ordinal)));

    /// <summary>
    /// The tag text (decision 2): "AML · {code}" for 1.0/2.1, "AML 3.0 · {code}"
    /// when a 3.0 cell has a product code, otherwise "AML 3.0".
    /// </summary>
    public string TagText => IsAml30
        ? ProductCode is { } code ? $"AML 3.0 · {code}" : "AML 3.0"
        : $"AML · {ProductCode}";

    /// <summary>The tag tooltip: "Additional Military Layers · {product name} · {version}".</summary>
    public string TagToolTip => Version is { Length: > 0 } version
        ? $"Additional Military Layers · {ProductName} · {version}"
        : $"Additional Military Layers · {ProductName}";

    /// <summary>The product as "{code} · {name}", or the name alone when there's no code.</summary>
    public string ProductDisplay => ProductCode is { } code ? $"{code} · {ProductName}" : ProductName;

    /// <summary>Whether <paramref name="document"/> is an AML cell (<c>DSID</c> <c>PRSP</c> 51–57).</summary>
    /// <param name="document">The parsed S-57 document.</param>
    public static bool IsAml(S57Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.DataSetIdentification?.Product.Kind == S57ProductKind.Aml;
    }

    /// <summary>
    /// Reads the AML facts of a parsed cell.
    /// </summary>
    /// <param name="document">The parsed (and, where applicable, update-folded) S-57 document.</param>
    /// <param name="fileName">
    /// The cell's file name, used for the name decode when it differs from the
    /// data set name; <see langword="null"/> uses the data set name.
    /// </param>
    /// <returns>The facts, or <see langword="null"/> when <paramref name="document"/> isn't an AML cell.</returns>
    public static AmlDatasetInfo? FromDocument(S57Document document, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.DataSetIdentification is not { } dsid || dsid.Product.Kind != S57ProductKind.Aml)
            return null;

        var product = dsid.Product.AmlProduct ?? AmlProduct.Integrated;
        var header = ReadHeader(dsid.Comment);
        var security = ReadSecurityMetaFeature(document);

        AmlCellName.TryParse(fileName, out var fromName);
        if (fromName is null)
            AmlCellName.TryParse(dsid.DataSetName, out fromName);

        var code = AmlProducts.Code(product) ?? fromName?.Product;

        var (marking, source) = header.Marking is { } h ? (h, AmlMarkingSource.Header)
            : security.Marking is { } s ? (s, AmlMarkingSource.SecurityMetaFeature)
            : fromName?.Marking is { } n ? (n, AmlMarkingSource.CellName)
            : ((string?)null, AmlMarkingSource.None);

        return new AmlDatasetInfo
        {
            DataSetName = dsid.DataSetName,
            Product = product,
            ProductSpecification = dsid.ProductSpecification,
            ProductCode = code,
            ProductName = AmlProducts.Name(code) ?? AmlProducts.IntegratedName,
            Version = Clean(dsid.Product.Edition),
            IsUpdate = dsid.Product.IsUpdate,
            Title = header.Title,
            ProtectiveMarking = AmlProtectiveMarking.Normalize(marking),
            MarkingSource = source,
            IdoStatus = header.IdoStatus ?? security.IdoStatus,
            OwnerAuthority = header.OwnerAuthority ?? security.OwnerAuthority,
            Caveat = header.Caveat ?? security.Caveat,
            ScaleBand = AmlScaleBands.FromIntendedUsage(dsid.IntendedUsage) ?? fromName?.ScaleBand,
            FromName = fromName,
        };
    }

    private static (string? Title, string? Marking, string? IdoStatus, string? OwnerAuthority, string? Caveat) ReadHeader(string? comment)
    {
        if (!AmlDataSetMetadata.TryParse(comment, out var metadata))
            return default;

        // The UKHO cells quote their first pair ("title=…"; …), so the key is
        // read as "\"title" and the value keeps the closing quote.
        var title = metadata.Title;
        if (title is null)
        {
            foreach (var (key, value) in metadata.Entries)
            {
                if (string.Equals(key.Trim().Trim('"').Trim(), "title", StringComparison.OrdinalIgnoreCase))
                {
                    title = value;
                    break;
                }
            }
        }

        return (Clean(title), Clean(metadata.ProtectiveMarking), Clean(metadata.IdoStatus),
            Clean(metadata.OwnerAuthority), Clean(metadata.Caveat));
    }

    private static (string? Marking, string? IdoStatus, string? OwnerAuthority, string? Caveat) ReadSecurityMetaFeature(S57Document document)
    {
        var catalogue = document.GetObjectCatalogue();
        if (!catalogue.TryGetObjectClass(SecurityMetaFeatureAcronym, out var securityClass) || securityClass is null)
            return default;

        foreach (var feature in document.FeatureRecords)
        {
            if ((int)feature.ObjectCode != securityClass.Code)
                continue;

            string? marking = null, ido = null, owner = null, caveat = null;
            foreach (var attribute in feature.Attributes)
            {
                if (!catalogue.TryGetAttribute(attribute.AttributeCode, out var definition) || definition is null)
                    continue;

                switch (definition.Acronym)
                {
                    case ProtectiveMarkingAcronym:
                        marking ??= Decode(definition, attribute.Value);
                        break;
                    case IdoStatusAcronym:
                        ido ??= Decode(definition, attribute.Value);
                        break;
                    case OwnerAuthorityAcronym:
                        owner ??= Clean(attribute.Value);
                        break;
                    case CaveatAcronym:
                        caveat ??= Clean(attribute.Value);
                        break;
                }
            }

            return (marking, ido, owner, caveat);
        }

        return default;
    }

    private static string? Decode(S57AttributeDefinition definition, string? value)
    {
        var text = Clean(value);
        if (text is null)
            return null;

        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var code)
            && definition.TryGetValue(code, out var enumerated) && enumerated is not null
            ? enumerated.Name
            : text;
    }

    private static string? Clean(string? value)
    {
        var text = value?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
