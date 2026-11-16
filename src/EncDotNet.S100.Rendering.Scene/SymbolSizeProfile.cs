namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// The factors that turn S-100 Part 9 sizes into output pixels: how big point
/// symbols, line widths and offsets, pattern tiles and text come out.
/// <see cref="VectorSceneBuilder"/> applies them when it lowers a display list,
/// and the headless Skia renderer applies <see cref="PatternPixelsPerMillimetre"/>
/// when it rasterises pattern tiles.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Screen"/> is the default, and the profile the viewer draws with.
/// Its factors are tuned for on-screen display rather than physical size, so a
/// renderer that wants to match the viewer's output (an ArcGIS, QGIS or SVG
/// renderer drawing the same resolved instructions) uses these numbers:
/// </para>
/// <list type="bullet">
///   <item>point symbols at 0.6 of their 96 DPI size;</item>
///   <item>line widths, dashes and offsets at 0.32 mm per pixel;</item>
///   <item>pattern tiles at 1.5 pixels per millimetre;</item>
///   <item>font sizes in points drawn as the same number of pixels.</item>
/// </list>
/// <para>
/// <see cref="Part9Millimetres"/> draws everything at its physical size per
/// S-100 Part 9 for a given output resolution, for print. At 96 DPI its point
/// symbols are about 1.6 times the <see cref="Screen"/> size.
/// </para>
/// <para>
/// None of these factors includes the user's global symbol or text scale; the
/// builder multiplies those in on top.
/// </para>
/// </remarks>
public sealed record SymbolSizeProfile
{
    /// <summary>
    /// Pixels per millimetre at which a symbol's SVG picture comes out
    /// (96 DPI): the size a point-symbol scale of 1 draws at.
    /// </summary>
    public const double SvgPixelsPerMillimetre = 96.0 / 25.4;

    /// <summary>Millimetres per inch.</summary>
    private const double MillimetresPerInch = 25.4;

    /// <summary>Typographic points per inch.</summary>
    private const double PointsPerInch = 72.0;

    /// <summary>
    /// The default profile, matching the viewer: point symbols at 0.6 of
    /// 96 DPI, 0.32 mm per pixel, patterns at 1.5 px/mm and font points drawn
    /// as pixels.
    /// </summary>
    public static SymbolSizeProfile Screen { get; } = new();

    /// <summary>
    /// A profile that draws every size at its physical size per S-100 Part 9,
    /// at <paramref name="dotsPerInch"/> output pixels per inch: symbols at
    /// their SVG size in millimetres, lines, dashes and offsets in
    /// millimetres, pattern tiles in millimetres and fonts in points.
    /// </summary>
    /// <param name="dotsPerInch">Output resolution, for example 300 for print. Defaults to 96.</param>
    /// <returns>The profile for that resolution.</returns>
    public static SymbolSizeProfile Part9Millimetres(double dotsPerInch = 96)
    {
        RequirePositive(dotsPerInch, nameof(dotsPerInch));
        return new SymbolSizeProfile
        {
            PointSymbolScale = dotsPerInch / 96.0,
            MillimetresPerPixel = MillimetresPerInch / dotsPerInch,
            PatternPixelsPerMillimetre = dotsPerInch / MillimetresPerInch,
            PixelsPerFontPoint = dotsPerInch / PointsPerInch,
        };
    }

    private readonly double _pointSymbolScale = 0.6;
    private readonly double _millimetresPerPixel = VectorSceneBuilder.S100PixelSizeMm;
    private readonly double _patternPixelsPerMillimetre = 1.5;
    private readonly double _pixelsPerFontPoint = 1.0;

    /// <summary>
    /// Scale applied to a point symbol's 96 DPI SVG picture (before the
    /// instruction's own scale and the global symbol scale). Defaults to 0.6.
    /// </summary>
    public double PointSymbolScale
    {
        get => _pointSymbolScale;
        init => _pointSymbolScale = RequirePositive(value, nameof(PointSymbolScale));
    }

    /// <summary>
    /// Millimetres per output pixel, used for line widths, dash patterns,
    /// symbol and text offsets, halo widths and the symbols placed along a
    /// line style. Defaults to 0.32 (S-100 Part 9's nominal pixel).
    /// </summary>
    public double MillimetresPerPixel
    {
        get => _millimetresPerPixel;
        init => _millimetresPerPixel = RequirePositive(value, nameof(MillimetresPerPixel));
    }

    /// <summary>
    /// Output pixels per millimetre of a pattern area-fill tile. Defaults to
    /// 1.5, denser than physical so patterns repeat tightly on screen.
    /// </summary>
    public double PatternPixelsPerMillimetre
    {
        get => _patternPixelsPerMillimetre;
        init => _patternPixelsPerMillimetre = RequirePositive(value, nameof(PatternPixelsPerMillimetre));
    }

    /// <summary>
    /// Output pixels per point of a text instruction's font size (before the
    /// global text scale). Defaults to 1: a 10 pt label is 10 px.
    /// </summary>
    public double PixelsPerFontPoint
    {
        get => _pixelsPerFontPoint;
        init => _pixelsPerFontPoint = RequirePositive(value, nameof(PixelsPerFontPoint));
    }

    /// <summary>
    /// Scale of the dot drawn when a point symbol can't be resolved: a quarter
    /// of <see cref="PointSymbolScale"/>.
    /// </summary>
    public double FallbackDotScale => PointSymbolScale / 4;

    /// <summary>
    /// Scale applied to the symbols a line style places along a line, so they
    /// are sized in the same millimetres as the line's dashes and gaps.
    /// </summary>
    public double LineSymbolScale => 1 / MillimetresPerPixel / SvgPixelsPerMillimetre;

    private static double RequirePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name, value, "Must be a positive, finite number.");
        return value;
    }
}
