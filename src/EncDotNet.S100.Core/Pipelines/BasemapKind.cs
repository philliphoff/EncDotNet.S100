namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Selects the basemap drawn beneath the chart data in the headless render path
/// (issue #411). Kept renderer-agnostic in the core so every layer — the scene
/// IR, the Skia renderers, the dataset pipelines, the facade, and the CLI — can
/// name the same option without a backend dependency.
/// </summary>
/// <remarks>
/// Online raster-tile basemaps (available in the interactive viewer) are
/// intentionally absent: they require network access and are out of scope for
/// the offline, Mapsui-free headless path.
/// </remarks>
public enum BasemapKind
{
    /// <summary>
    /// No basemap. The output is byte-for-byte identical to the pre-#411
    /// behaviour: only the chart data is drawn over the background fill.
    /// </summary>
    None = 0,

    /// <summary>
    /// Bundled offline basemap: Natural Earth 1:10m land polygons (public
    /// domain) filled with a muted parchment tone, drawn beneath all chart
    /// layers and projected with the chart's own Web-Mercator viewport so it
    /// registers exactly. Requires no network access.
    /// </summary>
    Offline = 1,
}
