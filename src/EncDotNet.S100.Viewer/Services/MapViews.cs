using EncDotNet.S100.Rendering.Engine.Prediction;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The maps in the window and which one is active (split map, #1730; dual-map
/// design note §2.2).
/// </summary>
/// <remarks>
/// <para>
/// The singletons that stand for "the map" follow the active map: when it
/// changes, the <see cref="MapCapabilityAccessor{TCapability}"/> values for
/// <see cref="IMapCoordinateConverter"/>, <see cref="IMapViewportController"/>
/// and <see cref="IImageRenderer"/> are rebound to it, and
/// <see cref="ActiveMapViewportNotifier"/> follows it. The image renderer is
/// the MCP readiness gate, so it's bound after the other two and cleared
/// before them.
/// </para>
/// <para>
/// The registry is also the <see cref="IMapViewportSet"/> over every map, and
/// keeps every map's planner in the shared <see cref="DemandPlannerSet"/>.
/// The first map added becomes active. Removing the active map makes the one
/// before it active (or the next one when it was first).
/// </para>
/// </remarks>
internal sealed class MapViews : IMapViewportSet
{
    private readonly MapCapabilityAccessor<IMapViewportController> _viewport;
    private readonly MapCapabilityAccessor<IMapCoordinateConverter> _converter;
    private readonly MapCapabilityAccessor<IImageRenderer> _renderer;
    private readonly DemandPlannerSet _planners;
    private readonly Lock _lock = new();
    private readonly List<Registered> _views = [];
    private IMapViewCapabilities? _active;

    /// <summary>Creates an empty registry.</summary>
    /// <param name="viewport">The window-level viewport-controller accessor to rebind.</param>
    /// <param name="converter">The window-level coordinate-converter accessor to rebind.</param>
    /// <param name="renderer">The window-level image-renderer accessor to rebind.</param>
    /// <param name="planners">The planners the lazy-load coordinator reads; each map's is added.</param>
    public MapViews(
        MapCapabilityAccessor<IMapViewportController> viewport,
        MapCapabilityAccessor<IMapCoordinateConverter> converter,
        MapCapabilityAccessor<IImageRenderer> renderer,
        DemandPlannerSet planners)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(planners);
        _viewport = viewport;
        _converter = converter;
        _renderer = renderer;
        _planners = planners;

        // An MCP tool's map argument reaches any map through the same
        // accessors (split map D28, #1735).
        _viewport.BindMaps(MapIds, id => Find(id)?.ViewportController);
        _converter.BindMaps(MapIds, id => Find(id)?.CoordinateConverter);
        _renderer.BindMaps(MapIds, id => Find(id)?.ImageRenderer);
    }

    /// <summary>Every map, in the order they were added.</summary>
    public IReadOnlyList<IMapViewCapabilities> All
    {
        get
        {
            lock (_lock)
                return _views.Select(r => r.View).ToArray();
        }
    }

    /// <summary>The map with id <paramref name="id"/>, or <see langword="null"/>.</summary>
    /// <param name="id">A map id, such as <c>map-2</c>.</param>
    /// <returns>The map, or <see langword="null"/> when none has that id.</returns>
    public IMapViewCapabilities? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_lock)
            return _views.FirstOrDefault(r => r.View.Id.Value == id)?.View;
    }

    /// <summary>The map ids, in the order the maps were added.</summary>
    /// <returns>The ids.</returns>
    public IReadOnlyList<string> MapIds()
    {
        lock (_lock)
            return _views.Select(r => r.View.Id.Value).ToArray();
    }

    /// <summary>The map the user is working in, or <see langword="null"/> when there's none.</summary>
    public IMapViewCapabilities? Active
    {
        get
        {
            lock (_lock)
                return _active;
        }
    }

    /// <summary>Raised after <see cref="Active"/> changes, once the accessors are rebound.</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>Raised after a map is added, once it is in <see cref="All"/> (and active when it's the first).</summary>
    public event EventHandler<IMapViewCapabilities>? ViewAdded;

    /// <summary>
    /// Raised after a map is removed, before its owner tears it down, once
    /// another map is active.
    /// </summary>
    public event EventHandler<IMapViewCapabilities>? ViewRemoved;

    /// <inheritdoc />
    public IReadOnlyList<MapViewViewport> Current
    {
        get
        {
            lock (_lock)
                return ViewportsLocked();
        }
    }

    /// <inheritdoc />
    public event EventHandler<IReadOnlyList<MapViewViewport>>? ViewportsChanged;

    /// <summary>Adds a map. The first map added becomes active.</summary>
    /// <param name="view">The map.</param>
    /// <exception cref="InvalidOperationException">A map with the same id is already registered.</exception>
    public void Add(IMapViewCapabilities view)
    {
        ArgumentNullException.ThrowIfNull(view);
        bool activated;
        IReadOnlyList<MapViewViewport> viewports;
        lock (_lock)
        {
            if (_views.Any(r => r.View.Id == view.Id))
                throw new InvalidOperationException($"A map with id '{view.Id}' is already registered.");

            var registered = new Registered(view);
            registered.Handler = (_, _) => OnViewportChanged(registered);
            _views.Add(registered);
            view.Viewport.ViewportChanged += registered.Handler;
            _planners.Add(view.Planner);
            activated = _active is null;
            if (activated)
                _active = view;
            viewports = ViewportsLocked();
        }

        if (activated)
            OnActiveChanged();
        if (view.Viewport.Current is not null)
            ViewportsChanged?.Invoke(this, viewports);
        ViewAdded?.Invoke(this, view);
    }

    /// <summary>
    /// Removes a map. When it was active, the map before it (or the next
    /// one) becomes active; when it was the last map, the accessors clear.
    /// </summary>
    /// <param name="view">The map.</param>
    /// <returns><see langword="true"/> when the map was registered.</returns>
    public bool Remove(IMapViewCapabilities view)
    {
        ArgumentNullException.ThrowIfNull(view);
        bool activeChanged;
        IReadOnlyList<MapViewViewport> viewports;
        lock (_lock)
        {
            var index = _views.FindIndex(r => ReferenceEquals(r.View, view));
            if (index < 0)
                return false;

            var registered = _views[index];
            _views.RemoveAt(index);
            view.Viewport.ViewportChanged -= registered.Handler;
            _planners.Remove(view.Planner);
            activeChanged = ReferenceEquals(_active, view);
            if (activeChanged)
                _active = _views.Count == 0 ? null : _views[Math.Max(0, index - 1)].View;
            viewports = ViewportsLocked();
        }

        if (activeChanged)
            OnActiveChanged();
        if (view.Viewport.Current is not null)
            ViewportsChanged?.Invoke(this, viewports);
        ViewRemoved?.Invoke(this, view);
        return true;
    }

    /// <summary>Makes <paramref name="view"/> the active map.</summary>
    /// <param name="view">A registered map.</param>
    /// <exception cref="ArgumentException">The map isn't registered.</exception>
    public void Activate(IMapViewCapabilities view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (_lock)
        {
            if (!_views.Any(r => ReferenceEquals(r.View, view)))
                throw new ArgumentException($"The map '{view.Id}' isn't registered.", nameof(view));
            if (ReferenceEquals(_active, view))
                return;
            _active = view;
        }

        OnActiveChanged();
    }

    private void OnActiveChanged()
    {
        var active = Active;
        if (active is null)
        {
            // The readiness gate goes first, so no MCP call starts against
            // a map whose other capabilities are already gone.
            _renderer.Current = null;
            _converter.Current = null;
            _viewport.Current = null;
        }
        else
        {
            _converter.Current = active.CoordinateConverter;
            _viewport.Current = active.ViewportController;
            // The readiness gate goes last, once the capabilities used
            // alongside it are bound.
            _renderer.Current = active.ImageRenderer;
        }

        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnViewportChanged(Registered registered)
    {
        IReadOnlyList<MapViewViewport> viewports;
        lock (_lock)
        {
            if (!_views.Contains(registered))
                return;
            viewports = ViewportsLocked();
        }

        ViewportsChanged?.Invoke(this, viewports);
    }

    private MapViewViewport[] ViewportsLocked()
    {
        var viewports = new List<MapViewViewport>(_views.Count);
        foreach (var registered in _views)
        {
            if (registered.View.Viewport.Current is { } snapshot)
                viewports.Add(new MapViewViewport(registered.View.Id, snapshot));
        }

        return [.. viewports];
    }

    private sealed class Registered(IMapViewCapabilities view)
    {
        public IMapViewCapabilities View { get; } = view;

        public EventHandler<MapViewportSnapshot>? Handler { get; set; }
    }
}
