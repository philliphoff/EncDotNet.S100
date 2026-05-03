using System;
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

    public MapsuiMapHost(MapControl mapControl)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        _mapControl = mapControl;
    }

    public void AddLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _mapControl.Map?.Layers.Add(layer);
    }

    public void RemoveLayer(ILayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _mapControl.Map?.Layers.Remove(layer);
    }

    public void ZoomToExtent(MRect extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        if (_mapControl.Map?.Navigator is { } nav)
        {
            nav.ZoomToBox(extent.Grow(extent.Width * 0.1, extent.Height * 0.1));
        }
    }
}
