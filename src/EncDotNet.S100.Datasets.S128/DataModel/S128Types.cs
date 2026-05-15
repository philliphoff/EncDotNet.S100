using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S128.DataModel;

/// <summary>
/// The geometry primitive kind of an S-128 catalogue entry.
/// </summary>
public enum S128GeometryKind
{
    /// <summary>No geometry. Common for container / metadata features (S-128 § 12).</summary>
    None,

    /// <summary>Single point (<see cref="GeoPosition"/>).</summary>
    Point,

    /// <summary>Curve (ordered sequence of <see cref="GeoPosition"/>).</summary>
    Curve,

    /// <summary>Surface exterior ring (closed sequence of <see cref="GeoPosition"/>).</summary>
    Surface,
}

/// <summary>
/// Classification of an S-128 <c>ProductMapping/categoryOfProductMapping</c>
/// value (S-128 § 12).
/// </summary>
/// <remarks>
/// <para>
/// S-128 Edition 2.0.0's published <c>categoryOfProductMapping</c>
/// enumeration carries a single defined code: <c>1</c> — "Higher Priority
/// Alternative" — which is the carrier the official sample uses to encode
/// supersession. Future enumeration values fall through to
/// <see cref="Other"/> without breaking the projection; the raw text is
/// preserved on <see cref="S128ProductReference.RawCategoryText"/>.
/// </para>
/// </remarks>
public enum S128ProductMappingCategory
{
    /// <summary>The mapping carries no recognisable category value.</summary>
    Unknown = 0,

    /// <summary>
    /// S-128 § 12 code <c>1</c> — Higher Priority Alternative. The
    /// referring product supersedes the referenced product.
    /// </summary>
    HigherPriorityAlternative,

    /// <summary>A category code outside the recognised set; raw text preserved.</summary>
    Other,
}

/// <summary>
/// A typed product cross-reference resolved from an S-128
/// <c>theReference</c> xlink and its companion <c>ProductMapping</c>
/// complex attribute (S-128 § 12).
/// </summary>
/// <param name="Target">The resolved catalogue entry the xlink points to.</param>
/// <param name="Category">Classified mapping category.</param>
/// <param name="RawCategoryText">The raw text content of <c>categoryOfProductMapping</c>, when present.</param>
public sealed record S128ProductReference(
    S128CatalogueEntry Target,
    S128ProductMappingCategory Category,
    string? RawCategoryText);

/// <summary>
/// A typed online-resource record, projected from the S-128
/// <c>onlineResource</c> complex attribute (S-128 § 12).
/// </summary>
public sealed record S128OnlineResource
{
    /// <summary>Application profile (e.g. <c>"Kinds of publications and Information Summary"</c>).</summary>
    public string? ApplicationProfile { get; init; }

    /// <summary>The linkage URL.</summary>
    public string? Linkage { get; init; }
}

/// <summary>
/// Abstract base for the three S-128 product feature classes
/// (<c>ElectronicProduct</c>, <c>PhysicalProduct</c>, <c>S100Service</c>).
/// </summary>
/// <remarks>
/// The strongly-typed projection of an <see cref="S128Dataset"/> exposes
/// products through this base type so that callers can iterate the
/// catalogue uniformly without pattern matching on the concrete subclass.
/// Use <see cref="FeatureType"/> or runtime type tests when product-class
/// dispatch is required.
/// </remarks>
public abstract class S128CatalogueEntry
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>The product feature class (<c>ElectronicProduct</c> / <c>PhysicalProduct</c> / <c>S100Service</c>).</summary>
    public required string FeatureType { get; init; }

    /// <summary>The product number / dataset name carried by the feature (S-128 § 12 attribute <c>productNumber</c>, falling back to <c>datasetName</c>).</summary>
    public string? ProductNumber { get; init; }

    /// <summary>The product edition number (S-128 § 12 attribute <c>editionNumber</c>).</summary>
    public int? EditionNumber { get; init; }

    /// <summary>The product update number (S-128 § 12 attribute <c>updateNumber</c>).</summary>
    public int? UpdateNumber { get; init; }

    /// <summary>The product issue date (S-128 § 12 attribute <c>issueDate</c>).</summary>
    public DateTimeOffset? IssueDate { get; init; }

    /// <summary>The product update / edition date (<c>updateDate</c> falling back to <c>editionDate</c>).</summary>
    public DateTimeOffset? UpdateDate { get; init; }

    /// <summary>The catalogue element classification text (S-128 § 12 <c>catalogueElementClassification</c>).</summary>
    public string? Classification { get; init; }

    /// <summary>Whether the feature carries <c>notForNavigation="true"</c>.</summary>
    public bool? NotForNavigation { get; init; }

    /// <summary>Referenced product specification name (e.g. <c>"S-101"</c>, <c>"S-104"</c>) from <c>productSpecification/name</c>.</summary>
    public string? ProductSpecificationName { get; init; }

    /// <summary>Referenced product specification version (from <c>productSpecification/version</c>).</summary>
    public string? ProductSpecificationVersion { get; init; }

    /// <summary>Geometry primitive kind of the source feature.</summary>
    public S128GeometryKind GeometryKind { get; init; }

    /// <summary>Coordinates of the source feature (semantics depend on <see cref="GeometryKind"/>).</summary>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <summary>Online-resource records projected from <c>onlineResource</c> complex attributes.</summary>
    public ImmutableArray<S128OnlineResource> OnlineResources { get; init; } = ImmutableArray<S128OnlineResource>.Empty;

    /// <summary>
    /// Resolved targets that this entry supersedes — populated from
    /// <c>theReference</c> xlinks whose companion
    /// <c>ProductMapping/categoryOfProductMapping</c> classifies as
    /// <see cref="S128ProductMappingCategory.HigherPriorityAlternative"/>
    /// (S-128 § 12).
    /// </summary>
    public ImmutableArray<S128CatalogueEntry> Supersedes { get; internal set; } =
        ImmutableArray<S128CatalogueEntry>.Empty;

    /// <summary>
    /// Resolved entries that supersede this one — the inverse traversal of
    /// <see cref="Supersedes"/>.
    /// </summary>
    public ImmutableArray<S128CatalogueEntry> SupersededBy { get; internal set; } =
        ImmutableArray<S128CatalogueEntry>.Empty;

    /// <summary>
    /// All <c>theReference</c> cross-references whose mapping category did
    /// **not** classify as supersession. Raw category text is preserved so
    /// callers can dispatch on future enumeration extensions without an
    /// enum revision (S-128 § 12).
    /// </summary>
    public ImmutableArray<S128ProductReference> RelatedProducts { get; internal set; } =
        ImmutableArray<S128ProductReference>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>The originating parsed feature.</summary>
    public required S128Feature Source { get; init; }
}

