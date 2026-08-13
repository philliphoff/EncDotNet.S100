using EncDotNet.S100.Renderers.Mapsui.Avalonia;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class MapCapabilityBoundaryTests
{
    [Fact]
    public void Mapsui_host_implements_each_focused_capability()
    {
        var interfaces = typeof(MapsuiMapHost).GetInterfaces();

        Assert.Contains(typeof(IMapLayerCollection), interfaces);
        Assert.Contains(typeof(IMapViewportController), interfaces);
        Assert.Contains(typeof(IMapCoordinateConverter), interfaces);
        // The host now speaks the reusable Core .Maps IImageRenderer contract
        // directly (RenderToPngAsync + PreferredSize), replacing the former
        // Viewer-private IMapSnapshotRenderer synonym and its ViewerImageRenderer
        // adapter.
        Assert.Contains(typeof(IImageRenderer), interfaces);
        Assert.Contains(typeof(IMapInvalidator), interfaces);
        Assert.Contains(typeof(IDisposable), interfaces);
    }

    [Fact]
    public void Mapsui_host_attaches_through_the_capture_synchronized_control()
    {
        var constructor = Assert.Single(typeof(MapsuiMapHost).GetConstructors());
        var parameters = constructor.GetParameters();

        // The host now sets S-100 up through the public mapControl.AddS100 entry
        // point, so it takes the live control and obtains the session and the
        // Avalonia adapter from that single call — it no longer hand-builds a
        // session over an externally-attached adapter.
        Assert.Equal(typeof(CaptureSynchronizedMapControl), parameters[0].ParameterType);
        Assert.DoesNotContain(
            parameters, p => p.ParameterType == typeof(AvaloniaMapsuiMapAdapter));
    }

    [Fact]
    public void Late_bound_accessors_are_independent_by_capability()
    {
        var viewport = new MapCapabilityAccessor<IMapViewportController>();
        var renderer = new MapCapabilityAccessor<IImageRenderer>();

        Assert.Null(viewport.Current);
        Assert.Null(renderer.Current);
        Assert.NotEqual(viewport.GetType(), renderer.GetType());
    }

    [Fact]
    public void Consumers_request_only_the_capabilities_they_use()
    {
        var initialize = typeof(DatasetLoaderService).GetMethod(nameof(DatasetLoaderService.Initialize));
        var setViewportConstructor = Assert.Single(typeof(SetViewportTool).GetConstructors());
        var pickConstructors = typeof(PickFeaturesTool).GetConstructors();

        Assert.NotNull(initialize);
        Assert.Equal(typeof(IMapLayerCollection), initialize!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(IMapViewportController), initialize.GetParameters()[1].ParameterType);
        Assert.Equal(
            typeof(ICapabilityAccessor<IMapViewportController>),
            setViewportConstructor.GetParameters()[0].ParameterType);
        Assert.All(
            pickConstructors,
            constructor => Assert.Equal(
                typeof(ICapabilityAccessor<IMapCoordinateConverter>),
                constructor.GetParameters()[0].ParameterType));
    }
}
