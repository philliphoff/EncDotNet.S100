using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using Mapsui;
using Mapsui.Layers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

public class DynamicSourceOverlayHostTests
{
    private static DynamicFeature Point(string id) => new()
    {
        Id = id,
        GeometryType = GeometryType.Point,
        Coordinates = new[] { (47.6, -122.3) },
        LastUpdated = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Register_AddsOverlayLayer_AndPopulatesFromSnapshot()
    {
        var host = new FakeMapHost();
        var source = new FakeDynamicFeatureSource("ownship", new DynamicSourceMetadata
        {
            DisplayName = "Own Ship",
        });
        source.SetFeatures(new[] { Point("ownship") });

        using var sut = new DynamicSourceOverlayHost(host, new ServiceCollection().BuildServiceProvider(), SyncMarshal);
        var registration = sut.Register(source);

        Assert.Single(host.OverlayLayers);
        var layer = Assert.IsType<MemoryLayer>(host.OverlayLayers[0]);
        Assert.NotEmpty(layer.Features);

        registration.Dispose();
        Assert.Empty(host.OverlayLayers);
    }

    [Fact]
    public void Changed_TriggersRebuild()
    {
        var host = new FakeMapHost();
        var source = new FakeDynamicFeatureSource("ownship", new DynamicSourceMetadata
        {
            DisplayName = "Own Ship",
        });

        using var sut = new DynamicSourceOverlayHost(host, new ServiceCollection().BuildServiceProvider(), SyncMarshal);
        sut.Register(source);
        var layer = (MemoryLayer)host.OverlayLayers[0];
        var initial = layer.Features.Count();

        source.SetFeatures(new[] { Point("a"), Point("b") });
        source.RaiseChanged(new DynamicFeaturesChanged
        {
            Kind = DynamicSourceChangeKind.Added,
            ChangedIds = new[] { "a", "b" },
        });

        Assert.NotEqual(initial, layer.Features.Count());
        Assert.Equal(2, layer.Features.Count());
    }

    [Fact]
    public void UnknownRendererKey_FallsBackToDefault()
    {
        var host = new FakeMapHost();
        var source = new FakeDynamicFeatureSource("any", new DynamicSourceMetadata
        {
            DisplayName = "Any",
            RendererKey = "no.such.key",
        });
        source.SetFeatures(new[] { Point("a") });

        using var sut = new DynamicSourceOverlayHost(host, new ServiceCollection().BuildServiceProvider(), SyncMarshal);
        sut.Register(source);

        Assert.Single(host.OverlayLayers);
        var layer = (MemoryLayer)host.OverlayLayers[0];
        Assert.Single(layer.Features);
    }

    [Fact]
    public void RegisteredRendererKey_UsesResolvedRenderer()
    {
        var host = new FakeMapHost();
        var services = new ServiceCollection();
        services.AddDynamicFeatureRenderer<CountingRenderer>("custom.key");
        var sp = services.BuildServiceProvider();

        var source = new FakeDynamicFeatureSource("any", new DynamicSourceMetadata
        {
            DisplayName = "Any",
            RendererKey = "custom.key",
        });
        source.SetFeatures(new[] { Point("a") });

        CountingRenderer.RenderCalls = 0;
        using var sut = new DynamicSourceOverlayHost(host, sp, SyncMarshal);
        sut.Register(source);

        Assert.Single(host.OverlayLayers);
        Assert.Equal(1, CountingRenderer.RenderCalls);
    }

    [Fact]
    public void DuplicateId_Throws()
    {
        var host = new FakeMapHost();
        var sp = new ServiceCollection().BuildServiceProvider();
        using var sut = new DynamicSourceOverlayHost(host, sp, SyncMarshal);
        sut.Register(new FakeDynamicFeatureSource("dup", new DynamicSourceMetadata { DisplayName = "A" }));

        Assert.Throws<InvalidOperationException>(() =>
            sut.Register(new FakeDynamicFeatureSource("dup", new DynamicSourceMetadata { DisplayName = "B" })));
    }

    [Fact]
    public void Dispose_RemovesAllOverlayLayers()
    {
        var host = new FakeMapHost();
        var sp = new ServiceCollection().BuildServiceProvider();
        var sut = new DynamicSourceOverlayHost(host, sp, SyncMarshal);
        sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));
        sut.Register(new FakeDynamicFeatureSource("b", new DynamicSourceMetadata { DisplayName = "B" }));

        Assert.Equal(2, host.OverlayLayers.Count);
        sut.Dispose();
        Assert.Empty(host.OverlayLayers);
    }

    [Fact]
    public void Marshal_InvokedForChangedEvents()
    {
        var host = new FakeMapHost();
        var sp = new ServiceCollection().BuildServiceProvider();
        int marshalCalls = 0;
        Action<Action> marshal = a => { marshalCalls++; a(); };

        var source = new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" });
        using var sut = new DynamicSourceOverlayHost(host, sp, marshal);
        sut.Register(source);
        var afterRegister = marshalCalls;

        source.RaiseChanged(new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset });

        Assert.True(marshalCalls > afterRegister);
    }

    private static void SyncMarshal(Action a) => a();

    private sealed class CountingRenderer : IDynamicFeatureRenderer
    {
        public static int RenderCalls;
        public bool CanRender(DynamicFeature feature) => true;
        public IEnumerable<IFeature> Render(DynamicFeature feature)
        {
            RenderCalls++;
            return Array.Empty<IFeature>();
        }
    }
}
