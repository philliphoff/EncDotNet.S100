using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Tiling;

namespace EncDotNet.S100.Viewer;

public partial class MainWindow : Window
{
    private const string CoverageLayerName = "S-102 Coverage";

    public MainWindow()
    {
        InitializeComponent();

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

    private async Task LoadDatasetAsync(string path)
    {
        SetStatus($"Loading {Path.GetFileName(path)}...");
        OpenButton.IsEnabled = false;

        try
        {
            var (layer, extent, info) = await Task.Run(() => BuildCoverageLayer(path));

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

    private static (ILayer Layer, MRect Extent, string Info) BuildCoverageLayer(string path)
    {
        // 1. Read the S-102 dataset
        using var hdf5 = PureHdfFile.Open(path);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var metadata = source.Metadata;

        // 2. Build the styled coverage layer
        var catalogue = new S102PortrayalCatalogue { FourShades = true };
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
