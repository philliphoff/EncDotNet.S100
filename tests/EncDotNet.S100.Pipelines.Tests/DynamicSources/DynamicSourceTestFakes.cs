using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

/// <summary>
/// Test-only <see cref="IMapsuiOverlayLayerHost"/> that records overlay-band
/// activity without spinning up a real Mapsui map.
/// </summary>
internal sealed class FakeOverlayLayerHost : IMapsuiOverlayLayerHost
{
    public List<ILayer> OverlayLayers { get; } = new();

    public void AddOverlayLayer(ILayer layer) => OverlayLayers.Add(layer);

    public void RemoveOverlayLayer(ILayer layer) => OverlayLayers.Remove(layer);
}

/// <summary>
/// Test-only <see cref="IDynamicFeatureSource"/> that lets the test drive the
/// snapshot and the change event.
/// </summary>
internal sealed class FakeDynamicFeatureSource : IDynamicFeatureSource
{
    private IReadOnlyList<DynamicFeature> _features = Array.Empty<DynamicFeature>();

    public FakeDynamicFeatureSource(string id, DynamicSourceMetadata metadata)
    {
        Id = id;
        Metadata = metadata;
    }

    public string Id { get; }
    public DynamicSourceMetadata Metadata { get; }
    public IReadOnlyList<DynamicFeature> CurrentFeatures => Volatile.Read(ref _features);
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    public void SetFeatures(IReadOnlyList<DynamicFeature> features) =>
        Volatile.Write(ref _features, features);

    public void RaiseChanged(DynamicFeaturesChanged payload) =>
        Changed?.Invoke(this, payload);
}
