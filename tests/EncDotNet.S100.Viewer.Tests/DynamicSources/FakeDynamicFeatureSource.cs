using EncDotNet.S100.DynamicSources;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

/// <summary>
/// Test-only <see cref="IDynamicFeatureSource"/> that lets the test
/// drive the snapshot and the change event.
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
    public IReadOnlyList<DynamicFeature> CurrentFeatures => _features;
    public event EventHandler<DynamicFeaturesChanged>? Changed;

    public void SetFeatures(IReadOnlyList<DynamicFeature> features) => _features = features;

    public void RaiseChanged(DynamicFeaturesChanged payload)
        => Changed?.Invoke(this, payload);
}
