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
    /// but its labels and screen-relative symbols stay upright, as in the
    /// viewer: each layer paints its areas, lines and coverage north-up into a
    /// <see cref="RotatedViewport.NorthUpCover"/> surface, which is rotated onto
    /// the output about its centre, and then paints its point symbols and text
    /// at the rotated anchors, turning only north-relative symbols. Layers are
    /// still composited one at a time, bottom-most first, so a layer's labels
    /// and symbols sit under the layers above it exactly as they do north-up.
    /// </remarks>
    /// <param name="viewport">The shared composite viewport (explicit; no auto-fit).</param>
    /// <param name="layers">Ordered layers, bottom-most first.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public SKBitmap Render(Viewport viewport, IReadOnlyList<CompositeLayer> layers) => Render(viewport, layers, deviceScale: 1f);

    /// <summary>
    /// Composites the ordered layers against the shared viewport at
    /// <paramref name="deviceScale"/> image pixels per viewport pixel, into a
    /// newly allocated bitmap of <see cref="Viewport.WidthPixels"/> ×
    /// <see cref="Viewport.HeightPixels"/> times the scale, rounded. The
    /// viewport's pixels are device-independent: a scale of 2 draws the same
    /// view, with symbols, text and lines twice as many pixels across, as a
    /// HiDPI display does.
    /// </summary>
    /// <param name="viewport">The shared composite viewport, in device-independent pixels.</param>
    /// <param name="layers">Ordered layers, bottom-most first.</param>
    /// <param name="deviceScale">Image pixels per viewport pixel; positive and finite.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public SKBitmap Render(Viewport viewport, IReadOnlyList<CompositeLayer> layers, float deviceScale)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewport.WidthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(viewport.HeightPixels);
        if (!float.IsFinite(deviceScale) || deviceScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(deviceScale), deviceScale, "The device scale must be positive and finite.");

        var bitmap = new SKBitmap(
            ScaledPixels(viewport.WidthPixels, deviceScale),
            ScaledPixels(viewport.HeightPixels, deviceScale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Background.ToSkia());
        if (deviceScale != 1f)
            canvas.Scale(deviceScale);

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
            DrawRotated(canvas, viewport, layers, deviceScale);
        }

        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// The image pixels <paramref name="pixels"/> viewport pixels cover at
    /// <paramref name="deviceScale"/>, rounded, and at least one.
    /// </summary>
    /// <param name="pixels">Viewport pixels.</param>
    /// <param name="deviceScale">Image pixels per viewport pixel.</param>
    public static int ScaledPixels(int pixels, float deviceScale) =>
        Math.Max(1, (int)Math.Round(pixels * (double)deviceScale, MidpointRounding.AwayFromZero));

    private static void DrawRotated(SKCanvas canvas, Viewport viewport, IReadOnlyList<CompositeLayer> layers, float deviceScale)
    {
        var cover = RotatedViewport.NorthUpCover(viewport);
        float rotation = (float)viewport.RotationDegrees;
        float centerX = viewport.WidthPixels / 2f;
        float centerY = viewport.HeightPixels / 2f;

        // The cover is centred on the output: its origin sits this far from the
        // output's (whole pixels; see NorthUpCover).
        float offsetX = (viewport.WidthPixels - cover.WidthPixels) / 2f;
        float offsetY = (viewport.HeightPixels - cover.HeightPixels) / 2f;

        // Symbols and labels are culled by where their rotated anchor lands on
        // the output, which in cover pixels is the output rectangle moved by the
        // offset.
        var screenCull = SKRect.Create(
            -offsetX - SkiaDisplayListRenderer.PointCullMarginPx,
            -offsetY - SkiaDisplayListRenderer.PointCullMarginPx,
            viewport.WidthPixels + 2 * SkiaDisplayListRenderer.PointCullMarginPx,
            viewport.HeightPixels + 2 * SkiaDisplayListRenderer.PointCullMarginPx);

        // The north-up surface holds image pixels, so a HiDPI render keeps
        // its areas and lines sharp; it draws back at its viewport size.
        using var surface = SKSurface.Create(new SKImageInfo(
            ScaledPixels(cover.WidthPixels, deviceScale),
            ScaledPixels(cover.HeightPixels, deviceScale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);
        var coverRect = SKRect.Create(offsetX, offsetY, cover.WidthPixels, cover.HeightPixels);

        foreach (var layer in layers)
        {
            ArgumentNullException.ThrowIfNull(layer);

            surface.Canvas.Clear(SKColors.Transparent);
            int surfaceSave = surface.Canvas.Save();
            if (deviceScale != 1f)
                surface.Canvas.Scale(deviceScale);
            layer.DrawRotating(surface.Canvas, cover);
            surface.Canvas.RestoreToCount(surfaceSave);
            using (var image = surface.Snapshot())
            {
                canvas.Save();
                canvas.RotateDegrees(rotation, centerX, centerY);
                if (deviceScale == 1f)
                    canvas.DrawImage(image, offsetX, offsetY, sampling);
                else
                    canvas.DrawImage(image, coverRect, sampling);
                canvas.Restore();
            }

            canvas.Save();
            canvas.Translate(offsetX, offsetY);
            layer.DrawScreenPlaced(canvas, cover, rotation, screenCull);
            canvas.Restore();
        }
    }
}
