using Avalonia;
using Avalonia.Input;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// The Range &amp; bearing map tool (#1130 D11, D12): an EBL/VRM pair from an
/// origin the user chooses. The first click sets the origin, the bearing line
/// and range ring then follow the cursor, a second click fixes the end and a
/// click after that starts again. Enter fixes the end at the cursor, Backspace
/// goes back to placing the origin, Delete clears it, and Esc (handled by the
/// window) leaves the tool. Dragging still pans.
/// </summary>
/// <remarks>
/// The model is the shared <see cref="RangeBearingState"/>, which the card and
/// the MCP tools also change; the tool redraws its overlay whenever it does.
/// </remarks>
internal sealed class RangeBearingTool : IMapTool
{
    public const string ToolId = "rangeBearing";

    /// <summary>Click vs. drag threshold (DIPs), as for the measure tool.</summary>
    private const double DragThresholdPx = 3.0;

    private readonly RangeBearingState _state;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private readonly Func<DistanceUnit> _unit;
    private MapToolContext? _context;
    private MemoryLayer? _layer;
    private Point? _pressPosition;

    /// <param name="state">The shared model.</param>
    /// <param name="appearance">Accent and theme for the overlay.</param>
    /// <param name="unit">The Distance unit setting, for map labels.</param>
    public RangeBearingTool(RangeBearingState state, IMeasureOverlayAppearanceProvider appearance, Func<DistanceUnit> unit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(unit);
        _state = state;
        _appearance = appearance;
        _unit = unit;
    }

    public string Id => ToolId;

    private Cursor? _cursor;
    /// <inheritdoc />
    public Cursor? Cursor => _cursor ??= new Cursor(StandardCursorType.Cross);

    /// <summary>Exposed for tests.</summary>
    internal RangeBearingState State => _state;

    public void OnActivated(MapToolContext context)
    {
        _context = context;
        _layer = RangeBearingOverlayLayer.Create();
        context.AddLayer(_layer);
        _appearance.Changed += OnRedraw;
        _state.Changed += OnRedraw;
        Refresh();
    }

    public void OnDeactivated()
    {
        _appearance.Changed -= OnRedraw;
        _state.Changed -= OnRedraw;
        if (_context is not null && _layer is not null)
            _context.RemoveLayer(_layer);
        _context?.SetStatusSummary(null);
        _layer = null;
        _context = null;
        _pressPosition = null;
    }

    /// <summary>Redraws when the Distance unit changes.</summary>
    public void InvalidateOverlay() => Refresh();

    private void OnRedraw(object? sender, EventArgs e) => Refresh();

    public bool OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_context is null)
            return false;
        var props = e.GetCurrentPoint(_context.MapControl).Properties;
        if (props.IsRightButtonPressed)
        {
            _state.Commit();
            return true; // no context menu
        }

        if (props.IsLeftButtonPressed)
            _pressPosition = e.GetPosition(_context.MapControl);
        return false; // a drag still pans
    }

    public bool OnPointerMoved(PointerEventArgs e)
    {
        if (_context is null)
            return false;
        var pos = e.GetPosition(_context.MapControl);
        var bounds = _context.MapControl.Bounds;
        var inside = pos.X >= 0 && pos.Y >= 0 && pos.X <= bounds.Width && pos.Y <= bounds.Height;
        _state.Hover(inside ? _context.ScreenToLatLon(pos) : null);
        return false; // keep the lat/long readout updating
    }

    public bool OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_context is null || _pressPosition is not { } press)
            return false;
        _pressPosition = null;
        var release = e.GetPosition(_context.MapControl);
        var dx = release.X - press.X;
        var dy = release.Y - press.Y;
        if (dx * dx + dy * dy > DragThresholdPx * DragThresholdPx)
            return false;
        if (_context.ScreenToLatLon(release) is not { } world)
            return false;
        _state.Click(world);
        return true;
    }

    // A double click is two clicks: origin and end. Don't zoom.
    public bool OnDoubleTapped(TappedEventArgs e) => true;

    public bool OnAction(MapToolAction action) => action switch
    {
        MapToolAction.Commit => _state.Commit(),
        MapToolAction.Backstep => _state.Backstep(),
        MapToolAction.Discard => _state.Discard(),
        _ => false,
    };

    private void Refresh()
    {
        if (_layer is null || _context is null)
            return;
        RangeBearingOverlayLayer.Update(_layer, _state, _appearance.Current, _unit());
        _context.SetStatusSummary(RangeBearingText.Summary(_state, _unit()));
        _context.RefreshGraphics();
    }
}
