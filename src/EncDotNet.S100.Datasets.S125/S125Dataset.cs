using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.S125;

/// <summary>
/// Root data model for an S-125 Marine Aids to Navigation dataset, parsed
/// from S-100 Part 10b GML encoding via <see cref="S125DatasetReader"/>.
/// </summary>
/// <remarks>
/// S-125 Edition 1.0.0 application schema namespace is
/// <c>http://www.iho.int/S125/1.0</c>; geometry uses the S-100 GML 5.0
/// profile namespace <c>http://www.iho.int/s100gml/5.0</c>. See the
/// S-125 1.0.0 product specification (Marine Aids to Navigation) and
/// Annex A Data Classification and Encoding Guide for the full feature
/// and attribute model.
/// </remarks>
public sealed class S125Dataset
{
    /// <summary>The product specification identifier (e.g. <c>"S-125"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>
    /// The declared product-specification edition (e.g. <c>"2.0.0"</c>) read
    /// from <c>DatasetIdentificationInformation/productEdition</c>, or
    /// <c>null</c> when the dataset declares none. S-100 Part 10b.
    /// </summary>
    public string? DeclaredEdition { get; init; }

    /// <summary>The dataset identifier (gml:id of the dataset root).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required IReadOnlyList<S125Feature> Features { get; init; }

    /// <summary>Information type instances contained in the dataset.</summary>
    public required IReadOnlyList<S125InformationType> InformationTypes { get; init; }

    /// <summary>Opens an S-125 dataset from a file path.</summary>
    public static S125Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S125DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-125 dataset from a stream.</summary>
    public static S125Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S125DatasetReader.Read(stream);
    }
}

/// <summary>
/// A geographic feature parsed from an S-125 GML dataset. Concrete feature
/// classes include AtoN structures (<c>Landmark</c>, <c>CardinalBuoy</c>,
/// <c>LateralBeacon</c>, …), aggregations (<c>AtonAggregation</c>,
/// <c>AtonAssociation</c>), and dataset metadata features such as
/// <c>DataCoverage</c>. See the S-125 Edition 1.0.0 Feature Catalogue for
/// the full set.
/// </summary>
public sealed class S125Feature : IS100Feature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (PascalCase, e.g. <c>"LateralBuoy"</c>).</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type.</summary>
    public S100GeometryType GeometryType { get; init; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    public IReadOnlyList<GeoPosition> Points { get; init; } = [];

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; init; } = [];

    /// <summary>Surface exterior ring coordinates.</summary>
    public IReadOnlyList<GeoPosition> ExteriorRing { get; init; } = [];

    /// <summary>Surface interior ring coordinates (holes).</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = [];

    /// <summary>Simple attributes keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups, each containing sub-attribute values.</summary>
    public required IReadOnlyList<IS100ComplexAttribute> ComplexAttributes { get; init; }


    /// <summary>
    /// Information-type association references (e.g. <c>AtoNStatus</c> bindings to
    /// <see cref="S125InformationType"/> instances). Preserved so XSLT portrayal
    /// rules can resolve cross-references.
    /// </summary>
    public required IReadOnlyList<S125InformationReference> InformationReferences { get; init; }

    /// <summary>
    /// Feature-to-feature association references (e.g. the <c>parent</c> role of
    /// the <c>StructureEquipment</c> association binding a light to its host
    /// beacon, or the <c>peerAtonAggregation</c> role on
    /// <c>AtonAggregation</c>). Each entry preserves the original role name and
    /// the raw <c>xlink:href</c> value so the strongly-typed projection (S-125
    /// Edition 1.0.0 §AtonStatusIndicationAssociation, §StructureEquipment,
    /// §AtonAggregations, §AtonAssociations, §PhysicalAIS, §SyntheticAIS,
    /// §VirtualAIS, §DangerousFeatureAssociation, §RangeSystem) can resolve
    /// them at projection time.
    /// </summary>
    public IReadOnlyList<S125FeatureReference> FeatureReferences { get; init; } =
        [];
}

/// <summary>
/// An information type instance parsed from an S-125 GML dataset
/// (e.g. <c>AtonStatusInformation</c>, <c>SpatialQuality</c>).
/// </summary>
public sealed class S125InformationType : IS100InformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (e.g. <c>"AtonStatusInformation"</c>).</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required IReadOnlyList<S125ComplexAttribute> ComplexAttributes { get; init; }
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S125ComplexAttribute : IS100ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required IReadOnlyDictionary<string, string> SubAttributes { get; init; }
}

/// <summary>
/// A reference from a feature to an information type instance,
/// represented in S-125 GML as a child element bearing an
/// <c>xlink:href</c> (or simple <c>informationRef</c>) attribute that
/// resolves to an <c>imember</c>'s gml:id.
/// </summary>
public sealed class S125InformationReference
{
    /// <summary>The role / association name (e.g. <c>"AtonStatus"</c>).</summary>
    public required string Role { get; init; }

    /// <summary>The referenced information type's gml:id (without leading <c>#</c>).</summary>
    public required string InformationRef { get; init; }
}

/// <summary>
/// A reference from a feature to another feature, captured from an
/// <c>xlink:href</c>-bearing child element whose target is a
/// <see cref="S125Feature"/> rather than an <see cref="S125InformationType"/>.
/// Examples include the <c>parent</c> / <c>child</c> roles of the
/// <c>StructureEquipment</c> feature association (S-125 Edition 1.0.0
/// §StructureEquipment), the <c>peerAtonAggregation</c> /
/// <c>atonAggregationBy</c> roles of <c>AtonAggregations</c>, and the AIS
/// broadcast association roles.
/// </summary>
public sealed class S125FeatureReference
{
    /// <summary>The role / association name as written in the source GML.</summary>
    public required string Role { get; init; }

    /// <summary>The referenced feature's gml:id (without leading <c>#</c>).</summary>
    public required string FeatureRef { get; init; }
}


