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
    public required string ProductSpec { get; init; }
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
    /// </summary>
    public required IReadOnlyList<(double Latitude, double Longitude)> Coordinates { get; init; }

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
