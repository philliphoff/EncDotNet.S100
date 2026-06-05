using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Concrete <see cref="IMapViewportNotifier"/> backed by a Mapsui
/// <c>Navigator</c>. Translates the navigator's native EPSG:3857
/// viewport rectangle into a lat/lon <see cref="MapViewportSnapshot"/>
/// on every <c>ViewportChanged</c> event.
/// </summary>
/// <remarks>
/// <para>
/// The notifier is constructed at DI registration time but is inert
/// until <see cref="Bind"/> is called with a live navigator. This
/// mirrors the existing <c>DynamicFeatureSourceRegistryAccessor</c>
/// pattern: the singleton exists in the container before MainWindow
/// constructs the MapControl, but only starts emitting once it has
/// the navigator to listen to.
/// </para>
/// <para>
/// Re-binding to a different navigator (or the same navigator after
/// a re-init) is safe — the notifier detaches from the previous one
/// before subscribing to the new one.
/// </para>
/// </remarks>
internal sealed class MapViewportNotifier : IMapViewportNotifier, IDisposable
{
    private readonly object _lock = new();
    private Navigator? _navigator;
    private Navigator.ViewportChangedEventHandler? _handler;
    private MapViewportSnapshot? _current;

    /// <inheritdoc />
    public MapViewportSnapshot? Current
    {
        get
        {
            lock (_lock) return _current;
        }
    }

    /// <inheritdoc />
    public event EventHandler<MapViewportSnapshot>? ViewportChanged;

    /// <summary>
    /// Subscribes to <paramref name="navigator"/>'s viewport-change
    /// events. Detaches from any previously-bound navigator first.
    /// Pushes the navigator's current viewport synchronously so
    /// subscribers see an initial snapshot without waiting for the
    /// next user interaction.
    /// </summary>
    public void Bind(Navigator navigator)
    {
        ArgumentNullException.ThrowIfNull(navigator);

        lock (_lock)
        {
            DetachLocked();
            _navigator = navigator;
            _handler = (_, _) => HandleChange(navigator);
            navigator.ViewportChanged += _handler;
        }

        HandleChange(navigator);
    }

    private void HandleChange(Navigator navigator)
    {
        var snapshot = TryProject(navigator.Viewport);
        if (snapshot is null) return;

        lock (_lock) _current = snapshot;
        ViewportChanged?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Builds a lat/lon snapshot from a Mapsui EPSG:3857 viewport.
    /// Returns <see langword="null"/> if the viewport hasn't been
    /// laid out yet (zero width or height) — those events fire
    /// during early window construction and carry no useful data.
    /// </summary>
    internal static MapViewportSnapshot? TryProject(Viewport viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0)
            return null;

        var halfW = viewport.Width * viewport.Resolution / 2.0;
        var halfH = viewport.Height * viewport.Resolution / 2.0;

        var minX = viewport.CenterX - halfW;
        var maxX = viewport.CenterX + halfW;
        var minY = viewport.CenterY - halfH;
        var maxY = viewport.CenterY + halfH;

        // SphericalMercator.ToLonLat clamps Y to the projection's
        // valid range (±20037508.34) and returns lat/lon in degrees.
        var (minLon, minLat) = SphericalMercator.ToLonLat(minX, minY);
        var (maxLon, maxLat) = SphericalMercator.ToLonLat(maxX, maxY);

        return new MapViewportSnapshot
        {
            MinLatitude = minLat,
            MinLongitude = minLon,
            MaxLatitude = maxLat,
            MaxLongitude = maxLon,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            DetachLocked();
            _current = null;
        }
    }

    private void DetachLocked()
    {
        if (_navigator is { } nav && _handler is { } h)
        {
            nav.ViewportChanged -= h;
        }
        _navigator = null;
        _handler = null;
    }
}
