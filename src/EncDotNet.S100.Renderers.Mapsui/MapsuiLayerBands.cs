using EncDotNet.S100.Renderers.Mapsui.DynamicSources;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Owns the ordered basemap, dataset, overlay, and tool layer bands of a
/// <see cref="Map"/>.
/// </summary>
/// <remarks>
/// The component mutates the supplied map directly and does not marshal calls
/// to a UI thread. UI-framework hosts remain responsible for invoking it from
/// the thread required by their map control.
/// </remarks>
public sealed class MapsuiLayerBands : IMapsuiOverlayLayerHost, IS100MapLayerHost
{
    private readonly Map _map;
    private readonly HashSet<ILayer> _datasetLayers = [];
    private readonly HashSet<ILayer> _overlayLayers = [];
    private readonly HashSet<ILayer> _toolLayers = [];
    private ILayer? _basemapLayer;

    /// <summary>
    /// Creates layer-band bookkeeping for <paramref name="map"/>.
    /// </summary>
    /// <param name="map">The Mapsui map whose layers are managed.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="map"/> is <see langword="null"/>.
    /// </exception>
    public MapsuiLayerBands(Map map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
    }

    /// <summary>
    /// Replaces the current basemap layer, or removes it when
    /// <paramref name="layer"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="layer">The new basemap layer, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="layer"/> belongs to another managed band or is already
    /// present in the map.
    /// </exception>
    public void SetBasemapLayer(ILayer? layer)
    {
        if (ReferenceEquals(_basemapLayer, layer))
        {
            return;
        }

        EnsureAvailable(layer);

        if (_basemapLayer is not null)
        {
            _map.Layers.Remove(_basemapLayer);
        }

        _basemapLayer = layer;
        if (layer is not null)
        {
            _map.Layers.Insert(0, layer, 0);
        }
    }

