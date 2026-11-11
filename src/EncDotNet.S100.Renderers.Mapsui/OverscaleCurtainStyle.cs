using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Mapsui style marking a polygon to be filled with the S-52 / S-101 overscale
/// "curtain" (issue #441, <c>AP(OVERSC01)</c> Form A): a set of evenly spaced,
/// world-anchored vertical lines drawn over a region displayed beyond its
/// compilation scale. Rendered by <see cref="OverscaleCurtainRenderer"/>.
/// </summary>
/// <remarks>
/// Unlike a tiled pattern fill, the curtain is drawn as direct vertical strokes
/// (see <see cref="OverscaleCurtainRenderer"/>), which keeps it crisp on HiDPI
/// surfaces, cheap (a few hundred line segments per frame rather than tens of
/// thousands of stamped tiles), and safe to replay into the offscreen
/// picture-recording surface used when capturing a window screenshot.
/// </remarks>
public sealed class OverscaleCurtainStyle : BaseStyle
{
    /// <summary>Spacing between adjacent vertical lines, in millimetres.</summary>
    public double LineSpacingMm { get; init; } = 3.2;

    /// <summary>Stroke width of each vertical line, in millimetres.</summary>
    public double LineWidthMm { get; init; } = 0.3;

    /// <summary>
    /// Line colour (including alpha). A mid-grey at moderate alpha keeps a usable
    /// contrast across the day, dusk, and night palettes without competing with
    /// chart colour, and lets the chart read through — the curtain is an
    /// indication, not an obstruction.
    /// </summary>
    public SKColor LineColor { get; init; } = new(0x80, 0x80, 0x80, 0x66);
}
