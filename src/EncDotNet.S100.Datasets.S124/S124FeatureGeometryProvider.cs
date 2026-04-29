using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S124;

/// <summary>
/// Adapter that exposes the geometry of an <see cref="S124Dataset"/> through the
/// product-agnostic <see cref="IFeatureGeometryProvider"/> contract used by the
/// unified Mapsui display-list renderer.
/// </summary>
/// <remarks>
/// S-124 feature references are GML identifiers (e.g. <c>NW.GB.123</c>) carried
/// on the originating feature. When a feature exposes multiple geometry kinds
/// the provider prefers, in order: surface (with interior rings), the first
/// curve, then points.
/// </remarks>
public sealed class S124FeatureGeometryProvider : IFeatureGeometryProvider
{
    private readonly Dictionary<string, FeatureGeometry> _byId;

    /// <summary>Builds a provider over the supplied dataset.</summary>
    public S124FeatureGeometryProvider(S124Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _byId = new Dictionary<string, FeatureGeometry>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in dataset.Features)
        {
            var geometry = BuildGeometry(f);
            if (geometry is not null)
                _byId[f.Id] = geometry;
        }
    }

    /// <inheritdoc />
    public FeatureGeometry? GetGeometry(string featureReference) =>
        _byId.TryGetValue(featureReference, out var g) ? g : null;

    private static FeatureGeometry? BuildGeometry(S124Feature feature)
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
            // Concatenate multiple curves into a single polyline (matches the
            // existing S-124 renderer behaviour).
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