    /// <summary>
    /// Adds a layer to the dataset band.
    /// </summary>
    /// <param name="layer">The dataset layer to add.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="layer"/> belongs to another managed band or is already
    /// present in the map.
    /// </exception>
    public void AddDatasetLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_datasetLayers.Contains(layer))
        {
            return;
        }

        EnsureAvailable(layer);
        var insertAt = FindInsertAfter(_datasetLayers, bottomLayer: _basemapLayer);
        _map.Layers.Insert(insertAt, layer, 0);
        _datasetLayers.Add(layer);
    }

    /// <summary>
    /// Removes a layer from the dataset band.
    /// </summary>
    /// <param name="layer">The dataset layer to remove.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layer"/> is <see langword="null"/>.
    /// </exception>
    public void RemoveDatasetLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_datasetLayers.Remove(layer))
        {
            _map.Layers.Remove(layer);
        }
    }

    /// <summary>
    /// Replaces the complete dataset band with
    /// <paramref name="orderedDatasetLayers"/>.
    /// </summary>
    /// <param name="orderedDatasetLayers">
    /// The authoritative dataset layers in bottom-to-top order.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="orderedDatasetLayers"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The sequence contains a null, duplicate, basemap, overlay, tool, or
    /// unmanaged layer already present in the map.
    /// </exception>
    public void ReplaceDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers)
    {
        ArgumentNullException.ThrowIfNull(orderedDatasetLayers);

        var replacements = new HashSet<ILayer>();
        foreach (var layer in orderedDatasetLayers)
        {
            if (layer is null)
            {
                throw new ArgumentException(
                    "The dataset layer sequence cannot contain null values.",
                    nameof(orderedDatasetLayers));
            }

            if (!replacements.Add(layer))
            {
                throw new ArgumentException(
                    "The dataset layer sequence cannot contain duplicate layers.",
                    nameof(orderedDatasetLayers));
            }

            EnsureAvailableOutsideDatasetBand(layer, orderedDatasetLayers);
        }

        // Apply only the difference. Every Mapsui layer-collection change
        // raises an event that walks all layers (extent, refresh), so
        // removing and re-inserting a band of hundreds of layers one by one
        // on every compose made loading many datasets quadratic and froze
        // the UI thread.
        var current = CurrentDatasetLayersInOrder();
        if (current.SequenceEqual(orderedDatasetLayers))
        {
            return;
        }

        var removed = current.Where(layer => !replacements.Contains(layer)).ToArray();
        var kept = current.Where(replacements.Contains).ToList();
        var keptInNewOrder = orderedDatasetLayers.Where(_datasetLayers.Contains).ToList();

        if (kept.SequenceEqual(keptInNewOrder))
        {
            // The surviving layers keep their order: drop the removed ones in
            // one change and insert each new layer at its final position.
            var originalStart = FindFirstDatasetIndex();
            if (removed.Length > 0)
            {
                _map.Layers.Remove(removed);
                foreach (var layer in removed)
                {
                    _datasetLayers.Remove(layer);
                }
            }

            var bandStart = kept.Count > 0
                ? FindFirstDatasetIndex()
                : Math.Min(originalStart, _map.Layers.Count);
            for (var i = 0; i < orderedDatasetLayers.Count; i++)
            {
                var layer = orderedDatasetLayers[i];
                if (_datasetLayers.Add(layer))
                {
                    _map.Layers.Insert(bandStart + i, layer, 0);
                }
            }

            return;
        }

        // Reordered: replace the band in one removal and one insertion.
        var insertAt = FindFirstDatasetIndex();
        if (current.Count > 0)
        {
            _map.Layers.Remove(current.ToArray());
        }

        _datasetLayers.Clear();
        insertAt = Math.Min(insertAt, _map.Layers.Count);
        _map.Layers.Insert(insertAt, orderedDatasetLayers, 0);
        foreach (var layer in orderedDatasetLayers)
        {
            _datasetLayers.Add(layer);
        }
    }

    private List<ILayer> CurrentDatasetLayersInOrder()
    {
        var layers = new List<ILayer>(_datasetLayers.Count);
        if (_datasetLayers.Count == 0)
        {
            return layers;
        }

        foreach (var layer in _map.Layers)
        {
            if (_datasetLayers.Contains(layer))
            {
                layers.Add(layer);
            }
        }

        return layers;
    }

    /// <summary>
    /// Adds a layer to the overlay band above all dataset layers.
    /// </summary>
    /// <param name="layer">The overlay layer to add.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="layer"/> belongs to another managed band or is already
    /// present in the map.
    /// </exception>
    public void AddOverlayLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_overlayLayers.Contains(layer))
        {
            return;
        }

        EnsureAvailable(layer);
        var insertAt = FindInsertAfter(_overlayLayers, _datasetLayers, _basemapLayer);
        _map.Layers.Insert(insertAt, layer, 0);
        _overlayLayers.Add(layer);
    }

    /// <summary>
    /// Removes a layer from the overlay band.
    /// </summary>
    /// <param name="layer">The overlay layer to remove.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layer"/> is <see langword="null"/>.
    /// </exception>
    public void RemoveOverlayLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_overlayLayers.Remove(layer))
        {
            _map.Layers.Remove(layer);
        }
    }

    /// <summary>
    /// Adds a layer to the topmost tool band.
    /// </summary>
    /// <param name="layer">The tool layer to add.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="layer"/> belongs to another managed band or is already
    /// present in the map.
    /// </exception>
    public void AddToolLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_toolLayers.Contains(layer))
        {
            return;
        }

        EnsureAvailable(layer);
        _map.Layers.Add(layer);
        _toolLayers.Add(layer);
    }

    /// <summary>
    /// Removes a layer from the tool band.
    /// </summary>
    /// <param name="layer">The tool layer to remove.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="layer"/> is <see langword="null"/>.
    /// </exception>
    public void RemoveToolLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (_toolLayers.Remove(layer))
        {
            _map.Layers.Remove(layer);
        }
    }

    private int FindFirstDatasetIndex()
    {
        var index = 0;
        foreach (var layer in _map.Layers)
        {
            if (_datasetLayers.Contains(layer))
            {
                return index;
            }

            index++;
        }

        return FindInsertAfter(_basemapLayer);
    }

    private int FindInsertAfter(
        HashSet<ILayer> upperBand,
        HashSet<ILayer>? lowerBand = null,
        ILayer? bottomLayer = null)
    {
        var lastIndex = -1;
        var index = 0;
        foreach (var layer in _map.Layers)
        {
            if (upperBand.Contains(layer)
                || (lowerBand?.Contains(layer) ?? false)
                || ReferenceEquals(layer, bottomLayer))
            {
                lastIndex = index;
            }

            index++;
        }

        return lastIndex + 1;
    }

    private int FindInsertAfter(ILayer? layer)
    {
        if (layer is null)
        {
            return 0;
        }

        var index = 0;
        foreach (var candidate in _map.Layers)
        {
            if (ReferenceEquals(candidate, layer))
            {
                return index + 1;
            }

            index++;
        }

        return 0;
    }

    private void EnsureAvailable(ILayer? layer)
    {
        if (layer is null)
        {
            return;
        }

        if (ReferenceEquals(layer, _basemapLayer)
            || _datasetLayers.Contains(layer)
            || _overlayLayers.Contains(layer)
            || _toolLayers.Contains(layer)
            || _map.Layers.Contains(layer))
        {
            throw new ArgumentException(
                "A layer can belong to only one managed layer band.",
                nameof(layer));
        }
    }

    private void EnsureAvailableOutsideDatasetBand(
        ILayer layer,
        IReadOnlyList<ILayer> orderedDatasetLayers)
    {
        if (ReferenceEquals(layer, _basemapLayer)
            || _overlayLayers.Contains(layer)
            || _toolLayers.Contains(layer)
            || (!_datasetLayers.Contains(layer) && _map.Layers.Contains(layer)))
        {
            throw new ArgumentException(
                "A layer can belong to only one managed layer band.",
                nameof(orderedDatasetLayers));
        }
    }
}