/// <summary>
/// Typed projection of an S-128 <c>ElectronicProduct</c> feature
/// (S-128 § 12) — a digital nautical product such as an ENC or HDF5 cell.
/// </summary>
public sealed class S128ElectronicProduct : S128CatalogueEntry
{
}

/// <summary>
/// Typed projection of an S-128 <c>PhysicalProduct</c> feature
/// (S-128 § 12) — a paper chart or printed publication.
/// </summary>
public sealed class S128PhysicalProduct : S128CatalogueEntry
{
}

/// <summary>
/// Typed projection of an S-128 <c>S100Service</c> feature (S-128 § 12)
/// — a discoverable S-100 service endpoint (e.g. SECOM-served S-104).
/// </summary>
public sealed class S128Service : S128CatalogueEntry
{
    /// <summary>
    /// Service status classified from S-128 § 12 attribute
    /// <c>serviceStatus</c>.
    /// </summary>
    public S128ServiceStatus ServiceStatus { get; init; } = S128ServiceStatus.Unknown;
}

/// <summary>
/// Service-lifecycle status of an <see cref="S128Service"/>
/// (S-128 § 12 <c>serviceStatus</c>).
/// </summary>
public enum S128ServiceStatus
{
    /// <summary>The attribute is absent or unrecognised.</summary>
    Unknown = 0,

    /// <summary>S-128 § 12 code <c>1</c> — Planned.</summary>
    Planned,

    /// <summary>S-128 § 12 code <c>2</c> — Released (in force).</summary>
    Released,

    /// <summary>S-128 § 12 code <c>3</c> — Withdrawn.</summary>
    Withdrawn,
}

/// <summary>
/// Typed projection of an S-128 <c>ProducerInformation</c> feature
/// (S-128 § 12).
/// </summary>
public sealed class S128ProducerInformation
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The agency responsible for production, when supplied.</summary>
    public string? AgencyResponsibleForProduction { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>The originating parsed feature.</summary>
    public required S128Feature Source { get; init; }
}

/// <summary>
/// Typed projection of an S-128 <c>DistributorInformation</c> feature
/// (S-128 § 12).
/// </summary>
public sealed class S128DistributorInformation
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The distributor name, when supplied.</summary>
    public string? DistributorName { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>The originating parsed feature.</summary>
    public required S128Feature Source { get; init; }
}

/// <summary>
/// Typed projection of an S-128 <c>ContactDetails</c> feature
/// (S-128 § 12).
/// </summary>
public sealed class S128ContactDetails
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Free-text contact instructions, when supplied.</summary>
    public string? ContactInstructions { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>The originating parsed feature.</summary>
    public required S128Feature Source { get; init; }
}

/// <summary>
/// Typed projection of an S-128 <c>CatalogueSectionHeader</c> feature
/// (S-128 § 12) — a logical grouping marker inside the catalogue.
/// </summary>
public sealed class S128CatalogueSectionHeader
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The catalogue section number, when supplied.</summary>
    public string? CatalogueSectionNumber { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>The originating parsed feature.</summary>
    public required S128Feature Source { get; init; }
}
