using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
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
using EncDotNet.S100.Viewer.Tools;
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
    private readonly IExchangeSetService _exchangeSetService;
    private readonly MainViewModel _viewModel;
    private readonly DatasetCatalogAggregator _catalogAggregator;
    private ValidationOverlayService? _validationOverlay;
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
            ResolveOrFallback<IFileDialogService>(static () => new FileDialogService()),
            ResolveOrFallback<IExchangeSetService>(static () => throw new InvalidOperationException(
                "IExchangeSetService cannot be resolved without the application service provider.")))
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
        IFileDialogService fileDialog,
        IExchangeSetService exchangeSetService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(catalogAggregator);
        ArgumentNullException.ThrowIfNull(recentFiles);
        ArgumentNullException.ThrowIfNull(screenshotService);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(pickService);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(exchangeSetService);

        InitializeComponent();

        // Wire the ShadUI toast host to the DI-managed ToastManager so
        // background services can surface notifications through toasts.
        ToastHost.Manager = App.Services.GetRequiredService<ShadUI.ToastManager>();

        _viewModel = viewModel;
        _catalogAggregator = catalogAggregator;
        _recentFiles = recentFiles;
        _screenshotService = screenshotService;
        _loader = loader;
        _pickService = pickService;
        _fileDialog = fileDialog;
        _exchangeSetService = exchangeSetService;

        // Hand the loader a map host now that the Mapsui control exists, and
        // seed catalogues / build the pipeline factory from CLI options. The
        // loader subscribes to its own settings dependencies internally.
        var mapHost = new MapsuiMapHost(MapControl);
        App.Services.GetRequiredService<IMapHostAccessor>().Current = mapHost;
        _loader.Initialize(mapHost, options);
        // Wire validation finding click-to-zoom: each finding view-model
        // routes its <c>ZoomToFindingCommand</c> through this dispatcher.
        _viewModel.Datasets.ZoomDispatcher = mapHost.ZoomToExtent;
        // Build the validation findings overlay layer that draws above
        // all dataset layers for the currently-selected dataset. The
        // service subscribes to the datasets view-model and lives for
        // the lifetime of the window.
        _validationOverlay = new ValidationOverlayService(mapHost, _viewModel.Datasets);
        Closed += (_, _) =>
        {
            _validationOverlay?.Dispose();
            _validationOverlay = null;
            // PR-M3: flush any pending debounced size writes so the last
            // splitter drag isn't lost on shutdown.
            _viewModel.OnShutdown();
        };
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
            openExchangeSetAsync: OpenExchangeSetAsync,
            openExchangeSetZipAsync: OpenExchangeSetZipAsync);

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
        {
            _viewModel.StatusText = string.Format(Strings.Status_UnrecognizedFileType, extension);
            App.Services.GetRequiredService<IToastService>().ShowWarning(
                Strings.Toast_Warning,
                string.Format(Strings.Status_UnrecognizedFileType, extension));
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

        // Enable trackpad pan/pinch/rotate gestures, single/double-tap pick,
        // long-press pick, mouse lat/lon readout, scale-bar/compass viewport
        // sync, and the zoom in/out overlay buttons.
        var interactionController = new MapInteractionController(_viewModel, _pickService, _loader);
        interactionController.Attach(MapControl, ZoomInButton, ZoomOutButton, ZoomToExtentButton, ScaleBar, CompassRose);

        // Wire the map-tool controller to the map: tools are registered with
        // the view-model's controller and pointer events are forwarded by
        // the interaction controller.
        InitializeMapTools(interactionController);

        // Apply the cursor that matches the current mode and keep it in sync
        // with the active tool.
        ApplyToolCursor();
        _viewModel.Tools.ActiveToolChanged += _ => Dispatcher.UIThread.Post(ApplyToolCursor);

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
                _viewModel.SelectDefaultTab();
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
    /// Updates the map's cursor to reflect the active map tool (Pick Mode
    /// cross-hair, Measure Mode cross-hair, etc.). Called once at startup
    /// and again whenever the active tool changes.
    /// </summary>
    private void ApplyToolCursor()
    {
        MapControl.Cursor = _viewModel.Tools.ActiveTool?.Cursor ?? Cursor.Default;
    }

    /// <summary>
    /// Registers the available <see cref="IMapTool"/>s with the view-model's
    /// <see cref="MapToolController"/>, then initialises the controller with
    /// a context that knows how to add overlay layers, refresh graphics, and
    /// project pointer positions to lat/lon.
    /// </summary>
    private void InitializeMapTools(MapInteractionController interactionController)
    {
        var tools = _viewModel.Tools;

        var context = new MapToolContext(
            mapControl: MapControl,
            addLayer: layer => MapControl.Map?.Layers.Add(layer),
            removeLayer: layer => MapControl.Map?.Layers.Remove(layer),
            setStatusSummary: text => Dispatcher.UIThread.Post(() => _viewModel.MeasureSummary = text),
            refreshGraphics: () => MapControl.RefreshGraphics(),
            screenToLatLon: ScreenToLatLon);

        // Tools (pick, measure) were registered by the view-model in its
        // constructor; here we just hand them an Avalonia-aware context.
        tools.Initialize(context);

        // Hand the same controller to the interaction controller so pointer
        // events are offered to the active tool first.
        interactionController.SetToolController(tools);

        // Tool selection is intentionally not persisted across launches —
        // entering Pick or Measure mode must be an explicit user action.
    }

    /// <summary>
    /// Converts a pointer position (in <see cref="MapControl"/> client
    /// coordinates) to a WGS-84 lat/lon, or <c>null</c> when the pointer
    /// projects to an invalid Mercator location. Mirrors the projection
    /// math used by the mouse lat/lon readout in
    /// <see cref="MapInteractionController"/>.
    /// </summary>
    private (double Lat, double Lon)? ScreenToLatLon(Point screen)
    {
        if (MapControl.Map?.Navigator is not { } navigator)
            return null;

        var world = navigator.Viewport.ScreenToWorld(screen.X, screen.Y);
        var (lon, lat) = SphericalMercator.ToLonLat(world.X, world.Y);
        if (double.IsNaN(lat) || double.IsNaN(lon) ||
            double.IsInfinity(lat) || double.IsInfinity(lon) ||
            lat < -90.0 || lat > 90.0)
        {
            return null;
        }

        // Normalize longitude into the canonical (-180, 180] range so paths
        // that cross the antimeridian render with consistent endpoints.
        lon = ((lon + 540.0) % 360.0) - 180.0;
        return (lat, lon);
    }

    private async Task OpenDatasetAsync()
    {
        var paths = await _fileDialog.OpenDatasetsAsync(this, allowMultiple: true);
        if (paths.Count == 0)
            return;

        _viewModel.SelectDefaultTab();

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            await _viewModel.Datasets.LoadFromPathAsync(path);
        }
    }

    private async Task OpenExchangeSetAsync()
    {
        var folder = await _fileDialog.OpenExchangeSetFolderAsync(this);
        if (folder is null)
            return;

        await RunExchangeSetAsync(folder);
    }

    private async Task OpenExchangeSetZipAsync()
    {
        var zip = await _fileDialog.OpenExchangeSetZipAsync(this);
        if (zip is null)
            return;

        await RunExchangeSetAsync(zip);
    }

    private async Task RunExchangeSetAsync(string sourcePath)
    {
        _viewModel.SelectDefaultTab();

        var token = _viewModel.BeginExchangeSetLoad(sourcePath);
        var progress = new Progress<Services.ExchangeSetProgress>(
            p => _viewModel.ReportExchangeSetProgress(p));

        // Show a loading toast with a Cancel action that mirrors the
        // overlay's Cancel button. The toast supplements the progress
        // overlay for users who switch to another activity panel.
        var toasts = App.Services.GetRequiredService<IToastService>();
        toasts.ShowLoading(
            Strings.Toast_ExchangeSetLoading,
            sourcePath,
            Strings.Toast_Cancel,
            () => _viewModel.CancelExchangeSetCommand.Execute(null));

        // Subscribe to per-dataset load completions for the duration of
        // this open. We accumulate each loaded entry's layer extents so
        // we can zoom to their union — the catalogue may not declare
        // per-dataset bounding boxes (S-101 producer dumps often skip
        // them) and Map.Extent alone could include unrelated layers.
        var loadedEntries = new HashSet<DatasetEntry>();
        var unionSlot = new MRect?[1];
        var lastEventTicks = new long[1];
        Action<DatasetEntry> handler = entry =>
        {
            // Only count entries from any exchange set — we filter to
            // this specific open below by checking the entry's Source.
            if (!entry.IsFromExchangeSet) return;
            if (!loadedEntries.Add(entry)) return;
            if (_loader.EntryLayers.TryGetValue(entry, out var layers))
            {
                foreach (var layer in layers)
                {
                    if (layer.Extent is { } e && e.Width > 0 && e.Height > 0)
                    {
                        unionSlot[0] = unionSlot[0] is null
                            ? new MRect(e.MinX, e.MinY, e.MaxX, e.MaxY)
                            : unionSlot[0]!.Join(e);
                    }
                }
            }
            Interlocked.Exchange(ref lastEventTicks[0], Environment.TickCount64);
        };
        _loader.DatasetLoaded += handler;

        try
        {
            var result = await _exchangeSetService.OpenAsync(sourcePath, progress, token);
            _viewModel.EndExchangeSetLoad(result);

            // Prefer the catalogue's union bbox when available — it's
            // ready immediately and matches producer intent.
            if (result.UnionBoundingBox is { } bbox &&
                MapControl.Map?.Navigator is { } nav)
            {
                ZoomToCatalogueBoundingBox(nav, bbox);
                return;
            }

            // Otherwise debounce on DatasetLoaded events: zoom once no
            // new event has arrived for QuietWindowMs. This naturally
            // handles per-dataset load failures (which never raise the
            // event) without waiting a fixed timeout.
            await ZoomWhenLoadingQuietsAsync(loadedEntries, unionSlot, lastEventTicks);
        }
        catch (Exception ex)
        {
            _viewModel.EndExchangeSetLoad(new Services.ExchangeSetOpenResult
            {
                SourcePath = sourcePath,
                FailureMessage = ex.Message,
            });
        }
        finally
        {
            _loader.DatasetLoaded -= handler;
            // Dismiss the exchange-set loading toast; the result
            // toasts from ExchangeSetService will follow immediately.
            toasts.DismissAll();
        }
    }

    private void ZoomToCatalogueBoundingBox(Mapsui.Navigator nav, EncDotNet.S100.ExchangeSets.BoundingBox bbox)
    {
        // EPSG:4326 lat/lon → web mercator. SphericalMercator clamps
        // the input range, so polar catalogues degrade gracefully.
        var (minX, minY) = SphericalMercator.FromLonLat(
            bbox.WestBoundLongitude, bbox.SouthBoundLatitude);
        var (maxX, maxY) = SphericalMercator.FromLonLat(
            bbox.EastBoundLongitude, bbox.NorthBoundLatitude);
        var extent = new MRect(minX, minY, maxX, maxY);
        if (extent.Width > 0 && extent.Height > 0)
        {
            nav.ZoomToBox(extent.Grow(extent.Width * 0.1, extent.Height * 0.1), duration: 250);
        }
    }

    private async Task ZoomWhenLoadingQuietsAsync(
        HashSet<DatasetEntry> loadedEntries,
        MRect?[] unionSlot,
        long[] lastEventTicks)
    {
        // Quiet-window debounce: the per-dataset loaders complete on
        // their own background tasks and may fail silently (caught and
        // logged inside DatasetLoaderService), so we can't simply
        // await an exact count. Instead we wait until DatasetLoaded
        // events stop arriving for a short window — at that point
        // every dispatched dataset has either completed or errored,
        // and the accumulated union extent is final.
        const int QuietWindowMs = 600;
        const int PollMs = 100;
        const int MaxWaitMs = 30_000;

        var startedAt = Environment.TickCount64;
        while (true)
        {
            await Task.Delay(PollMs);

            var lastEvent = Interlocked.Read(ref lastEventTicks[0]);
            var now = Environment.TickCount64;

            // No events yet — keep waiting up to MaxWaitMs from start.
            if (lastEvent == 0)
            {
                if (now - startedAt >= MaxWaitMs) return;
                continue;
            }

            // We've seen at least one event; trigger as soon as the
            // bus is quiet for QuietWindowMs.
            if (now - lastEvent >= QuietWindowMs)
                break;

            if (now - startedAt >= MaxWaitMs)
                break;
        }

        if (MapControl.Map?.Navigator is not { } nav) return;

        var extent = unionSlot[0] ?? MapControl.Map.Extent;
        if (extent is null || extent.Width <= 0 || extent.Height <= 0) return;

        nav.ZoomToBox(extent.Grow(extent.Width * 0.1, extent.Height * 0.1), duration: 250);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { } files)
            return;

        _viewModel.SelectDefaultTab();

        foreach (var item in files)
        {
            var path = item.TryGetLocalPath();
            if (path is null)
                continue;

            // Folder drop: treat as an exchange set when CATALOG.XML
            // is at the root, otherwise ignore (the dataset loader is
            // single-file).
            if (Directory.Exists(path))
            {
                if (ExchangeSetDetection.LooksLikeExchangeSetFolder(path))
                {
                    await RunExchangeSetAsync(path);
                }
                continue;
            }

            if (!File.Exists(path))
                continue;

            // File drop: a .zip with a root-level CATALOG.XML is an
            // exchange-set ZIP; everything else falls through to the
            // single-dataset loader.
            if (ExchangeSetDetection.IsZipPath(path) &&
                ExchangeSetDetection.LooksLikeExchangeSetZip(path))
            {
                await RunExchangeSetAsync(path);
                continue;
            }

            await _viewModel.Datasets.LoadFromPathAsync(path);
        }
    }
}
