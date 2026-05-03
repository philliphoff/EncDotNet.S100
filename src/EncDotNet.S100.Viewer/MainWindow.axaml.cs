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
            openDatasetAsync: OpenDatasetAsync);

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

        // Enable trackpad pan/pinch/rotate gestures, single/double-tap pick,
        // long-press pick, mouse lat/lon readout, scale-bar/compass viewport
        // sync, and the zoom in/out overlay buttons.
        new MapInteractionController(_viewModel, _pickService, _loader)
            .Attach(MapControl, ZoomInButton, ZoomOutButton, ScaleBar, CompassRose);

        // Apply the cursor that matches the current mode and keep it in sync
        // with the view-model.
        ApplyPickModeCursor();
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsPickModeActive))
                ApplyPickModeCursor();
        };

        // Enable drag & drop of dataset files onto the map
        AddHandler(DragDrop.DropEvent, OnDrop);

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

    /// <summary>
    /// Updates the map's cursor to reflect Pick Mode (cross-hair) vs. the
    /// default panning cursor. Called once at startup and again whenever
    /// <see cref="MainViewModel.IsPickModeActive"/> changes.
    /// </summary>
    private void ApplyPickModeCursor()
    {
        MapControl.Cursor = _viewModel.IsPickModeActive
            ? new Cursor(StandardCursorType.Cross)
            : Cursor.Default;
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
