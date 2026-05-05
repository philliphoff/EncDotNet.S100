using System;
using System.Collections.Generic;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// <see cref="IMapHost"/> implementation backed by a live Mapsui
/// <see cref="MapControl"/>. Created by <see cref="MainWindow"/> after
/// the control has been initialized and handed to consumers via
/// <see cref="IDatasetLoaderService.Initialize"/>.
/// </summary>
internal sealed class MapsuiMapHost : IMapHost
{
    private readonly MapControl _mapControl;

    /// <summary>
    /// Tracks which layers are dataset layers (as opposed to the basemap
    /// or tool overlays). Used to compute the correct insertion point
    /// when a new dataset layer is added and to identify which subset
    /// of <c>Map.Layers</c> to shuffle on reorder.
    /// </summary>
    private readonly HashSet<ILayer> _datasetLayers = new();

    public MapsuiMapHost(MapControl mapControl)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        _mapControl = mapControl;
    }

    public void AddLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var map = _mapControl.Map;
        if (map is null) return;
        if (!_datasetLayers.Add(layer)) return;

        var insertAt = ComputeDatasetInsertIndex(map.Layers);
        map.Layers.Insert(insertAt, layer, 0);
    }

    public void RemoveLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _datasetLayers.Remove(layer);
        _mapControl.Map?.Layers.Remove(layer);
    }

    public void ReorderDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers)
    {
        ArgumentNullException.ThrowIfNull(orderedDatasetLayers);
        var map = _mapControl.Map;
        if (map is null) return;

        // Find the lowest index currently occupied by any dataset layer;
        // that is the slot immediately above the basemap (or below the
        // first overlay if datasets and overlays already coexist).
        int insertAt = -1;
        int i = 0;
        foreach (var existing in map.Layers)
        {
            if (_datasetLayers.Contains(existing))
            {
                insertAt = i;
                break;
            }
            i++;
        }
        if (insertAt < 0) insertAt = Math.Min(1, map.Layers.Count);

        // Remove every dataset layer (in any order) then re-insert in
        // the requested order at the captured base index. Layers in
        // `orderedDatasetLayers` that the host has never seen are
        // skipped — the contract says they're ignored.
        foreach (var l in orderedDatasetLayers)
        {
            if (_datasetLayers.Contains(l))
                map.Layers.Remove(l);
        }
        int idx = insertAt;
        foreach (var l in orderedDatasetLayers)
        {
            if (_datasetLayers.Contains(l))
            {
                if (idx > map.Layers.Count) idx = map.Layers.Count;
                map.Layers.Insert(idx++, l, 0);
            }
        }
    }

    public void ZoomToExtent(MRect extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        if (_mapControl.Map?.Navigator is { } nav)
        {
            nav.ZoomToBox(extent.Grow(extent.Width * 0.1, extent.Height * 0.1));
        }
    }

    /// <summary>
    /// Returns the index at which a new dataset layer should be
    /// inserted: just after the last existing dataset layer, or — when
    /// no dataset layer has been added yet — immediately above the
    /// basemap (index 1). Tool overlays added later via
    /// <c>Map.Layers.Add</c> sort above this band naturally.
    /// </summary>
    private int ComputeDatasetInsertIndex(LayerCollection layers)
    {
        int last = -1;
        int i = 0;
        foreach (var l in layers)
        {
            if (_datasetLayers.Contains(l)) last = i;
            i++;
        }
        if (last >= 0) return last + 1;
        return Math.Min(1, layers.Count);
    }
}
