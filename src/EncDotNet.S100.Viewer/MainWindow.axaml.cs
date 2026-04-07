using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Skia;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using SkiaSharp;

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

    private static (MemoryLayer Layer, MRect Extent, string Info) BuildCoverageLayer(string path)
    {
        // 1. Read the S-102 dataset
        using var hdf5 = PureHdfFile.Open(path);
        var dataset = S102DatasetReader.Read(hdf5);
        var source = new S102CoverageSource(dataset);
        var metadata = source.Metadata;

        // 2. Run the portrayal pipeline to get color scheme
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
        var fieldData = sampled.GetField(colorScheme.FieldName);
        int srcRows = fieldData.GetLength(0);
        int srcCols = fieldData.GetLength(1);

        // 3. Pre-resolve color bands
        var resolvedBands = new (float Min, float Max, SKColor Color)[colorScheme.Bands.Count];
        for (int i = 0; i < colorScheme.Bands.Count; i++)
        {
            var band = colorScheme.Bands[i];
            resolvedBands[i] = (band.MinValue, band.MaxValue, RgbaColor.FromHex(band.Color).ToSkia());
        }

        float noDataValue = S102CoverageSource.FillValue;

        // 4. Build CRS transform: native CRS → EPSG:3857 (Web Mercator)
        var grid = metadata.GridMetadata;
        int crs = dataset.HorizontalCRS ?? 4326;

        // First pass: project every grid node to Mercator to find the bounding box
        // and determine the output bitmap dimensions
        var nodePositions = new (double MercX, double MercY)[srcRows, srcCols];
        double mercMinX = double.MaxValue, mercMinY = double.MaxValue;
        double mercMaxX = double.MinValue, mercMaxY = double.MinValue;

        ProjNet.CoordinateSystems.Transformations.MathTransform? nativeToWgs84 = null;
        if (crs != 4326)
        {
            var wgs84 = GeographicCoordinateSystem.WGS84;
            CoordinateSystem sourceCrs;
            if (crs is >= 32601 and <= 32660)
                sourceCrs = ProjectedCoordinateSystem.WGS84_UTM(crs - 32600, true);
            else if (crs is >= 32701 and <= 32760)
                sourceCrs = ProjectedCoordinateSystem.WGS84_UTM(crs - 32700, false);
            else
                sourceCrs = wgs84; // fallback: treat as geographic
            nativeToWgs84 = new CoordinateTransformationFactory()
                .CreateFromCoordinateSystems(sourceCrs, wgs84).MathTransform;
        }

        for (int r = 0; r < srcRows; r++)
        for (int c = 0; c < srcCols; c++)
        {
            // Native coordinates (easting/lon, northing/lat for projected/geographic)
            double nativeX = grid.OriginLongitude + c * grid.SpacingLongitudinal;
            double nativeY = grid.OriginLatitude + r * grid.SpacingLatitudinal;

            double lon, lat;
            if (nativeToWgs84 is not null)
            {
                var (tx, ty) = nativeToWgs84.Transform(nativeX, nativeY);
                lon = tx;
                lat = ty;
            }
            else
            {
                lon = nativeX;
                lat = nativeY;
            }

            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            nodePositions[r, c] = (mx, my);

            if (mx < mercMinX) mercMinX = mx;
            if (my < mercMinY) mercMinY = my;
            if (mx > mercMaxX) mercMaxX = mx;
            if (my > mercMaxY) mercMaxY = my;
        }

        // Determine pixel spacing in Mercator (use median node spacing)
        double avgMercSpacingX = (mercMaxX - mercMinX) / (srcCols - 1);
        double avgMercSpacingY = (mercMaxY - mercMinY) / (srcRows - 1);
        double mercCellSize = Math.Min(avgMercSpacingX, avgMercSpacingY);

        // Pad extent by half a cell
        mercMinX -= mercCellSize / 2;
        mercMinY -= mercCellSize / 2;
        mercMaxX += mercCellSize / 2;
        mercMaxY += mercCellSize / 2;

        int outCols = (int)Math.Ceiling((mercMaxX - mercMinX) / mercCellSize);
        int outRows = (int)Math.Ceiling((mercMaxY - mercMinY) / mercCellSize);

        // Cap output size to avoid huge allocations
        const int MaxDim = 4096;
        if (outCols > MaxDim || outRows > MaxDim)
        {
            double scale = (double)MaxDim / Math.Max(outCols, outRows);
            outCols = (int)(outCols * scale);
            outRows = (int)(outRows * scale);
            mercCellSize = Math.Max((mercMaxX - mercMinX) / outCols, (mercMaxY - mercMinY) / outRows);
        }

        Console.WriteLine($"[Viewer] CRS: EPSG:{crs}, Grid: {srcCols}x{srcRows}");
        Console.WriteLine($"[Viewer] Mercator extent: ({mercMinX:F2}, {mercMinY:F2}) → ({mercMaxX:F2}, {mercMaxY:F2})");
        Console.WriteLine($"[Viewer] Output bitmap: {outCols}x{outRows}, cell={mercCellSize:F2}m");

        // 5. Render: for each grid node, place its color at the correct Mercator pixel
        var bmp = new SKBitmap(outCols, outRows, SKColorType.Rgba8888, SKAlphaType.Premul);

        for (int r = 0; r < srcRows; r++)
        for (int c = 0; c < srcCols; c++)
        {
            float value = fieldData[r, c];
            bool isNoData = value == noDataValue || float.IsNaN(value);
            if (isNoData) continue;

            SKColor color = SKColors.Transparent;
            for (int b = 0; b < resolvedBands.Length; b++)
            {
                if (value >= resolvedBands[b].Min && value < resolvedBands[b].Max)
                {
                    color = resolvedBands[b].Color;
                    break;
                }
            }
            if (color.Alpha == 0) continue;

            var (mx, my) = nodePositions[r, c];
            int px = (int)((mx - mercMinX) / mercCellSize);
            int py = outRows - 1 - (int)((my - mercMinY) / mercCellSize); // flip Y: Mercator Y↑, bitmap Y↓

            if (px >= 0 && px < outCols && py >= 0 && py < outRows)
                bmp.SetPixel(px, py, color);
        }

        // 6. Encode to PNG
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = data.ToArray();
        bmp.Dispose();

        var mercatorExtent = new MRect(mercMinX, mercMinY, mercMaxX, mercMaxY);

        // 7. Build the Mapsui layer
        var rasterFeature = new RasterFeature(new MRaster(pngBytes, mercatorExtent))
        {
            Styles = { new RasterStyle() },
        };

        var mapLayer = new MemoryLayer
        {
            Name = CoverageLayerName,
            Features = new List<RasterFeature> { rasterFeature },
            Style = null,
            Opacity = 0.8,
        };

        var geoId = dataset.GeographicIdentifier ?? Path.GetFileName(path);
        var info = $"{geoId} — {srcCols}×{srcRows} grid, CRS: EPSG:{crs}";

        return (mapLayer, mercatorExtent, info);
    }

    /// <summary>
    /// Transforms a bounding box from the given EPSG code to WGS84 (EPSG:4326).
    /// Supports UTM zones and other well-known projected CRS via ProjNet.
    /// </summary>
    private static (double South, double West, double North, double East) TransformToWgs84(
        int epsgCode, double south, double west, double north, double east)
    {
        var wgs84 = GeographicCoordinateSystem.WGS84;

        CoordinateSystem sourceCrs;

        // Check for UTM zones (326xx = north, 327xx = south)
        if (epsgCode is >= 32601 and <= 32660)
        {
            int zone = epsgCode - 32600;
            sourceCrs = ProjectedCoordinateSystem.WGS84_UTM(zone, true);
        }
        else if (epsgCode is >= 32701 and <= 32760)
        {
            int zone = epsgCode - 32700;
            sourceCrs = ProjectedCoordinateSystem.WGS84_UTM(zone, false);
        }
        else
        {
            // Fallback: assume coordinates are already in lat/lon
            return (south, west, north, east);
        }

        var transform = new CoordinateTransformationFactory()
            .CreateFromCoordinateSystems(sourceCrs, wgs84);

        var mathTransform = transform.MathTransform;

        // Transform all four corners (UTM coords: x=easting, y=northing)
        var sw = mathTransform.Transform(west, south);
        var ne = mathTransform.Transform(east, north);

        // MathTransform returns (x=lon, y=lat) for WGS84
        return (South: sw.y, West: sw.x, North: ne.y, East: ne.x);
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
