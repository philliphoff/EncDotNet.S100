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
/// Renders oriented symbols (e.g. current arrows) from a <see cref="StyledCoverageLayer"/>
/// as a georeferenced raster that scales with map zoom, using SkiaSharp to draw
/// rotated arrow polygons colored by speed band.
/// </summary>
public sealed class MapsuiCoverageArrowRenderer
{
    private const int MaxDim = 4096;

    private readonly ICrsTransformFactory _transformFactory;

    /// <summary>
    /// Arrow shape from the S-111 SVG symbols, centered at origin, pointing north
    /// (negative Y in screen coords). The path is 4 units wide and 10 units tall.
    /// </summary>
    private static readonly SKPath ArrowPath = CreateArrowPath();

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "Coverage Arrows";

    /// <summary>Layer opacity (0.0–1.0). Defaults to 1.0.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Target maximum number of arrows along the longest grid axis.
    /// The renderer computes a stride so that no more than roughly
    /// <c>MaxArrowsPerAxis²</c> arrows are produced.
    /// Set to 0 to disable subsampling.
    /// </summary>
    public int MaxArrowsPerAxis { get; set; } = 80;

    /// <summary>
    /// Minimum number of pixels allocated per arrow in the output raster
    /// (along the shorter arrow axis). Controls the output bitmap resolution.
    /// </summary>
    public int MinArrowPixels { get; set; } = 20;

    public MapsuiCoverageArrowRenderer(ICrsTransformFactory transformFactory)
    {
        _transformFactory = transformFactory;
    }

