using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Generic geometry provider for GML-encoded S-100 datasets.
/// </summary>
/// <remarks>
/// Replaces the identical per-spec <c>S{NNN}FeatureGeometryProvider</c>
/// classes that each implemented the same surface → curve → point preference
/// logic. When a feature exposes multiple geometry kinds the provider
/// prefers, in order: surface (with interior rings), the first curve, then
/// points.
/// </remarks>
/// <typeparam name="TFeature">
/// The concrete feature type, constrained to <see cref="IGmlFeature"/>.
/// </typeparam>
public sealed class GmlFeatureGeometryProvider<TFeature> : IFeatureGeometryProvider
    where TFeature : IGmlFeature
{
    private readonly Dictionary<string, FeatureGeometry> _byId;

    /// <summary>Builds a provider over the supplied features.</summary>
    public GmlFeatureGeometryProvider(IReadOnlyList<TFeature> features)
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
        if (!feature.ExteriorRing.IsDefaultOrEmpty)
        {
            var holes = feature.InteriorRings.IsDefaultOrEmpty
                ? Array.Empty<IReadOnlyList<(double Latitude, double Longitude)>>()
                : feature.InteriorRings.Select(r => (IReadOnlyList<(double, double)>)r.ToArray()).ToArray();

            return new FeatureGeometry
            {
                Type = GeometryType.Surface,
                Coordinates = feature.ExteriorRing.ToArray(),
                InteriorRings = holes,
            };
        }

        if (!feature.Curves.IsDefaultOrEmpty && feature.Curves.Length > 0)
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

        if (!feature.Points.IsDefaultOrEmpty)
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
