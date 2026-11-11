using EncDotNet.S100.Pipelines;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>
/// Extension methods for converting <see cref="RgbaColor"/> to SkiaSharp types.
/// Internal: not part of the package's public/stable surface — it exists only to
/// support this assembly's rendering paths. Consumers embedding the renderer work
/// with the scene IR (<c>VectorScene</c> / <c>PaintOp</c>), not SkiaSharp colours.
/// </summary>
internal static class SkiaColorExtensions
{
    /// <summary>Converts an <see cref="RgbaColor"/> to an <see cref="SKColor"/>.</summary>
    public static SKColor ToSkia(this RgbaColor color) =>
        new(color.R, color.G, color.B, color.A);
}
