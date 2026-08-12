using System.Diagnostics.CodeAnalysis;
using Avalonia.Threading;
using EncDotNet.S100.DataModel;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Projections;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia;

/// <summary>
/// Adapts a live Mapsui Avalonia control to framework-specific redraw,
/// coordinate conversion, and capture operations.
/// </summary>
/// <remarks>
/// <para>
/// The adapter borrows its control and map. It does not own layers, navigation
/// policy, datasets, or presentation state, and disposing it does not dispose
/// either borrowed object.
/// </para>
/// <para>
/// Use <see cref="EncDotNet.S100.Renderers.Mapsui.MapsuiLayerBands"/> and
/// <see cref="EncDotNet.S100.Renderers.Mapsui.MapsuiMapNavigator"/> directly
/// for framework-neutral layer and navigation behavior.
/// </para>
/// </remarks>
public sealed class AvaloniaMapsuiMapAdapter : IDisposable
{
    private readonly MapControl _mapControl;
    private readonly Map _map;
    private volatile bool _disposed;

    private AvaloniaMapsuiMapAdapter(
        MapControl mapControl,
        Map map)
    {
        _mapControl = mapControl;
        _map = map;
    }

    /// <summary>
    /// Attaches an adapter to an initialized live map control.
    /// </summary>
    /// <param name="mapControl">
    /// The control to adapt. Any Mapsui Avalonia map control works. A
    /// <see cref="CaptureSynchronizedMapControl"/> additionally makes
    /// <see cref="RenderCurrentViewToPngAsync"/> race-safe against live paints;
    /// a plain control captures best-effort (see that method's remarks).
    /// </param>
    /// <returns>A disposable adapter that borrows the control and its map.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mapControl"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The call is not on Avalonia's UI thread, or the control has no map.
    /// </exception>
    public static AvaloniaMapsuiMapAdapter Attach(
        MapControl mapControl)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException(
                "A Mapsui Avalonia map adapter must be attached on the UI thread.");
        }

        var map = mapControl.Map
            ?? throw new InvalidOperationException(
                "The Mapsui map control must have a map before attaching an adapter.");
        return new AvaloniaMapsuiMapAdapter(mapControl, map);
    }

    /// <summary>
    /// Schedules a live map redraw on Avalonia's UI thread.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The adapter is detached.</exception>
    public void RequestRedraw()
    {
        ThrowIfDisposed();
        if (Dispatcher.UIThread.CheckAccess())
        {
            EnsureMapAttached();
            _mapControl.RefreshGraphics();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && ReferenceEquals(_mapControl.Map, _map))
            {
                _mapControl.RefreshGraphics();
            }
        });
    }

    /// <summary>
    /// Returns the laid-out live viewport size in pixels.
    /// </summary>
    /// <returns>
    /// Width and height, or <see langword="null"/> when the viewport is not
    /// laid out.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The adapter is detached.</exception>
    public (double Width, double Height)? TryGetViewportSizePx() =>
        InvokeOnUiThread<(double Width, double Height)?>(() =>
        {
            var viewport = EnsureMapAttached().Navigator.Viewport;
            return viewport.Width > 0 && viewport.Height > 0
                ? (viewport.Width, viewport.Height)
                : null;
        });

    /// <summary>
    /// Converts a live viewport pixel to WGS-84.
    /// </summary>
    /// <param name="xPx">Horizontal live-control pixel coordinate.</param>
    /// <param name="yPx">Vertical live-control pixel coordinate.</param>
    /// <returns>
    /// The geographic position, or <see langword="null"/> for invalid input or
    /// an unlaid-out viewport.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The adapter is detached.</exception>
    public GeoPosition? TryScreenToWgs84(double xPx, double yPx) =>
        InvokeOnUiThread(() =>
        {
            if (!double.IsFinite(xPx) || !double.IsFinite(yPx))
            {
                return null;
            }

            var map = EnsureMapAttached();
            var viewport = map.Navigator.Viewport;
            if (viewport.Width <= 0 || viewport.Height <= 0)
            {
                return null;
            }

            return ToWgs84(
                viewport.ScreenToWorld(xPx, yPx),
                map.CRS);
        });

    /// <summary>
    /// The default point/curve pick tolerance in metres, mirroring
    /// <see cref="EncDotNet.S100.Renderers.Mapsui.GeographicPickQuery.RadiusMeters"/>.
    /// </summary>
    public const double DefaultPickRadiusMeters = 50.0;

    /// <summary>
    /// Picks the S-100 features and coverage samples under a live-viewport pixel
    /// by translating it to a geographic query against a session's
    /// <see cref="EncDotNet.S100.Renderers.Mapsui.IS100MapQuery"/>.
    /// </summary>
    /// <remarks>
    /// This is the UI-framework interaction adapter for
    /// <see cref="EncDotNet.S100.Renderers.Mapsui.IS100MapQuery.PickAsync"/>: it
    /// reads the live viewport on the UI thread to convert the pixel to WGS-84
    /// and to capture the current resolution (so the session can drop cells
    /// scaled out at this zoom), then runs the pick off the UI thread. Pointer
    /// gestures, hit panels, and selection remain the host's responsibility.
    /// </remarks>
    /// <param name="query">The session query surface to pick against.</param>
    /// <param name="xPx">Horizontal live-control pixel coordinate.</param>
    /// <param name="yPx">Vertical live-control pixel coordinate.</param>
    /// <param name="radiusMeters">
    /// Point/curve tolerance in metres; defaults to
    /// <see cref="DefaultPickRadiusMeters"/>.
    /// </param>
    /// <param name="maxResults">
    /// Optional cap on the returned picks (topmost-first).
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// The ranked picks, topmost-first — empty when the pixel is invalid, the
    /// viewport is not laid out, or the map CRS is unsupported.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="query"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The adapter is detached.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    public async Task<IReadOnlyList<S100Pick>> PickAtScreenAsync(
        IS100MapQuery query,
        double xPx,
        double yPx,
        double radiusMeters = DefaultPickRadiusMeters,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var pickQuery = InvokeOnUiThread(
            () => TryBuildScreenPickQuery(xPx, yPx, radiusMeters, maxResults));
        if (pickQuery is null)
        {
            return Array.Empty<S100Pick>();
        }

        // Only a UI-thread caller can capture the UI sync-context. The default
        // MapsuiMapSession.PickAsync resumes with ConfigureAwait(true); invoked
        // directly from the UI thread its continuation would post back to that
        // thread and a synchronous waiter (.GetAwaiter().GetResult()) would
        // deadlock, so dispatch it onto the thread pool. Off the UI thread there
        // is no context to capture, so call directly and skip the extra hop.
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await Task.Run(
                () => query.PickAsync(pickQuery, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        return await query.PickAsync(pickQuery, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a current-view snapshot pixel to WGS-84 using the same extent,
    /// fit, and rotation rules as <see cref="RenderCurrentViewToPngAsync"/>.
    /// </summary>
    /// <param name="xPx">Horizontal snapshot pixel coordinate.</param>
    /// <param name="yPx">Vertical snapshot pixel coordinate.</param>
    /// <param name="imageWidthPx">Snapshot width in pixels.</param>
    /// <param name="imageHeightPx">Snapshot height in pixels.</param>
    /// <returns>
    /// The geographic position, or <see langword="null"/> for invalid input or
    /// an unlaid-out viewport.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The adapter is detached.</exception>
    public GeoPosition? TryImagePixelToWgs84(
        double xPx,
        double yPx,
        int imageWidthPx,
        int imageHeightPx) =>
        InvokeOnUiThread(() =>
        {
            if (!double.IsFinite(xPx)
                || !double.IsFinite(yPx)
                || imageWidthPx <= 0
                || imageHeightPx <= 0)
            {
                return null;
            }

            var liveMap = EnsureMapAttached();
            if (!TryConfigureSnapshotMap(
                liveMap,
                imageWidthPx,
                imageHeightPx,
                out var snapshot))
            {
                return null;
            }

            using (snapshot)
            {
                var world = snapshot.Navigator.Viewport.ScreenToWorld(xPx, yPx);
                return ToWgs84(world, snapshot.CRS);
            }
        });

    /// <summary>
    /// Renders the current live map view to PNG bytes without mutating the live
    /// navigator.
    /// </summary>
    /// <remarks>
    /// When the adapted control is a <see cref="CaptureSynchronizedMapControl"/>,
    /// the capture is serialized against the control's live paint so it never
    /// reads shared Skia/GPU resources mid-frame. Over a plain Mapsui map control
    /// there is no gate to synchronize against, so the capture runs best-effort:
    /// on a quiescent map it renders the current view, but it is not serialized
    /// against a concurrent live paint touching the shared render resources, so
    /// under active repaint the result can, in rare cases, be torn or partial.
    /// Hosts that capture under an actively repainting map should adapt a
    /// <see cref="CaptureSynchronizedMapControl"/>.
    /// </remarks>
    /// <param name="widthPx">Output width in pixels.</param>
    /// <param name="heightPx">Output height in pixels.</param>
    /// <param name="pixelDensity">Output pixel-density multiplier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// PNG bytes, or <see langword="null"/> when the live viewport is not laid
    /// out.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// An output dimension or <paramref name="pixelDensity"/> is not positive
    /// and finite.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The adapter is detached.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    public async Task<byte[]?> RenderCurrentViewToPngAsync(
        int widthPx,
        int heightPx,
        double pixelDensity,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        if (!double.IsFinite(pixelDensity) || pixelDensity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelDensity),
                pixelDensity,
                "Pixel density must be positive and finite.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await CaptureCoordinator.CaptureDrainedAsync(
            () => InvokeOnUiThreadAsync(_mapControl.InvalidateVisual),
            () => InvokeOnUiThreadAsync(
                () => RenderCurrentViewToPngOnUiThread(
                    widthPx,
                    heightPx,
                    pixelDensity,
                    cancellationToken)),
            cancellationToken,
            // Only a capture-synchronized control drains the live paint through
            // its render markers; over a plain control there is no drain to wait
            // for, so capture unsynchronized (best-effort) rather than stalling.
            synchronized: _mapControl is CaptureSynchronizedMapControl).ConfigureAwait(false);
    }

    /// <summary>
    /// Detaches the adapter without disposing the borrowed control or map.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }

    /// <summary>
    /// Builds a geographic pick query from a live-viewport pixel on the UI
    /// thread, or returns <see langword="null"/> when the pixel is invalid, the
    /// viewport is not laid out, or the map CRS is unsupported.
    /// </summary>
    private GeographicPickQuery? TryBuildScreenPickQuery(
        double xPx,
        double yPx,
        double radiusMeters,
        int? maxResults)
    {
        if (!double.IsFinite(xPx) || !double.IsFinite(yPx))
        {
            return null;
        }

        var map = EnsureMapAttached();
        var viewport = map.Navigator.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return null;
        }

        if (ToWgs84(viewport.ScreenToWorld(xPx, yPx), map.CRS) is not { } position)
        {
            return null;
        }

        return new GeographicPickQuery
        {
            Latitude = position.Latitude,
            Longitude = position.Longitude,
            RadiusMeters = radiusMeters,
            // The live resolution lets the session's whole-cell scale window
            // exclude cells scaled out at the current zoom.
            Resolution = viewport.Resolution,
            MaxResults = maxResults,
        };
    }

    private static GeoPosition? ToWgs84(MPoint world, string? sourceCrs)
    {
        if (!string.Equals(
            sourceCrs,
            "EPSG:3857",
            StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var (longitude, latitude) = SphericalMercator.ToLonLat(world.X, world.Y);
        var position = new GeoPosition(latitude, longitude);
        return double.IsFinite(position.Latitude)
            && double.IsFinite(position.Longitude)
            && position.Latitude is >= -90.0 and <= 90.0
            && position.Longitude is >= -180.0 and <= 180.0
                ? position
                : null;
    }

    private byte[]? RenderCurrentViewToPngOnUiThread(
        int widthPx,
        int heightPx,
        double pixelDensity,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var liveMap = EnsureMapAttached();
        if (!TryConfigureSnapshotMap(
            liveMap,
            widthPx,
            heightPx,
            out var snapshot))
        {
            return null;
        }

        using (snapshot)
        using (var stream = new MapRenderer().RenderToBitmapStream(
            snapshot.Navigator.Viewport,
            liveMap.Layers,
            liveMap.RenderService,
            liveMap.BackColor,
            pixelDensity: (float)pixelDensity,
            widgets: snapshot.Widgets,
            renderFormat: RenderFormat.Png,
            quality: 100))
        {
            stream.Position = 0;
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }
    }

    internal static bool TryConfigureSnapshotMap(
        Map liveMap,
        int widthPx,
        int heightPx,
        [NotNullWhen(true)] out Map? snapshot)
    {
        var liveViewport = liveMap.Navigator.Viewport;
        if (liveViewport.Width <= 0 || liveViewport.Height <= 0)
        {
            snapshot = null;
            return false;
        }

        var extent = liveViewport.ToExtent();
        if (extent is null || extent.Width <= 0 || extent.Height <= 0)
        {
            snapshot = null;
            return false;
        }

        snapshot = new Map
        {
            BackColor = liveMap.BackColor,
            CRS = liveMap.CRS,
        };
        snapshot.Navigator.SetSize(widthPx, heightPx);
        snapshot.Navigator.ZoomToBox(extent, MBoxFit.Fit);
        if (liveViewport.Rotation != 0)
        {
            snapshot.Navigator.RotateTo(liveViewport.Rotation, duration: 0);
        }

        return true;
    }

    private static Task InvokeOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private static Task<T> InvokeOnUiThreadAsync<T>(Func<T> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(action());
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private T InvokeOnUiThread<T>(Func<T> action)
    {
        ThrowIfDisposed();
        if (Dispatcher.UIThread.CheckAccess())
        {
            return action();
        }

        // The Viewer capability contracts are synchronous. Block only the
        // calling worker while Avalonia reads the live control state.
        return Dispatcher.UIThread.InvokeAsync(action).GetTask().GetAwaiter().GetResult();
    }

    private Map EnsureMapAttached()
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(_mapControl.Map, _map))
        {
            throw new InvalidOperationException(
                "The map control's map changed after the Avalonia adapter was attached.");
        }

        return _map;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
