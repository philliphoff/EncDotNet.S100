using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The encoding- and renderer-neutral geometry of a single vector feature, in
/// WGS-84 lat/lon — the reusable complement to <see cref="FeatureInfo"/>
/// (attributes) resolved for the same feature. Returned by
/// <see cref="IDatasetProcessor.GetFeatureGeometryAt"/> so a host can highlight
/// or outline a picked feature without reaching into a product's feature model.
/// </summary>
/// <remarks>
/// Mirrors the primitive shape exposed by
/// <see cref="EncDotNet.S100.Features.IS100Feature"/>: a feature carries only the
/// coordinate lists matching its <see cref="Primitive"/> (an area fills the
/// rings, a curve fills <see cref="Curves"/>, a point fills <see cref="Points"/>),
/// though a producer may populate more than one. Coordinate lists are shared,
/// not copied; treat them as read-only.
/// </remarks>
public sealed record S100FeatureGeometry
{
    /// <summary>The feature's declared geometry primitive.</summary>
    public required S100GeometryType Primitive { get; init; }

    /// <summary>Point coordinates (empty unless the feature has point geometry).</summary>
    public IReadOnlyList<GeoPosition> Points { get; init; } = [];

    /// <summary>Curve coordinate sequences (empty unless the feature has curves).</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves { get; init; } = [];

    /// <summary>Surface exterior-ring coordinates (empty unless the feature is an area).</summary>
    public IReadOnlyList<GeoPosition> ExteriorRing { get; init; } = [];

    /// <summary>Surface interior-ring (hole) coordinate sequences.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = [];

    /// <summary>
    /// <see langword="true"/> when at least one drawable coordinate is present
    /// across any primitive.
    /// </summary>
    public bool HasGeometry =>
        Points.Count > 0
        || Curves.Count > 0
        || ExteriorRing.Count > 0
        || InteriorRings.Count > 0;
}
