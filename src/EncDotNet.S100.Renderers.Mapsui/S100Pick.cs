using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// One resolved result of <see cref="IS100MapQuery.PickAsync"/> — a vector
/// feature or a coverage sample found at the query point, already ranked against
/// the session's S-98 paint stack.
/// </summary>
public sealed record S100Pick
{
    /// <summary>The dataset the pick came from.</summary>
    public required MapDatasetId DatasetId { get; init; }

    /// <summary>The resolved feature or coverage information.</summary>
    public required FeatureInfo Info { get; init; }

    /// <summary>
    /// The picked feature's renderer-neutral geometry (rings / curves / points
    /// in WGS-84), for outlining or highlighting the hit. <see langword="null"/>
    /// for a coverage pick, or when the owning processor does not expose the
    /// feature's geometry.
    /// </summary>
    public S100FeatureGeometry? Geometry { get; init; }

    /// <summary>
    /// <see langword="true"/> for a coverage sample (S-102 / S-104 / S-111 via
    /// <see cref="IDatasetProcessor.GetCoverageInfo"/>); <see langword="false"/>
    /// for a vector feature.
    /// </summary>
    public required bool IsCoverage { get; init; }

    /// <summary>
    /// The vector feature type code, or <see langword="null"/> for a coverage
    /// pick.
    /// </summary>
    public string? FeatureType { get; init; }

    /// <summary>
    /// The geometry primitive the feature was matched against, or
    /// <see langword="null"/> for a coverage pick.
    /// </summary>
    public S100GeometryType? Primitive { get; init; }

    /// <summary>
    /// <see langword="true"/> when the point lies inside an area feature (or for
    /// a coverage sample); <see langword="false"/> for a point/curve feature
    /// matched within the search radius.
    /// </summary>
    public required bool Inside { get; init; }

    /// <summary>
    /// Distance from the pick to the feature in metres — 0 when
    /// <see cref="Inside"/>.
    /// </summary>
    public required double DistanceMeters { get; init; }
}
