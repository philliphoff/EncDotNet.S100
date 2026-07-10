using EncDotNet.S100.DataModel;
namespace EncDotNet.S100.Features;

/// <summary>
/// Encoding-neutral shape shared by all S-100 feature instances,
/// regardless of how they are encoded on disk.
/// </summary>
/// <remarks>
/// Implemented by per-spec GML feature classes (e.g. <c>S124Feature</c>,
/// <c>S421Feature</c>) and by the generic vector-pipeline
/// <see cref="EncDotNet.S100.Pipelines.Vector.Feature"/> used by the
/// ISO 8211-encoded S-101 / S-57 path, so that generic pipeline
/// components (<see cref="EncDotNet.S100.Pipelines.Vector.FeatureGeometryProvider{TFeature}"/>,
/// shared FeatureXML builders, extent calculators) and the MCP query
/// tools can operate over any S-100 product without encoding-specific
/// coupling.
/// </remarks>
public interface IS100Feature
{
    /// <summary>The identifier of the feature (GML id, or decimal RCID for S-101).</summary>
    string Id { get; }

    /// <summary>The feature type code (the GML element local name, or the S-101 acronym).</summary>
    string FeatureType { get; }

    /// <summary>The geometry primitive type.</summary>
    S100GeometryType GeometryType { get; }

    /// <summary>Point geometries (latitude, longitude pairs).</summary>
    IReadOnlyList<GeoPosition> Points { get; }

    /// <summary>Curve geometries as ordered coordinate sequences.</summary>
    IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; }

    /// <summary>Surface exterior ring coordinates.</summary>
    IReadOnlyList<GeoPosition> ExteriorRing { get; }

    /// <summary>Surface interior ring coordinates (holes).</summary>
    IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; }

    /// <summary>Simple attributes keyed by code.</summary>
    IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>Complex (nested) attributes associated with the feature.</summary>
    IReadOnlyList<IS100ComplexAttribute> ComplexAttributes { get; }
}

/// <summary>
/// Encoding-neutral shape shared by all S-100 complex (nested) attribute
/// instances.
/// </summary>
public interface IS100ComplexAttribute
{
    /// <summary>The complex attribute code.</summary>
    string Code { get; }

    /// <summary>Sub-attribute values keyed by code.</summary>
    IReadOnlyDictionary<string, string> SubAttributes { get; }
}

/// <summary>
/// Encoding-neutral shape shared by all S-100 information type instances.
/// </summary>
public interface IS100InformationType
{
    /// <summary>The identifier.</summary>
    string Id { get; }

    /// <summary>The information type code.</summary>
    string TypeCode { get; }

    /// <summary>Simple attributes keyed by code.</summary>
    IReadOnlyDictionary<string, string> Attributes { get; }
}
