using Avalonia.Threading;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Diagnostics;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Focused map capability implementation backed by a live Mapsui
/// <see cref="MapControl"/>.
/// </summary>
/// <remarks>
/// Layer ownership remains delegated to <see cref="MapsuiLayerBands"/>. This
/// adapter supplies only the Avalonia control, dispatcher, and capture behavior
/// that cannot live in the reusable Mapsui layer-band component.
/// </remarks>
internal sealed class MapsuiMapHost :
    IMapLayerCollection,
    IMapViewportController,
    IMapCoordinateConverter,
    IMapSnapshotRenderer,
    IMapInvalidator
{
    private readonly MapControl _mapControl;
    private readonly MapsuiLayerBands _layerBands;

    public MapsuiMapHost(MapControl mapControl)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        _mapControl = mapControl;
        _layerBands = new MapsuiLayerBands(
            mapControl.Map
            ?? throw new InvalidOperationException(
                "The Mapsui map control must have a map before creating its host."));
        RenderSubsystem = ChartRenderSubsystemFactory.CreateActive();
        RenderSubsystem.Activate();
    }

    /// <inheritdoc />
    public IChartRenderSubsystem RenderSubsystem { get; }

    public void AddDatasetLayer(ILayer layer)
    {
        _layerBands.AddDatasetLayer(layer);
    }

    public void RemoveDatasetLayer(ILayer layer)
    {
        _layerBands.RemoveDatasetLayer(layer);
    }

    public void ReplaceDatasetLayers(IReadOnlyList<ILayer> orderedDatasetLayers)
    {
        _layerBands.ReplaceDatasetLayers(orderedDatasetLayers);
    }

    public void SetBasemapLayer(ILayer? layer) => _layerBands.SetBasemapLayer(layer);

    public void AddToolLayer(ILayer layer) => _layerBands.AddToolLayer(layer);

    public void RemoveToolLayer(ILayer layer) => _layerBands.RemoveToolLayer(layer);

    public void RequestRedraw()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _mapControl.RefreshGraphics();
            return;
        }

        Dispatcher.UIThread.Post(_mapControl.RefreshGraphics);
    }

    public void ZoomToExtent(MRect extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        if (_mapControl.Map?.Navigator is { } nav)
        {
            nav.ZoomToBox(extent.Grow(extent.Width * 0.1, extent.Height * 0.1));
        }
    }

    public void SetViewportToExtent(MRect mercatorExtent)
    {
        ArgumentNullException.ThrowIfNull(mercatorExtent);
        if (_mapControl.Map?.Navigator is { } nav)
        {
            // duration: 0 for an instantaneous, scripted viewport set —
            // animations would prevent reproducible measurement runs.
            nav.ZoomToBox(mercatorExtent, duration: 0);
        }
    }

    public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution)
    {
        ArgumentNullException.ThrowIfNull(mercatorCenter);
        if (_mapControl.Map?.Navigator is { } nav)
        {
            nav.CenterOnAndZoomTo(mercatorCenter, resolution, duration: 0);
        }
    }

    public void SetRotation(double degrees)
    {
        if (_mapControl.Map?.Navigator is { } nav)
        {
            // duration: 0 for an instantaneous, scripted rotation — animations
            // would prevent reproducible measurement / capture runs.
            nav.RotateTo(degrees, duration: 0);
        }
    }

    public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300)
    {
        if (double.IsNaN(latitudeWgs84) || double.IsNaN(longitudeWgs84)
            || double.IsInfinity(latitudeWgs84) || double.IsInfinity(longitudeWgs84)
            || latitudeWgs84 < -90.0 || latitudeWgs84 > 90.0)
        {
            return;
        }

        if (_mapControl.Map?.Navigator is not { } nav)
            return;

        var (x, y) = SphericalMercator.FromLonLat(longitudeWgs84, latitudeWgs84);
        // CenterOn keeps the current resolution, so the zoom level is
        // preserved — only the viewport centre moves.
        nav.CenterOn(x, y, durationMs);
    }

    public GeoPosition? TryGetViewportCenterWgs84()
    {
        if (_mapControl.Map?.Navigator is not { } nav)
            return null;

        var viewport = nav.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return null;

        var (lon, lat) = SphericalMercator.ToLonLat(viewport.CenterX, viewport.CenterY);
        if (double.IsNaN(lat) || double.IsNaN(lon) || lat < -90.0 || lat > 90.0)
            return null;

        return new GeoPosition(lat, lon);
    }

    public (double Width, double Height)? TryGetViewportSizePx()
    {
        if (_mapControl.Map?.Navigator is not { } nav)
            return null;

        var viewport = nav.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return null;

        return (viewport.Width, viewport.Height);
    }

    public GeoPosition? TryScreenToWgs84(double xPx, double yPx)
    {
        if (double.IsNaN(xPx) || double.IsNaN(yPx) || double.IsInfinity(xPx) || double.IsInfinity(yPx))
            return null;

        if (_mapControl.Map?.Navigator is not { } nav)
            return null;

        var viewport = nav.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return null;

        var world = viewport.ScreenToWorld(xPx, yPx);
        var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
        if (double.IsNaN(lat) || double.IsNaN(lon)
            || lat < -90.0 || lat > 90.0 || lon < -180.0 || lon > 180.0)
        {
            return null;
        }

        return new GeoPosition(lat, lon);
    }

    public GeoPosition? TryImagePixelToWgs84(
        double xPx, double yPx, int imageWidthPx, int imageHeightPx)
    {
        if (double.IsNaN(xPx) || double.IsNaN(yPx) || double.IsInfinity(xPx) || double.IsInfinity(yPx))
            return null;

        if (imageWidthPx <= 0 || imageHeightPx <= 0)
            return null;

        if (_mapControl.Map?.Navigator is not { } liveNav)
            return null;

        var liveViewport = liveNav.Viewport;
        if (liveViewport.Width <= 0 || liveViewport.Height <= 0)
            return null;

        // Reproduce the exact geometry render_to_image uses: a navigator
        // sized to the requested image, zoomed to the live viewport's
        // world extent with MBoxFit.Fit (aspect mismatches show slightly
        // more area, never crop). This makes a pixel measured on the
        // captured PNG resolve to the same ground point even when the
        // capture's size / aspect differs from the live on-screen frame.
        var extent = liveViewport.ToExtent();
        if (extent is null || extent.Width <= 0 || extent.Height <= 0)
            return null;

        using var probe = new Map();
        probe.Navigator.SetSize(imageWidthPx, imageHeightPx);
        probe.Navigator.ZoomToBox(extent, MBoxFit.Fit);

        var world = probe.Navigator.Viewport.ScreenToWorld(xPx, yPx);
        var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
        if (double.IsNaN(lat) || double.IsNaN(lon)
            || lat < -90.0 || lat > 90.0 || lon < -180.0 || lon > 180.0)
        {
            return null;
        }

        return new GeoPosition(lat, lon);
    }

    public void AddOverlayLayer(ILayer layer)
    {
        _layerBands.AddOverlayLayer(layer);
    }

    public void RemoveOverlayLayer(ILayer layer)
    {
        _layerBands.RemoveOverlayLayer(layer);
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
            //
            // PERF: the snapshot Map is IDisposable. Prior to this
            // change it was let go to the GC, which left subscriptions
            // from its layer-collection / property-changed plumbing
            // rooting it indefinitely; over the course of many
            // render_to_image calls the per-call PNG buffer plus
            // associated native bitmaps could not be reclaimed
            // (RSS grew ~9× over 150 renders in the perf report's
            // Track-B measurements). We now detach the live layers
            // from the snapshot before disposing it so the snapshot's
            // dispose path only touches its own owned resources, not
            // the live layers we don't own.
            var snapshot = new Map { CRS = liveMap.CRS, BackColor = liveMap.BackColor };
            try
            {
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

                // Carry the live viewport rotation onto the snapshot so a
                // rotated on-screen view is captured rotated (the base chart
                // turns; the screen-space symbol/label overlay holds upright).
                // Without this the snapshot would render north-up — flattening
                // the rotation and making the rotated overlay path unverifiable
                // via render_to_image. ToExtent() above is the rotated view's
                // axis-aligned bound, so fitting it then rotating shows the same
                // content slightly zoomed out (matching the "slightly more area"
                // contract). duration: 0 keeps the capture deterministic.
                if (liveViewport.Rotation != 0)
                {
                    snapshot.Navigator.RotateTo(liveViewport.Rotation, duration: 0);
                }

                // Serialise the Skia render against the live on-screen
                // paint. Both share the live layers' cached SKImage symbol
                // textures; on a GPU-backed build a concurrent live paint
                // uploading those images crashes in
                // sk_image_make_texture_image (issue #337). The shared
                // CaptureDrained protocol marks a capture pending, forces one
                // fully-drained live frame, then holds the gate for the
                // offscreen render.
                return RenderGate.CaptureDrained(
                    () => _mapControl.InvalidateVisual(),
                    () =>
                    {
                        using var stream = new MapRenderer().RenderToBitmapStream(
                            snapshot,
                            pixelDensity: (float)pixelDensity,
                            renderFormat: RenderFormat.Png,
                            quality: 100);
                        stream.Position = 0;
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    });
            }
            finally
            {
                // Detach the live layers from the snapshot before the
                // snapshot is disposed, so the snapshot's dispose path
                // (and any teardown subscriptions Mapsui has registered
                // on layer collection mutations) cannot release / dispose
                // layer instances the live Map still owns. Best-effort:
                // we never want a teardown failure to mask a render
                // failure or otherwise crash the dispatcher.
                try
                {
                    snapshot.Layers.ClearAllGroups();
                }
                catch
                {
                    // ignore — see comment above
                }
                snapshot.Dispose();
            }
        }).GetTask().ConfigureAwait(false);
    }

}
