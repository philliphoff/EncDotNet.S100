using System.Collections.Immutable;

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
public sealed class S127Feature
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
    public S127GeometryType GeometryType { get; init; }

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
}

/// <summary>
/// An information type instance parsed from an S-127 GML dataset.
/// </summary>
public sealed class S127InformationType
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
public sealed class S127ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    public required string Code { get; init; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    public required ImmutableDictionary<string, string> SubAttributes { get; init; }
}

/// <summary>
/// The type of geometry associated with an S-127 feature.
/// </summary>
public enum S127GeometryType
{
    /// <summary>The feature has no associated geometry.</summary>
    None = 0,

    /// <summary>A single point.</summary>
    Point = 1,

    /// <summary>One or more curves (polylines).</summary>
    Curve = 2,

    /// <summary>A surface (polygon with optional holes).</summary>
    Surface = 3,
}
