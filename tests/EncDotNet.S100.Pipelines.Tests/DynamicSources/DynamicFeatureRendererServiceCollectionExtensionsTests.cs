using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Pipelines.Tests.DynamicSources;

public class DynamicFeatureRendererServiceCollectionExtensionsTests
{
    private sealed class FakeRenderer : IDynamicFeatureRenderer
    {
        public bool CanRender(DynamicFeature feature) => true;
        public IEnumerable<IFeature> Render(DynamicFeature feature) => Array.Empty<IFeature>();
    }

    private sealed class FakeSource : IDynamicFeatureSource
    {
        public string Id => "fake";
        public DynamicSourceMetadata Metadata { get; } = new() { DisplayName = "Fake", RendererKey = "fake.kind" };
        public IReadOnlyList<DynamicFeature> CurrentFeatures => Array.Empty<DynamicFeature>();
        public event EventHandler<DynamicFeaturesChanged>? Changed { add { } remove { } }
    }

    [Fact]
    public void AddDynamicFeatureRenderer_RegistersKeyedRenderer()
    {
        var services = new ServiceCollection();
        services.AddDynamicFeatureRenderer<FakeRenderer>("fake.kind");

        var sp = services.BuildServiceProvider();
        var resolved = sp.GetKeyedService<IDynamicFeatureRenderer>("fake.kind");

        Assert.NotNull(resolved);
        Assert.IsType<FakeRenderer>(resolved);
    }

    [Fact]
    public void AddDynamicFeatureSource_RegistersSourceAndKeyedRenderer()
    {
        var services = new ServiceCollection();
        services.AddDynamicFeatureSource<FakeSource, FakeRenderer>("fake.kind");

        var sp = services.BuildServiceProvider();
        var renderer = sp.GetKeyedService<IDynamicFeatureRenderer>("fake.kind");
        var sources = sp.GetServices<IDynamicFeatureSource>().ToArray();

        Assert.NotNull(renderer);
        Assert.Single(sources);
        Assert.IsType<FakeSource>(sources[0]);
    }

    [Fact]
    public void UnknownKey_ResolvesNull()
    {
        var services = new ServiceCollection();
        services.AddDynamicFeatureRenderer<FakeRenderer>("known");
        var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetKeyedService<IDynamicFeatureRenderer>("unknown"));
    }
}
