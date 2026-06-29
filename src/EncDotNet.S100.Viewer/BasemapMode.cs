namespace EncDotNet.S100.Viewer;

/// <summary>
/// Selects the basemap drawn beneath the chart data (issue #295).
/// </summary>
public enum BasemapMode
{
    /// <summary>
    /// No basemap. The map control's water-coloured background shows
    /// through; the lightest, fully offline option.
    /// </summary>
    None,

    /// <summary>
    /// Bundled offline basemap: Natural Earth 1:10m land polygons
    /// (public domain). Requires no network access and is the default.
    /// </summary>
    Offline,

    /// <summary>
    /// Online OpenStreetMap raster tiles. Requires Internet access;
    /// previously viewed areas may persist via the on-disk tile cache.
    /// </summary>
    Online,
}
