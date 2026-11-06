using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The dataset layers a map draws, with each chart layer expanded into the
/// layers of its stack (map rendering slice 8), so session tests can keep
/// asserting the projected stack layer by layer.
/// </summary>
internal static class MapDatasetLayers
{
    public static IReadOnlyList<ILayer> DatasetLayers(this Map map) =>
        [.. map.Layers.SelectMany(static layer => layer is ChartLayer chart
            ? chart.Stack.Entries.Select(static entry => (ILayer)entry.Source)
            : [layer])];
}
