using EncDotNet.S100.DataModel;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

public class S100DynamicSourceHostTests
{
    private static DynamicFeature Point(string id) => new()
    {
        Id = id,
        GeometryType = GeometryType.Point,
        Coordinates = new[] { new GeoPosition(47.6, -122.3) },
        LastUpdated = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void Register_AddsOverlayLayer_AndPopulatesFromSnapshot()
    {
        var host = new FakeOverlayLayerHost();
        var source = new FakeDynamicFeatureSource("ownship", new DynamicSourceMetadata
        {
            DisplayName = "Own Ship",
        });
        source.SetFeatures(new[] { Point("ownship") });

        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
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
        var host = new FakeOverlayLayerHost();
        var source = new FakeDynamicFeatureSource("ownship", new DynamicSourceMetadata
        {
            DisplayName = "Own Ship",
        });

        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
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
        var host = new FakeOverlayLayerHost();
        var source = new FakeDynamicFeatureSource("any", new DynamicSourceMetadata
        {
            DisplayName = "Any",
            RendererKey = "no.such.key",
        });
        source.SetFeatures(new[] { Point("a") });

        // Resolver that resolves nothing → default renderer fallback.
        using var sut = new S100DynamicSourceHost(host, rendererResolver: _ => null, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
        sut.Register(source);

        Assert.Single(host.OverlayLayers);
        var layer = (MemoryLayer)host.OverlayLayers[0];
        Assert.Single(layer.Features);
    }

    [Fact]
    public void RegisteredRendererKey_UsesResolvedRenderer()
    {
        var host = new FakeOverlayLayerHost();
        var counting = new CountingRenderer();
        Func<string?, IDynamicFeatureRenderer?> resolver =
            key => key == "custom.key" ? counting : null;

        var source = new FakeDynamicFeatureSource("any", new DynamicSourceMetadata
        {
            DisplayName = "Any",
            RendererKey = "custom.key",
        });
        source.SetFeatures(new[] { Point("a") });

        using var sut = new S100DynamicSourceHost(host, resolver, SyncMarshal, coalesceWindow: TimeSpan.Zero);
        sut.Register(source);

        Assert.Single(host.OverlayLayers);
        Assert.Equal(1, counting.RenderCalls);
    }

    [Fact]
    public void DuplicateId_Throws()
    {
        var host = new FakeOverlayLayerHost();
        using var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
        sut.Register(new FakeDynamicFeatureSource("dup", new DynamicSourceMetadata { DisplayName = "A" }));

        Assert.Throws<InvalidOperationException>(() =>
            sut.Register(new FakeDynamicFeatureSource("dup", new DynamicSourceMetadata { DisplayName = "B" })));
    }

    [Fact]
    public void Dispose_RemovesAllOverlayLayers()
    {
        var host = new FakeOverlayLayerHost();
        var sut = new S100DynamicSourceHost(host, marshal: SyncMarshal, coalesceWindow: TimeSpan.Zero);
        sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));
        sut.Register(new FakeDynamicFeatureSource("b", new DynamicSourceMetadata { DisplayName = "B" }));

        Assert.Equal(2, host.OverlayLayers.Count);
        sut.Dispose();
        Assert.Empty(host.OverlayLayers);
    }

    [Fact]
    public void Marshal_InvokedForChangedEvents()
    {
        var host = new FakeOverlayLayerHost();
        int marshalCalls = 0;
        Action<Action> marshal = a => { marshalCalls++; a(); };

        var source = new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" });
        using var sut = new S100DynamicSourceHost(host, marshal: marshal, coalesceWindow: TimeSpan.Zero);
        sut.Register(source);
        var afterRegister = marshalCalls;

        source.RaiseChanged(new DynamicFeaturesChanged { Kind = DynamicSourceChangeKind.Reset });

        Assert.True(marshalCalls > afterRegister);
    }

    [Fact]
    public void DefaultMarshal_RunsInline()
    {
        // No marshal supplied → the host runs overlay mutations inline on the
        // caller's thread (suits headless / single-threaded hosts).
        var host = new FakeOverlayLayerHost();
        var source = new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" });
        source.SetFeatures(new[] { Point("a") });

        using var sut = new S100DynamicSourceHost(host, coalesceWindow: TimeSpan.Zero);
        sut.Register(source);

        // Inline marshal means the overlay layer is installed synchronously by
        // the time Register returns.
        Assert.Single(host.OverlayLayers);
    }

    private static void SyncMarshal(Action a) => a();

    [Fact]
    public async Task RebuildCoalesce_collapses_burst_into_at_most_two_rebuilds()
    {
        // Leading-edge rebuild + at most one trailing rebuild for any
        // burst inside one window. AIS at world scale would otherwise
        // pin the map thread.
        var host = new FakeOverlayLayerHost();
        var source = new FakeDynamicFeatureSource("ais", new DynamicSourceMetadata { DisplayName = "AIS" });
        source.SetFeatures(new[] { Point("seed") });

        var burstRaised = 0;
        var rebuildApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<Action> marshal = action =>
        {
            action();
            if (Volatile.Read(ref burstRaised) != 0 &&
                host.OverlayLayers.Count > 0 &&
                ((MemoryLayer)host.OverlayLayers[0]).Features.Count() == 2)
            {
                rebuildApplied.TrySetResult();
            }
        };

        var window = TimeSpan.FromMilliseconds(80);
        using var sut = new S100DynamicSourceHost(host, marshal: marshal, coalesceWindow: window);
        sut.Register(source);

        // Fire 50 events back-to-back inside a single window.
        for (int i = 0; i < 50; i++)
        {
            source.SetFeatures(new[] { Point("seed"), Point("v" + i) });
            source.RaiseChanged(new DynamicFeaturesChanged
            {
                Kind = DynamicSourceChangeKind.Updated,
                ChangedIds = new[] { "v" + i },
            });
        }
        Volatile.Write(ref burstRaised, 1);

        await rebuildApplied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var layer = (MemoryLayer)host.OverlayLayers[0];
        // Trailing rebuild should reflect the final feature set
        // (seed + last update). The intermediate 49 events were
        // collapsed into the same trailing rebuild.
        Assert.Equal(2, layer.Features.Count());
    }

    private sealed class CountingRenderer : IDynamicFeatureRenderer
    {
        public int RenderCalls;
        public bool CanRender(DynamicFeature feature) => true;
        public IEnumerable<IFeature> Render(DynamicFeature feature)
        {
            RenderCalls++;
            return Array.Empty<IFeature>();
        }
    }
}
