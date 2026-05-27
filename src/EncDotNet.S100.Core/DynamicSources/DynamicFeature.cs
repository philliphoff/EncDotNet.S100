using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// A single push-driven feature published by an
/// <see cref="IDynamicFeatureSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// Geometry vocabulary is intentionally identical to the static
/// vector pipeline's <see cref="Pipelines.Vector.Feature"/>:
/// <see cref="GeometryType"/> is the same enum and
/// <see cref="Coordinates"/> uses the same
/// <c>(Latitude, Longitude)</c> tuple convention. This keeps
/// renderer authors from learning two systems and gives adapters
/// that want to snapshot a dynamic source as a static dataset a
/// one-line projection.
/// </para>
/// <para>
/// The record is <i>not</i> derived from
/// <see cref="Pipelines.Vector.Feature"/> because of three semantic
/// mismatches documented in
/// <c>docs/design/dynamic-feature-source.md</c> §5 Q2: stable string
/// identity (vs the static record's <c>long</c> id), opaque
/// renderer-dispatch <see cref="Kind"/> (vs FC-bound
/// <c>FeatureType</c>), and the optional
/// <see cref="Motion"/> / required <see cref="LastUpdated"/>
/// temporal fields.
/// </para>
/// <para>
/// Dynamic sources must only use
/// <see cref="Pipelines.Vector.GeometryType.Point"/>,
/// <see cref="Pipelines.Vector.GeometryType.Curve"/>, or
/// <see cref="Pipelines.Vector.GeometryType.Surface"/>.
/// <see cref="Pipelines.Vector.GeometryType.Coverage"/> push is
/// deferred to a future <c>IDynamicCoverageSource</c>;
/// <see cref="Pipelines.Vector.GeometryType.None"/> is degenerate.
/// </para>
/// </remarks>
public sealed record DynamicFeature
{
    /// <summary>
    /// Source-stable opaque identity. The source chooses the
    /// semantics — MMSI for AIS, <c>"ownship"</c> for an own-ship
    /// singleton, a GUID for a route waypoint, an isobar key for a
    /// weather contour. Stability across updates is a hard contract:
    /// the same feature must round-trip the same <see cref="Id"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Opaque renderer-dispatch hint. Has no Feature Catalogue
    /// meaning. Conventional examples: <c>"vessel.cargo"</c>,
    /// <c>"vessel.tanker"</c>, <c>"vessel.unknown"</c>,
    /// <c>"ownship"</c>, <c>"waypoint"</c>, <c>"weather.isobar"</c>.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Geometry kind. Restricted to Point / Curve / Surface for
    /// dynamic sources (see remarks on the type).
    /// </summary>
    public required GeometryType GeometryType { get; init; }

    /// <summary>
    /// Geometry coordinates in WGS-84 lat/lon. Same convention as
    /// the static vector pipeline: latitude first. Cardinality
    /// depends on <see cref="GeometryType"/> — exactly 1 for Point,
    /// at least 2 for Curve, a closed ring (first and last
    /// coincident) for Surface.
    /// </summary>
    public required IReadOnlyList<(double Latitude, double Longitude)> Coordinates { get; init; }

    /// <summary>
    /// Optional motion sidecar — only meaningful for moving point
    /// features.
    /// </summary>
    public DynamicMotion? Motion { get; init; }

    /// <summary>
    /// Caller-defined extra attributes — vessel name, MMSI, call
    /// sign, pressure level, sensor reading, leg label, etc.
    /// Renderers consume this opaquely.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>UTC timestamp of the most recent update.</summary>
    public required DateTimeOffset LastUpdated { get; init; }
}
