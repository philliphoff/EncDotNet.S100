using Avalonia;
using Avalonia.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Services;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Distance &amp; bearing measure tool. Click adds waypoints; clicking a
/// leg's label selects that leg for the card (D9); double-click, Enter, or
/// right-click finalises; Backspace removes the last waypoint; Esc (handled
/// by the controller) exits the tool. The path lives in a shared
/// <see cref="MeasureSession"/> so the measure card and the MCP tools see
/// the same measurement.
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

    /// <summary>Extra room, in DIPs, around a leg label that still counts as clicking it.</summary>
    private const double LabelHitSlopPx = 4.0;

    /// <summary>Approximate advance of one label character, in DIPs (12 px monospace).</summary>
    private const double LabelCharWidthPx = 7.3;

    private readonly MeasureSession _session;
    private readonly IMeasureOverlayAppearanceProvider _appearance;
    private MapToolContext? _context;
    private MemoryLayer? _layer;
    private Point? _pressPosition;
    private bool _pressIsLeftButton;

    /// <summary>
    /// Creates a measure tool that draws its overlay using the appearance
    /// supplied by <paramref name="appearance"/>. The tool subscribes to
    /// <see cref="IMeasureOverlayAppearanceProvider.Changed"/> while
    /// active so accent/theme updates are reflected live, and
    /// unsubscribes on deactivation.
    /// </summary>
    public MeasureTool(IMeasureOverlayAppearanceProvider appearance, MeasureSession session)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(session);
        _appearance = appearance;
        _session = session;
    }

    /// <summary>Test-only ctor that uses a stub appearance provider returning the default appearance.</summary>
    internal MeasureTool() : this(new StaticAppearanceProvider(MeasureOverlayAppearance.Default), new MeasureSession()) { }

    private sealed class StaticAppearanceProvider : IMeasureOverlayAppearanceProvider
    {
        public MeasureOverlayAppearance Current { get; }
        public event EventHandler? Changed { add { } remove { } }
        public StaticAppearanceProvider(MeasureOverlayAppearance value) => Current = value;
    }

    /// <summary>Exposed for tests.</summary>
    internal MeasurePathState State => _session.State;

    /// <summary>The shared measure state.</summary>
    internal MeasureSession Session => _session;

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
        _appearance.Changed += OnAppearanceChanged;
        _session.Changed += OnSessionChanged;
        _session.PositionCommitted += OnPositionCommitted;
        _session.IsActive = true;
        Refresh();
    }

    public void OnDeactivated()
    {
        _appearance.Changed -= OnAppearanceChanged;
        _session.Changed -= OnSessionChanged;
        _session.PositionCommitted -= OnPositionCommitted;
        if (_context is not null && _layer is not null)
            _context.RemoveLayer(_layer);
        _layer = null;
        _context?.SetStatusSummary(null);
        _context = null;
        _pressPosition = null;
        _session.IsActive = false;
        State.Discard();
        _session.NotifyChanged();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        if (_layer is not null) Redraw();
    }

    private void OnSessionChanged(object? sender, EventArgs e) => Redraw();

    /// <summary>Pans, without zooming, to a typed position that's off screen (D7).</summary>
    private void OnPositionCommitted(object? sender, GeoPosition position)
    {
        if (_context is null)
            return;
        var bounds = _context.MapControl.Bounds;
        if (_context.LatLonToScreen(position) is { } screen
            && screen.X >= 0 && screen.Y >= 0 && screen.X <= bounds.Width && screen.Y <= bounds.Height)
        {
            return;
        }

        var (x, y) = SphericalMercator.FromLonLat(position.Longitude, position.Latitude);
        _context.MapControl.Map.Navigator.CenterOn(x, y);
    }

    public bool OnPointerPressed(PointerPressedEventArgs e)
    {
        if (_context is null) return false;
        var props = e.GetCurrentPoint(_context.MapControl).Properties;

        if (props.IsRightButtonPressed)
        {
            // Right-click finalises the current path.
            if (State.Finalise())
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
            if (State.Hover(null)) Refresh();
            return false;
        }

        var world = _context.ScreenToLatLon(pos);
        if (State.Hover(world)) Refresh();
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

        if (HitLegLabel(release) is { } legIndex)
        {
            if (State.SelectLeg(legIndex))
                Refresh();
            return true;
        }

        var world = _context.ScreenToLatLon(release);
        if (world is not { } w) return false;

        if (State.Click(w.Latitude, w.Longitude))
            Refresh();
        return true;
    }

    public bool OnDoubleTapped(TappedEventArgs e)
    {
        // Double-tap finalises the path. The first tap of the double-tap
        // already placed a waypoint via OnPointerReleased; the second tap
        // would have placed a duplicate — drop the duplicate and finalise
        // instead.
        if (State.Phase == MeasurePathState.MeasurePhase.Drawing && State.Waypoints.Count >= 2)
        {
            State.Backstep();
        }

        if (State.Finalise())
        {
            Refresh();
        }
        return true;
    }

    public bool OnAction(MapToolAction action)
    {
        bool changed = action switch
        {
            MapToolAction.Commit => State.Finalise(),
            MapToolAction.Backstep => State.Backstep(),
            MapToolAction.Discard => State.Discard(),
            _ => false,
        };
        if (changed) Refresh();
        return changed;
    }

    /// <summary>
    /// The one-based index of the placed leg whose map label is under
    /// <paramref name="screen"/>, or <c>null</c>. The pending rubber-band leg
    /// isn't selectable: it follows the cursor.
    /// </summary>
    private int? HitLegLabel(Point screen)
    {
        if (_context is null)
            return null;
        var legs = State.ComputeLegs();
        if (legs.Count < 2)
            return null;

        foreach (var leg in legs)
        {
            if (leg.IsRubberBand || _context.LatLonToScreen(MeasureOverlayLayer.LabelAnchor(leg)) is not { } anchor)
                continue;
            var text = MeasureOverlayLayer.LegLabelText(leg, _session.Unit);
            var halfWidth = text.Length * LabelCharWidthPx / 2.0 + LabelHitSlopPx;
            var halfHeight = MeasureOverlayLayer.LabelFontSize / 2.0 + LabelHitSlopPx;
            var centreY = anchor.Y + MeasureOverlayLayer.LabelOffsetY;
            if (Math.Abs(screen.X - anchor.X) <= halfWidth && Math.Abs(screen.Y - centreY) <= halfHeight)
                return leg.Index;
        }

        return null;
    }

    /// <summary>Tells everyone sharing the session that the path changed.</summary>
    private void Refresh() => _session.NotifyChanged();

    private void Redraw()
    {
        if (_layer is null || _context is null) return;
        MeasureOverlayLayer.Update(_layer, State, _appearance.Current, _session.Unit);
        _context.SetStatusSummary(State.FormatSummary(_session.Unit));
        _context.RefreshGraphics();
    }
}
