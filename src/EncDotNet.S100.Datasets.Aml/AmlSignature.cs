using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// The AML overlay signature (AML design handoff decision 15): every AML line
/// is dash-dot (14·3·2·3 px) on a 5 px halo in the palette's background;
/// areas have no fill and inward ticks in the family's ink; Q-routes are
/// heavier, with chevrons in the direction of the leg. No chart line uses
/// dash-dot on a halo, so AML never reads as ENC.
/// </summary>
/// <remarks>
/// <para>
/// Lengths are in millimetres on the nominal S-100 display, where a pixel is
/// <see cref="PixelMm"/>. Each line style is a composite
/// <see cref="LineStyle"/>: the halo pen, then the ink pen, then any symbols.
/// </para>
/// <para>
/// Ticks go on the right of the ring as it is walked. The portrayal turns
/// every exterior ring clockwise and every interior ring anticlockwise (seen
/// north up), so the right is always inside the area.
/// </para>
/// </remarks>
public static class AmlSignature
{
    /// <summary>The nominal S-100 display pixel, 0.32 mm.</summary>
    public const double PixelMm = 0.32;

    /// <summary>The ink pen of a line or area boundary: 1.4 px.</summary>
    public const double InkWidth = 1.4 * PixelMm;

    /// <summary>The halo under a line or area boundary: 5 px.</summary>
    public const double HaloWidth = 5 * PixelMm;

    /// <summary>The ink pen of a Q-route leg: 2.2 px.</summary>
    public const double RouteInkWidth = 2.2 * PixelMm;

    /// <summary>The halo under a Q-route leg: 6 px.</summary>
    public const double RouteHaloWidth = 6 * PixelMm;

    /// <summary>The dash: 14 px.</summary>
    public const double Dash = 14 * PixelMm;

    /// <summary>The gaps either side of the dot: 3 px.</summary>
    public const double Gap = 3 * PixelMm;

    /// <summary>The dot: 2 px.</summary>
    public const double Dot = 2 * PixelMm;

    /// <summary>The length of one dash-dot: 22 px.</summary>
    public const double Interval = Dash + Gap + Dot + Gap;

    /// <summary>The distance between boundary ticks: 9 px.</summary>
    public const double TickSpacing = 9 * PixelMm;

    /// <summary>The distance between Q-route chevrons: 40 px.</summary>
    public const double ChevronSpacing = 40 * PixelMm;

    /// <summary>The name stem of a family's tick symbol: <c>{stem}_TICK</c>.</summary>
    public const string TickSuffix = "TICK";

    /// <summary>The name stem of a family's chevron symbol: <c>{stem}_CHEV</c>.</summary>
    public const string ChevronSuffix = "CHEV";

    /// <summary>The name stem of a family's fraction bar symbol: <c>{stem}_BAR</c>.</summary>
    public const string BarSuffix = "BAR";

    /// <summary>The name stem of a family's line style: <c>{stem}_LINE</c>.</summary>
    public const string LineSuffix = "LINE";

    /// <summary>The name stem of a family's area boundary style: <c>{stem}_AREA</c>.</summary>
    public const string AreaSuffix = "AREA";

    /// <summary>The name stem of a family's route style: <c>{stem}_ROUTE</c>.</summary>
    public const string RouteSuffix = "ROUTE";

    /// <summary>The names no glyph may take, because the signature's own symbols use them.</summary>
    public static IReadOnlySet<string> ReservedNames { get; } =
        new HashSet<string>(StringComparer.Ordinal) { TickSuffix, ChevronSuffix, BarSuffix, LineSuffix, AreaSuffix, RouteSuffix };

    /// <summary>The dash-dot pattern, as (start, length) pairs within <see cref="Interval"/>.</summary>
    public static IReadOnlyList<(double Start, double Length)> DashDot { get; } = [(0, Dash), (Dash + Gap, Dot)];

