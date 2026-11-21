using System.Globalization;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S57.Aml;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// How a Library item records the AML facts of a NATO Additional Military
/// Layers cell in its <see cref="CollectionItem.Properties"/>, and reads
/// them back, so the Library and the MCP tools describe an AML item without
/// opening the cell.
/// </summary>
/// <remarks>
/// AML items have <see cref="CollectionItem.ProductSpec"/>
/// <see cref="AmlDatasetInfo.SpecName"/> (<c>AML</c>). Every key starts with
/// <c>aml</c>; empty facts aren't written.
/// </remarks>
public static class AmlLibraryProperties
{
    /// <summary>The <c>DSID</c> <c>PRSP</c> code, 51–57.</summary>
    public const string ProductSpecification = "amlProductSpecification";

    /// <summary>The product code (<c>CLB</c>), when there is one.</summary>
    public const string Product = "amlProduct";

    /// <summary>The product's full name.</summary>
    public const string ProductName = "amlProductName";

    /// <summary>The product specification edition (<c>1.0</c>, <c>3.0</c>).</summary>
    public const string Version = "amlVersion";

    /// <summary>The cell title.</summary>
    public const string Title = "amlTitle";

    /// <summary>The protective marking.</summary>
    public const string ProtectiveMarking = "amlProtectiveMarking";

    /// <summary>Where the marking came from: <c>header</c>, <c>m_clas</c> or <c>cell-name</c>.</summary>
    public const string MarkingSource = "amlMarkingSource";

    /// <summary>The IDO status.</summary>
    public const string IdoStatus = "amlIdoStatus";

    /// <summary>The owner authority.</summary>
    public const string OwnerAuthority = "amlOwnerAuthority";

    /// <summary>The handling caveat.</summary>
    public const string Caveat = "amlCaveat";

    /// <summary>The AML scale band, 0–9.</summary>
    public const string ScaleBand = "amlScaleBand";

    /// <summary>Whether it's an update cell (<c>true</c>), written only when it is.</summary>
    public const string IsUpdate = "amlUpdate";

    /// <summary>The marking source names, as written to <see cref="MarkingSource"/>.</summary>
    public static string MarkingSourceName(AmlMarkingSource source) => source switch
    {
        AmlMarkingSource.Header => "header",
        AmlMarkingSource.SecurityMetaFeature => "m_clas",
        AmlMarkingSource.CellName => "cell-name",
        _ => "none",
    };

    /// <summary>Whether <paramref name="key"/> is one of the AML property keys.</summary>
    /// <param name="key">A property key.</param>
    public static bool IsAmlKey(string key) =>
        key.StartsWith("aml", StringComparison.Ordinal) && key.Length > 3 && char.IsUpper(key[3]);

    /// <summary>Writes the AML facts of <paramref name="info"/> to <paramref name="properties"/>.</summary>
    /// <param name="info">The cell's AML facts.</param>
    /// <param name="properties">The item's properties.</param>
    public static void Write(AmlDatasetInfo info, IDictionary<string, string> properties)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(properties);

        properties[ProductSpecification] = info.ProductSpecification.ToString(CultureInfo.InvariantCulture);
        Set(Product, info.ProductCode);
        Set(ProductName, info.ProductName);
        Set(Version, info.Version);
        Set(Title, info.Title);
        Set(ProtectiveMarking, info.ProtectiveMarking);
        if (info.ProtectiveMarking is not null)
            properties[MarkingSource] = MarkingSourceName(info.MarkingSource);
        Set(IdoStatus, info.IdoStatus);
        Set(OwnerAuthority, info.OwnerAuthority);
        Set(Caveat, info.Caveat);
        Set(ScaleBand, info.ScaleBand?.ToString(CultureInfo.InvariantCulture));
        if (info.IsUpdate)
            properties[IsUpdate] = "true";

        void Set(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                properties[key] = value;
        }
    }

    /// <summary>
    /// Reads the AML facts back from an item's properties.
    /// </summary>
    /// <param name="properties">The item's properties.</param>
    /// <param name="name">The item's name, decoded again for <see cref="AmlDatasetInfo.FromName"/>.</param>
    /// <returns>The facts, or <see langword="null"/> when the properties carry no AML product.</returns>
    public static AmlDatasetInfo? Read(IReadOnlyDictionary<string, string> properties, string? name)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!properties.TryGetValue(ProductSpecification, out var prsp)
            || !int.TryParse(prsp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            || !AmlProducts.IsAmlProductSpecification(code))
        {
            return null;
        }

        AmlCellName.TryParse(name, out var fromName);
        return new AmlDatasetInfo
        {
            DataSetName = name ?? string.Empty,
            Product = (AmlProduct)code,
            ProductSpecification = code,
            ProductCode = Get(Product),
            ProductName = Get(ProductName) ?? AmlProducts.IntegratedName,
            Version = Get(Version),
            IsUpdate = Get(IsUpdate) == "true",
            Title = Get(Title),
            ProtectiveMarking = Get(ProtectiveMarking),
            MarkingSource = Get(MarkingSource) switch
            {
                "header" => AmlMarkingSource.Header,
                "m_clas" => AmlMarkingSource.SecurityMetaFeature,
                "cell-name" => AmlMarkingSource.CellName,
                _ => AmlMarkingSource.None,
            },
            IdoStatus = Get(IdoStatus),
            OwnerAuthority = Get(OwnerAuthority),
            Caveat = Get(Caveat),
            ScaleBand = int.TryParse(Get(ScaleBand), NumberStyles.Integer, CultureInfo.InvariantCulture, out var band) ? band : null,
            FromName = fromName,
        };

        string? Get(string key) => properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}