    /// <summary>
    /// Renders the symbol scheme from a styled coverage layer into a georeferenced
    /// raster layer with rotated arrow polygons. Returns <c>null</c> if the layer
    /// has no symbol scheme.
    /// </summary>
    public ILayer? Render(StyledCoverageLayer layer, PipelineViewport viewport)
    {
        var symbolScheme = layer.SymbolScheme;
        if (symbolScheme is null)
            return null;

        var sampled = layer.Coverage;
        var georeferencer = layer.Georeferencer;
        var colorScheme = layer.ColorScheme;
        var valueData = sampled.GetField(symbolScheme.ValueFieldName);
        var rotationData = sampled.GetField(symbolScheme.RotationFieldName);
        int srcRows = valueData.GetLength(0);
        int srcCols = valueData.GetLength(1);

        float noDataValue = layer.NoDataValue;
        bool noDataIsNaN = float.IsNaN(noDataValue);

        // Build CRS transform: native grid CRS → WGS84
        var nativeToWgs84 = _transformFactory.Create(georeferencer.CRS, "EPSG:4326");

        // Compute stride to keep arrow count manageable on large grids.
        int stride = 1;
        if (MaxArrowsPerAxis > 0)
        {
            int longestAxis = Math.Max(srcRows, srcCols);
            stride = Math.Max(1, (longestAxis + MaxArrowsPerAxis - 1) / MaxArrowsPerAxis);
        }

        // Pre-resolve color bands: hex → SKColor
        var bands = new (float Min, float Max, SKColor Color)[colorScheme.Bands.Count];
        for (int i = 0; i < colorScheme.Bands.Count; i++)
        {
            var band = colorScheme.Bands[i];
            var rgba = RgbaColor.FromHex(band.Color);
            bands[i] = (band.MinValue, band.MaxValue, new SKColor(rgba.R, rgba.G, rgba.B, rgba.A));
        }

        // Project grid corners to Mercator to determine extent.
        double mercMinX = double.MaxValue, mercMinY = double.MaxValue;
        double mercMaxX = double.MinValue, mercMaxY = double.MinValue;

        void ExpandExtent(int r, int c)
        {
            var (nX, nY) = georeferencer.ToNative(r, c);
            double lon, lat;
            if (nativeToWgs84.IsIdentity) { lon = nX; lat = nY; }
            else { (lon, lat) = nativeToWgs84.Transform(nX, nY); }
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            if (mx < mercMinX) mercMinX = mx;
            if (my < mercMinY) mercMinY = my;
            if (mx > mercMaxX) mercMaxX = mx;
            if (my > mercMaxY) mercMaxY = my;
        }

        ExpandExtent(0, 0);
        ExpandExtent(0, srcCols - 1);
        ExpandExtent(srcRows - 1, 0);
        ExpandExtent(srcRows - 1, srcCols - 1);

        double cellSizeX = (mercMaxX - mercMinX) / Math.Max(srcCols - 1, 1);
        double cellSizeY = (mercMaxY - mercMinY) / Math.Max(srcRows - 1, 1);

        // Pad extent by half a cell (match color renderer alignment)
        mercMinX -= cellSizeX / 2;
        mercMinY -= cellSizeY / 2;
        mercMaxX += cellSizeX / 2;
        mercMaxY += cellSizeY / 2;

        double mercWidth = mercMaxX - mercMinX;
        double mercHeight = mercMaxY - mercMinY;

        // Compute output bitmap dimensions: ensure each arrow gets MinArrowPixels,
        // while preserving the geographic aspect ratio.
        int arrowsX = (srcCols + stride - 1) / stride;
        int arrowsY = (srcRows + stride - 1) / stride;
        double aspect = mercWidth / mercHeight;

        int outCols, outRows;
        if (aspect >= 1.0)
        {
            outCols = Math.Min(MaxDim, arrowsX * MinArrowPixels);
            outRows = Math.Max(1, (int)(outCols / aspect));
        }
        else
        {
            outRows = Math.Min(MaxDim, arrowsY * MinArrowPixels);
            outCols = Math.Max(1, (int)(outRows * aspect));
        }

        double pxPerMercX = outCols / mercWidth;
        double pxPerMercY = outRows / mercHeight;

        // Arrow height in pixels: proportional to stride × cell spacing, scaled to
        // leave room between arrows. The arrow path is 10 units tall (-5 to +5).
        double arrowSpacingPx = stride * cellSizeY * pxPerMercY;
        double baseArrowScale = arrowSpacingPx * 0.5 / 10.0;
        baseArrowScale = Math.Max(baseArrowScale, 0.5);

        // Draw arrows onto a transparent bitmap
        using var bmp = new SKBitmap(outCols, outRows, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        using var strokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = SKColors.Black,
            StrokeWidth = 0.32f,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round,
            IsAntialias = true,
        };

        for (int r = 0; r < srcRows; r += stride)
        for (int c = 0; c < srcCols; c += stride)
        {
            float value = valueData[r, c];
            bool isNoData = noDataIsNaN ? float.IsNaN(value) : value == noDataValue;
            if (isNoData) continue;

            float direction = rotationData[r, c];
            bool dirNoData = noDataIsNaN ? float.IsNaN(direction) : direction == noDataValue;
            if (dirNoData) continue;

            // Find fill color from color scheme bands
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

            // Band-specific scale factor from the symbol scheme
            var symbolBand = symbolScheme.Resolve(value);
            double bandScale = symbolBand is not null
                ? (symbolBand.ScaleByValue ? symbolBand.ScaleFactor * value : symbolBand.ScaleFactor)
                : 1.0;
            float totalScale = (float)(baseArrowScale * bandScale);

            // Project to pixel coordinates
            var (nativeX, nativeY) = georeferencer.ToNative(r, c);
            double lon, lat;
            if (nativeToWgs84.IsIdentity) { lon = nativeX; lat = nativeY; }
            else { (lon, lat) = nativeToWgs84.Transform(nativeX, nativeY); }
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);

            float px = (float)((mx - mercMinX) * pxPerMercX);
            float py = (float)(outRows - 1 - (my - mercMinY) * pxPerMercY);

            // Draw rotated + scaled arrow
            canvas.Save();
            canvas.Translate(px, py);
            canvas.RotateDegrees(direction);
            canvas.Scale(totalScale);

            fillPaint.Color = color;
            canvas.DrawPath(ArrowPath, fillPaint);
            canvas.DrawPath(ArrowPath, strokePaint);

            canvas.Restore();
        }

        // Encode to PNG and wrap as a georeferenced raster layer
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = data.ToArray();

        var mercatorExtent = new MRect(mercMinX, mercMinY, mercMaxX, mercMaxY);
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

    private static SKPath CreateArrowPath()
    {
        var path = new SKPath();
        path.MoveTo(0, 5);
        path.LineTo(-0.5f, 5);
        path.LineTo(-1.0f, -1.5f);
        path.LineTo(-2.0f, -1.5f);
        path.LineTo(0, -5);
        path.LineTo(2.0f, -1.5f);
        path.LineTo(1.0f, -1.5f);
        path.LineTo(0.5f, 5);
        path.Close();
        return path;
    }
}
