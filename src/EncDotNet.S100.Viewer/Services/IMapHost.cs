using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// A WGS-84 description of a map viewport: the bounding box the viewport
/// covers, its centre, and the equivalent web-mercator zoom level.
/// Returned by <see cref="IMapHost.TryGetViewportWgs84"/>.
/// </summary>
/// <param name="South">South edge in decimal degrees, WGS-84.</param>
/// <param name="West">West edge in decimal degrees, WGS-84.</param>
/// <param name="North">North edge in decimal degrees, WGS-84.</param>
/// <param name="East">East edge in decimal degrees, WGS-84.</param>
/// <param name="CenterLatitude">Viewport-centre latitude in decimal degrees, WGS-84.</param>
/// <param name="CenterLongitude">Viewport-centre longitude in decimal degrees, WGS-84.</param>
/// <param name="Zoom">Equivalent standard web-mercator zoom level.</param>
internal sealed record MapViewportWgs84(
    double South,
    double West,
    double North,
    double East,
    double CenterLatitude,
    double CenterLongitude,
    double Zoom);

/// <summary>
/// Minimal map mutation surface used by services that need to manage
/// dataset layers without taking a hard dependency on
/// <see cref="Mapsui.UI.Avalonia.MapControl"/>. Implemented by
/// <see cref="MapsuiMapHost"/> over a live <c>MapControl</c>; tests can
/// supply a fake.
/// </summary>
/// <remarks>
/// The host carves the Mapsui layer collection into three implicit
/// tiers. The viewer's basemap is added to the live <c>Map.Layers</c>
/// before the host sees any traffic; map-tool overlays are appended to
/// the same collection on demand. Layers added via <see cref="AddLayer"/>
/// are treated as <em>dataset</em> layers and inserted strictly above
/// the basemap and below any overlays. <see cref="ReorderDatasetLayers"/>
/// preserves that invariant — overlays never move when datasets reorder.
/// </remarks>
internal interface IMapHost
{
    /// <summary>
    /// Adds a dataset layer to the map, above the basemap and below
    /// any tool overlays.
    /// </summary>
    void AddLayer(ILayer layer);

    /// <summary>Removes a layer from the map.</summary>
    void RemoveLayer(ILayer layer);

    /// <summary>
    /// Replaces the host's dataset-layer slice with the supplied
    /// sequence, preserving the relative position of the basemap
    /// (below) and any tool overlays (above). The supplied list is
    /// <b>authoritative</b>: any previously-tracked dataset layer not
    /// present in the new sequence is removed from the map, and any
    /// layer in the new sequence that the host has not seen before
    /// (e.g. a rule-filtered MemoryLayer replica) is inserted and
    /// tracked. Callers therefore use this method both to reorder and
    /// to hide/replace dataset layers — including swapping out inactive
    /// datasets when the user toggles their Active flag.
    /// </summary>
    void ReorderDatasetLayers(System.Collections.Generic.IReadOnlyList<ILayer> orderedDatasetLayers);

    /// <summary>
    /// Adds an overlay-tier layer to the map. Overlay layers sit
    /// above all dataset layers and below tool overlays (e.g. the
    /// measure-mode chrome). Intended for push-driven sources
    /// (dynamic feature sources, validation findings overlays) that
    /// want a stable z-order above the dataset stack without being
    /// part of dataset reordering.
    /// </summary>
    void AddOverlayLayer(ILayer layer);

    /// <summary>
    /// Removes an overlay layer previously added via
    /// <see cref="AddOverlayLayer"/>. No-op when the layer was not
    /// tracked as an overlay.
    /// </summary>
    void RemoveOverlayLayer(ILayer layer);

    /// <summary>
    /// Pans/zooms the navigator to the supplied extent (no-op when the
    /// map's navigator is unavailable).
    /// </summary>
    void ZoomToExtent(MRect extent);

    /// <summary>
    /// Sets the navigator's viewport to exactly the supplied Mercator
    /// extent. Unlike <see cref="ZoomToExtent"/> (which adds 10 % padding
    /// for the load-time auto-fit use case), this method applies no
    /// padding — it is intended for programmatic / scripted viewport
    /// overrides where the caller wants the precise box they asked for.
    /// No-op when the map's navigator is unavailable.
    /// </summary>
    void SetViewportToExtent(MRect mercatorExtent);

