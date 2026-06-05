using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.DynamicSources;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Projections;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Owns the map's gesture, pointer, scale-bar, and mouse-readout
/// wiring. MainWindow constructs one of these and calls
/// <see cref="Attach"/>; afterwards the window has no direct
/// awareness of map gestures, viewport changes, or long-press
/// timing.
/// </summary>
internal sealed class MapInteractionController
{
    /// <summary>
    /// Threshold (milliseconds) for treating a press-and-hold as a long-press
    /// pick gesture. Mirrors typical touch UI conventions and ECDIS
    /// "press to identify" behavior.
    /// </summary>
    private const int LongPressMillis = 500;

    /// <summary>
    /// Maximum movement (in pixels) tolerated during a long-press before the
    /// gesture is cancelled (the user is panning, not picking).
    /// </summary>
    private const double LongPressMoveTolerance = 6.0;

    private readonly MainViewModel _viewModel;
    private readonly IPickService _pickService;
    private readonly IDatasetLoaderService _loader;
    private readonly IDynamicSourcePickService? _dynamicPickService;

    private MapControl? _mapControl;

    private DispatcherTimer? _longPressTimer;
    private Point? _longPressOrigin;
    private bool _longPressFired;

    /// <summary>
    /// Modifier state captured at the most recent pointer-press on the map,
    /// used by the tap handler to detect modifier-click since the Mapsui
    /// tap event doesn't carry keyboard modifiers itself.
    /// </summary>
    private KeyModifiers _lastPressedModifiers = KeyModifiers.None;

    private Point? _lastMouseScreenPos;

    /// <summary>
    /// Active map-tool registry. Pointer / double-tap events are offered to
    /// the active tool first; if the tool marks the event handled the
    /// existing pick / pan logic is skipped for that gesture.
    /// </summary>
    private MapToolController? _toolController;

    /// <summary>
    /// Called by <see cref="MainWindow"/> after construction to inject the
    /// tool controller. Kept as a setter (rather than a constructor param)
    /// because the controller is owned by the view-model and the
    /// interaction-controller is built before the view-model is fully wired.
    /// </summary>
    public void SetToolController(MapToolController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _toolController = controller;
    }

    public MapInteractionController(
        MainViewModel viewModel,
        IPickService pickService,
        IDatasetLoaderService loader)
        : this(viewModel, pickService, loader, dynamicPickService: null)
    {
    }

    public MapInteractionController(
        MainViewModel viewModel,
        IPickService pickService,
        IDatasetLoaderService loader,
        IDynamicSourcePickService? dynamicPickService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(pickService);
        ArgumentNullException.ThrowIfNull(loader);

        _viewModel = viewModel;
        _pickService = pickService;
        _loader = loader;
        _dynamicPickService = dynamicPickService;
    }

    /// <summary>
    /// Wires the map control, zoom buttons, scale-bar, and compass to this
    /// controller. Call once after the visual tree is built.
    /// </summary>
    public void Attach(
        MapControl mapControl,
        Button zoomInButton,
        Button zoomOutButton,
        Button zoomToExtentButton,
        ScaleBarView scaleBar,
        CompassRoseView compassRose)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        ArgumentNullException.ThrowIfNull(zoomInButton);
        ArgumentNullException.ThrowIfNull(zoomOutButton);
        ArgumentNullException.ThrowIfNull(zoomToExtentButton);
        ArgumentNullException.ThrowIfNull(scaleBar);
        ArgumentNullException.ThrowIfNull(compassRose);

        _mapControl = mapControl;

