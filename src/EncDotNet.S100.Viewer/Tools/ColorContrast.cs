using EncDotNet.S100.Pipelines;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// WCAG 2 relative luminance and contrast ratio, and the per-palette
/// adjustment of the app accent, for the route overlay's palette pass (#906).
/// </summary>
internal static class ColorContrast
{
    /// <summary>The smallest contrast a route line keeps against its casing (WCAG 1.4.11, non-text).</summary>
    public const double MinimumGraphicContrast = 3.0;

    /// <summary>WCAG 2 relative luminance of an sRGB colour, 0 (black) to 1 (white).</summary>
    public static double Luminance(SKColor color)
    {
        static double Linear(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.Red)) + (0.7152 * Linear(color.Green)) + (0.0722 * Linear(color.Blue));
    }

    /// <summary>WCAG 2 contrast ratio between two opaque colours, 1 to 21.</summary>
    public static double Ratio(SKColor a, SKColor b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary><paramref name="over"/> at <paramref name="alpha"/> over <paramref name="under"/>.</summary>
    public static SKColor Over(SKColor under, SKColor over, double alpha)
    {
        byte M(byte u, byte o) => (byte)Math.Round(o * alpha + (u * (1 - alpha)));
        return new SKColor(M(under.Red, over.Red), M(under.Green, over.Green), M(under.Blue, over.Blue));
    }

    /// <summary>
    /// The app accent as the route editing line draws it on a chart palette
    /// (handoff A6). Day keeps it. Dusk tones it down a little. Night lowers
    /// its chroma and brightness so it doesn't glare on a dark chart. On the
    /// black casing of Dusk and Night it is lightened back up, if needed, to
    /// keep <see cref="MinimumGraphicContrast"/>.
    /// </summary>
    public static SKColor AccentFor(SKColor accent, PaletteType palette)
    {
        if (palette == PaletteType.Day)
            return accent;

        accent.ToHsv(out var h, out var s, out var v);
        var (sat, value) = palette == PaletteType.Night ? (0.55f, 0.8f) : (0.85f, 0.9f);
        s *= sat;
        v *= value;
        var color = SKColor.FromHsv(h, s, v);
        while (Ratio(color, SKColors.Black) < MinimumGraphicContrast && v < 100)
        {
            v = Math.Min(100, v + 4);
            color = SKColor.FromHsv(h, s, v);
        }

        return color;
    }
}
