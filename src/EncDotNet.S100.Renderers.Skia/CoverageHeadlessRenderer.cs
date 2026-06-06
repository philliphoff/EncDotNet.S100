using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>
/// Source-agnostic, Mapsui-free entry point for rendering a
/// <see cref="StyledCoverageLayer"/> (colour-band fill and/or oriented arrow
/// symbology) to a standalone <see cref="SKBitmap"/> of caller-requested pixel
/// dimensions. The coverage's geographic extent is projected to EPSG:3857 and
/// fitted into the output rectangle preserving aspect (letter-boxed against the
/// background), so non-square requests do not distort the data.
/// </summary>
/// <remarks>
/// The colour raster produced by <see cref="SkiaCoverageRenderer"/> is one
/// pixel per grid cell; it is resampled into the fitted destination rectangle.
/// Grid rows are mapped linearly in latitude, so the Web-Mercator latitude
/// non-linearity is approximated across the (typically small) coverage extent —
/// the same approximation the existing direct-Skia coverage path makes. Arrow
/// symbols are projected exactly through <see cref="WebMercator"/>.
/// </remarks>
public sealed class CoverageHeadlessRenderer
{
    /// <summary>Background fill painted before the coverage. Defaults to opaque white.</summary>
    public RgbaColor Background { get; init; } = new(255, 255, 255, 255);

    /// <summary>Colour used for no-data cells in the colour raster. Defaults to transparent.</summary>
    public RgbaColor NoDataColor { get; init; } = RgbaColor.Transparent;

    /// <summary>
    /// Optional arrow renderer used when the layer carries a
    /// <see cref="StyledCoverageLayer.SymbolScheme"/>. When <c>null</c>, arrow
    /// symbology is skipped even if the layer defines a scheme.
    /// </summary>
    public SkiaCoverageArrowRenderer? ArrowRenderer { get; init; }

    /// <summary>
    /// Transform from the grid's native CRS to WGS84, used to project arrow
    /// positions. Defaults to identity (geographic grids).
    /// </summary>
    public ICrsTransform NativeToWgs84 { get; init; } = IdentityCrsTransform.Instance;

    /// <summary>
    /// Renders the styled coverage layer to a bitmap of the requested size.
    /// </summary>
    /// <param name="layer">The styled coverage layer (colour scheme and/or symbol scheme).</param>
    /// <param name="westLongitude">Western extent edge in WGS84 degrees.</param>
    /// <param name="eastLongitude">Eastern extent edge in WGS84 degrees.</param>
    /// <param name="southLatitude">Southern extent edge in WGS84 degrees.</param>
    /// <param name="northLatitude">Northern extent edge in WGS84 degrees.</param>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public SKBitmap Render(
        StyledCoverageLayer layer,
        double westLongitude,
        double eastLongitude,
        double southLatitude,
        double northLatitude,
        int widthPixels,
        int heightPixels)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);

        var bitmap = new SKBitmap(widthPixels, heightPixels, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Background.ToSkia());

        var (minX, minY) = WebMercator.FromLonLat(westLongitude, southLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(eastLongitude, northLatitude);
        double spanX = maxX - minX;
        double spanY = maxY - minY;
        if (spanX <= 0 || spanY <= 0)
            return bitmap; // Degenerate extent — return the background only.

        // Fit the projected extent into the output rectangle, preserving aspect.
        double scale = Math.Min(widthPixels / spanX, heightPixels / spanY);
        double drawW = spanX * scale;
        double drawH = spanY * scale;
        double offsetX = (widthPixels - drawW) / 2.0;
        double offsetY = (heightPixels - drawH) / 2.0;

        (float X, float Y) Project((double X, double Y) world)
        {
            float px = (float)(offsetX + (world.X - minX) * scale);
            float py = (float)(offsetY + (maxY - world.Y) * scale);
            return (px, py);
        }

        if (layer.ColorScheme is not null)
        {
            var rasterRenderer = new SkiaCoverageRenderer { NoDataColor = NoDataColor };
            // SkiaCoverageRenderer emits one pixel per grid cell and ignores the
            // viewport pixel size, so the viewport here only carries the extent.
            var nativeViewport = new Viewport
            {
                MinLongitude = westLongitude,
                MaxLongitude = eastLongitude,
                MinLatitude = southLatitude,
                MaxLatitude = northLatitude,
                WidthPixels = 1,
                HeightPixels = 1,
                ScaleDenominator = 1.0,
            };
            using var raster = rasterRenderer.Render(layer, nativeViewport);
            using var rasterImage = SKImage.FromBitmap(raster);
            var dest = new SKRect(
                (float)offsetX,
                (float)offsetY,
                (float)(offsetX + drawW),
                (float)(offsetY + drawH));
            canvas.DrawImage(rasterImage, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }

        if (layer.SymbolScheme is not null && ArrowRenderer is not null)
        {
            ArrowRenderer.Draw(canvas, layer, NativeToWgs84, Project);
        }

        canvas.Flush();
        return bitmap;
    }
}
