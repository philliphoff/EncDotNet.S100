using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mapsui.Extensions;
using Mapsui.Manipulations;
using Microsoft.Extensions.DependencyInjection;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Catalogs;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Tiling;

namespace EncDotNet.S100.Viewer;

public partial class MainWindow : ShadUI.Window
{
    private readonly IRecentFilesService _recentFiles;
    private readonly ScreenshotService _screenshotService;
    private readonly IDatasetLoaderService _loader;
    private readonly IPickService _pickService;
    private readonly IFileDialogService _fileDialog;
    private readonly MainViewModel _viewModel;
    private readonly DatasetCatalogAggregator _catalogAggregator;
    private string? _screenshotPath;
    private double _lastPaneWidth = 320;
    private double _lastPickPanelWidth = 360;
    private Point? _lastMouseScreenPos;

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

    private DispatcherTimer? _longPressTimer;
    private Avalonia.Point? _longPressOrigin;
    private bool _longPressFired;

    /// <summary>
    /// Modifier state captured at the most recent pointer-press on the map,
    /// used by <see cref="OnMapTapped"/> to detect modifier-click since the
    /// Mapsui tap event doesn't carry keyboard modifiers itself.
    /// </summary>
    private KeyModifiers _lastPressedModifiers = KeyModifiers.None;

    private void ApplyPickPanelColumnState()
    {
        // Pick panel lives in column index 4; the splitter is column index 3.
        var col = ContentGrid.ColumnDefinitions[4];
        if (_viewModel.IsPickPanelVisible)
        {
            col.Width = new GridLength(_lastPickPanelWidth, GridUnitType.Pixel);
            col.MinWidth = 240;
            col.MaxWidth = 600;
        }
        else
        {
            _lastPickPanelWidth = col.Width.IsAbsolute ? col.Width.Value : 360;
            col.Width = new GridLength(0);
            col.MinWidth = 0;
            col.MaxWidth = 0;
        }
    }

    public MainWindow() : this(null) { }

    /// <summary>
    /// Legacy constructor retained for the Avalonia design-time previewer
    /// and for callers that pre-DI created their own instance. Falls through
    /// to the dependency-injected constructor by resolving services from
    /// <see cref="App.Services"/> if available, else newing up defaults.
    /// </summary>
    internal MainWindow(ViewerCommandSettings? options)
        : this(
            options,
            ResolveOrFallback<MainViewModel>(static () => throw new InvalidOperationException(
                "MainViewModel cannot be resolved without the application service provider.")),
            ResolveOrFallback<DatasetCatalogAggregator>(static () => new DatasetCatalogAggregator()),
            ResolveOrFallback<IRecentFilesService>(static () => throw new InvalidOperationException(
                "IRecentFilesService cannot be resolved without the application service provider.")),
            ResolveOrFallback<ScreenshotService>(static () => new ScreenshotService()),
            ResolveOrFallback<IDatasetLoaderService>(static () => throw new InvalidOperationException(
                "IDatasetLoaderService cannot be resolved without the application service provider.")),
            ResolveOrFallback<IPickService>(static () => throw new InvalidOperationException(
                "IPickService cannot be resolved without the application service provider.")),
            ResolveOrFallback<IFileDialogService>(static () => new FileDialogService()))
    {
    }

    private static T ResolveOrFallback<T>(Func<T> fallback) where T : class
    {
        try
        {
            return App.Services.GetRequiredService<T>();
        }
        catch (InvalidOperationException)
        {
            return fallback();
        }
    }

    internal MainWindow(
        ViewerCommandSettings? options,
        MainViewModel viewModel,
        DatasetCatalogAggregator catalogAggregator,
        IRecentFilesService recentFiles,
        ScreenshotService screenshotService,
        IDatasetLoaderService loader,
        IPickService pickService,
        IFileDialogService fileDialog)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(catalogAggregator);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(screenshotService);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(pickService);
        ArgumentNullException.ThrowIfNull(fileDialog);

