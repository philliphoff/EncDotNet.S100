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
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mapsui.Manipulations;
using System.Text.RegularExpressions;
using EncDotNet.S100.Features;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Tiling;

namespace EncDotNet.S100.Viewer;

public partial class MainWindow : ShadUI.Window
{
    private readonly ViewerSettings _settings;
    private readonly PortrayalCatalogueManager _catalogueManager = new();
    private readonly DatasetPipelineFactory _pipelineFactory;
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<DatasetEntry, IDatasetProcessor> _processors = new();
    private readonly Dictionary<DatasetEntry, List<ILayer>> _entryLayers = new();
    private readonly NativeMenu _openRecentMenu = new();
    private NativeMenuItem? _openRecentMenuItem;
    private string? _screenshotPath;
    private double _lastPaneWidth = 320;

    public MainWindow() : this(null) { }

    internal MainWindow(ViewerCommandSettings? options)
    {
        InitializeComponent();

        _settings = ViewerSettings.Load();

        // Seed catalogue manager from persisted settings
        foreach (var (spec, path) in _settings.CataloguePaths)
        {
            if (Directory.Exists(path))
            {
                _catalogueManager.SetPath(spec, path);
            }
        }

        // Apply CLI portrayal catalogues (transient — not persisted)
        if (options?.PortrayalCatalogues is { } cliPcs)
        {
            foreach (var pcPath in cliPcs)
            {
                if (Directory.Exists(pcPath) && DetectPortrayalCatalogueSpec(pcPath) is { } pcSpec)
                {
                    _catalogueManager.SetPath(pcSpec, pcPath);
                }
            }
        }

        // Collect CLI feature catalogues (transient — not persisted)
        var transientFcPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (options?.FeatureCatalogues is { } cliFcs)
        {
            foreach (var fcPath in cliFcs)
            {
                if (File.Exists(fcPath) && DetectFeatureCatalogueSpec(fcPath) is { } fcSpec)
                {
                    transientFcPaths[fcSpec] = fcPath;
                }
            }
        }

        // Register bundled portrayal catalogues as fallback for any spec not yet configured
        foreach (var spec in Specifications.Specification.AvailableSpecs)
        {
            if (!_catalogueManager.HasCatalogue(spec) && Specifications.Specification.HasPortrayalCatalogue(spec))
            {
                _catalogueManager.SetSource(spec, Specifications.Specification.CreatePortrayalCatalogueSource(spec));
            }
        }

        _pipelineFactory = new DatasetPipelineFactory(
            _catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            spec => transientFcPaths.TryGetValue(spec, out var p) ? File.OpenRead(p)
                  : _settings.FeatureCataloguePaths.TryGetValue(spec, out var sp) ? File.OpenRead(sp)
                  : Specifications.Specification.TryOpenFeatureCatalogue(spec));

        _viewModel = new MainViewModel(_settings, _catalogueManager);
        DataContext = _viewModel;

        // Build native menu bar
        var sideBarItem = new NativeMenuItem("Primary Side Bar")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _viewModel.IsPaneVisible,
        };
        sideBarItem.Click += (_, _) => _viewModel.TogglePrimarySideBarCommand.Execute(null);

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPaneVisible))
                sideBarItem.IsChecked = _viewModel.IsPaneVisible;
        };

        var statusBarItem = new NativeMenuItem("Status Bar")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = _viewModel.IsStatusBarVisible,
        };
        statusBarItem.Click += (_, _) => _viewModel.ToggleStatusBarCommand.Execute(null);

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsStatusBarVisible))
                statusBarItem.IsChecked = _viewModel.IsStatusBarVisible;
        };

        var appearanceMenu = new NativeMenuItem("Appearance")
        {
            Menu = new NativeMenu { sideBarItem, statusBarItem },
        };

        var viewMenu = new NativeMenuItem("View")
        {
            Menu = new NativeMenu { appearanceMenu },
        };

        var nativeMenu = new NativeMenu { BuildFileMenu(), viewMenu };
        NativeMenu.SetMenu(this, nativeMenu);
        RebuildOpenRecentMenu();

        // Show built-in specification entries in the catalogue views
        foreach (var spec in Specifications.Specification.AvailableSpecs)
        {
            _viewModel.FeatureCatalogues.AddBuiltIn(spec, "(built-in)", ReadBuiltInFeatureCatalogueVersion(spec));

            if (Specifications.Specification.HasPortrayalCatalogue(spec))
            {
                _viewModel.PortrayalCatalogues.AddBuiltIn(spec, "(built-in)", ReadBuiltInPortrayalCatalogueVersion(spec));
            }
        }

        // Apply persisted accent color
        ApplyAccentColor(_viewModel.Settings.AccentColor);
        _viewModel.Settings.AccentColorChanged += ApplyAccentColor;

        // Re-render all loaded datasets when the color profile changes
        _viewModel.Settings.PaletteChanged += palette => _ = ReRenderAllDatasetsAsync();

        // Re-render all loaded datasets when symbol or text scale changes
        _viewModel.Settings.DisplayScaleChanged += () => _ = ReRenderAllDatasetsAsync();

        // Apply persisted scale-bar distance unit and react to changes.
        ScaleBar.Unit = _viewModel.Settings.DistanceUnit;
        _viewModel.Settings.DistanceUnitChanged += unit => ScaleBar.Unit = unit;

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

        // Wire up dataset load requests
        _viewModel.Datasets.LoadRequested += entry => _ = LoadDatasetAsync(entry);

        // Clean up layers when a dataset entry is removed from the list
        _viewModel.Datasets.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove && e.OldItems is not null)
            {
                foreach (DatasetEntry removed in e.OldItems)
                {
                    RemoveEntryLayers(removed);
                    _processors.Remove(removed);
                }
            }
        };

        MapControl.Map?.Layers.Add(OpenStreetMap.CreateTileLayer());

        // Enable pinch-to-zoom on the map control via trackpad magnify gesture
        MapControl.AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, OnMapMagnify);

        // Enable trackpad rotate gesture to rotate the map
        MapControl.AddHandler(Gestures.PointerTouchPadGestureRotateEvent, OnMapRotateGesture);

        // Enable double-tap to zoom in
        MapControl.DoubleTapped += OnMapDoubleTapped;

        // Enable single-tap feature identify (pick report)
        MapControl.MapTapped += OnMapTapped;

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
                if (Directory.Exists(pcPath) && DetectPortrayalCatalogueSpec(pcPath) is { } pcSpec)
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
                if (File.Exists(fcPath) && DetectFeatureCatalogueSpec(fcPath) is { } fcSpec)
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
                    await LoadDatasetAsync(entry);
                }
            };
        }
    }

    private void ApplyAccentColor(Color color)
    {
        Resources["AccentBrush"] = new SolidColorBrush(color);
    }

    private RenderContext CreateRenderContext(IDatasetProcessor processor, DateTime? timeStep = null)
    {
        var palette = _viewModel.Settings.SelectedPalette;
        var symbolScale = _viewModel.Settings.SymbolScale;
        var textScale = _viewModel.Settings.TextScale;

        return processor switch
        {
            S104DatasetProcessor when timeStep is not null
                => new S104RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S104DatasetProcessor
                => new S104RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S111DatasetProcessor when timeStep is not null
                => new S111RenderContext(timeStep) { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S111DatasetProcessor
                => new S111RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S101DatasetProcessor
                => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S102DatasetProcessor
                => new S102RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S124DatasetProcessor
                => new S124RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S127DatasetProcessor
                => new S127RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S129DatasetProcessor
                => new S129RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            S411DatasetProcessor
                => new S411RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
            _ => new S101RenderContext { Palette = palette, SymbolScale = symbolScale, TextScale = textScale },
        };
    }

    private async Task LoadDatasetAsync(DatasetEntry entry)
    {
        var spec = DatasetPipelineFactory.DetectProductSpec(entry.FilePath);
        if (spec is null)
        {
            _viewModel.StatusText = $"Unrecognized file type: {Path.GetExtension(entry.FilePath)}";
            return;
        }

        // S-104 ships a built-in portrayal catalogue; all others need an external one.
        if (spec != "S-104" && !_catalogueManager.HasCatalogue(spec))
        {
            _viewModel.StatusText = $"Please select a portrayal catalogue for {spec} first.";
            return;
        }

        _viewModel.StatusText = $"Loading {Path.GetFileName(entry.FilePath)}...";

        try
        {
            var processor = await Task.Run(() => _pipelineFactory.CreateProcessor(entry.FilePath));
            _processors[entry] = processor;

            var palette = _viewModel.Settings.SelectedPalette;
            var initialContext = CreateRenderContext(processor);
            var result = await Task.Run(() => processor.Render(initialContext));

            RemoveEntryLayers(entry);
            var layers = result.Layers.ToList();
            _entryLayers[entry] = layers;
            foreach (var layer in layers)
            {
                MapControl.Map?.Layers.Add(layer);
            }

            if (MapControl.Map?.Navigator is { } nav)
            {
                nav.ZoomToBox(result.Extent.Grow(result.Extent.Width * 0.1, result.Extent.Height * 0.1));
            }

            entry.IsLoaded = true;
            entry.Info = result.Info;
            _viewModel.StatusText = result.Info;

            _settings.AddRecentDataset(entry.FilePath);
            _settings.Save();
            RebuildOpenRecentMenu();

            // Populate time steps for S-111 datasets
            if (processor is S111DatasetProcessor s111)
            {
                entry.AvailableTimes = s111.AvailableTimes;
                // SelectedTimeIndex defaults to 0, matching the initial render
            }

            // Populate time steps for S-104 datasets
            if (processor is S104DatasetProcessor s104)
            {
                entry.AvailableTimes = s104.AvailableTimes;
            }

            // Re-render when the user changes the time step
            entry.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DatasetEntry.SelectedTimeIndex))
                    _ = ReRenderTimeStepAsync(entry);
            };

            if (_screenshotPath is not null)
            {
                await Task.Delay(2000);
                await Dispatcher.UIThread.InvokeAsync(() => CaptureScreenshot(_screenshotPath));
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error: {ex.Message}";
            Console.Error.WriteLine($"Failed to load {entry.FilePath}:\n{ex}");
        }
    }

    private async Task ReRenderTimeStepAsync(DatasetEntry entry)
    {
        if (!_processors.TryGetValue(entry, out var proc))
            return;

        var times = entry.AvailableTimes;
        var idx = entry.SelectedTimeIndex;
        if (times is null || idx < 0 || idx >= times.Count)
            return;

        _viewModel.StatusText = $"Rendering time step {idx + 1}/{times.Count}...";

        try
        {
            var context = CreateRenderContext(proc, times[idx]);
            var result = await Task.Run(() => proc.Render(context));

            RemoveEntryLayers(entry);
            var layers = result.Layers.ToList();
            _entryLayers[entry] = layers;
            foreach (var layer in layers)
            {
                MapControl.Map?.Layers.Add(layer);
            }

            entry.Info = result.Info;
            _viewModel.StatusText = result.Info;
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Error: {ex.Message}";
            Console.Error.WriteLine($"Failed to re-render time step:\n{ex}");
        }
    }

    private async Task ReRenderAllDatasetsAsync()
    {
        var palette = _viewModel.Settings.SelectedPalette;
        _viewModel.StatusText = $"Switching to {palette} palette...";

        foreach (var (entry, proc) in _processors.ToArray())
        {
            if (!entry.IsLoaded) continue;

            try
            {
                // Build a context that preserves the current time step (if any)
                var times = entry.AvailableTimes;
                var idx = entry.SelectedTimeIndex;

                DateTime? timeStep = times is not null && idx >= 0 && idx < times.Count ? times[idx] : null;
                var context = CreateRenderContext(proc, timeStep);

                var result = await Task.Run(() => proc.Render(context));

                RemoveEntryLayers(entry);
                var layers = result.Layers.ToList();
                _entryLayers[entry] = layers;
                foreach (var layer in layers)
                {
                    MapControl.Map?.Layers.Add(layer);
                }

                entry.Info = result.Info;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to re-render {entry.FilePath} with {palette} palette:\n{ex}");
            }
        }

        _viewModel.StatusText = $"{palette} palette applied.";
    }

    private void CaptureScreenshot(string outputPath)
    {
        try
        {
            var pixelSize = new PixelSize((int)MapControl.Bounds.Width, (int)MapControl.Bounds.Height);
            if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            {
                Console.Error.WriteLine($"[Screenshot] Map control has zero size, skipping.");
                return;
            }

            using var bitmap = new RenderTargetBitmap(pixelSize);
            bitmap.Render(MapControl);
            bitmap.Save(outputPath);

            Console.WriteLine($"[Screenshot] Saved {pixelSize.Width}x{pixelSize.Height} to {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Screenshot] Failed: {ex.Message}");
        }
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

        var datasetLayers = _entryLayers.Values.SelectMany(l => l);
        var mapInfo = e.GetMapInfo?.Invoke(datasetLayers);
        if (mapInfo?.Feature is not { } hitFeature || mapInfo.Layer is not { } hitLayer)
            return;

        if (hitFeature[MapsuiDisplayListRenderer.FeatureRefKey] is not string featureRef)
            return;

        // Find which dataset entry owns the hit layer
        DatasetEntry? owningEntry = null;
        foreach (var (entry, layers) in _entryLayers)
        {
            if (layers.Contains(hitLayer))
            {
                owningEntry = entry;
                break;
            }
        }

        if (owningEntry is null || !_processors.TryGetValue(owningEntry, out var processor))
            return;

        var info = processor.GetFeatureInfo(featureRef);
        if (info is null)
        {
            _viewModel.StatusText = $"Feature {featureRef} (no details available)";
            return;
        }

        var attrs = string.Join(", ", info.Attributes
            .Where(a => a.Value is not null)
            .Select(a => $"{a.Key}={a.Value}"));

        _viewModel.StatusText = string.IsNullOrEmpty(attrs)
            ? $"{info.FeatureType} [{info.FeatureRef}]"
            : $"{info.FeatureType} [{info.FeatureRef}]: {attrs}";
    }

    private NativeMenuItem BuildFileMenu()
    {
        var openItem = new NativeMenuItem("Open Dataset...")
        {
            Gesture = new KeyGesture(Key.O, KeyModifiers.Meta),
        };
        openItem.Click += (_, _) => _ = OpenDatasetAsync();

        _openRecentMenuItem = new NativeMenuItem("Open Recent")
        {
            Menu = _openRecentMenu,
        };

        return new NativeMenuItem("File")
        {
            Menu = new NativeMenu { openItem, _openRecentMenuItem },
        };
    }

    private void RebuildOpenRecentMenu()
    {
        _openRecentMenu.Items.Clear();

        var paths = _settings.RecentDatasetPaths;
        if (paths.Count == 0)
        {
            var empty = new NativeMenuItem("(No recent datasets)") { IsEnabled = false };
            _openRecentMenu.Items.Add(empty);
            if (_openRecentMenuItem is not null)
                _openRecentMenuItem.IsEnabled = false;
            return;
        }

        if (_openRecentMenuItem is not null)
            _openRecentMenuItem.IsEnabled = true;

        foreach (var path in paths)
        {
            var label = Path.GetFileName(path);
            var item = new NativeMenuItem(label)
            {
                ToolTip = path,
                IsEnabled = File.Exists(path),
            };
            var captured = path;
            item.Click += async (_, _) => await OpenRecentAsync(captured);
            _openRecentMenu.Items.Add(item);
        }

        _openRecentMenu.Items.Add(new NativeMenuItemSeparator());
        var clear = new NativeMenuItem("Clear Recently Opened");
        clear.Click += (_, _) =>
        {
            _settings.ClearRecentDatasets();
            _settings.Save();
            RebuildOpenRecentMenu();
        };
        _openRecentMenu.Items.Add(clear);
    }

    private async Task OpenDatasetAsync()
    {
        var picker = StorageProvider;
        if (picker is null)
            return;

        var fileTypes = new List<FilePickerFileType>
        {
            new("S-100 Datasets")
            {
                Patterns = new[] { "*.000", "*.h5", "*.hdf5", "*.gml" },
            },
            FilePickerFileTypes.All,
        };

        var files = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Dataset",
            AllowMultiple = true,
            FileTypeFilter = fileTypes,
        });

        if (files is null || files.Count == 0)
            return;

        _viewModel.SelectedActivity = ViewModels.ActivityKind.Datasets;

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null || !File.Exists(path))
                continue;

            await LoadDatasetFromPathAsync(path);
        }
    }

    private async Task OpenRecentAsync(string path)
    {
        if (!File.Exists(path))
        {
            _viewModel.StatusText = $"File no longer exists: {path}";
            // Drop the missing entry so the menu reflects reality.
            _settings.RecentDatasetPaths.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            RebuildOpenRecentMenu();
            return;
        }

        _viewModel.SelectedActivity = ViewModels.ActivityKind.Datasets;
        await LoadDatasetFromPathAsync(path);
    }

    private async Task LoadDatasetFromPathAsync(string path)
    {
        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        if (spec is null)
        {
            _viewModel.StatusText = $"Unrecognized file type: {Path.GetExtension(path)}";
            return;
        }

        var entry = _viewModel.Datasets.Add(path, spec);
        await LoadDatasetAsync(entry);
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

            var spec = DatasetPipelineFactory.DetectProductSpec(path);
            if (spec is null)
            {
                _viewModel.StatusText = $"Unrecognized file type: {Path.GetExtension(path)}";
                continue;
            }

            var entry = _viewModel.Datasets.Add(path, spec);
            await LoadDatasetAsync(entry);
        }
    }

    private void RemoveEntryLayers(DatasetEntry entry)
    {
        if (MapControl.Map is not { } map)
            return;

        if (_entryLayers.TryGetValue(entry, out var oldLayers))
        {
            foreach (var layer in oldLayers)
            {
                map.Layers.Remove(layer);
            }

            _entryLayers.Remove(entry);
        }
    }

    private static string? DetectPortrayalCatalogueSpec(string folderPath)
    {
        try
        {
            var cataloguePath = Path.Combine(folderPath, "portrayal_catalogue.xml");
            if (!File.Exists(cataloguePath)) return null;

            using var stream = File.OpenRead(cataloguePath);
            var catalogue = PortrayalCatalogueReader.Read(stream);
            return string.IsNullOrEmpty(catalogue.ProductId) ? null : catalogue.ProductId;
        }
        catch
        {
            return null;
        }
    }

    private static readonly Regex SpecPattern = new(@"S-\d+", RegexOptions.Compiled);

    private static string? DetectFeatureCatalogueSpec(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var catalogue = FeatureCatalogueReader.Read(stream);
            var match = SpecPattern.Match(catalogue.Name);
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadBuiltInFeatureCatalogueVersion(string spec)
    {
        try
        {
            using var stream = Specifications.Specification.TryOpenFeatureCatalogue(spec);
            if (stream is null) return null;
            var catalogue = FeatureCatalogueReader.Read(stream);
            return string.IsNullOrEmpty(catalogue.VersionNumber) ? null : catalogue.VersionNumber;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadBuiltInPortrayalCatalogueVersion(string spec)
    {
        try
        {
            using var source = Specifications.Specification.CreatePortrayalCatalogueSource(spec);
            using var stream = source.OpenAsync("portrayal_catalogue.xml").GetAwaiter().GetResult();
            var catalogue = PortrayalCatalogueReader.Read(stream);
            return string.IsNullOrEmpty(catalogue.Version) ? null : catalogue.Version;
        }
        catch
        {
            return null;
        }
    }
}
