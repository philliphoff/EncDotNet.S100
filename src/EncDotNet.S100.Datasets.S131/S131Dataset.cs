using System.Collections.Immutable;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S131;

/// <summary>
/// Root data model for an S-131 Marine Harbour Infrastructure dataset,
/// parsed from S-100 Part 10b GML encoding via <see cref="S131DatasetReader"/>.
/// </summary>
/// <remarks>
/// S-131 Edition 1.0.0 (FC) / 2.0.0 (PC) application schema namespace is
/// <c>http://www.iho.int/S131/1.0</c>; geometry uses the S-100 GML 5.0
/// profile namespace <c>http://www.iho.int/s100gml/5.0</c>. See the
/// S-131 Product Specification and its Data Classification and Encoding
/// Guide for the full feature and attribute model.
/// </remarks>
public sealed class S131Dataset
{
    /// <summary>The product specification identifier (e.g. <c>"S-131"</c>).</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier (gml:id of the dataset root).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required ImmutableArray<S131Feature> Features { get; init; }

    /// <summary>Information type instances contained in the dataset.</summary>
    public required ImmutableArray<S131InformationType> InformationTypes { get; init; }

    /// <summary>Opens an S-131 dataset from a file path.</summary>
    public static S131Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S131DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-131 dataset from a stream.</summary>
    public static S131Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S131DatasetReader.Read(stream);
    }
}

/// <summary>
/// A geographic feature parsed from an S-131 GML dataset. Concrete feature
/// types include harbour infrastructure such as <c>Berth</c>, <c>Bollard</c>,
/// <c>Terminal</c>, <c>DryDock</c>, <c>AnchorageArea</c>,
/// <c>HarbourAreaAdministrative</c>, and container features such as
/// <c>Authority</c>. See the S-131 Edition 1.0.0 Feature Catalogue for the
/// full set of 31 feature types.
/// </summary>
public sealed class S131Feature : IGmlFeature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>The feature type code (PascalCase, e.g. <c>"Berth"</c>).</summary>
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
    public required ImmutableArray<S131ComplexAttribute> ComplexAttributes { get; init; }

    /// <inheritdoc/>
    IEnumerable<IGmlComplexAttribute> IGmlFeature.GmlComplexAttributes => ComplexAttributes.Cast<IGmlComplexAttribute>();

    /// <summary>
    /// Information-type and feature-type association references resolved from
    /// <c>xlink:href</c> attributes. Used by the Lua data provider to service
    /// <c>HostFeatureGetAssociatedInformationIDs</c> and
    /// <c>HostFeatureGetAssociatedFeatureIDs</c> host calls.
    /// </summary>
    public required ImmutableArray<S131Reference> References { get; init; }
}

/// <summary>
/// An information type instance parsed from an S-131 GML dataset
/// (e.g. <c>ContactDetails</c>, <c>NauticalInformation</c>,
/// <c>Applicability</c>, <c>ServiceHours</c>).
/// </summary>
public sealed class S131InformationType : IGmlInformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code (e.g. <c>"ContactDetails"</c>).</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required ImmutableArray<S131ComplexAttribute> ComplexAttributes { get; init; }
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S131ComplexAttribute : IGmlComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }
}

/// <summary>
/// A reference from a feature or information type to another object within
/// the dataset, represented in S-131 GML as a child element bearing an
/// <c>xlink:href</c> attribute that resolves to a <c>gml:id</c>.
/// </summary>
public sealed class S131Reference
{
    /// <summary>The role / association name (e.g. <c>"theContactDetails"</c>).</summary>
    public required string Role { get; init; }

    /// <summary>The referenced object's gml:id (without leading <c>#</c>).</summary>
    public required string TargetRef { get; init; }
}
