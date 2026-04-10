using System;
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
    private string? _screenshotPath;
    private double _lastPaneWidth = 320;

    public MainWindow()
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

        _pipelineFactory = new DatasetPipelineFactory(
            _catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            spec => _settings.FeatureCataloguePaths.TryGetValue(spec, out var p) ? p : null);

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

        MapControl.Map?.Layers.Add(OpenStreetMap.CreateTileLayer());

        // Enable pinch-to-zoom on the map control via trackpad magnify gesture
        MapControl.AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, OnMapMagnify);

        // Enable double-tap to zoom in
        MapControl.DoubleTapped += OnMapDoubleTapped;

        // Enable trackpad scroll/swipe to pan the map (tunnel phase to intercept before MapControl)
        MapControl.AddHandler(PointerWheelChangedEvent, OnMapPointerWheelChanged, RoutingStrategies.Tunnel);

        // Parse command-line arguments
        var cliArgs = Environment.GetCommandLineArgs();
        string? datasetArg = null;

        for (int i = 1; i < cliArgs.Length; i++)
        {
            if (cliArgs[i] == "--screenshot" && i + 1 < cliArgs.Length)
            {
                _screenshotPath = cliArgs[++i];
            }
            else if (datasetArg is null && File.Exists(cliArgs[i]))
            {
                datasetArg = cliArgs[i];
            }
        }

        if (datasetArg is not null)
        {
            Opened += async (_, _) =>
            {
                var spec = DatasetPipelineFactory.DetectProductSpec(datasetArg) ?? "S-101";
                var entry = _viewModel.Datasets.Add(datasetArg, spec);
                _viewModel.SelectedActivity = ViewModels.ActivityKind.Datasets;
                await LoadDatasetAsync(entry);
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

        if (!_catalogueManager.HasCatalogue(spec))
        {
            _viewModel.StatusText = $"Please select a portrayal catalogue for {spec} first.";
            return;
        }

        _viewModel.StatusText = $"Loading {Path.GetFileName(entry.FilePath)}...";

        try
        {
            var result = await Task.Run(() => _pipelineFactory.Process(entry.FilePath));

            RemoveDatasetLayers();
            MapControl.Map?.Layers.Add(result.Layer);

            if (MapControl.Map?.Navigator is { } nav)
            {
                nav.ZoomToBox(result.Extent.Grow(result.Extent.Width * 0.1, result.Extent.Height * 0.1));
            }

            entry.IsLoaded = true;
            entry.Info = result.Info;
            _viewModel.StatusText = result.Info;

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

    private void RemoveDatasetLayers()
    {
        if (MapControl.Map is not { } map)
            return;

        // Remove any layers added by dataset processing (named "S-1xx: ...")
        var toRemove = map.Layers
            .Where(l => l.Name?.StartsWith("S-10", StringComparison.Ordinal) == true)
            .ToList();

        foreach (var layer in toRemove)
        {
            map.Layers.Remove(layer);
        }
    }
}
