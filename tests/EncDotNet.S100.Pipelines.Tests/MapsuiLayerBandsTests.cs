using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

public sealed class MapsuiLayerBandsTests
{
    [Fact]
    public void AddLayers_MixedBandOrder_OrdersBasemapDatasetsOverlaysAndTools()
    {
        using var map = new Map();
        var bands = new MapsuiLayerBands(map);
        var basemap = Layer("basemap");
        var dataset1 = Layer("dataset-1");
        var dataset2 = Layer("dataset-2");
        var overlay1 = Layer("overlay-1");
        var overlay2 = Layer("overlay-2");
        var tool1 = Layer("tool-1");
        var tool2 = Layer("tool-2");

        bands.AddToolLayer(tool1);
        bands.AddOverlayLayer(overlay1);
        bands.AddDatasetLayer(dataset1);
        bands.SetBasemapLayer(basemap);
        bands.AddToolLayer(tool2);
        bands.AddOverlayLayer(overlay2);
        bands.AddDatasetLayer(dataset2);

        Assert.Equal(
            [basemap, dataset1, dataset2, overlay1, overlay2, tool1, tool2],
            map.Layers);
    }

    [Fact]
    public void ReplaceDatasetLayers_NewOrderAndInstances_ReplacesOnlyDatasetBand()
    {
        using var map = new Map();
        var bands = new MapsuiLayerBands(map);
        var basemap1 = Layer("basemap-1");
        var basemap2 = Layer("basemap-2");
        var dataset1 = Layer("dataset-1");
        var dataset2 = Layer("dataset-2");
        var replacement = Layer("replacement");
        var overlay = Layer("overlay");
        var tool = Layer("tool");
        bands.SetBasemapLayer(basemap1);
        bands.AddDatasetLayer(dataset1);
        bands.AddDatasetLayer(dataset2);
        bands.AddOverlayLayer(overlay);
        bands.AddToolLayer(tool);

        bands.SetBasemapLayer(basemap2);
        bands.ReplaceDatasetLayers([dataset2, replacement]);

        Assert.Equal([basemap2, dataset2, replacement, overlay, tool], map.Layers);
        Assert.DoesNotContain(basemap1, map.Layers);
        Assert.DoesNotContain(dataset1, map.Layers);
    }

    [Fact]
    public void RemoveLayers_OwnedLayers_RemovesEveryBandWithoutMovingSurvivors()
    {
        using var map = new Map();
        var bands = new MapsuiLayerBands(map);
        var basemap = Layer("basemap");
        var dataset1 = Layer("dataset-1");
        var dataset2 = Layer("dataset-2");
        var overlay1 = Layer("overlay-1");
        var overlay2 = Layer("overlay-2");
        var tool1 = Layer("tool-1");
        var tool2 = Layer("tool-2");
        bands.SetBasemapLayer(basemap);
        bands.AddDatasetLayer(dataset1);
        bands.AddDatasetLayer(dataset2);
        bands.AddOverlayLayer(overlay1);
        bands.AddOverlayLayer(overlay2);
        bands.AddToolLayer(tool1);
        bands.AddToolLayer(tool2);

        bands.SetBasemapLayer(null);
        bands.RemoveDatasetLayer(dataset1);
        bands.RemoveOverlayLayer(overlay1);
        bands.RemoveToolLayer(tool1);

        Assert.Equal([dataset2, overlay2, tool2], map.Layers);
    }

    [Fact]
    public void AddLayers_LayerAlreadyPresentInMap_ThrowsWithoutDuplicatingLayer()
    {
        Action<MapsuiLayerBands, ILayer>[] additions =
        [
            (bands, layer) => bands.SetBasemapLayer(layer),
            (bands, layer) => bands.AddDatasetLayer(layer),
            (bands, layer) => bands.AddOverlayLayer(layer),
            (bands, layer) => bands.AddToolLayer(layer),
        ];

        foreach (var add in additions)
        {
            using var map = new Map();
            var bands = new MapsuiLayerBands(map);
            var layer = Layer("external");
            map.Layers.Add(layer);

            Assert.Throws<ArgumentException>(() => add(bands, layer));
            Assert.Equal([layer], map.Layers);
        }
    }

    [Fact]
    public void ReplaceDatasetLayers_UnmanagedLayerAlreadyPresentInMap_Throws()
    {
        using var map = new Map();
        var bands = new MapsuiLayerBands(map);
        var external = Layer("external");
        map.Layers.Add(external);

        Assert.Throws<ArgumentException>(() => bands.ReplaceDatasetLayers([external]));
        Assert.Equal([external], map.Layers);
    }

    private static MemoryLayer Layer(string name) => new() { Name = name };
}