    /// <summary>The name of <paramref name="family"/>'s line style.</summary>
    /// <param name="family">The family.</param>
    public static string LineStyleName(AmlFamily family) => Name(family, LineSuffix);

    /// <summary>The name of <paramref name="family"/>'s area boundary style.</summary>
    /// <param name="family">The family.</param>
    public static string AreaStyleName(AmlFamily family) => Name(family, AreaSuffix);

    /// <summary>The name of <paramref name="family"/>'s route style.</summary>
    /// <param name="family">The family.</param>
    public static string RouteStyleName(AmlFamily family) => Name(family, RouteSuffix);

    /// <summary>The name of <paramref name="family"/>'s tick symbol.</summary>
    /// <param name="family">The family.</param>
    public static string TickSymbol(AmlFamily family) => Name(family, TickSuffix);

    /// <summary>The name of <paramref name="family"/>'s chevron symbol.</summary>
    /// <param name="family">The family.</param>
    public static string ChevronSymbol(AmlFamily family) => Name(family, ChevronSuffix);

    /// <summary>The name of <paramref name="family"/>'s fraction bar symbol.</summary>
    /// <param name="family">The family.</param>
    public static string BarSymbol(AmlFamily family) => Name(family, BarSuffix);

    /// <summary>The name of the framed symbol of <paramref name="glyph"/> in <paramref name="family"/>: <c>AML_AIR_WPT</c>.</summary>
    /// <param name="family">The family.</param>
    /// <param name="glyph">The glyph.</param>
    public static string FramedSymbol(AmlFamily family, AmlGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        return Name(family, glyph.Name);
    }

    /// <summary>The line styles of <paramref name="family"/>: the line, the area boundary and the route.</summary>
    /// <param name="family">The family.</param>
    public static IEnumerable<LineStyle> LineStyles(AmlFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        yield return Composite(LineStyleName(family), family, HaloWidth, InkWidth, null);
        yield return Composite(
            AreaStyleName(family),
            family,
            HaloWidth,
            InkWidth,
            new LineStyle
            {
                Name = AreaStyleName(family) + "_TICKS",
                Width = 0,
                Color = family.ColorToken,
                HasPen = false,
                IntervalLength = (float)TickSpacing,
                // Rotation 180 puts the tick on the right of the line, inside a clockwise ring.
                Symbols = [new LineStyleSymbol(TickSymbol(family), (float)(TickSpacing / 2), 180)],
            });
        yield return Composite(
            RouteStyleName(family),
            family,
            RouteHaloWidth,
            RouteInkWidth,
            new LineStyle
            {
                Name = RouteStyleName(family) + "_CHEVRONS",
                Width = 0,
                Color = family.ColorToken,
                HasPen = false,
                IntervalLength = (float)ChevronSpacing,
                Symbols = [new LineStyleSymbol(ChevronSymbol(family), (float)(ChevronSpacing / 2), 0)],
            });
    }

    private static LineStyle Composite(string name, AmlFamily family, double haloWidth, double inkWidth, LineStyle? symbols)
    {
        var halo = new LineStyle
        {
            Name = name + "_HALO",
            Width = (float)haloWidth,
            Color = AmlPalette.HaloToken,
        };
        var ink = new LineStyle
        {
            Name = name + "_INK",
            Width = (float)inkWidth,
            Color = family.ColorToken,
            DashPattern = [.. DashDot.SelectMany(d => new[] { (float)d.Start, (float)d.Length })],
            IntervalLength = (float)Interval,
        };
        return new LineStyle
        {
            Name = name,
            Width = (float)inkWidth,
            Color = family.ColorToken,
            HasPen = false,
            Components = symbols is null ? [halo, ink] : [halo, ink, symbols],
        };
    }

    private static string Name(AmlFamily family, string suffix)
    {
        ArgumentNullException.ThrowIfNull(family);
        return family.SymbolStem + "_" + suffix;
    }
}
