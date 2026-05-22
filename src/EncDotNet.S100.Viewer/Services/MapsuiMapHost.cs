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

        // PR-L3 fix: treat the supplied list as the **authoritative**
        // dataset-layer slice of <c>map.Layers</c>.
        //
        // 1. Any previously-known dataset layer that is NOT in the new
        //    list must be removed from the map (e.g. a dataset whose
        //    Active flag was just toggled off, or whose original layer
        //    instance was just replaced by a rule-filtered MemoryLayer
        //    such as the one produced by R-101-102-B).
        // 2. Conversely, layers in the new list that the host has not
        //    seen before — typically the rule-filtered replicas — must
        //    be inserted *and* tracked so the next reorder cycle treats
        //    them correctly.
        foreach (var existing in _datasetLayers)
        {
            map.Layers.Remove(existing);
        }
        _datasetLayers.Clear();

        int idx = insertAt;
        foreach (var l in orderedDatasetLayers)
        {
            if (l is null) continue;
            if (idx > map.Layers.Count) idx = map.Layers.Count;
            map.Layers.Insert(idx++, l, 0);
            _datasetLayers.Add(l);
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
