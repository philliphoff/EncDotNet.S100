using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.MoonSharp;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Tiling;

namespace EncDotNet.S100.Viewer;

public partial class MainWindow : Window
{
    private const string CoverageLayerName = "S-102 Coverage";
    private static readonly ILuaEngine LuaEngine = new MoonSharpLuaEngine();
    private readonly ViewerSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        _settings = ViewerSettings.Load();
        UpdatePortrayalButtonText();

        MapControl.Map?.Layers.Add(OpenStreetMap.CreateTileLayer());
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open S-102 HDF5 File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("HDF5 Files") { Patterns = ["*.h5", "*.H5", "*.hdf5"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (path is null)
            return;

        await LoadDatasetAsync(path);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        RemoveCoverageLayer();
        ClearButton.IsEnabled = false;
        SetStatus(null);
    }

    private async void OnPortrayalClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select S-102 Portrayal Catalogue Folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
            return;

        var folderPath = folders[0].TryGetLocalPath();
        if (folderPath is null)
            return;

        _settings.PortrayalCataloguePath = folderPath;
        _settings.Save();
        UpdatePortrayalButtonText();
        SetStatus($"Portrayal catalogue: {folderPath}");
    }

    private void UpdatePortrayalButtonText()
    {
        if (_settings.PortrayalCataloguePath is { } p)
        {
            PortrayalButton.Content = $"Portrayal: {Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar))}";
        }
        else
        {
            PortrayalButton.Content = "Portrayal Catalogue...";
        }
    }

    private async Task LoadDatasetAsync(string path)
    {
        if (_settings.PortrayalCataloguePath is not { } portrayalPath
            || !Directory.Exists(portrayalPath))
        {
            SetStatus("Please select an S-102 Portrayal Catalogue folder first.");
            return;
        }

        SetStatus($"Loading {Path.GetFileName(path)}...");
        OpenButton.IsEnabled = false;

        try
        {
            var (layer, extent, info) = await Task.Run(() => BuildCoverageLayer(path, portrayalPath));

            RemoveCoverageLayer();
            MapControl.Map?.Layers.Add(layer);

            if (MapControl.Map?.Navigator is { } nav)
            {
                nav.ZoomToBox(extent.Grow(extent.Width * 0.1, extent.Height * 0.1));
            }

            ClearButton.IsEnabled = true;
            SetStatus(info);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            Debug.WriteLine($"Failed to load {path}: {ex}");
        }
        finally
        {
            OpenButton.IsEnabled = true;
        }
    }

    private static (ILayer Layer, MRect Extent, string Info) BuildCoverageLayer(string path, string portrayalDir)
    {
        // 1. Read the S-102 dataset
        using var hdf5 = PureHdfFile.Open(path);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var metadata = source.Metadata;

        // 2. Open the portrayal catalogue
        using var assetSource = FileSystemAssetSource.Create(portrayalDir);
        using var provider = PortrayalCatalogueProvider.OpenAsync(assetSource).GetAwaiter().GetResult();
        var catalogue = new S102PortrayalCatalogue(LuaEngine, provider) { FourShades = true };
        var context = new NavigationContext
        {
            Viewport = new Pipelines.Viewport
            {
                MinLatitude = metadata.Extent.SouthLatitude,
                MaxLatitude = metadata.Extent.NorthLatitude,
                MinLongitude = metadata.Extent.WestLongitude,
                MaxLongitude = metadata.Extent.EastLongitude,
                WidthPixels = metadata.GridMetadata.NumColumns,
                HeightPixels = metadata.GridMetadata.NumRows,
            },
            ScaleDenominator = 50_000,
        };

        var colorScheme = catalogue.ResolveColorScheme(context);
        var sampled = source.Sample(GridRegion.Full);

        var styledLayer = new StyledCoverageLayer
        {
            Coverage = sampled,
            ColorScheme = colorScheme,
            NoDataValue = S102CoverageSource.FillValue,
            Georeferencer = new GridGeoreferencer(
                metadata.GridMetadata,
                metadata.HorizontalCRS),
        };

        // 3. Render to a Mapsui layer via the reprojecting renderer
        var renderer = new MapsuiCoverageRenderer(new ProjNetCrsTransformFactory())
        {
            LayerName = CoverageLayerName,
        };

        var mapLayer = renderer.Render(styledLayer, context.Viewport);

        // 4. Extract extent for zoom-to-fit
        var extent = mapLayer.Extent ?? new MRect(0, 0, 0, 0);

        var grid = metadata.GridMetadata;
        int crs = dataset.HorizontalCRS ?? 4326;
        var geoId = dataset.GeographicIdentifier ?? Path.GetFileName(path);
        var info = $"{geoId} — {grid.NumColumns}×{grid.NumRows} grid, CRS: EPSG:{crs}";

        return (mapLayer, extent, info);
    }

    private void RemoveCoverageLayer()
    {
        if (MapControl.Map is not { } map)
            return;

        var existing = map.Layers.FindLayer(CoverageLayerName);
        foreach (var layer in existing)
        {
            map.Layers.Remove(layer);
        }
    }

    private void SetStatus(string? message)
    {
        if (message is null)
        {
            StatusBorder.IsVisible = false;
        }
        else
        {
            StatusText.Text = message;
            StatusBorder.IsVisible = true;
        }
    }
}
