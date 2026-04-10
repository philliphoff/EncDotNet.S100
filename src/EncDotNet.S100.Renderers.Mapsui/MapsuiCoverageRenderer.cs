using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using SkiaSharp;

using PipelineViewport = EncDotNet.S100.Pipelines.Viewport;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders a <see cref="StyledCoverageLayer"/> to a Mapsui <see cref="ILayer"/>
/// by reprojecting each grid node to EPSG:3857 (Web Mercator) and producing a
/// georeferenced raster overlay.
/// </summary>
public sealed class MapsuiCoverageRenderer : ICoverageRenderer<ILayer>
{
    private const string TargetCrs = "EPSG:3857";
    private const int MaxDim = 4096;

    private readonly ICrsTransformFactory _transformFactory;

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-102 Coverage";

    /// <summary>Layer opacity (0.0–1.0). Defaults to 0.8.</summary>
    public double Opacity { get; set; } = 0.8;

    public MapsuiCoverageRenderer(ICrsTransformFactory transformFactory)
    {
        _transformFactory = transformFactory;
    }

    public ILayer Render(StyledCoverageLayer layer, PipelineViewport viewport)
    {
        var sampled = layer.Coverage;
        var georeferencer = layer.Georeferencer;
        var colorScheme = layer.ColorScheme;
        var fieldData = sampled.GetField(colorScheme.FieldName);
        int srcRows = fieldData.GetLength(0);
        int srcCols = fieldData.GetLength(1);

        // Pre-resolve color bands
        var bands = new (float Min, float Max, SKColor Color)[colorScheme.Bands.Count];
        for (int i = 0; i < colorScheme.Bands.Count; i++)
        {
            var band = colorScheme.Bands[i];
            var rgba = RgbaColor.FromHex(band.Color);
            bands[i] = (band.MinValue, band.MaxValue, new SKColor(rgba.R, rgba.G, rgba.B, rgba.A));
        }

        float noDataValue = layer.NoDataValue;
        bool noDataIsNaN = float.IsNaN(noDataValue);

        // Build CRS transform: native grid CRS → WGS84
        var nativeToWgs84 = _transformFactory.Create(georeferencer.CRS, "EPSG:4326");

        // First pass: project every grid node to Mercator and find bounding box
        var nodePositions = new (double MercX, double MercY)[srcRows, srcCols];
        double mercMinX = double.MaxValue, mercMinY = double.MaxValue;
        double mercMaxX = double.MinValue, mercMaxY = double.MinValue;

        for (int r = 0; r < srcRows; r++)
        for (int c = 0; c < srcCols; c++)
        {
            var (nativeX, nativeY) = georeferencer.ToNative(r, c);

            double lon, lat;
            if (nativeToWgs84.IsIdentity)
            {
                lon = nativeX;
                lat = nativeY;
            }
            else
            {
                (lon, lat) = nativeToWgs84.Transform(nativeX, nativeY);
            }

            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            nodePositions[r, c] = (mx, my);

            if (mx < mercMinX) mercMinX = mx;
            if (my < mercMinY) mercMinY = my;
            if (mx > mercMaxX) mercMaxX = mx;
            if (my > mercMaxY) mercMaxY = my;
        }

        // Determine output bitmap dimensions using separate X/Y cell sizes
        // to account for non-conformal projections (e.g. WGS84 → Mercator distortion).
        double cellSizeX = (mercMaxX - mercMinX) / Math.Max(srcCols - 1, 1);
        double cellSizeY = (mercMaxY - mercMinY) / Math.Max(srcRows - 1, 1);

        // Pad extent by half a cell
        mercMinX -= cellSizeX / 2;
        mercMinY -= cellSizeY / 2;
        mercMaxX += cellSizeX / 2;
        mercMaxY += cellSizeY / 2;

        int outCols = (int)Math.Ceiling((mercMaxX - mercMinX) / cellSizeX);
        int outRows = (int)Math.Ceiling((mercMaxY - mercMinY) / cellSizeY);

        // Cap output size
        if (outCols > MaxDim || outRows > MaxDim)
        {
            double scale = (double)MaxDim / Math.Max(outCols, outRows);
            outCols = (int)(outCols * scale);
            outRows = (int)(outRows * scale);
            cellSizeX = (mercMaxX - mercMinX) / outCols;
            cellSizeY = (mercMaxY - mercMinY) / outRows;
        }

        // Render: for each grid node, place its color at the correct Mercator pixel
        using var bmp = new SKBitmap(outCols, outRows, SKColorType.Rgba8888, SKAlphaType.Premul);

        for (int r = 0; r < srcRows; r++)
        for (int c = 0; c < srcCols; c++)
        {
            float value = fieldData[r, c];
            bool isNoData = noDataIsNaN ? float.IsNaN(value) : value == noDataValue;
            if (isNoData) continue;

            SKColor color = SKColors.Transparent;
            for (int b = 0; b < bands.Length; b++)
            {
                if (value >= bands[b].Min && value < bands[b].Max)
                {
                    color = bands[b].Color;
                    break;
                }
            }
            if (color.Alpha == 0) continue;

            var (mx, my) = nodePositions[r, c];
            int px = (int)((mx - mercMinX) / cellSizeX);
            int py = outRows - 1 - (int)((my - mercMinY) / cellSizeY);

            if (px >= 0 && px < outCols && py >= 0 && py < outRows)
                bmp.SetPixel(px, py, color);
        }

        // Encode to PNG
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = data.ToArray();

        var mercatorExtent = new MRect(mercMinX, mercMinY, mercMaxX, mercMaxY);

        // Build the Mapsui layer
        var rasterFeature = new RasterFeature(new MRaster(pngBytes, mercatorExtent))
        {
            Styles = { new RasterStyle() },
        };

        return new MemoryLayer
        {
            Name = LayerName,
            Features = new List<RasterFeature> { rasterFeature },
            Style = null,
            Opacity = Opacity,
        };
    }
}
