using System.Collections.ObjectModel;
using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Abstracts read access to a vector (feature) dataset for pipeline consumption.
/// Product-specific adapters (e.g. S101VectorSource) implement this interface.
/// </summary>
public interface IVectorSource
{
    /// <summary>Metadata available immediately after opening the dataset.</summary>
    VectorMetadata Metadata { get; }

    /// <summary>
    /// Returns all features in the dataset that intersect the given extent.
    /// Pass <c>null</c> to retrieve all features.
    /// </summary>
    IReadOnlyList<Feature> GetFeatures(BoundingBox? extent = null);
}

/// <summary>
/// Dataset-level metadata for a vector source.
/// </summary>
public sealed class VectorMetadata
{
    /// <summary>The product specification (name + edition) this dataset declares conformance to.</summary>
    public required SpecRef Spec { get; init; }
    public required BoundingBox Extent { get; init; }
    public required string HorizontalCRS { get; init; }
    public required int CompilationScaleDenominator { get; init; }
}

/// <summary>
/// A single geographic feature read from a vector dataset.
/// </summary>
/// <remarks>
/// Implements the encoding-neutral <see cref="IS100Feature"/> shape so that
/// generic pipeline components (e.g. <see cref="FeatureGeometryProvider{TFeature}"/>)
/// and the MCP query tools can consume ISO 8211-encoded S-101 / S-57 features
/// alongside the GML-encoded products without an intermediate adapter. The
/// single <see cref="Coordinates"/> / <see cref="GeometryType"/> pair is mapped
/// onto the interface's <c>Points</c> / <c>Curves</c> / <c>ExteriorRing</c>
/// buckets on demand.
/// </remarks>
public sealed class Feature : IS100Feature
{
    /// <summary>Feature type code (e.g. "DepthArea", "LandArea", "Buoy").</summary>
    public required string FeatureType { get; init; }

    /// <summary>Feature record identifier within the dataset.</summary>
    public required long Id { get; init; }

    /// <summary>Geometric primitive type.</summary>
    public required GeometryType GeometryType { get; init; }

    /// <summary>
    /// Geometry coordinates.
    /// Points: single coordinate. Lines/Areas: ordered list of (lat, lon) pairs.
    /// For surfaces this is the exterior ring; holes are carried in <see cref="InteriorRings"/>.
    /// </summary>
    public required IReadOnlyList<GeoPosition> Coordinates { get; init; }

    /// <summary>
    /// Interior (hole) rings for surface geometries, each an ordered list of
    /// (lat, lon) pairs. Empty for non-surface geometries or surfaces without holes.
    /// Renderers subtract these from the exterior ring (<see cref="Coordinates"/>)
    /// when filling, so that, for example, a sea/depth area encoded around islands
    /// does not paint over the land cut out as holes.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = [];

    /// <summary>Feature attribute values keyed by attribute code.</summary>
    public required IReadOnlyDictionary<string, object?> Attributes { get; init; }

    string IS100Feature.Id => Id.ToString(CultureInfo.InvariantCulture);

    string IS100Feature.FeatureType => FeatureType;

    S100GeometryType IS100Feature.GeometryType => GeometryType switch
    {
        GeometryType.Point => S100GeometryType.Point,
        GeometryType.Curve => S100GeometryType.Curve,
        GeometryType.Surface => S100GeometryType.Surface,
        _ => S100GeometryType.None,
    };

    IReadOnlyList<GeoPosition> IS100Feature.Points =>
        GeometryType == GeometryType.Point ? Coordinates : [];

    IReadOnlyList<IReadOnlyList<GeoPosition>> IS100Feature.Curves =>
        GeometryType == GeometryType.Curve && Coordinates.Count > 0
            ? [Coordinates]
            : [];

    IReadOnlyList<GeoPosition> IS100Feature.ExteriorRing =>
        GeometryType == GeometryType.Surface ? Coordinates : [];

    IReadOnlyList<IReadOnlyList<GeoPosition>> IS100Feature.InteriorRings =>
        GeometryType == GeometryType.Surface ? InteriorRings : [];

    IReadOnlyDictionary<string, string> IS100Feature.Attributes
    {
        get
        {
            if (Attributes.Count == 0) return ReadOnlyDictionary<string, string>.Empty;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in Attributes)
            {
                result[key] = value switch
                {
                    null => string.Empty,
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty,
                };
            }
            return result;
        }
    }

    IReadOnlyList<IS100ComplexAttribute> IS100Feature.ComplexAttributes => [];
}

public enum GeometryType
{
    Point,
    Curve,
    Surface,
    Coverage,
    None
}
