using System;
using System.Linq;
using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using Mapsui.Layers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources;

/// <summary>
/// PR-D2.1: <see cref="DynamicSourceOverlayHost"/> as
/// <see cref="IDynamicFeatureSourceRegistry"/>.
/// </summary>
public class DynamicSourceOverlayHostRegistryTests
{
    [Fact]
    public void Sources_ReturnsRegisteredInOrder_AndFiresEventOnRegister()
    {
        var host = new FakeMapHost();
        using var sut = new DynamicSourceOverlayHost(host, new ServiceCollection().BuildServiceProvider(), SyncMarshal);
        IDynamicFeatureSourceRegistry registry = sut;
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
        var host = new FakeMapHost();
        using var sut = new DynamicSourceOverlayHost(host, new ServiceCollection().BuildServiceProvider(), SyncMarshal);
        var reg = sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));
        IDynamicFeatureSourceRegistry registry = sut;
        int events = 0;
        registry.SourcesChanged += () => events++;

        reg.Dispose();

        Assert.Empty(registry.Sources);
        Assert.Equal(1, events);
    }

    [Fact]
    public void SetVisible_TogglesMemoryLayerEnabled_AndFiresEvent()
    {
        var host = new FakeMapHost();
        using var sut = new DynamicSourceOverlayHost(host, new ServiceCollection().BuildServiceProvider(), SyncMarshal);
        sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));
        IDynamicFeatureSourceRegistry registry = sut;
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
        var host = new FakeMapHost();
        using var sut = new DynamicSourceOverlayHost(host, new ServiceCollection().BuildServiceProvider(), SyncMarshal);
        IDynamicFeatureSourceRegistry registry = sut;

        registry.SetVisible("a", false);
        sut.Register(new FakeDynamicFeatureSource("a", new DynamicSourceMetadata { DisplayName = "A" }));

        var layer = (MemoryLayer)host.OverlayLayers[0];
        Assert.False(layer.Enabled);
        Assert.False(registry.GetVisible("a"));
    }

    [Fact]
    public void GetVisible_UnknownId_DefaultsToTrue()
    {
        var host = new FakeMapHost();
        using var sut = new DynamicSourceOverlayHost(host, new ServiceCollection().BuildServiceProvider(), SyncMarshal);

        Assert.True(((IDynamicFeatureSourceRegistry)sut).GetVisible("missing"));
    }

    private static void SyncMarshal(Action a) => a();
}