        InitializeComponent();

        _viewModel = viewModel;
        _catalogAggregator = catalogAggregator;
        _recentFiles = recentFiles;
        _screenshotService = screenshotService;
        _loader = loader;
        _pickService = pickService;
        _fileDialog = fileDialog;

        // Hand the loader a map host now that the Mapsui control exists, and
        // seed catalogues / build the pipeline factory from CLI options. The
        // loader subscribes to its own settings dependencies internally.
        _loader.Initialize(new MapsuiMapHost(MapControl), options);
        _loader.StatusChanged += text => _viewModel.StatusText = text;
        _loader.DatasetLoaded += entry =>
        {
            if (_screenshotPath is not null)
            {
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await Task.Delay(2000);
                    CaptureScreenshot(_screenshotPath);
                });
            }
        };

        DataContext = _viewModel;

        // Build the native menu bar (File / View › Appearance) and keep its
        // toggle items mirrored against the view-model. The builder owns the
        // PropertyChanged subscriptions for the lifetime of this window.
        new NativeMenuBuilder(_viewModel, _recentFiles).Attach(
            window: this,
            openDatasetAsync: OpenDatasetAsync,
            openRecentAsync: OpenRecentAsync,
            onPickModeToggled: () =>
            {
                ApplyPickModeCursor();
                ApplyPickModeButtonState();
            });

        // Show built-in specification entries in the catalogue views
        foreach (var spec in Specifications.Specification.AvailableSpecs)
        {
            _viewModel.FeatureCatalogues.AddBuiltIn(spec, Strings.Catalogue_BuiltInLabel, CatalogueSpecDetection.ReadBuiltInFeatureCatalogueVersion(spec));

            if (Specifications.Specification.HasPortrayalCatalogue(spec))
            {
                _viewModel.PortrayalCatalogues.AddBuiltIn(spec, Strings.Catalogue_BuiltInLabel, CatalogueSpecDetection.ReadBuiltInPortrayalCatalogueVersion(spec));
            }
        }

        // Apply persisted accent color
        ApplyAccentColor(_viewModel.Settings.AccentColor);
        _viewModel.Settings.AccentColorChanged += ApplyAccentColor;

        // Apply persisted scale-bar distance unit and react to changes.
        ScaleBar.Unit = _viewModel.Settings.DistanceUnit;
        _viewModel.Settings.DistanceUnitChanged += unit => ScaleBar.Unit = unit;

        // Surface DatasetsViewModel rejection of unknown file extensions.
        _viewModel.Datasets.UnrecognizedFileEncountered += extension =>
            _viewModel.StatusText = string.Format(Strings.Status_UnrecognizedFileType, extension);

        // If no pane is initially selected, start collapsed
        if (!_viewModel.IsPaneVisible)
        {
            var col = ContentGrid.ColumnDefinitions[0];
            col.Width = new GridLength(0);
            col.MinWidth = 0;
            col.MaxWidth = 0;
        }

        // Collapse/expand the pane column when visibility changes
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPaneVisible))
            {
                var col = ContentGrid.ColumnDefinitions[0];
                if (_viewModel.IsPaneVisible)
                {
                    col.Width = new GridLength(_lastPaneWidth, GridUnitType.Pixel);
                    col.MinWidth = 200;
                    col.MaxWidth = 600;
                }
                else
                {
                    _lastPaneWidth = col.Width.IsAbsolute ? col.Width.Value : 320;
                    col.Width = new GridLength(0);
                    col.MinWidth = 0;
                    col.MaxWidth = 0;
                }
            }
        };

        // Pick panel column starts collapsed; expand only when a pick is shown.
        ApplyPickPanelColumnState();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPickPanelVisible))
                ApplyPickPanelColumnState();
        };

        // Clean up layers when a dataset entry is removed from the list.
        _viewModel.Datasets.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems is not null)
            {
                foreach (DatasetEntry removed in e.OldItems)
                {
                    _loader.RemoveEntry(removed);
                }
            }
        };

        MapControl.Map?.Layers.Add(OpenStreetMap.CreateTileLayer());

        // Disable Mapsui's built-in LoggingWidget — it can throw "minX > maxX" on
        // narrow viewports during resize, and the exception is raised on the
        // render thread where we cannot intercept it.
        Mapsui.Widgets.InfoWidgets.LoggingWidget.ShowLoggingInMap = Mapsui.Widgets.ActiveMode.No;
        if (MapControl.Map is { } mapForWidgets)
        {
            var remaining = mapForWidgets.Widgets
                .Where(w => w is not Mapsui.Widgets.InfoWidgets.LoggingWidget)
                .ToArray();
            mapForWidgets.Widgets.Clear();
            foreach (var w in remaining)
            {
                mapForWidgets.Widgets.Enqueue(w);
            }
        }

        // Enable pinch-to-zoom on the map control via trackpad magnify gesture
        MapControl.AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, OnMapMagnify);

        // Enable trackpad rotate gesture to rotate the map
        MapControl.AddHandler(Gestures.PointerTouchPadGestureRotateEvent, OnMapRotateGesture);

        // Enable double-tap to zoom in
        MapControl.DoubleTapped += OnMapDoubleTapped;

        // Enable single-tap feature identify (pick report)
        MapControl.MapTapped += OnMapTapped;

        // Long-press (~500ms hold without moving) is always a one-shot pick
        // regardless of Pick Mode. Tunneling lets us see the press before
        // MapControl handles it.
        MapControl.AddHandler(PointerPressedEvent, OnMapPointerPressed, RoutingStrategies.Tunnel);
        MapControl.AddHandler(PointerMovedEvent, OnMapPointerMoved, RoutingStrategies.Tunnel);
        MapControl.AddHandler(PointerReleasedEvent, OnMapPointerReleased, RoutingStrategies.Tunnel);
        MapControl.AddHandler(PointerCaptureLostEvent, OnMapPointerCaptureLost, RoutingStrategies.Tunnel);
        MapControl.PointerExited += OnMapPointerExited;

        // Esc exits Pick Mode.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);

        // Apply the cursor that matches the current mode.
        ApplyPickModeCursor();

        // Enable drag & drop of dataset files onto the map
        AddHandler(DragDrop.DropEvent, OnDrop);

        // Enable trackpad scroll/swipe to pan the map (tunnel phase to intercept before MapControl)
        MapControl.AddHandler(PointerWheelChangedEvent, OnMapPointerWheelChanged, RoutingStrategies.Tunnel);

        // Zoom in/out overlay buttons
        ZoomInButton.Click += OnZoomInClick;
        ZoomOutButton.Click += OnZoomOutClick;

        // Keep the scale bar in sync with the viewport.
        if (MapControl.Map?.Navigator is { } scaleNav)
        {
            scaleNav.ViewportChanged += OnViewportChangedForScaleBar;
            scaleNav.ViewportChanged += OnViewportChangedForMouseLatLon;
            UpdateScaleBar(scaleNav.Viewport);
        }

        // Drive the map rotation from compass-rose drag gestures.
        CompassRose.RotationRequested += OnCompassRotationRequested;
        CompassRose.RotationResetRequested += OnCompassRotationReset;

        // Apply CLI options
        _screenshotPath = options?.ScreenshotPath;

        // Add CLI portrayal catalogues to the view model (transient — not persisted)
        if (options?.PortrayalCatalogues is { } pcArgs)
        {
            foreach (var pcPath in pcArgs)
            {
                if (Directory.Exists(pcPath) && CatalogueSpecDetection.DetectPortrayalCatalogueSpec(pcPath) is { } pcSpec)
                {
                    _viewModel.PortrayalCatalogues.AddTransient(pcSpec, pcPath);
                }
            }
        }

        // Add CLI feature catalogues to the view model (transient — not persisted)
        if (options?.FeatureCatalogues is { } fcArgs)
        {
            foreach (var fcPath in fcArgs)
            {
                if (File.Exists(fcPath) && CatalogueSpecDetection.DetectFeatureCatalogueSpec(fcPath) is { } fcSpec)
                {
                    _viewModel.FeatureCatalogues.AddTransient(fcSpec, fcPath);
                }
            }
        }

        // Load CLI dataset files
        var datasetPaths = options?.Datasets?.Where(File.Exists).ToArray() ?? [];
        if (datasetPaths.Length > 0)
        {
            Opened += async (_, _) =>
            {
                _viewModel.SelectedActivity = ViewModels.ActivityKind.Datasets;
                foreach (var datasetPath in datasetPaths)
                {
                    var spec = DatasetPipelineFactory.DetectProductSpec(datasetPath) ?? "S-101";
                    var entry = _viewModel.Datasets.Add(datasetPath, spec);
                    await _loader.LoadAsync(entry);
                }
            };
        }
    }

    private void ApplyAccentColor(Color color)
    {
        Resources["AccentBrush"] = new SolidColorBrush(color);
    }

    private void CaptureScreenshot(string outputPath)
    {
        _screenshotService.Capture(MapControl, outputPath);
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
            return;

        navigator.ZoomTo(navigator.Viewport.Resolution / 2, 250);
    }

    private void OnViewportChangedForScaleBar(object? sender, Mapsui.ViewportChangedEventArgs e)
    {
        if (MapControl.Map?.Navigator is not { } nav)
            return;

        var viewport = nav.Viewport;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            UpdateScaleBar(viewport);
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateScaleBar(viewport));
        }
    }

    private void UpdateScaleBar(Mapsui.Viewport viewport)
    {
        ScaleBar.UpdateForViewport(viewport.Resolution, viewport.CenterY);
        CompassRose.UpdateForViewport(viewport.Rotation);
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
            return;

        navigator.ZoomTo(navigator.Viewport.Resolution * 2, 250);
    }

    private void OnMapMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
            return;

        var resolution = navigator.Viewport.Resolution;
        var newResolution = resolution / (1 + e.Delta.Y);
        var position = e.GetPosition(MapControl);
        var center = new ScreenPosition(position.X, position.Y);
        navigator.ZoomTo(newResolution, center);
        e.Handled = true;
    }

    private void OnMapRotateGesture(object? sender, PointerDeltaEventArgs e)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
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
        if (MapControl.Map?.Navigator is not { } navigator)
            return;
        navigator.RotateTo(rotationDegrees);
    }

    private void OnCompassRotationReset()
    {
        if (MapControl.Map?.Navigator is not { } navigator)
            return;
        // Pick the equivalent rotation closest to 0 to avoid spinning the long way.
        var current = navigator.Viewport.Rotation;
        var target = current > 180.0 ? 360.0 : 0.0;
        navigator.RotateTo(target, 250);
    }

    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
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
        // In Pick Mode the double-tap zoom is suppressed so that successive
        // taps on adjacent features each register as picks rather than zooms.
        if (_viewModel.IsPickModeActive)
        {
            e.Handled = true;
            return;
        }

        if (MapControl.Map?.Navigator is not { } navigator)
            return;

        var resolution = navigator.Viewport.Resolution;
        var newResolution = resolution / 2;
        var position = e.GetPosition(MapControl);
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

    /// <summary>
    /// Starts the long-press timer when the primary pointer button is pressed.
    /// </summary>
    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(MapControl).Properties;
        if (!props.IsLeftButtonPressed)
            return;

        // Capture modifier state for OnMapTapped's modifier-click test (the
        // Mapsui tap event doesn't carry keyboard modifiers).
        _lastPressedModifiers = e.KeyModifiers;

        // Modifier-click is handled in OnMapTapped (where we have a MapInfo
        // resolver); skip the long-press timer for that case.
        if (IsPickModifierActive())
            return;

        _longPressOrigin = e.GetPosition(MapControl);
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

        if (_longPressTimer is null || _longPressOrigin is not { } origin)
            return;

        var current = e.GetPosition(MapControl);
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

    private void OnViewportChangedForMouseLatLon(object? sender, EventArgs e)
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
        var position = e.GetPosition(MapControl);
        var bounds = MapControl.Bounds;
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
        if (MapControl.Map?.Navigator is not { } navigator)
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
        if (_longPressOrigin is not { } origin)
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
        var mapInfo = MapControl.GetMapInfo(new ScreenPosition(origin.X, origin.Y), datasetLayers);
        _longPressOrigin = null;
        _longPressFired = true;
        _pickService.HandlePick(mapInfo);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.IsPickModeActive)
        {
            _viewModel.ExitPickModeCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Updates the map's cursor to reflect Pick Mode (cross-hair) vs. the
    /// default panning cursor.
    /// </summary>
    private void ApplyPickModeCursor()
    {
        MapControl.Cursor = _viewModel.IsPickModeActive
            ? new Cursor(StandardCursorType.Cross)
            : Cursor.Default;
    }

    /// <summary>
    /// Toggles a CSS-style "pickActive" class on the Pick Mode button so the
    /// XAML style selectors can light it up with the accent color.
    /// </summary>
    private void ApplyPickModeButtonState()
    {
        const string activeClass = "pickActive";
        if (_viewModel.IsPickModeActive)
        {
            PickModeButton.Classes.Add(activeClass);
            // Set Background as a local value so it beats ShadUI's Button.Icon
            // :pointerover style (local values outrank all style setters).
            if (this.TryFindResource("AccentBrush", out var brush) && brush is IBrush accent)
            {
                PickModeButton.Background = accent;
                PickModeButton.BorderBrush = accent;
            }
        }
        else
        {
            PickModeButton.Classes.Remove(activeClass);
            PickModeButton.ClearValue(Button.BackgroundProperty);
            PickModeButton.ClearValue(Button.BorderBrushProperty);
        }
    }

    /// <summary>
    /// Click handler for the Pick Mode toolbar button — flips the view-model
    /// flag, which in turn updates cursor + button styling via PropertyChanged.
    /// </summary>
    private void OnPickModeButtonClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.TogglePickModeCommand.Execute(null);
    }

    private void PerformPickAt(BaseEventArgs e)
    {
        var datasetLayers = GetDatasetLayers();
        var mapInfo = e.GetMapInfo?.Invoke(datasetLayers);
        _pickService.HandlePick(mapInfo);
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

    private async Task OpenDatasetAsync()
    {
        var paths = await _fileDialog.OpenDatasetsAsync(this, allowMultiple: true);
        if (paths.Count == 0)
            return;

        _viewModel.SelectedActivity = ViewModels.ActivityKind.Datasets;

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            await _viewModel.Datasets.LoadFromPathAsync(path);
        }
    }

    private async Task OpenRecentAsync(string path)
    {
        if (!File.Exists(path))
        {
            _viewModel.StatusText = string.Format(Strings.Status_FileNoLongerExists, path);
            // Drop the missing entry so the menu reflects reality.
            _recentFiles.Remove(path);
            return;
        }

        _viewModel.SelectedActivity = ViewModels.ActivityKind.Datasets;
        await _viewModel.Datasets.LoadFromPathAsync(path);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files)
            return;

        _viewModel.SelectedActivity = ViewModels.ActivityKind.Datasets;

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (path is null || !File.Exists(path))
                continue;

            await _viewModel.Datasets.LoadFromPathAsync(path);
        }
    }
}
