namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Resolves the spatial geometry for a feature given its S-100 feature reference.
/// </summary>
/// <remarks>
/// The unified Mapsui display-list renderer relies on this abstraction so that
/// product-specific datasets (S-101 ISO 8211, S-124/S-129/S-421 GML, etc.)
/// can supply geometry for the features named by drawing instructions without
/// the renderer needing to know the underlying encoding.
/// </remarks>
public interface IFeatureGeometryProvider
{
    /// <summary>
    /// Returns the geometry for the feature identified by <paramref name="featureReference"/>,
    /// or <see langword="null"/> if no matching feature exists.
    /// </summary>
    FeatureGeometry? GetGeometry(string featureReference);
}

/// <summary>
/// Lightweight, encoding-agnostic representation of a feature's geometry.
/// </summary>
public sealed class FeatureGeometry
{
    /// <summary>Geometric primitive type.</summary>
    public required GeometryType Type { get; init; }

    /// <summary>
    /// Primary coordinate sequence in (latitude, longitude) order.
    /// Points: a single coordinate. Curves: an ordered polyline.
    /// Surfaces: the exterior ring (CCW recommended).
    /// </summary>
    public required IReadOnlyList<(double Latitude, double Longitude)> Coordinates { get; init; }

    /// <summary>
    /// Optional interior (hole) rings for surface geometries.
    /// Empty for non-surface geometries.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(double Latitude, double Longitude)>> InteriorRings { get; init; } = [];
}
