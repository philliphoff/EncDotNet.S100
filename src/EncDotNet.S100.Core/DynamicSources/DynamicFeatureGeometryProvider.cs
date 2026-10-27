using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// An <see cref="IFeatureGeometryProvider"/> over dynamic features, where a
/// feature reference is a <see cref="DynamicFeature.Id"/>. It lets drawing
/// instructions built for dynamic features go through the same renderers as a
/// vector product's.
/// </summary>
public sealed class DynamicFeatureGeometryProvider : IFeatureGeometryProvider
{
    private readonly Dictionary<string, FeatureGeometry> _byId;

    /// <summary>Creates a provider over <paramref name="features"/>. A later duplicate id wins.</summary>
    /// <param name="features">The features.</param>
    public DynamicFeatureGeometryProvider(IEnumerable<DynamicFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        _byId = new Dictionary<string, FeatureGeometry>(StringComparer.Ordinal);
        foreach (var feature in features)
        {
            ArgumentNullException.ThrowIfNull(feature, nameof(features));
            _byId[feature.Id] = new FeatureGeometry
            {
                Type = feature.GeometryType,
                Coordinates = feature.Coordinates,
            };
        }
    }

    /// <inheritdoc />
    public FeatureGeometry? GetGeometry(string featureReference) =>
        _byId.TryGetValue(featureReference, out var geometry) ? geometry : null;
}
