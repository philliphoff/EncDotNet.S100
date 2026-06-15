using EncDotNet.S100.Core;

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
public sealed class Feature
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
    public required IReadOnlyList<(double Latitude, double Longitude)> Coordinates { get; init; }

    /// <summary>
    /// Interior (hole) rings for surface geometries, each an ordered list of
    /// (lat, lon) pairs. Empty for non-surface geometries or surfaces without holes.
    /// Renderers subtract these from the exterior ring (<see cref="Coordinates"/>)
    /// when filling, so that, for example, a sea/depth area encoded around islands
    /// does not paint over the land cut out as holes.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(double Latitude, double Longitude)>> InteriorRings { get; init; } = [];

    /// <summary>Feature attribute values keyed by attribute code.</summary>
    public required IReadOnlyDictionary<string, object?> Attributes { get; init; }
}

public enum GeometryType
{
    Point,
    Curve,
    Surface,
    Coverage,
    None
}
