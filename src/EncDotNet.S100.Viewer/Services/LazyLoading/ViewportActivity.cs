namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Remembers when the map view last moved, so background work can stay out of
/// the way while the user pans or zooms (#1266). Panning over tiles that are
/// already drawn loads nothing and leaves no tiles to draw, so without this the
/// map would look idle in the middle of a pan.
/// </summary>
internal sealed class ViewportActivity : IDisposable
{
    private readonly IMapViewportNotifier _notifier;
    private readonly TimeProvider _time;
    private long _lastChange;
    private int _changed;

    /// <summary>Starts following <paramref name="notifier"/>.</summary>
    /// <param name="notifier">Raises the map's viewport changes.</param>
    /// <param name="time">Clock for the timings.</param>
    public ViewportActivity(IMapViewportNotifier notifier, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(time);
        _notifier = notifier;
        _time = time;
        _notifier.ViewportChanged += OnViewportChanged;
    }

    /// <summary>Whether the view moved within the last <paramref name="window"/>.</summary>
    /// <param name="window">How recent a move counts.</param>
    /// <returns><see langword="true"/> when the view moved that recently.</returns>
    public bool MovedWithin(TimeSpan window) =>
        Volatile.Read(ref _changed) != 0
        && _time.GetElapsedTime(Interlocked.Read(ref _lastChange)) < window;

    /// <inheritdoc />
    public void Dispose() => _notifier.ViewportChanged -= OnViewportChanged;

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        Interlocked.Exchange(ref _lastChange, _time.GetTimestamp());
        Volatile.Write(ref _changed, 1);
    }
}
