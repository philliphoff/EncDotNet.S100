using System.Collections.Immutable;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S127;

/// <summary>
/// Root data model for an S-127 Marine Resources and Services dataset
/// (IHO S-127 Edition 2.0.0, S-100 Part 10b GML encoding).
/// </summary>
/// <remarks>
/// S-127 carries marine traffic-management features such as pilot
/// boarding places, routeing measures, restricted areas, vessel
/// traffic services, and signal stations. See S-127 Edition 2.0.0
/// §1 (Overview) for product scope.
/// </remarks>
public sealed class S127Dataset
{
    /// <summary>The product specification identifier (e.g. "S-127").</summary>
    public string? ProductIdentifier { get; init; }

    /// <summary>The dataset identifier (the GML <c>gml:id</c> of the root element).</summary>
    public string? DatasetIdentifier { get; init; }

    /// <summary>Feature instances contained in the dataset.</summary>
    public required ImmutableArray<S127Feature> Features { get; init; }

    /// <summary>
    /// Information type instances contained in the dataset.
    /// S-127 Edition 2.0.0 currently has no information types defined,
    /// but the parser preserves any <c>imember</c> children for
    /// forward compatibility with future editions.
    /// </summary>
    public required ImmutableArray<S127InformationType> InformationTypes { get; init; }

    /// <summary>Opens an S-127 dataset from a file path.</summary>
    public static S127Dataset Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = File.OpenRead(path);
        return S127DatasetReader.Read(stream);
    }

    /// <summary>Opens an S-127 dataset from a stream.</summary>
    public static S127Dataset Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return S127DatasetReader.Read(stream);
    }
}

/// <summary>
/// A geographic feature parsed from an S-127 GML dataset.
/// </summary>
public sealed class S127Feature : IGmlFeature
{
    /// <summary>The GML identifier of the feature.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The feature type code as it appears in the GML (e.g.
    /// <c>PilotBoardingPlace</c>, <c>RouteingMeasure</c>,
    /// <c>RestrictedArea</c>). Drives XSLT template selection in
    /// the bundled portrayal catalogue.
    /// </summary>
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

    /// <summary>Complex attribute groups keyed by code, each containing sub-attribute dictionaries.</summary>
    public required ImmutableArray<S127ComplexAttribute> ComplexAttributes { get; init; }

    /// <summary>
    /// Feature-to-feature association references captured from
    /// <c>xlink:href</c>-bearing child elements (e.g. the
    /// <c>theAuthority</c> role binding a service to an
    /// <c>Authority</c> feature — S-127 Edition 2.0.0 §12). Each entry
    /// preserves the source role name and the raw <c>xlink:href</c> with
    /// the leading <c>#</c> stripped, so the strongly-typed projection
    /// can resolve them through <see cref="EncDotNet.S100.DataModel.XlinkResolver"/>.
    /// </summary>
    public ImmutableArray<S127FeatureReference> FeatureReferences { get; init; } =
        ImmutableArray<S127FeatureReference>.Empty;

    /// <inheritdoc/>
    IEnumerable<IGmlComplexAttribute> IGmlFeature.GmlComplexAttributes => ComplexAttributes.Cast<IGmlComplexAttribute>();
}

/// <summary>
/// An information type instance parsed from an S-127 GML dataset.
/// </summary>
public sealed class S127InformationType : IGmlInformationType
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The information type code.</summary>
    public required string TypeCode { get; init; }

    /// <summary>Simple attributes keyed by code.</summary>
    public required ImmutableDictionary<string, string> Attributes { get; init; }

    /// <summary>Complex attribute groups.</summary>
    public required ImmutableArray<S127ComplexAttribute> ComplexAttributes { get; init; }
}

/// <summary>
/// A complex attribute instance containing sub-attributes.
/// </summary>
public sealed class S127ComplexAttribute : IGmlComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }
}

/// <summary>
/// A reference from an <see cref="S127Feature"/> to another
/// <see cref="S127Feature"/>, captured from an <c>xlink:href</c>-bearing
/// child element. Examples include the <c>theAuthority</c> role binding
/// services such as <c>VesselTrafficServiceArea</c> or
/// <c>PilotBoardingPlace</c> to their administering <c>Authority</c>
/// (S-127 Edition 2.0.0 §12).
/// </summary>
public sealed class S127FeatureReference
{
    /// <summary>The role / association name as written in the source GML.</summary>
    public required string Role { get; init; }

    /// <summary>The referenced feature's <c>gml:id</c> (with any leading <c>#</c> stripped).</summary>
    public required string FeatureRef { get; init; }
}


