using System.Collections.Immutable;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S201;

/// <summary>
/// Root data model for an S-201 Aids to Navigation Information dataset,
/// parsed from S-100 Part 10b GML encoding via <see cref="S201DatasetReader"/>.
/// </summary>
/// <remarks>
/// S-201 Edition 2.0.0 application schema namespace is
/// <c>http://www.iho.int/S-201/gml/cs0/1.0</c> and geometry uses the
/// S-100 GML 5.0 profile namespace
/// <c>http://www.iho.int/s100gml/5.0</c>. See the S-201 Edition 2.0.0
/// product specification (Aids to Navigation Information) and Annex A
/// Data Classification and Encoding Guide for the full feature and
/// attribute model.
/// </remarks>
public sealed class S201Dataset
{
    /// <summary>The product specification identifier (e.g. <c>"S-201"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier (gml:id of the dataset root).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required ImmutableArray<S201Feature> Features { get; init; }

    /// <summary>Information type instances contained in the dataset.</summary>
    public required ImmutableArray<S201InformationType> InformationTypes { get; init; }

    /// <summary>Opens an S-201 dataset from a file path.</summary>
    public static S201Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S201DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-201 dataset from a stream.</summary>
    public static S201Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S201DatasetReader.Read(stream);
    }

    /// <summary>
    /// Returns the features whose <see cref="S201Feature.Id"/> matches one
    /// of the targets referenced by <paramref name="feature"/> via the given
    /// <paramref name="role"/>. Used for resolving association xlinks such as
    /// equipment ↔ host structure (S-201 Edition 2.0.0 Annex A —
    /// <c>Structure/Equipment</c> association role) without forcing callers
    /// to walk the raw <see cref="S201Feature.FeatureReferences"/> list.
    /// </summary>
    /// <param name="feature">The source feature whose references to follow.</param>
    /// <param name="role">The xlink role / association name (e.g. <c>"theParentFeature"</c>).</param>
    public IEnumerable<S201Feature> ResolveReferencedFeatures(S201Feature feature, string role)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentException.ThrowIfNullOrEmpty(role);

        if (feature.FeatureReferences.IsDefaultOrEmpty) yield break;

        foreach (var reference in feature.FeatureReferences)
        {
            if (!string.Equals(reference.Role, role, StringComparison.Ordinal))
                continue;

            foreach (var target in Features)
            {
                if (string.Equals(target.Id, reference.TargetRef, StringComparison.Ordinal))
                    yield return target;
            }
        }
    }

    /// <summary>
    /// Returns the information types whose <see cref="S201InformationType.Id"/>
    /// matches one of the bindings on <paramref name="feature"/> via the
    /// given <paramref name="role"/>. Used to walk feature → information-type
    /// associations such as <c>AtoNStatus</c> (binding to
    /// <see cref="S201InformationType"/> instances of type
    /// <c>AtonStatusInformation</c>).
    /// </summary>
    public IEnumerable<S201InformationType> ResolveReferencedInformationTypes(S201Feature feature, string role)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentException.ThrowIfNullOrEmpty(role);

        if (feature.InformationReferences.IsDefaultOrEmpty) yield break;

        foreach (var reference in feature.InformationReferences)
        {
            if (!string.Equals(reference.Role, role, StringComparison.Ordinal))
                continue;

            foreach (var target in InformationTypes)
            {
                if (string.Equals(target.Id, reference.InformationRef, StringComparison.Ordinal))
                    yield return target;
            }
        }
    }
}

