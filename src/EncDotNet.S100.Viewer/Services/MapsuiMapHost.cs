using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
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

    /// <summary>
    /// Tracks overlay-tier layers added via
    /// <see cref="AddOverlayLayer"/> — distinct from tool overlays
    /// (e.g. measure chrome) which the viewer adds straight to
    /// <c>Map.Layers</c>. Overlay-tier layers sit above the
    /// dataset slice but below any subsequently-added tool overlays.
    /// </summary>
    private readonly HashSet<ILayer> _overlayLayers = new();

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

    public void AddOverlayLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var map = _mapControl.Map;
        if (map is null) return;
        if (!_overlayLayers.Add(layer)) return;

        // Insert above the dataset slice but below any subsequently-
        // added tool overlays. We compute the slot as "after the last
        // tracked dataset layer", which keeps the overlay stable even
        // if the dataset slice is reordered later.
        var insertAt = ComputeOverlayInsertIndex(map.Layers);
        map.Layers.Insert(insertAt, layer, 0);
    }

    public void RemoveOverlayLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (!_overlayLayers.Remove(layer)) return;
        _mapControl.Map?.Layers.Remove(layer);
    }

    /// <inheritdoc />
    public async Task<byte[]?> RenderCurrentViewToPngAsync(
        int widthPx,
        int heightPx,
        double pixelDensity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Marshal to the UI thread: Mapsui's Map/Navigator state must
        // not be read or mutated concurrently with the live control's
        // own render loop, and Avalonia layers are UI-affine.
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var liveMap = _mapControl.Map;
            if (liveMap is null) return null;

            var liveNav = liveMap.Navigator;
            var liveViewport = liveNav.Viewport;
            if (liveViewport.Width <= 0 || liveViewport.Height <= 0) return null;

            // Build a snapshot Map that shares the live Layers list (so
            // styles, time-step content, palette switches, and any other
            // mutable per-layer state mirror the user's current view
            // exactly) but owns its own Navigator. The live map is
            // therefore untouched: setting size / zoom on the clone
            // does not trigger a redraw on screen.
            var snapshot = new Map { CRS = liveMap.CRS, BackColor = liveMap.BackColor };
            foreach (var layer in liveMap.Layers)
            {
                snapshot.Layers.Add(layer);
            }

            snapshot.Navigator.SetSize(widthPx, heightPx);

            // Match the world-extent the user currently sees. With
            // MBoxFit.Fit, aspect-ratio mismatches show slightly more
            // area rather than cropping — acceptable for diagnostic
            // snapshots; the requested pixel dimensions are exact.
            var extent = liveViewport.ToExtent();
            if (extent is not null && extent.Width > 0 && extent.Height > 0)
            {
                snapshot.Navigator.ZoomToBox(extent, MBoxFit.Fit);
            }

            using var stream = new MapRenderer().RenderToBitmapStream(
                snapshot,
                pixelDensity: (float)pixelDensity,
                renderFormat: RenderFormat.Png,
                quality: 100);
            stream.Position = 0;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }).GetTask().ConfigureAwait(false);
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

    /// <summary>
    /// Returns the index at which a new overlay-tier layer should be
    /// inserted: just after the last existing dataset or overlay
    /// layer the host tracks, or — when no such layer is present —
    /// immediately above the basemap (index 1). Layers added straight
    /// to <c>Map.Layers</c> by callers other than the host
    /// (e.g. tool chrome) sort above this band naturally because
    /// they were added later.
    /// </summary>
    private int ComputeOverlayInsertIndex(LayerCollection layers)
    {
        int last = -1;
        int i = 0;
        foreach (var l in layers)
        {
            if (_datasetLayers.Contains(l) || _overlayLayers.Contains(l)) last = i;
            i++;
        }
        if (last >= 0) return last + 1;
        return Math.Min(1, layers.Count);
    }
}
