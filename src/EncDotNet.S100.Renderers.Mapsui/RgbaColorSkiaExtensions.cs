using EncDotNet.S100.Pipelines;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>Converts <see cref="RgbaColor"/> to SkiaSharp colours for the custom style renderers.</summary>
internal static class RgbaColorSkiaExtensions
{
    /// <summary>Converts an <see cref="RgbaColor"/> to an <see cref="SKColor"/>.</summary>
    public static SKColor ToSkia(this RgbaColor color) => new(color.R, color.G, color.B, color.A);
}
