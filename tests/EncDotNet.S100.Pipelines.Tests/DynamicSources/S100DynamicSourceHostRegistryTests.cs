using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

/// <summary>
/// <see cref="S100DynamicSourceHost"/> as <see cref="IS100DynamicSourceRegistry"/>.
/// </summary>
public class S100DynamicSourceHostRegistryTests
{
    [Fact]
    public void Sources_ReturnsRegisteredInOrder_AndFiresEventOnRegister()
    {
        var host = new FakeOverlayLayerHost();
        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
        IS100DynamicSourceRegistry registry = sut;
        int events = 0;
        registry.SourcesChanged += () => events++;

        sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));
        sut.Register(new FakeDynamicFeatureSource("b", new DynamicSourceMetadata { DisplayName = "B" }));

        Assert.Equal(new[] { "a", "b" }, registry.Sources.Select(s => s.Id));
        Assert.Equal(new[] { "A", "B" }, registry.Sources.Select(s => s.DisplayName));
        Assert.Equal(2, events);
    }

    [Fact]
    public void DisposeRegistration_RemovesFromSources_AndFiresEvent()
    {
        var host = new FakeOverlayLayerHost();
        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
        var reg = sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));
        IS100DynamicSourceRegistry registry = sut;
        int events = 0;
        registry.SourcesChanged += () => events++;

        reg.Dispose();

        Assert.Empty(registry.Sources);
        Assert.Equal(1, events);
    }

    [Fact]
    public void SetVisible_TogglesMemoryLayerEnabled_AndFiresEvent()
    {
        var host = new FakeOverlayLayerHost();
        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
        sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));
        IS100DynamicSourceRegistry registry = sut;
        var layer = (MemoryLayer)host.OverlayLayers[0];
        int events = 0;
        registry.SourcesChanged += () => events++;

        Assert.True(layer.Enabled);
        registry.SetVisible("a", false);

        Assert.False(layer.Enabled);
        Assert.False(registry.GetVisible("a"));
        Assert.Equal(1, events);
    }

    [Fact]
    public void SetVisible_BeforeRegister_SeedsInitialEnabledState()
    {
        var host = new FakeOverlayLayerHost();
        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
        IS100DynamicSourceRegistry registry = sut;

        registry.SetVisible("a", false);
        sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));

        var layer = (MemoryLayer)host.OverlayLayers[0];
        Assert.False(layer.Enabled);
        Assert.False(registry.GetVisible("a"));
    }

    [Fact]
    public void GetVisible_UnknownId_DefaultsToTrue()
    {
        var host = new FakeOverlayLayerHost();
        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);

        Assert.True(((IS100DynamicSourceRegistry)sut).GetVisible("missing"));
    }

    [Fact]
    public void HitTest_ReturnsVisibleSourceHits_AndExcludesHidden()
    {
        const double lat = 47.6;
        const double lon = -122.3;
        var host = new FakeOverlayLayerHost();
        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
        IS100DynamicSourceRegistry registry = sut;

        var visible = new FakeDynamicFeatureSource("visible", new DynamicSourceMetadata { DisplayName = "V" });
        visible.SetFeatures(new[] { PointFeature("v1", lat, lon) });
        var hidden = new FakeDynamicFeatureSource("hidden", new DynamicSourceMetadata { DisplayName = "H" });
        hidden.SetFeatures(new[] { PointFeature("h1", lat, lon) });
        sut.Register(visible);
        sut.Register(hidden);
        registry.SetVisible("hidden", false);

        var (x, y) = SphericalMercator.FromLonLat(lon, lat);
        var hits = registry.HitTest(new MPoint(x, y), resolution: 10.0);

        var hit = Assert.Single(hits);
        Assert.Equal("v1", hit.Feature.Id);
        Assert.Equal("visible", hit.Source.Id);
    }

    private static DynamicFeature PointFeature(string id, double lat, double lon) => new()
    {
        Id = id,
        GeometryType = GeometryType.Point,
        Coordinates = new[] { new GeoPosition(lat, lon) },
        LastUpdated = DateTimeOffset.UtcNow,
    };

    private static void SyncMarshal(Action a) => a();
}
