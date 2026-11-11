using Avalonia.Input;
using Avalonia.Interactivity;
using Mapsui.Manipulations;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Renderers.Mapsui.Avalonia;

/// <summary>
/// Reports the flings a Mapsui map control starts to the map's demand planner
/// (map rendering slice&#160;9, open question&#160;5). The control computes
/// its release velocity with a private <see cref="FlingTracker"/>; this feeds
/// a public one the same pointer events, so on release it gets the same
/// velocity, and <see cref="MapsuiMapNavigator.ForecastFling"/> predicts where
/// the control's fling comes to rest. It changes nothing about the gesture.
/// </summary>
internal sealed class AvaloniaFlingForecaster : IDisposable
{
    private readonly MapControl _control;
    private readonly MapsuiMapNavigator _navigator;
    private readonly FlingTracker _tracker = new();
    private readonly HashSet<long> _pressed = [];
    private bool _disposed;

    public AvaloniaFlingForecaster(MapControl control, MapsuiMapNavigator navigator)
    {
        _control = control;
        _navigator = navigator;
        // Tunnel, and handled events too: the control's own handlers run on
        // the bubble, and a tool may mark the events handled first.
        _control.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _control.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _control.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _control.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        _control.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
        _control.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressed.Add(e.Pointer.Id);
        if (_pressed.Count == 1)
            _tracker.Restart();
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        // As the control: one pointer down and dragging.
        if (_pressed.Count != 1 || !_pressed.Contains(e.Pointer.Id))
            return;
        var position = e.GetPosition(_control);
        _tracker.AddEvent(new ScreenPosition(position.X, position.Y), DateTime.Now.Ticks);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var single = _pressed.Count == 1;
        _pressed.Remove(e.Pointer.Id);
        if (!single || !_control.UseFling)
            return;

        // The control flings for this velocity after this handler, from its
        // own tracker fed the same events.
        _tracker.FlingIfNeeded((vx, vy) => _navigator.ForecastFling(vx, vy));
    }
}
