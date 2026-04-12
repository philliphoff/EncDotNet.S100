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

        _pipelineFactory = new DatasetPipelineFactory(
            _catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            spec => transientFcPaths.TryGetValue(spec, out var p) ? p
                  : _settings.FeatureCataloguePaths.TryGetValue(spec, out var sp) ? sp
                  : null);

        _viewModel = new MainViewModel(_settings, _catalogueManager);
        DataContext = _viewModel;

        // Apply persisted accent color
        ApplyAccentColor(_viewModel.Settings.AccentColor);
        _viewModel.Settings.AccentColorChanged += ApplyAccentColor;

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

        // Enable double-tap to zoom in
        MapControl.DoubleTapped += OnMapDoubleTapped;

        // Enable trackpad scroll/swipe to pan the map (tunnel phase to intercept before MapControl)
        MapControl.AddHandler(PointerWheelChangedEvent, OnMapPointerWheelChanged, RoutingStrategies.Tunnel);

        // Zoom in/out overlay buttons
        ZoomInButton.Click += OnZoomInClick;
        ZoomOutButton.Click += OnZoomOutClick;

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

            var result = await Task.Run(() => processor.Render());

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
            RenderContext context = proc switch
            {
                S104DatasetProcessor => new S104RenderContext(times[idx]),
                _ => new S111RenderContext(times[idx]),
            };
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

    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
            return;

        var viewport = navigator.Viewport;
        var dx = e.Delta.X * viewport.Resolution * 50;
        var dy = e.Delta.Y * viewport.Resolution * 50;
        navigator.CenterOn(viewport.CenterX - dx, viewport.CenterY + dy);
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
}
