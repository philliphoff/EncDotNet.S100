using EncDotNet.S100.Viewer;
using Mapsui.Layers;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Issue #295: the basemap factory builds the bundled offline Natural
/// Earth land layer, returns nothing for <see cref="BasemapMode.None"/>,
/// and an online tile layer for <see cref="BasemapMode.Online"/>.
/// </summary>
public class BasemapLayerFactoryTests
{
    [Fact]
    public void None_ReturnsNull()
    {
        Assert.Null(BasemapLayerFactory.Create(BasemapMode.None));
    }

    [Fact]
    public void Offline_BuildsMemoryLayerWithLandFeatures()
    {
        var layer = BasemapLayerFactory.Create(BasemapMode.Offline);

        var memory = Assert.IsType<MemoryLayer>(layer);
        Assert.NotEmpty(memory.Features);
    }

    [Fact]
    public void Online_BuildsTileLayer()
    {
        var layer = BasemapLayerFactory.Create(BasemapMode.Online);

        Assert.NotNull(layer);
        Assert.IsType<Mapsui.Tiling.Layers.TileLayer>(layer);
    }
}
