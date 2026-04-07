using SkiaSharp;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Renderers.Skia;

/// <summary>
/// Extension methods for converting <see cref="RgbaColor"/> to SkiaSharp types.
/// </summary>
public static class SkiaColorExtensions
{
    /// <summary>Converts an <see cref="RgbaColor"/> to an <see cref="SKColor"/>.</summary>
    public static SKColor ToSkia(this RgbaColor color) =>
        new(color.R, color.G, color.B, color.A);
}