        // Trackpad magnify / rotate gestures, double-tap zoom, single-tap pick.
        mapControl.AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, OnMapMagnify);
        mapControl.AddHandler(Gestures.PointerTouchPadGestureRotateEvent, OnMapRotateGesture);
        mapControl.DoubleTapped += OnMapDoubleTapped;
        mapControl.MapTapped += OnMapTapped;

        // Long-press (~500ms hold without moving) is always a one-shot pick
        // regardless of Pick Mode. Tunneling lets us see the press before
        // MapControl handles it.
        mapControl.AddHandler(InputElement.PointerPressedEvent, OnMapPointerPressed, RoutingStrategies.Tunnel);
        mapControl.AddHandler(InputElement.PointerMovedEvent, OnMapPointerMoved, RoutingStrategies.Tunnel);
        mapControl.AddHandler(InputElement.PointerReleasedEvent, OnMapPointerReleased, RoutingStrategies.Tunnel);
        mapControl.AddHandler(InputElement.PointerCaptureLostEvent, OnMapPointerCaptureLost, RoutingStrategies.Tunnel);
        mapControl.PointerExited += OnMapPointerExited;

        // Trackpad scroll/swipe to pan (tunnel phase to intercept before MapControl).
        mapControl.AddHandler(InputElement.PointerWheelChangedEvent, OnMapPointerWheelChanged, RoutingStrategies.Tunnel);

        // Zoom in/out overlay buttons.
        zoomInButton.Click += OnZoomInClick;
        zoomOutButton.Click += OnZoomOutClick;
        zoomToExtentButton.Click += OnZoomToExtentClick;

        // Compass-rose drag drives map rotation.
        compassRose.RotationRequested += OnCompassRotationRequested;
        compassRose.RotationResetRequested += OnCompassRotationReset;

        // Keep the scale-bar and mouse lat/lon readouts synced with the
        // viewport.
        if (mapControl.Map?.Navigator is { } nav)
        {
            nav.ViewportChanged += (_, _) => HandleViewportChangedForScaleBar(scaleBar, compassRose);
            nav.ViewportChanged += (_, _) => HandleViewportChangedForMouseLatLon();
            UpdateScaleBar(scaleBar, compassRose, nav.Viewport);
        }
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        if (_mapControl?.Map?.Navigator is not { } navigator)
            return;
        navigator.ZoomTo(navigator.Viewport.Resolution / 2, 250);
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        if (_mapControl?.Map?.Navigator is not { } navigator)
            return;
        navigator.ZoomTo(navigator.Viewport.Resolution * 2, 250);
    }

    /// <summary>
    /// Zooms out to the combined extent of all loaded layers (the union
    /// reported by <see cref="Mapsui.Map.Extent"/>) so the user can see
    /// every loaded dataset at once without repeatedly clicking
    /// Zoom Out.
    /// </summary>
    private void OnZoomToExtentClick(object? sender, RoutedEventArgs e)
    {
        if (_mapControl?.Map is not { } map || map.Navigator is not { } navigator)
            return;

        var extent = map.Extent;
        if (extent is null || extent.Width <= 0 || extent.Height <= 0)
            return;

        // Match DatasetLoaderService.ZoomToExtent: pad by 10% so features
        // at the edges aren't flush against the viewport border.
        navigator.ZoomToBox(extent.Grow(extent.Width * 0.1, extent.Height * 0.1), duration: 250);
    }

    private void HandleViewportChangedForScaleBar(ScaleBarView scaleBar, CompassRoseView compassRose)
    {
        if (_mapControl?.Map?.Navigator is not { } nav)
            return;

        var viewport = nav.Viewport;
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateScaleBar(scaleBar, compassRose, viewport);
        }
        else
        {
            Dispatcher.UIThread.Post(() => UpdateScaleBar(scaleBar, compassRose, viewport));
        }
    }

    private static void UpdateScaleBar(ScaleBarView scaleBar, CompassRoseView compassRose, Viewport viewport)
    {
        scaleBar.UpdateForViewport(viewport.Resolution, viewport.CenterY);
        compassRose.UpdateForViewport(viewport.Rotation);
    }

    private void OnMapMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (_mapControl?.Map?.Navigator is not { } navigator)
            return;

        var resolution = navigator.Viewport.Resolution;
        var newResolution = resolution / (1 + e.Delta.Y);
        var position = e.GetPosition(_mapControl);
        var center = new ScreenPosition(position.X, position.Y);
        navigator.ZoomTo(newResolution, center);
        e.Handled = true;
    }

    private void OnMapRotateGesture(object? sender, PointerDeltaEventArgs e)
    {
        if (_mapControl?.Map?.Navigator is not { } navigator)
            return;

        // macOS reports the rotation delta in degrees (counter-clockwise positive).
        // Mapsui's viewport rotation is clockwise positive, so negate.
        var deltaDegrees = -e.Delta.X;
        if (deltaDegrees == 0)
            return;

        var newRotation = navigator.Viewport.Rotation + deltaDegrees;
        // Normalize to [0, 360).
        newRotation = ((newRotation % 360.0) + 360.0) % 360.0;
        navigator.RotateTo(newRotation);
        e.Handled = true;
    }

    private void OnCompassRotationRequested(double rotationDegrees)
    {
        if (_mapControl?.Map?.Navigator is not { } navigator)
            return;
        navigator.RotateTo(rotationDegrees);
    }

    private void OnCompassRotationReset()
    {
        if (_mapControl?.Map?.Navigator is not { } navigator)
            return;
        // Pick the equivalent rotation closest to 0 to avoid spinning the long way.
        var current = navigator.Viewport.Rotation;
        var target = current > 180.0 ? 360.0 : 0.0;
        navigator.RotateTo(target, 250);
    }

    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_mapControl?.Map?.Navigator is not { } navigator)
            return;

        var viewport = navigator.Viewport;

        // Pan vector in the unrotated (screen-aligned) world frame: dragging
        // the content right/up (Delta.X/Y > 0) moves the viewport center
        // left/up by the same amount.
        var dxScreen = -e.Delta.X * viewport.Resolution * 50;
        var dyScreen = e.Delta.Y * viewport.Resolution * 50;

        // Rotate the screen-space delta into world space by the viewport
        // rotation so swipes always agree with the on-screen orientation.
        var rad = viewport.Rotation * Math.PI / 180.0;
        var sin = Math.Sin(rad);
        var cos = Math.Cos(rad);
        var dxWorld = dxScreen * cos - dyScreen * sin;
        var dyWorld = dxScreen * sin + dyScreen * cos;

        navigator.CenterOn(viewport.CenterX + dxWorld, viewport.CenterY + dyWorld);
        e.Handled = true;
    }

    private void OnMapDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Active tool gets first refusal (e.g. measure-mode finalises on double-tap).
        if (_toolController?.OnDoubleTapped(e) == true)
        {
            e.Handled = true;
            return;
        }

        // In Pick Mode the double-tap zoom is suppressed so that successive
        // taps on adjacent features each register as picks rather than zooms.
        if (_viewModel.IsPickModeActive)
        {
            e.Handled = true;
            return;
        }

        if (_mapControl?.Map?.Navigator is not { } navigator)
            return;

        var resolution = navigator.Viewport.Resolution;
        var newResolution = resolution / 2;
        var position = e.GetPosition(_mapControl);
        var center = new ScreenPosition(position.X, position.Y);
        navigator.ZoomTo(newResolution, center, 250);
        e.Handled = true;
    }

    private void OnMapTapped(object? sender, BaseEventArgs e)
    {
        if (e.GestureType != GestureType.SingleTap)
            return;

        // A long-press already produced a pick at PointerPressed → release time;
        // suppress the synthesized SingleTap that follows so we don't pick twice
        // (or, worse, show then immediately clear the panel).
        if (_longPressFired)
        {
            _longPressFired = false;
            return;
        }

        // Modifier-click is always a one-shot pick regardless of Pick Mode:
        // Cmd-click on macOS, Ctrl-click on Windows / Linux.
        if (IsPickModifierActive())
        {
            PerformPickAt(e);
            return;
        }

        // Outside Pick Mode, plain single-tap is a no-op so it doesn't fight
        // with double-tap-to-zoom (the first tap of a double-tap also fires
        // here).
        if (!_viewModel.IsPickModeActive)
            return;

        PerformPickAt(e);
    }

    /// <summary>
    /// Returns true when the modifier state captured at the most recent
    /// pointer-press matches the platform's pick modifier: <c>Cmd</c> (Meta)
    /// on macOS, <c>Ctrl</c> on Windows and Linux.
    /// </summary>
    private bool IsPickModifierActive()
    {
        if (OperatingSystem.IsMacOS())
            return (_lastPressedModifiers & KeyModifiers.Meta) != 0;
        return (_lastPressedModifiers & KeyModifiers.Control) != 0;
    }

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mapControl is null)
            return;

        // Offer the gesture to the active tool first (e.g. measure-mode
        // begins drag-vs-click tracking here). If the tool handles it,
        // skip the long-press / pick wiring entirely.
        if (_toolController?.OnPointerPressed(e) == true)
        {
            e.Handled = true;
            return;
        }

        var props = e.GetCurrentPoint(_mapControl).Properties;
        if (!props.IsLeftButtonPressed)
            return;

        // Capture modifier state for OnMapTapped's modifier-click test (the
        // Mapsui tap event doesn't carry keyboard modifiers).
        _lastPressedModifiers = e.KeyModifiers;

        // Modifier-click is handled in OnMapTapped (where we have a MapInfo
        // resolver); skip the long-press timer for that case.
        if (IsPickModifierActive())
            return;

        _longPressOrigin = e.GetPosition(_mapControl);
        _longPressFired = false;

        _longPressTimer?.Stop();
        _longPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(LongPressMillis),
        };
        _longPressTimer.Tick += OnLongPressElapsed;
        _longPressTimer.Start();
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        UpdateMouseLatLon(e);

        // Forward to active tool (e.g. measure-mode rubber-band update).
        // Move events are advisory: tools may set Handled but we still want
        // the lat/lon readout updated above.
        if (_toolController?.OnPointerMoved(e) == true)
        {
            e.Handled = true;
        }

        if (_longPressTimer is null || _longPressOrigin is not { } origin || _mapControl is null)
            return;

        var current = e.GetPosition(_mapControl);
        var dx = current.X - origin.X;
        var dy = current.Y - origin.Y;
        if ((dx * dx + dy * dy) > LongPressMoveTolerance * LongPressMoveTolerance)
            CancelLongPress();
    }

    private void OnMapPointerExited(object? sender, PointerEventArgs e)
    {
        _lastMouseScreenPos = null;
        _viewModel.MouseLatLonText = LatLonFormatter.Placeholder;
    }

    private void HandleViewportChangedForMouseLatLon()
    {
        // Pan/zoom can move the world under a stationary cursor (common with
        // trackpad gestures), so refresh the readout whenever the viewport
        // changes — but only if the cursor is currently over the map.
        if (_lastMouseScreenPos is not { } pos)
            return;

        Dispatcher.UIThread.Post(() => UpdateMouseLatLonFromScreen(pos));
    }

    private void UpdateMouseLatLon(PointerEventArgs e)
    {
        if (_mapControl is null)
            return;

        var position = e.GetPosition(_mapControl);
        var bounds = _mapControl.Bounds;
        if (position.X < 0 || position.Y < 0 ||
            position.X > bounds.Width || position.Y > bounds.Height)
        {
            _lastMouseScreenPos = null;
            _viewModel.MouseLatLonText = LatLonFormatter.Placeholder;
            return;
        }

        _lastMouseScreenPos = position;
        UpdateMouseLatLonFromScreen(position);
    }

    private void UpdateMouseLatLonFromScreen(Point position)
    {
        if (_mapControl?.Map?.Navigator is not { } navigator)
        {
            _viewModel.MouseLatLonText = LatLonFormatter.Placeholder;
            return;
        }

        var world = navigator.Viewport.ScreenToWorld(position.X, position.Y);
        var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
        if (double.IsNaN(lat) || double.IsNaN(lon) ||
            double.IsInfinity(lat) || double.IsInfinity(lon) ||
            lat < -90.0 || lat > 90.0)
        {
            _viewModel.MouseLatLonText = LatLonFormatter.Placeholder;
            return;
        }

        // Normalize longitude into the canonical [-180, 180] range so that
        // panning past the antimeridian still produces sensible readings.
        lon = ((lon + 540.0) % 360.0) - 180.0;
        _viewModel.MouseLatLonText = LatLonFormatter.Format(lat, lon);
    }

    private void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_toolController?.OnPointerReleased(e) == true)
        {
            e.Handled = true;
        }
        CancelLongPress();
    }

    private void OnMapPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CancelLongPress();
    }

    private void CancelLongPress()
    {
        _longPressTimer?.Stop();
        _longPressTimer = null;
        _longPressOrigin = null;
    }

    private void OnLongPressElapsed(object? sender, EventArgs e)
    {
        if (_longPressOrigin is not { } origin || _mapControl is null)
        {
            CancelLongPress();
            return;
        }

        _longPressTimer?.Stop();
        _longPressTimer = null;

        // Translate the press position to a Mapsui MapInfo and dispatch the
        // pick. Setting _longPressFired suppresses the SingleTap that will be
        // synthesized when the user lifts their finger.
        var datasetLayers = GetDatasetLayers();
        var mapInfo = _mapControl.GetMapInfo(new ScreenPosition(origin.X, origin.Y), datasetLayers);
        _longPressOrigin = null;
        _longPressFired = true;
        _pickService.HandlePick(mapInfo, CollectDynamicHits(mapInfo));
    }

    private void PerformPickAt(BaseEventArgs e)
    {
        var datasetLayers = GetDatasetLayers();
        var mapInfo = e.GetMapInfo?.Invoke(datasetLayers);
        _pickService.HandlePick(mapInfo, CollectDynamicHits(mapInfo));
    }

    /// <summary>
    /// Asks the dynamic-source pick service to hit-test the supplied
    /// <see cref="MapInfo"/>'s world position. Returns an empty list
    /// when the service is unavailable (legacy ctor / test stubs) or
    /// when the <see cref="MapInfo"/> lacks a world position.
    /// </summary>
    private IReadOnlyList<ViewModels.DynamicPickHit> CollectDynamicHits(MapInfo? mapInfo)
    {
        if (_dynamicPickService is null || mapInfo?.WorldPosition is not { } world)
        {
            return Array.Empty<ViewModels.DynamicPickHit>();
        }

        var resolution = _mapControl?.Map?.Navigator?.Viewport.Resolution ?? double.NaN;
        return _dynamicPickService.Pick(world, resolution);
    }

    private List<ILayer> GetDatasetLayers()
    {
        var result = new List<ILayer>();
        foreach (var layers in _loader.EntryLayers.Values)
        {
            result.AddRange(layers);
        }
        return result;
    }
}
