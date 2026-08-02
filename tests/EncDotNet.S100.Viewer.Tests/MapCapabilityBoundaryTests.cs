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
        Assert.Contains(typeof(IMapSnapshotRenderer), interfaces);
        Assert.Contains(typeof(IMapInvalidator), interfaces);
    }

    [Fact]
    public void Late_bound_accessors_are_independent_by_capability()
    {
        var viewport = new MapCapabilityAccessor<IMapViewportController>();
        var snapshot = new MapCapabilityAccessor<IMapSnapshotRenderer>();

        Assert.Null(viewport.Current);
        Assert.Null(snapshot.Current);
        Assert.NotEqual(viewport.GetType(), snapshot.GetType());
    }

    [Fact]
    public void Consumers_request_only_the_capabilities_they_use()
    {
        var initialize = typeof(DatasetLoaderService).GetMethod(nameof(DatasetLoaderService.Initialize));
        var renderConstructor = Assert.Single(typeof(RenderToImageTool).GetConstructors());
        var setViewportConstructor = Assert.Single(typeof(SetViewportTool).GetConstructors());
        var pickConstructors = typeof(PickFeaturesTool).GetConstructors();

        Assert.NotNull(initialize);
        Assert.Equal(typeof(IMapLayerCollection), initialize!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(IMapViewportController), initialize.GetParameters()[1].ParameterType);
        Assert.Equal(
            typeof(IMapCapabilityAccessor<IMapSnapshotRenderer>),
            renderConstructor.GetParameters()[0].ParameterType);
        Assert.Equal(
            typeof(IMapCapabilityAccessor<IMapCoordinateConverter>),
            renderConstructor.GetParameters()[1].ParameterType);
        Assert.Equal(
            typeof(IMapCapabilityAccessor<IMapViewportController>),
            setViewportConstructor.GetParameters()[0].ParameterType);
        Assert.All(
            pickConstructors,
            constructor => Assert.Equal(
                typeof(IMapCapabilityAccessor<IMapCoordinateConverter>),
                constructor.GetParameters()[0].ParameterType));
    }
}
