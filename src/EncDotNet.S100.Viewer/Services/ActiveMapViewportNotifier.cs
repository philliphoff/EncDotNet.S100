namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The window-level <see cref="IMapViewportNotifier"/>: the viewport of
/// whichever map is active in <see cref="MapViews"/> (split map, #1730).
/// </summary>
/// <remarks>
/// When the active map changes, the notifier rebinds to the new map's own
/// notifier and raises <see cref="ViewportChanged"/> with its viewport, so a
/// consumer sees the switch as the view moving. Events from a map that is no
/// longer active are dropped.
/// </remarks>
internal sealed class ActiveMapViewportNotifier : IMapViewportNotifier, IDisposable
{
    private readonly MapViews _views;
    private readonly Lock _lock = new();
    private IMapViewportNotifier? _bound;
    private bool _disposed;

    /// <summary>Starts following the active map of <paramref name="views"/>.</summary>
    /// <param name="views">The maps.</param>
    public ActiveMapViewportNotifier(MapViews views)
    {
        ArgumentNullException.ThrowIfNull(views);
        _views = views;
        _views.ActiveChanged += OnActiveChanged;
        Rebind();
    }

    /// <inheritdoc />
    public MapViewportSnapshot? Current
    {
        get
        {
            lock (_lock)
                return _bound?.Current;
        }
    }

    /// <inheritdoc />
    public event EventHandler<MapViewportSnapshot>? ViewportChanged;

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_bound is not null)
                _bound.ViewportChanged -= OnBoundViewportChanged;
            _bound = null;
        }

        _views.ActiveChanged -= OnActiveChanged;
    }

    private void OnActiveChanged(object? sender, EventArgs e) => Rebind();

    private void Rebind()
    {
        MapViewportSnapshot? current;
        lock (_lock)
        {
            if (_disposed)
                return;
            var next = _views.Active?.Viewport;
            if (ReferenceEquals(next, _bound))
                return;
            if (_bound is not null)
                _bound.ViewportChanged -= OnBoundViewportChanged;
            _bound = next;
            if (_bound is not null)
                _bound.ViewportChanged += OnBoundViewportChanged;
            current = _bound?.Current;
        }

        if (current is not null)
            ViewportChanged?.Invoke(this, current);
    }

    private void OnBoundViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        lock (_lock)
        {
            if (_disposed || !ReferenceEquals(sender, _bound))
                return;
        }

        ViewportChanged?.Invoke(this, snapshot);
    }
}
