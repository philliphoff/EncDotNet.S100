using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The Mapsui style for a coverage raster whose detail must survive being
/// drawn smaller than its pixels, such as an S-102 surface with relief
/// shading; drawn by <see cref="SmoothRasterRenderer"/>. Attach it to a
/// <see cref="RasterFeature"/> in place of a <see cref="RasterStyle"/>.
/// </summary>
public sealed class SmoothRasterStyle : BaseStyle
{
    /// <summary>
    /// The raster image, drawn over the feature's extent. Held here rather
    /// than encoded into the feature's <see cref="MRaster"/>: a shaded surface
    /// barely compresses, so a PNG round trip would cost more than the
    /// shading itself.
    /// </summary>
    public required SKImage Image { get; init; }
}

/// <summary>
/// Draws a <see cref="SmoothRasterStyle"/> raster with mipmaps. Mapsui's
/// <see cref="RasterStyle"/> filters without mipmaps, so a raster drawn much
/// smaller than its pixels skips most of them and fine detail (relief
/// shading) turns into a mottled pattern when zoomed out. Here a reduced
/// raster averages its pixels from the mipmap levels; a magnified one is
/// filtered as before.
/// </summary>
public sealed class SmoothRasterRenderer : ISkiaStyleRenderer
{
    /// <summary>Trilinear sampling: linear within a mipmap level and between levels.</summary>
    public static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>Singleton instance for registration.</summary>
    public static SmoothRasterRenderer Instance { get; } = new();

    internal static void Register() =>
        global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(typeof(SmoothRasterStyle), Instance);

    /// <inheritdoc/>
    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (style is not SmoothRasterStyle { Image: { } image }
            || feature is not RasterFeature { Extent: { } extent })
            return false;

        float opacity = (float)Math.Clamp(layer.Opacity * style.Opacity, 0, 1);
        if (opacity <= 0f)
            return true;

        // Image pixel space → screen through three corners, so a rotated map
        // turns the raster with it.
        var (x0, y0) = viewport.WorldToScreenXY(extent.MinX, extent.MaxY);
        var (x1, y1) = viewport.WorldToScreenXY(extent.MaxX, extent.MaxY);
        var (x2, y2) = viewport.WorldToScreenXY(extent.MinX, extent.MinY);
        var matrix = ImageToScreen(image.Width, image.Height, (x0, y0), (x1, y1), (x2, y2));

        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) };
        canvas.Save();
        try
        {
            canvas.Concat(in matrix);
            canvas.DrawImage(image, 0, 0, Sampling, paint);
        }
        finally
        {
            canvas.Restore();
        }

        return true;
    }

    /// <summary>
    /// The affine map from image pixels to screen points that puts the image's
    /// top-left, top-right and bottom-left corners on the given points.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="topLeft">Screen point of the image's top-left corner.</param>
    /// <param name="topRight">Screen point of the image's top-right corner.</param>
    /// <param name="bottomLeft">Screen point of the image's bottom-left corner.</param>
    /// <returns>The image-to-screen matrix.</returns>
    public static SKMatrix ImageToScreen(
        int width, int height,
        (double X, double Y) topLeft, (double X, double Y) topRight, (double X, double Y) bottomLeft)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return new SKMatrix(
            (float)((topRight.X - topLeft.X) / width), (float)((bottomLeft.X - topLeft.X) / height), (float)topLeft.X,
            (float)((topRight.Y - topLeft.Y) / width), (float)((bottomLeft.Y - topLeft.Y) / height), (float)topLeft.Y,
            0f, 0f, 1f);
    }
}
