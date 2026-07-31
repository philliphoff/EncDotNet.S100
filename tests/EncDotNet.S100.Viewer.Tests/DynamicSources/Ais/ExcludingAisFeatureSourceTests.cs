using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Viewer.Services.DynamicSources.Ais;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources.Ais;

public sealed class ExcludingAisFeatureSourceTests
{
    private static DynamicFeature Vessel(string id)
        => new()
        {
            Id = id,
            Kind = "vessel.ais.cargo",
            GeometryType = GeometryType.Point,
            Coordinates = new[] { new GeoPosition(50.0, -1.0) },
            LastUpdated = DateTimeOffset.UnixEpoch,
        };

    private static FakeDynamicFeatureSource MakeInner(params string[] ids)
    {
        var inner = new FakeDynamicFeatureSource(
            "ais",
            new DynamicSourceMetadata { DisplayName = "AIS targets", RendererKey = "vessel.ais" });
        inner.SetFeatures(Array.ConvertAll(ids, Vessel));
        return inner;
    }

    [Fact]
    public void PreservesInnerIdentityAndMetadata()
    {
        var inner = MakeInner();
        var decorator = new ExcludingAisFeatureSource(inner);

        Assert.Equal(inner.Id, decorator.Id);
        Assert.Same(inner.Metadata, decorator.Metadata);
        Assert.Equal("vessel.ais", decorator.Metadata.RendererKey);
    }

    [Fact]
    public void NoExclusion_PublishesAllFeatures()
    {
        var inner = MakeInner("ais:1", "ais:2", "ais:3");
        var decorator = new ExcludingAisFeatureSource(inner);

        Assert.Equal(3, decorator.CurrentFeatures.Count);
    }

    [Fact]
    public void ExcludedId_RemovedFromSnapshot()
    {
        var inner = MakeInner("ais:1", "ais:2", "ais:3");
        var decorator = new ExcludingAisFeatureSource(inner) { ExcludedId = "ais:2" };

        Assert.DoesNotContain(decorator.CurrentFeatures, f => f.Id == "ais:2");
        Assert.Equal(2, decorator.CurrentFeatures.Count);
    }

    [Fact]
    public void ChangingExcludedId_RaisesReset()
    {
        var inner = MakeInner("ais:1", "ais:2");
        var decorator = new ExcludingAisFeatureSource(inner);

        var resets = 0;
        decorator.Changed += (_, e) =>
        {
            if (e.Kind == DynamicSourceChangeKind.Reset) resets++;
        };

        decorator.ExcludedId = "ais:1";
        Assert.Equal(1, resets);

        // Setting the same value again is a no-op.
        decorator.ExcludedId = "ais:1";
        Assert.Equal(1, resets);

        // Clearing it raises again.
        decorator.ExcludedId = null;
        Assert.Equal(2, resets);
    }

    [Fact]
    public void ForwardsInnerChanged()
    {
        var inner = MakeInner("ais:1");
        var decorator = new ExcludingAisFeatureSource(inner);

        DynamicFeaturesChanged? seen = null;
        decorator.Changed += (_, e) => seen = e;

        var payload = new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Updated,
            ChangedIds = new[] { "ais:1" },
        };
        inner.RaiseChanged(payload);

        Assert.NotNull(seen);
        Assert.Equal(DynamicSourceChangeKind.Updated, seen!.Kind);
        Assert.Equal(new[] { "ais:1" }, seen.ChangedIds);
    }

    [Fact]
    public void ExcludedFeature_StillVisibleOnInner()
    {
        // The raw inner source must keep the excluded target so the
        // pirate-mode controller can still follow it.
        var inner = MakeInner("ais:42");
        var decorator = new ExcludingAisFeatureSource(inner) { ExcludedId = "ais:42" };

        Assert.Empty(decorator.CurrentFeatures);
        Assert.Contains(inner.CurrentFeatures, f => f.Id == "ais:42");
    }

    [Fact]
    public async Task Dispose_StopsForwardingAndDisposesInner()
    {
        var inner = new DisposableFakeSource();
        var decorator = new ExcludingAisFeatureSource(inner);

        var seen = 0;
        decorator.Changed += (_, _) => seen++;

        await decorator.DisposeAsync();

        inner.RaiseChanged(new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset });

        Assert.Equal(0, seen);
        Assert.True(inner.Disposed);
    }

    private sealed class DisposableFakeSource : IDynamicFeatureSource, IAsyncDisposable
    {
        public string Id => "ais";
        public DynamicSourceMetadata Metadata { get; } = new() { DisplayName = "AIS", RendererKey = "vessel.ais" };
        public IReadOnlyList<DynamicFeature> CurrentFeatures => Array.Empty<DynamicFeature>();
        public event EventHandler<DynamicFeaturesChanged>? Changed;
        public bool Disposed { get; private set; }

        public void RaiseChanged(DynamicFeaturesChanged e) => Changed?.Invoke(this, e);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
