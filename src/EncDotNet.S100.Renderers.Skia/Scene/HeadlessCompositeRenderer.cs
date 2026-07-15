using EncDotNet.S100.Pipelines;
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

        foreach (var layer in layers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            layer.Draw(canvas, viewport);
        }

        canvas.Flush();
        return bitmap;
    }
}
