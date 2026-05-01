using System.Collections.Immutable;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// A strongly-typed façade over a navigational-product <see cref="S128Feature"/>
/// (one of <c>ElectronicProduct</c>, <c>PhysicalProduct</c>, <c>S100Service</c>).
/// </summary>
/// <remarks>
/// <para>
/// S-128 catalogues describe individual nautical products. This façade exposes
/// the most commonly queried attributes — product number, edition, currency
/// status, coverage geometry, and outgoing references — without forcing
/// callers to traverse the underlying <see cref="S128Feature"/> directly.
/// </para>
/// <para>
/// Currency status is heuristic: S-128 2.0.0 has no single <c>status</c>
/// enumeration on a product feature, so <see cref="Status"/> is derived
/// from <c>serviceStatus</c> (S100Service), the presence of a
/// <c>theReference</c> arcrole indicating supersession, and edition/update
/// dates. See the project README for the full heuristic.
/// </para>
/// </remarks>
public sealed class S128ProductEntry
{
    /// <summary>The set of FC feature-type codes that represent navigational products.</summary>
    public static IReadOnlySet<string> ProductFeatureTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ElectronicProduct",
            "PhysicalProduct",
            "S100Service",
        };

    /// <summary>Returns true if the given feature is a catalogue product entry.</summary>
    public static bool IsProductFeature(S128Feature feature) =>
        ProductFeatureTypes.Contains(feature.FeatureType);

    /// <summary>The underlying parsed feature.</summary>
    public S128Feature Feature { get; }

    /// <summary>
    /// Initializes a new entry over the given product-class feature.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="feature"/> is not one of the navigational product feature types.
    /// </exception>
    public S128ProductEntry(S128Feature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (!IsProductFeature(feature))
            throw new ArgumentException(
                $"Feature type '{feature.FeatureType}' is not a navigational product.",
                nameof(feature));
        Feature = feature;
    }

    /// <summary>The feature's <c>gml:id</c>.</summary>
    public string Id => Feature.Id;

    /// <summary>
    /// The feature class (<c>ElectronicProduct</c> | <c>PhysicalProduct</c> |
    /// <c>S100Service</c>).
    /// </summary>
    public string FeatureType => Feature.FeatureType;

    /// <summary>
    /// The product number or dataset name carried by the feature
    /// (<c>productNumber</c> if present, otherwise <c>datasetName</c>).
    /// </summary>
    public string? ProductNumber =>
        Feature.Attributes.TryGetValue("productNumber", out var pn) ? pn :
        Feature.Attributes.TryGetValue("datasetName", out var dn) ? dn : null;

    /// <summary>The product edition number (S-128 § 12 attribute <c>editionNumber</c>).</summary>
    public string? EditionNumber =>
        Feature.Attributes.TryGetValue("editionNumber", out var v) ? v : null;

    /// <summary>The product update number (S-128 § 12 attribute <c>updateNumber</c>).</summary>
    public string? UpdateNumber =>
        Feature.Attributes.TryGetValue("updateNumber", out var v) ? v : null;

    /// <summary>The product issue date (ISO 8601).</summary>
    public string? IssueDate =>
        Feature.Attributes.TryGetValue("issueDate", out var v) ? v : null;

    /// <summary>The product update / edition date.</summary>
    public string? UpdateDate =>
        Feature.Attributes.TryGetValue("updateDate", out var u) ? u :
        Feature.Attributes.TryGetValue("editionDate", out var e) ? e : null;

    /// <summary>
    /// The referenced product specification (e.g. <c>"S-101"</c>, <c>"S-104"</c>,
    /// <c>"S-57"</c>) parsed from the <c>productSpecification/name</c>
    /// complex sub-attribute.
    /// </summary>
    public string? ProductSpecificationName
    {
        get
        {
            var ps = Feature.ComplexAttributes
                .FirstOrDefault(c => c.Code.Equals("productSpecification", StringComparison.OrdinalIgnoreCase));
            if (ps is null) return null;
            return ps.SubAttributes.TryGetValue("name", out var n) ? n : null;
        }
    }

    /// <summary>The catalogue element classification text (S-128 § 12 <c>catalogueElementClassification</c>).</summary>
    public string? Classification =>
        Feature.Attributes.TryGetValue("catalogueElementClassification", out var v) ? v : null;

    /// <summary>True when the feature carries <c>notForNavigation = "true"</c>.</summary>
    public bool NotForNavigation =>
        Feature.Attributes.TryGetValue("notForNavigation", out var v) &&
        v.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Coverage exterior ring of the entry's first surface geometry, or an
    /// empty array if the feature is geometry-less.
    /// </summary>
    public ImmutableArray<(double Latitude, double Longitude)> CoverageRing =>
        Feature.ExteriorRing.IsDefault ? [] : Feature.ExteriorRing;

    /// <summary>
    /// Outgoing references that mark this entry as superseded by another
    /// (<c>theReference</c> with arcrole containing <c>"theReference"</c> and
    /// a <c>ProductMapping/categoryOfProductMapping</c> of <c>1</c>).
    /// </summary>
    /// <remarks>
    /// In the IHO 2.0.0 sample the link is encoded inline as
    /// <c>&lt;S128:theReference xlink:href="#ID0002" .../&gt;&lt;ProductMapping&gt;...&lt;/ProductMapping&gt;</c>;
    /// we surface the href list and leave caller-side mapping interpretation
    /// to <see cref="S128Catalogue.Resolve"/>.
    /// </remarks>
    public IEnumerable<S128XlinkReference> ReferencedProducts =>
        Feature.References.Where(r =>
            r.Role.Equals("theReference", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Heuristic currency status of the product. See type remarks.
    /// </summary>
    public S128ProductStatus Status
    {
        get
        {
            // Explicit serviceStatus on S100Service: 1 Planned / 2 Released
            // (Withdrawn) per S-128 § 12.
            if (Feature.Attributes.TryGetValue("serviceStatus", out var svc))
            {
                if (svc.Contains("Released", StringComparison.OrdinalIgnoreCase) || svc == "2")
                    return S128ProductStatus.InForce;
                if (svc.Contains("Withdrawn", StringComparison.OrdinalIgnoreCase) || svc == "3")
                    return S128ProductStatus.Withdrawn;
                if (svc.Contains("Planned", StringComparison.OrdinalIgnoreCase) || svc == "1")
                    return S128ProductStatus.Planned;
            }

            // distributionStatus 1 = Available, 2 = Cancelled (S-128 § 12).
            if (Feature.Attributes.TryGetValue("distributionStatus", out var dist))
            {
                if (dist.Contains("Cancelled", StringComparison.OrdinalIgnoreCase) || dist == "2")
                    return S128ProductStatus.Withdrawn;
                if (dist.Contains("Available", StringComparison.OrdinalIgnoreCase) || dist == "1")
                    return S128ProductStatus.InForce;
            }

            return S128ProductStatus.InForce;
        }
    }
}

/// <summary>Heuristic currency status for an <see cref="S128ProductEntry"/>.</summary>
public enum S128ProductStatus
{
    /// <summary>Status could not be determined from the available metadata.</summary>
    Unknown = 0,
    /// <summary>The product is current (default when no contradicting attribute is present).</summary>
    InForce,
    /// <summary>The product has been replaced by a newer edition (resolved via <c>theReference</c>).</summary>
    Superseded,
    /// <summary>The product has been withdrawn or cancelled.</summary>
    Withdrawn,
    /// <summary>The product is announced but not yet released.</summary>
    Planned,
}
