using EncDotNet.S100.Features;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Generic geometry provider for S-100 vector datasets.
/// </summary>
/// <remarks>
/// Replaces the identical per-spec <c>S{NNN}FeatureGeometryProvider</c>
/// classes that each implemented the same surface → curve → point preference
/// logic. Serves both the GML-encoded products and the ISO 8211-encoded
/// S-101 path (whose pipeline <see cref="Feature"/> records implement
/// <see cref="IS100Feature"/>). When a feature exposes multiple geometry
/// kinds the provider prefers, in order: surface (with interior rings), the
/// first curve, then points.
/// </remarks>
/// <typeparam name="TFeature">
/// The concrete feature type, constrained to <see cref="IS100Feature"/>.
/// </typeparam>
public sealed class FeatureGeometryProvider<TFeature> : IFeatureGeometryProvider
    where TFeature : IS100Feature
{
    private readonly Dictionary<string, FeatureGeometry> _byId;

    /// <summary>Builds a provider over the supplied features.</summary>
    public FeatureGeometryProvider(IReadOnlyList<TFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        _byId = new Dictionary<string, FeatureGeometry>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in features)
        {
            if (string.IsNullOrEmpty(f.Id)) continue;
            var geometry = BuildGeometry(f);
            if (geometry is not null)
                _byId[f.Id] = geometry;
        }
    }

    /// <inheritdoc />
    public FeatureGeometry? GetGeometry(string featureReference) =>
        _byId.TryGetValue(featureReference, out var g) ? g : null;

    private static FeatureGeometry? BuildGeometry(TFeature feature)
    {
        if (feature.ExteriorRing.Count > 0)
        {
            var holes = feature.InteriorRings.Count == 0
                ? Array.Empty<IReadOnlyList<(double Latitude, double Longitude)>>()
                : feature.InteriorRings.Select(r => (IReadOnlyList<(double, double)>)r.ToArray()).ToArray();

            return new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates = feature.ExteriorRing.ToArray(),
                InteriorRings = holes,
            };
        }

        if (feature.Curves.Count > 0)
        {
            var coords = new List<(double Latitude, double Longitude)>();
            foreach (var curve in feature.Curves)
                coords.AddRange(curve);
            return new FeatureGeometry
            {
                Type = GeometryType.Curve,
                Coordinates = coords,
            };
        }

        if (feature.Points.Count > 0)
        {
            return new FeatureGeometry
            {
                Type = GeometryType.Point,
                Coordinates = feature.Points.ToArray(),
            };
        }

        return null;
    }
}
