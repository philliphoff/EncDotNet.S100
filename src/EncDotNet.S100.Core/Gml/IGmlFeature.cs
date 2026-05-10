using System.Collections.Immutable;

namespace EncDotNet.S100.Gml;

/// <summary>
/// Common shape shared by all GML-encoded S-100 feature types.
/// </summary>
/// <remarks>
/// Implemented by per-spec feature classes (e.g. <c>S124Feature</c>,
/// <c>S421Feature</c>) so that generic pipeline components
/// (<see cref="GmlFeatureGeometryProvider{TFeature}"/>, shared
/// FeatureXML builders, extent calculators) can operate over any
/// GML-encoded product without spec-specific coupling.
/// </remarks>
public interface IGmlFeature
{
    /// <summary>The GML identifier of the feature.</summary>
    string Id { get; }

    /// <summary>The feature type code (the GML element local name).</summary>
    string FeatureType { get; }

    /// <summary>The geometry primitive type.</summary>
    GmlGeometryType GeometryType { get; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    ImmutableArray<(double Latitude, double Longitude)> Points { get; }

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> Curves { get; }

    /// <summary>Surface exterior ring coordinates.</summary>
    ImmutableArray<(double Latitude, double Longitude)> ExteriorRing { get; }

    /// <summary>Surface interior ring coordinates (holes).</summary>
    ImmutableArray<ImmutableArray<(double Latitude, double Longitude)>> InteriorRings { get; }

    /// <summary>Simple attributes keyed by code.</summary>
    ImmutableDictionary<string, string> Attributes { get; }

    /// <summary>Complex attributes associated with the feature.</summary>
    IEnumerable<IGmlComplexAttribute> GmlComplexAttributes { get; }
}

/// <summary>
/// Common shape shared by all GML-encoded S-100 complex attribute types.
/// </summary>
public interface IGmlComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    string Code { get; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    ImmutableDictionary<string, string> SubAttributes { get; }
}

/// <summary>
/// Common shape shared by all GML-encoded S-100 information type instances.
/// </summary>
public interface IGmlInformationType
{
    /// <summary>The GML identifier.</summary>
    string Id { get; }

    /// <summary>The information type code.</summary>
    string TypeCode { get; }

    /// <summary>Simple attributes keyed by code.</summary>
    ImmutableDictionary<string, string> Attributes { get; }
}