/// <summary>
/// A geographic feature parsed from an S-201 GML dataset. Concrete feature
/// classes include AtoN structures (<c>Lighthouse</c>, <c>Landmark</c>,
/// <c>CardinalBuoy</c>, <c>LateralBeacon</c>, …), equipment
/// (<c>LightSectored</c>, <c>FogSignal</c>, <c>RadarReflector</c>),
/// AIS aids (<c>VirtualAISAidToNavigation</c>,
/// <c>PhysicalAISAidToNavigation</c>, <c>SyntheticAISAidToNavigation</c>),
/// aggregations (<c>AtonAggregation</c>, <c>AtonAssociation</c>), and
/// dataset metadata features such as <c>DataCoverage</c>. See the S-201
/// Edition 2.0.0 Feature Catalogue for the full set.
/// </summary>
public sealed class S201Feature : IGmlFeature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (PascalCase, e.g. <c>"LateralBuoy"</c>).</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive type.</summary>
    public GmlGeometryType GeometryType { get; init; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    public ImmutableArray<(double Latitude, double Longitude)> Points { get; init; }

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    public ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> Curves { get; init; }

    /// <summary>Surface exterior ring coordinates.</summary>
    public ImmutableArray<(double Latitude, double Longitude)> ExteriorRing { get; init; }

    /// <summary>Surface interior ring coordinates (holes).</summary>
    public ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> InteriorRings { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups, each containing sub-attribute values.</summary>
    public required ImmutableArray<S201ComplexAttribute> ComplexAttributes { get; init; }

    /// <inheritdoc/>
    IEnumerable<IGmlComplexAttribute> IGmlFeature.GmlComplexAttributes => ComplexAttributes.Cast<IGmlComplexAttribute>();

    /// <summary>
    /// Information-type association references (e.g. <c>AtoNStatus</c>
    /// bindings to <see cref="S201InformationType"/> instances such as
    /// <c>AtonStatusInformation</c>, <c>PositioningInformation</c>,
    /// <c>SpatialQuality</c>, <c>AtoNFixingMethod</c>). Preserved so XSLT
    /// portrayal rules can resolve cross-references.
    /// </summary>
    public required ImmutableArray<S201InformationReference> InformationReferences { get; init; }

    /// <summary>
    /// Feature-to-feature association references (e.g.
    /// <c>theParentFeature</c> / <c>theSubordinateFeature</c> on the
    /// <c>Structure/Equipment</c> association — S-201 Edition 2.0.0
    /// Annex A). Preserved so callers and portrayal rules can navigate
    /// equipment-on-structure or aggregation relationships.
    /// </summary>
    public required ImmutableArray<S201FeatureReference> FeatureReferences { get; init; }
}

/// <summary>
/// An information type instance parsed from an S-201 GML dataset
/// (e.g. <c>AtonStatusInformation</c>, <c>PositioningInformation</c>,
/// <c>SpatialQuality</c>, <c>AtoNFixingMethod</c>).
/// </summary>
public sealed class S201InformationType : IGmlInformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (e.g. <c>"AtonStatusInformation"</c>).</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required ImmutableArray<S201ComplexAttribute> ComplexAttributes { get; init; }
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S201ComplexAttribute : IGmlComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }
}

/// <summary>
/// A reference from a feature to an information type instance,
/// represented in S-201 GML as a child element bearing an
/// <c>xlink:href</c> attribute that resolves to an information type's
/// gml:id (e.g. <c>AtoNStatus</c>, <c>Positioning</c>,
/// <c>AtoNFixingMethod</c>, <c>SpatialQuality</c>).
/// </summary>
public sealed class S201InformationReference
{
    /// <summary>The role / association name (e.g. <c>"AtoNStatus"</c>).</summary>
    public required string Role { get; init; }

    /// <summary>The referenced information type's gml:id (without leading <c>#</c>).</summary>
    public required string InformationRef { get; init; }
}

/// <summary>
/// A reference from a feature to another feature, represented in S-201
/// GML as a child element bearing an <c>xlink:href</c> attribute that
/// resolves to a feature's gml:id (e.g. <c>theParentFeature</c>,
/// <c>theSubordinateFeature</c>, <c>peer</c>). Used for the S-201
/// <c>Structure/Equipment</c> aggregation, <c>Aggregations</c>, and
/// <c>Associations</c> roles defined in the Edition 2.0.0 Feature
/// Catalogue.
/// </summary>
public sealed class S201FeatureReference
{
    /// <summary>The role / association name (e.g. <c>"theParentFeature"</c>).</summary>
    public required string Role { get; init; }

    /// <summary>The referenced feature's gml:id (without leading <c>#</c>).</summary>
    public required string TargetRef { get; init; }
}
