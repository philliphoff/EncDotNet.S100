using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// An <see cref="IMapViewportSet"/> the test drives: one map
/// (<see cref="First"/>) by default, more with <see cref="Publish(MapViewId, MapViewportSnapshot)"/>.
/// </summary>
internal sealed class FakeViewportSet : IMapViewportSet
{
    /// <summary>The id <see cref="Publish(MapViewportSnapshot)"/> and <see cref="View"/> use.</summary>
    public static readonly MapViewId First = new("map-1");

    private readonly List<MapViewViewport> _views = [];

    /// <summary>Seeds <see cref="First"/>'s viewport without raising the event.</summary>
    public MapViewportSnapshot? View
    {
        init
        {
            if (value is not null)
                _views.Add(new MapViewViewport(First, value));
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MapViewViewport> Current => _views.ToArray();

    /// <inheritdoc />
    public event EventHandler<IReadOnlyList<MapViewViewport>>? ViewportsChanged;

    /// <summary>Moves <see cref="First"/>.</summary>
    public void Publish(MapViewportSnapshot snapshot) => Publish(First, snapshot);

    /// <summary>Moves (or adds) the map <paramref name="view"/>.</summary>
    public void Publish(MapViewId view, MapViewportSnapshot snapshot)
    {
        var index = _views.FindIndex(v => v.View == view);
        if (index < 0)
            _views.Add(new MapViewViewport(view, snapshot));
        else
            _views[index] = new MapViewViewport(view, snapshot);
        ViewportsChanged?.Invoke(this, Current);
    }

    /// <summary>Removes the map <paramref name="view"/>.</summary>
    public void Remove(MapViewId view)
    {
        _views.RemoveAll(v => v.View == view);
        ViewportsChanged?.Invoke(this, Current);
    }
}
