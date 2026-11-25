namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The map the status bar describes while the map is split (#1732): the map
/// under the pointer, or the active map when the pointer is over none. The
/// pointer's lat/lon, the scale, the overscale and the depth basis all read
/// this one map, so the readouts side by side never describe two maps.
/// </summary>
internal sealed class MapStatusFocus
{
    private readonly MapViews _views;

    /// <summary>Follows <paramref name="views"/>' active map and removed maps.</summary>
    public MapStatusFocus(MapViews views)
    {
        ArgumentNullException.ThrowIfNull(views);
        _views = views;
        views.ActiveChanged += (_, _) =>
        {
            if (Hovered is null)
                Changed?.Invoke(this, EventArgs.Empty);
        };
        views.ViewRemoved += (_, view) => PointerLeft(view);
    }

    /// <summary>The map under the pointer, or null.</summary>
    public IMapViewCapabilities? Hovered { get; private set; }

    /// <summary>The map the status bar describes: the hovered map, else the active one.</summary>
    public IMapViewCapabilities? Current => Hovered ?? _views.Active;

    /// <summary>Raised when <see cref="Current"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether <paramref name="view"/> is the map the status bar describes.</summary>
    public bool IsCurrent(IMapViewCapabilities view) => ReferenceEquals(Current, view);

    /// <summary>The pointer moved onto <paramref name="view"/>.</summary>
    public void PointerEntered(IMapViewCapabilities view)
    {
        if (ReferenceEquals(Hovered, view))
            return;
        var before = Current;
        Hovered = view;
        if (!ReferenceEquals(before, Current))
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The pointer left <paramref name="view"/>.</summary>
    public void PointerLeft(IMapViewCapabilities view)
    {
        if (!ReferenceEquals(Hovered, view))
            return;
        var before = Current;
        Hovered = null;
        if (!ReferenceEquals(before, Current))
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
