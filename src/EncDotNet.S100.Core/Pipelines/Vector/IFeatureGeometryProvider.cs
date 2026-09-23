using EncDotNet.S100.DataModel;
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
    /// Surfaces: the exterior ring (CCW recommended). For a curve or surface
    /// with several <see cref="Parts"/>, all of them joined in order, so a
    /// consumer that ignores <see cref="Parts"/> would join them.
    /// </summary>
    public required IReadOnlyList<GeoPosition> Coordinates { get; init; }

    /// <summary>
    /// The separate parts of a curve whose curves do not all meet end to start
    /// (see <see cref="CurveParts"/>), each an ordered polyline; or of a surface
    /// with several surfaces, each part's exterior ring, with its holes in
    /// <see cref="PartInteriorRings"/>. Draw each on its own: joining them would
    /// draw a straight segment across each gap. Empty when
    /// <see cref="Coordinates"/> is a single curve or surface, and for points.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Parts { get; init; } = [];

    /// <summary>
    /// For a surface with several <see cref="Parts"/>, the interior rings of
    /// each part, in the same order. Empty otherwise.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPosition>>> PartInteriorRings { get; init; } = [];

    /// <summary>
    /// Optional interior (hole) rings for surface geometries.
    /// Empty for non-surface geometries.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = [];
}
