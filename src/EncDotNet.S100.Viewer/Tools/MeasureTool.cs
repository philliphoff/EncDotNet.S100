using System;
using Avalonia;
using Avalonia.Input;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Distance &amp; bearing measure tool. Click adds waypoints; double-click,
/// Enter, or right-click finalises; Backspace removes the last waypoint;
/// Esc (handled by the controller) exits the tool.
/// </summary>
/// <remarks>
/// Drag detection: a mouse-down followed by a mouse-up within
/// <see cref="DragThresholdPx"/> is treated as a click and consumed; any
/// larger movement falls through to Mapsui as a pan. This lets the user
/// pan freely while in Measure Mode.
/// </remarks>
internal sealed class MeasureTool : IMapTool
{
    public const string ToolId = "measure";

    /// <summary>Click vs. drag threshold (DIPs) for distinguishing pan from waypoint placement.</summary>
    private const double DragThresholdPx = 3.0;

    private readonly MeasurePathState _state = new();
    private MapToolContext? _context;
    private MemoryLayer? _layer;
    private Point? _pressPosition;
    private bool _pressIsLeftButton;

    /// <summary>Exposed for tests.</summary>
    internal MeasurePathState State => _state;

    public string Id => ToolId;

    private Cursor? _cursor;
    /// <inheritdoc />
    /// <remarks>Lazy so the type is constructable before the Avalonia
    /// platform is initialised (e.g. in unit tests).</remarks>
    public Cursor? Cursor => _cursor ??= new Cursor(StandardCursorType.Cross);

    public void OnActivated(MapToolContext context)
    {
        _context = context;
        _layer = MeasureOverlayLayer.Create();
        context.AddLayer(_layer);
        PushSummary();
    }

    public void OnDeactivated()
    {
        if (_context is not null && _layer is not null)
            _context.RemoveLayer(_layer);
        _layer = null;
        _context?.SetStatusSummary(null);
        _context = null;
        _state.Discard();
        _pressPosition = null;
    }

    public bool OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_context is null) return false;
        var props = e.GetCurrentPoint(_context.MapControl).Properties;

        if (props.IsRightButtonPressed)
        {
            // Right-click finalises the current path.
            if (_state.Finalise())
            {
                Refresh();
                return true;
            }
            return true; // always consume so no context menu pops up
        }

        if (props.IsLeftButtonPressed)
        {
            _pressPosition = e.GetPosition(_context.MapControl);
            _pressIsLeftButton = true;
            // Don't consume yet — pan needs to win if the user drags.
            return false;
        }

        return false;
    }

    public bool OnPointerMoved(PointerEventArgs e)
    {
        if (_context is null) return false;
        var pos = e.GetPosition(_context.MapControl);
        var bounds = _context.MapControl.Bounds;
        if (pos.X < 0 || pos.Y < 0 || pos.X > bounds.Width || pos.Y > bounds.Height)
        {
            if (_state.Hover(null)) Refresh();
            return false;
        }

        var world = _context.ScreenToLatLon(pos);
        if (_state.Hover(world)) Refresh();
        return false; // never suppress — let the lat/long readout update
    }

    public bool OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_context is null || !_pressIsLeftButton || _pressPosition is not { } press)
            return false;

        _pressIsLeftButton = false;
        var release = e.GetPosition(_context.MapControl);
        _pressPosition = null;

        var dx = release.X - press.X;
        var dy = release.Y - press.Y;
        if ((dx * dx + dy * dy) > DragThresholdPx * DragThresholdPx)
            return false; // drag → let Mapsui handle the pan

        var world = _context.ScreenToLatLon(release);
        if (world is not { } w) return false;

        if (_state.Click(w.Lat, w.Lon))
            Refresh();
        return true;
    }

    public bool OnDoubleTapped(TappedEventArgs e)
    {
        // Double-tap finalises the path. The first tap of the double-tap
        // already placed a waypoint via OnPointerReleased; the second tap
        // would have placed a duplicate — drop the duplicate and finalise
        // instead.
        if (_state.Phase == MeasurePathState.MeasurePhase.Drawing && _state.Waypoints.Count >= 2)
        {
            _state.Backstep();
        }

        if (_state.Finalise())
        {
            Refresh();
        }
        return true;
    }

    public bool OnAction(MapToolAction action)
    {
        bool changed = action switch
        {
            MapToolAction.Commit => _state.Finalise(),
            MapToolAction.Backstep => _state.Backstep(),
            MapToolAction.Discard => _state.Discard(),
            _ => false,
        };
        if (changed) Refresh();
        return changed;
    }

    private void Refresh()
    {
        if (_layer is null || _context is null) return;
        MeasureOverlayLayer.Update(_layer, _state);
        PushSummary();
        _context.RefreshGraphics();
    }

    private void PushSummary()
    {
        _context?.SetStatusSummary(_state.FormatSummary());
    }
}
