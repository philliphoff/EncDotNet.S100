using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Mapsui-free entry point for compositing multiple, layered S-100 datasets
/// into a single headless bitmap. Given an <em>explicit</em> shared
/// <see cref="Viewport"/> and an ordered list of <see cref="CompositeLayer"/>
/// (bottom-most first, as resolved by the renderer-neutral S-98 ordering /
/// suppression engine), the compositor clears the background once and draws
/// each layer into the shared pixel space so overlaid datasets register.
/// </summary>
/// <remarks>
/// This is the multi-layer analogue of <see cref="HeadlessVectorRenderer"/> /
/// <see cref="CoverageHeadlessRenderer"/>: those render a single dataset and
/// auto-fit their own viewport, whereas the composite path requires one shared
/// viewport for all layers. Layer ordering and depth suppression are decided
/// upstream (S-98); this renderer only paints the already-ordered result.
/// </remarks>
public sealed class HeadlessCompositeRenderer
{
    /// <summary>Background fill painted once before the ordered layers. Defaults to opaque white.</summary>
    public RgbaColor Background { get; init; } = new(255, 255, 255, 255);

    /// <summary>
    /// Composites the ordered layers against the shared viewport into a newly
    /// allocated bitmap of <see cref="Viewport.WidthPixels"/> ×
    /// <see cref="Viewport.HeightPixels"/>.
    /// </summary>
    /// <remarks>
    /// Under a non-zero <see cref="Viewport.RotationDegrees"/> the chart turns
    /// but its labels stay upright, as in the viewer: each layer paints its
    /// areas, lines and symbols north-up into a
    /// <see cref="RotatedViewport.NorthUpCover"/> surface, which is rotated onto
    /// the output about its centre, and then paints its text unrotated at the
    /// rotated anchors. Layers are still composited one at a time, bottom-most
    /// first, so a layer's labels sit under the layers above it exactly as they
    /// do north-up.
    /// </remarks>
    /// <param name="viewport">The shared composite viewport (explicit; no auto-fit).</param>
    /// <param name="layers">Ordered layers, bottom-most first.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public SKBitmap Render(Viewport viewport, IReadOnlyList<CompositeLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewport.WidthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewport.HeightPixels);

        var bitmap = new SKBitmap(
            viewport.WidthPixels,
            viewport.HeightPixels,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Background.ToSkia());

        if (viewport.RotationDegrees % 360.0 == 0)
        {
            foreach (var layer in layers)
            {
                ArgumentNullException.ThrowIfNull(layer);
                layer.Draw(canvas, viewport);
            }
        }
        else
        {
            DrawRotated(canvas, viewport, layers);
        }

        canvas.Flush();
        return bitmap;
    }

    private static void DrawRotated(SKCanvas canvas, Viewport viewport, IReadOnlyList<CompositeLayer> layers)
    {
        var cover = RotatedViewport.NorthUpCover(viewport);
        float rotation = (float)viewport.RotationDegrees;
        float centerX = viewport.WidthPixels / 2f;
        float centerY = viewport.HeightPixels / 2f;

        // The cover is centred on the output: its origin sits this far from the
        // output's (whole pixels; see NorthUpCover).
        float offsetX = (viewport.WidthPixels - cover.WidthPixels) / 2f;
        float offsetY = (viewport.HeightPixels - cover.HeightPixels) / 2f;

        // Labels are culled by where their rotated anchor lands on the output,
        // which in cover pixels is the output rectangle moved by the offset.
        var textCull = SKRect.Create(
            -offsetX - SkiaDisplayListRenderer.PointCullMarginPx,
            -offsetY - SkiaDisplayListRenderer.PointCullMarginPx,
            viewport.WidthPixels + 2 * SkiaDisplayListRenderer.PointCullMarginPx,
            viewport.HeightPixels + 2 * SkiaDisplayListRenderer.PointCullMarginPx);

        using var surface = SKSurface.Create(new SKImageInfo(
            cover.WidthPixels, cover.HeightPixels, SKColorType.Rgba8888, SKAlphaType.Premul));
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);

        foreach (var layer in layers)
        {
            ArgumentNullException.ThrowIfNull(layer);

            surface.Canvas.Clear(SKColors.Transparent);
            layer.DrawRotating(surface.Canvas, cover);
            using (var image = surface.Snapshot())
            {
                canvas.Save();
                canvas.RotateDegrees(rotation, centerX, centerY);
                canvas.DrawImage(image, offsetX, offsetY, sampling);
                canvas.Restore();
            }

            canvas.Save();
            canvas.Translate(offsetX, offsetY);
            layer.DrawUprightText(canvas, cover, rotation, textCull);
            canvas.Restore();
        }
    }
}
