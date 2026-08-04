using EncDotNet.S100.Datasets.Pipelines;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Associates one host-projected Mapsui layer with its source dataset and
/// portrayal sub-layer.
/// </summary>
public sealed class MapsuiProjectedDatasetLayer
{
    /// <summary>Creates a projected layer association.</summary>
    /// <param name="datasetId">The source dataset identity.</param>
    /// <param name="layerKey">The stable portrayal sub-layer key.</param>
    /// <param name="layer">The layer to place in the map's dataset band.</param>
    public MapsuiProjectedDatasetLayer(
        MapDatasetId datasetId,
        string layerKey,
        ILayer layer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetId.Value, nameof(datasetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);
        ArgumentNullException.ThrowIfNull(layer);

        DatasetId = datasetId;
        LayerKey = layerKey;
        Layer = layer;
    }

    /// <summary>Gets the source dataset identity.</summary>
    public MapDatasetId DatasetId { get; }

    /// <summary>Gets the stable portrayal sub-layer key.</summary>
    public string LayerKey { get; }

    /// <summary>Gets the projected Mapsui layer.</summary>
    public ILayer Layer { get; }
}