    /// <summary>
    /// Sets the navigator to centre on the supplied Mercator point at
    /// the supplied resolution (metres per pixel). Used by scripted
    /// viewport overrides that specify a centre + zoom pair instead of
    /// a bounding box. No-op when the map's navigator is unavailable.
    /// </summary>
    void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution);

    /// <summary>
    /// Pans the navigator to centre on the supplied WGS-84 lat/lon
    /// <b>without changing the current zoom level</b> (resolution is
    /// preserved). Intended for UI-driven "reveal this feature"
    /// gestures — e.g. selecting a vessel in the Vessels panel — where
    /// the user wants the target brought into view at the zoom they are
    /// already using. The move is animated over
    /// <paramref name="durationMs"/>. No-op when the map's navigator is
    /// unavailable.
    /// </summary>
    /// <param name="latitudeWgs84">Target centre latitude in WGS-84 degrees.</param>
    /// <param name="longitudeWgs84">Target centre longitude in WGS-84 degrees.</param>
    /// <param name="durationMs">Animation duration in milliseconds (0 = instantaneous).</param>
    void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300);

    /// <summary>
    /// Returns the current viewport centre in WGS-84 degrees, or
    /// <see langword="null"/> when the map's navigator is unavailable or
    /// the viewport has not yet been laid out. Used by UI panels that want
    /// to order content by proximity to what the user is currently looking
    /// at — e.g. the Vessels panel sorts nearest-first relative to the
    /// viewport centre when no own-ship reference position is available.
    /// </summary>
    /// <returns>
    /// The viewport-centre latitude/longitude in WGS-84 degrees, or
    /// <see langword="null"/> when no laid-out viewport exists.
    /// </returns>
    (double Latitude, double Longitude)? TryGetViewportCenterWgs84();

    /// <summary>
    /// Returns the current viewport as a WGS-84 frame (bounding box plus
    /// centre and web-mercator zoom level), or <see langword="null"/>
    /// when the navigator is unavailable or the viewport has not yet been
    /// laid out. Used by the read-only <c>get_viewer_state</c> MCP tool so
    /// scripted runs can assert the live viewport without issuing a
    /// side-effecting <c>set_viewport</c>.
    /// </summary>
    /// <remarks>
    /// Implemented as a default-interface method returning
    /// <see langword="null"/> so existing test doubles need not change;
    /// <see cref="MapsuiMapHost"/> overrides it with the live value.
    /// </remarks>
    MapViewportWgs84? TryGetViewportWgs84() => null;

    /// <summary>
    /// Converts a pixel coordinate in an image of the supplied dimensions
    /// to a WGS-84 lat/lon using the live viewport, mirroring the
    /// world-extent that <see cref="RenderCurrentViewToPngAsync"/> would
    /// capture at the same size. Returns <see langword="null"/> when the
    /// navigator is unavailable or the conversion falls outside valid
    /// WGS-84 ranges. Powers the read-only <c>pick_feature_at</c> MCP tool.
    /// </summary>
    /// <param name="xPixels">Horizontal pixel offset from the image's left edge.</param>
    /// <param name="yPixels">Vertical pixel offset from the image's top edge.</param>
    /// <param name="widthPx">Reference image width in pixels (matches a prior render_to_image call).</param>
    /// <param name="heightPx">Reference image height in pixels.</param>
    /// <remarks>
    /// Implemented as a default-interface method returning
    /// <see langword="null"/> so existing test doubles need not change;
    /// <see cref="MapsuiMapHost"/> overrides it with the live conversion.
    /// </remarks>
    (double Latitude, double Longitude)? TryScreenToWgs84(
        double xPixels, double yPixels, int widthPx, int heightPx) => null;

    /// <summary>
    /// Captures the current map view as a PNG byte array.
    /// </summary>
    /// <param name="widthPx">Output image width in pixels (caller-clamped).</param>
    /// <param name="heightPx">Output image height in pixels (caller-clamped).</param>
    /// <param name="pixelDensity">Display pixel density multiplier (1.0 = device-independent pixels).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// PNG-encoded bytes of the current map state at the requested
    /// size, or <see langword="null"/> when the underlying map has not
    /// been initialised yet. The snapshot mirrors the user's current
    /// viewport, palette, time step, and loaded datasets exactly —
    /// nothing in the live map is mutated by this call.
    /// </returns>
    /// <remarks>
    /// Implementations must be safe to call from any thread; they
    /// marshal to the UI thread as needed.
    /// </remarks>
    System.Threading.Tasks.Task<byte[]?> RenderCurrentViewToPngAsync(
        int widthPx,
        int heightPx,
        double pixelDensity,
        System.Threading.CancellationToken cancellationToken = default);
}
