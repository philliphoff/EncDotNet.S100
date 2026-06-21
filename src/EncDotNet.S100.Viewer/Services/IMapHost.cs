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
    /// The active base-plane chart render subsystem (the A/B switch for the
    /// tiled/async render-subsystem redesign). Selected at construction from
    /// <see cref="EncDotNet.S100.Renderers.Mapsui.RenderingOptimizations.RenderSubsystem"/>
    /// and held here so the viewer "switches on" it at the host seam, per the
    /// render-subsystem design. In Phase&#160;0 both arms still draw through this
    /// host's Mapsui layers; the subsystem exposes identity, lifecycle, and a
    /// telemetry handle.
    /// </summary>
    IChartRenderSubsystem RenderSubsystem { get; }

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
    /// Returns the current on-screen viewport size in device-independent
    /// pixels, or <see langword="null"/> when the map's navigator is
    /// unavailable or the viewport has not yet been laid out. Used by the
    /// inverse-pick MCP tool to validate a requested screen coordinate
    /// against the live frame before converting it to a geographic point.
    /// </summary>
    (double Width, double Height)? TryGetViewportSizePx();

    /// <summary>
    /// Converts a screen pixel (relative to the live on-screen viewport's
    /// top-left, in device-independent pixels) to a WGS-84 lat/lon, or
    /// returns <see langword="null"/> when the map's navigator is
    /// unavailable, the viewport has not been laid out, or the point
    /// projects outside the valid Mercator domain. This is the inverse of
    /// the projection used to render the live map and underpins the
    /// <c>pick_features</c> MCP tool (the screen-xy → feature loop that
    /// complements <c>render_to_image</c>).
    /// </summary>
    /// <param name="xPx">Screen X in device-independent pixels from the viewport's left edge.</param>
    /// <param name="yPx">Screen Y in device-independent pixels from the viewport's top edge.</param>
    (double Latitude, double Longitude)? TryScreenToWgs84(double xPx, double yPx);

    /// <summary>
    /// Converts a pixel from a <c>render_to_image</c> capture back to a
    /// WGS-84 lat/lon — the faithful inverse of that tool at <em>any</em>
    /// capture size. It reproduces the snapshot geometry
    /// <see cref="RenderCurrentViewToPngAsync"/> uses (a navigator sized to
    /// the supplied image dimensions, fit to the live viewport's extent
    /// with <c>MBoxFit.Fit</c>), so a pixel measured on the returned PNG
    /// maps to the same ground point regardless of how the capture's size
    /// or aspect ratio differs from the live on-screen viewport.
    /// </summary>
    /// <param name="xPx">Pixel X from the capture's left edge, in the capture's logical pixel space.</param>
    /// <param name="yPx">Pixel Y from the capture's top edge, in the capture's logical pixel space.</param>
    /// <param name="imageWidthPx">Logical width the capture was rendered at (the <c>width</c> echoed by <c>render_to_image</c>).</param>
    /// <param name="imageHeightPx">Logical height the capture was rendered at (the <c>height</c> echoed by <c>render_to_image</c>).</param>
    /// <returns>
    /// The WGS-84 lat/lon under the pixel, or <see langword="null"/> when
    /// the navigator is unavailable, the live viewport has not been laid
    /// out, the image dimensions are non-positive, or the point projects
    /// outside the valid Mercator domain.
    /// </returns>
    (double Latitude, double Longitude)? TryImagePixelToWgs84(
        double xPx, double yPx, int imageWidthPx, int imageHeightPx);

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
