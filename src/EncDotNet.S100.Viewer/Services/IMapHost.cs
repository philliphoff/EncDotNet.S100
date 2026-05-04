using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Minimal map mutation surface used by services that need to manage
/// dataset layers without taking a hard dependency on
/// <see cref="Mapsui.UI.Avalonia.MapControl"/>. Implemented by
/// <see cref="MapsuiMapHost"/> over a live <c>MapControl</c>; tests can
/// supply a fake.
/// </summary>
internal interface IMapHost
{
    /// <summary>Adds a layer to the map.</summary>
    void AddLayer(ILayer layer);

    /// <summary>Removes a layer from the map.</summary>
    void RemoveLayer(ILayer layer);

    /// <summary>
    /// Pans/zooms the navigator to the supplied extent (no-op when the
    /// map's navigator is unavailable).
    /// </summary>
    void ZoomToExtent(MRect extent);
}
