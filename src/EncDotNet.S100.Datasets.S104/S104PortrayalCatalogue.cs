using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// The colours one palette uses for the SoundCharts S-104 portrayal.
/// </summary>
/// <param name="Ink">Station stroke, trend mark and value label; isoline lines and labels.</param>
/// <param name="SymbolFill">Station square fill.</param>
/// <param name="LabelHalo">Station value label halo.</param>
/// <param name="IsolineLabelHalo">Isoline label halo: the palette's water ground.</param>
public sealed record S104Ink(RgbaColor Ink, RgbaColor SymbolFill, RgbaColor LabelHalo, RgbaColor IsolineLabelHalo);

/// <summary>
/// The SoundCharts portrayal of S-104 water level: station gauges for
/// fixed-station series (dcf1 / dcf8) and labelled isolines for gridded
/// surfaces (dcf2), in one ink colour per Day / Dusk / Night palette.
/// </summary>
/// <remarks>
/// <para>
/// S-104 Ed 2.0.0 §9 defines <b>no</b> portrayal: the data exists to adjust
/// depths in the ENC or bathymetry (S-98 Annex C), and the edition removed the
/// earlier portrayal catalogue. Everything this catalogue describes is
/// SoundCharts' own symbology, and the viewer, CLI and MCP tools label it so.
/// Replace it with an official IHO portrayal catalogue if one is published.
/// </para>
/// <para>
/// The design keeps clear of official portrayal: no colour fill over the ENC,
/// and a desaturated teal ink because magenta (mariner information), red and
/// amber (danger) and blue and green (depth) are already taken. Colour encodes
/// neither trend nor height. Night stays under luminance 0.2.
/// </para>
/// <list type="bullet">
///   <item><description>
///   Station: a 16 px rounded square with a chevron up (rising), chevron down
///   (falling), dash (steady) or "?" (unknown), and the height at the selected
///   step to its right (<see cref="FormatStationValue"/>), shown at
///   1:<see cref="StationLabelMaxScaleDenominator"/> and larger scales. They are
///   ordinary S-100 symbols (<see cref="GetSymbol"/>) and text, so they follow
///   the same rules as S-101 symbols and labels: symbol and text scale, scale
///   minimum, label declutter, and upright drawing on a rotated display.
///   </description></item>
///   <item><description>
///   Surface: dashed isolines every <see cref="IsolineInterval"/> m, or
///   <see cref="FineIsolineInterval"/> m when the dataset's whole range is under
///   <see cref="FineIntervalRangeLimit"/> m (<see cref="SelectIsolineInterval"/>),
///   labelled with <see cref="FormatIsolineLabel"/>.
///   </description></item>
/// </list>
/// </remarks>
public class S104PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    /// <summary>
    /// The largest scale denominator the station value label is drawn at
    /// (1:100 000). At smaller scales only the square is drawn.
    /// </summary>
    public const double StationLabelMaxScaleDenominator = 100_000;

    /// <summary>The isoline interval, in metres.</summary>
    public const double IsolineInterval = 0.5;

    /// <summary>The isoline interval for a dataset with a small range, in metres.</summary>
    public const double FineIsolineInterval = 0.25;

    /// <summary>The dataset range, in metres, under which <see cref="FineIsolineInterval"/> is used.</summary>
    public const double FineIntervalRangeLimit = 1.0;

    /// <summary>Colour token of the ink (stroke, mark, label) in <see cref="GetColorPalette"/>.</summary>
    public const string InkToken = "S104INK";

    /// <summary>Colour token of the station square fill in <see cref="GetColorPalette"/>.</summary>
    public const string SymbolFillToken = "S104FILL";

    /// <summary>Colour token of the station label halo in <see cref="GetColorPalette"/>.</summary>
    public const string LabelHaloToken = "S104HALO";

    /// <summary>Station symbol for a rising water level.</summary>
    public const string RisingSymbol = "S104GAUGE_RISING";

    /// <summary>Station symbol for a falling water level.</summary>
    public const string FallingSymbol = "S104GAUGE_FALLING";

    /// <summary>Station symbol for a steady water level.</summary>
    public const string SteadySymbol = "S104GAUGE_STEADY";

    /// <summary>Station symbol for an unknown trend.</summary>
    public const string UnknownSymbol = "S104GAUGE_UNKNOWN";

    /// <summary>Station symbol for a time step with no value.</summary>
    public const string NoDataSymbol = "S104GAUGE_NODATA";

    /// <summary>Size of the station value label, in pixels (the text scale multiplies it).</summary>
    public const double StationLabelFontSize = 11;

    /// <summary>Distance from the station to the label's left edge, in millimetres (13 px).</summary>
    public const double StationLabelOffsetMm = 13 * PixelSizeMm;

    /// <summary>Width of the halo under station labels, in millimetres (3 px).</summary>
    public const double StationLabelHaloMm = 3 * PixelSizeMm;

    private const double PixelSizeMm = 0.32;
    private const char Minus = '\u2212';

    // S-100 SVG symbols, pivot at the origin, colours by palette class
    // (sTOKEN stroke, fTOKEN fill). Coordinates are the design's pixels; the
    // 18 px viewBox is sized in millimetres so the shared symbol rule (SVG
    // millimetres at 96 DPI, times the S-100 symbol factor 0.6) draws one
    // design pixel as one display pixel: 18 × 0.0254 / 96 × 1000 / 0.6 mm.
    // The square is 16 px with a 1.6 px stroke; marks are 1.9 px strokes with
    // round caps and joins.
    private const string SymbolHead =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.2\" width=\"7.9375mm\" height=\"7.9375mm\" viewBox=\"-9 -9 18 18\">" +
        "<rect class=\"symbolBox layout\" fill=\"none\" x=\"-8.8\" y=\"-8.8\" width=\"17.6\" height=\"17.6\"/>" +
        "<rect class=\"fS104FILL sS104INK\" stroke-width=\"1.6\" x=\"-8\" y=\"-8\" width=\"16\" height=\"16\" rx=\"3\"/>";

    private const string SymbolTail = "<circle class=\"pivotPoint layout\" fill=\"none\" cx=\"0\" cy=\"0\" r=\"1\"/></svg>";

    private static readonly IReadOnlyDictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [RisingSymbol] = SymbolHead + "<path class=\"sl f0 sS104INK\" stroke-width=\"1.9\" d=\"M -4 2 L 0 -2.5 L 4 2\"/>" + SymbolTail,
        [FallingSymbol] = SymbolHead + "<path class=\"sl f0 sS104INK\" stroke-width=\"1.9\" d=\"M -4 -2 L 0 2.5 L 4 -2\"/>" + SymbolTail,
        [SteadySymbol] = SymbolHead + "<path class=\"sl f0 sS104INK\" stroke-width=\"1.9\" d=\"M -4 0 L 4 0\"/>" + SymbolTail,
        [UnknownSymbol] = SymbolHead +
            "<path class=\"sl f0 sS104INK\" stroke-width=\"1.5\" d=\"M -2 -1.8 C -2 -4.2 2 -4.2 2 -1.8 C 2 -0.2 0 0 0 1.4\"/>" +
            "<circle class=\"fS104INK\" cx=\"0\" cy=\"3.5\" r=\"0.8\"/>" + SymbolTail,
        [NoDataSymbol] = SymbolHead + "<path class=\"f0 sS104INK\" stroke-width=\"1\" d=\"M -5 0 L 5 0\"/>" + SymbolTail,
    };

    private static readonly IReadOnlyDictionary<PaletteType, S104Ink> Inks =
        new Dictionary<PaletteType, S104Ink>
        {
            [PaletteType.Day] = new(
                RgbaColor.FromHex("#0F5E73"), RgbaColor.FromHex("#FFFFFF"),
                RgbaColor.FromHex("#FFFFFF"), RgbaColor.FromHex("#F6FBFE")),
            [PaletteType.Dusk] = new(
                RgbaColor.FromHex("#9FD3DE"), RgbaColor.FromHex("#3B4650"),
                RgbaColor.FromHex("#3B4650"), RgbaColor.FromHex("#3B4650")),
            [PaletteType.Night] = new(
                RgbaColor.FromHex("#3E7C88"), RgbaColor.FromHex("#0B0F14"),
                RgbaColor.FromHex("#0B0F14"), RgbaColor.FromHex("#0B0F14")),
        };

    private PaletteType _activePaletteType = PaletteType.Day;

    /// <summary>Gets the S-100 product specification identifier for this catalogue.</summary>
    public SpecRef Spec => new("S-104", default);

    /// <summary>Gets the edition of the product specification this catalogue draws.</summary>
    public string Edition => "2.0.0";

    /// <summary>Gets the currently active color palette.</summary>
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <summary>The colours of the active palette.</summary>
    public S104Ink ActiveInk => GetInk(_activePaletteType);

    /// <inheritdoc/>
    public ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default)
    {
        _activePaletteType = type;
        ActivePalette = ColorPalette.FromType(type);
        return ValueTask.CompletedTask;
    }

    /// <summary>The colours of <paramref name="palette"/>; Day for an unknown palette.</summary>
    /// <param name="palette">The palette.</param>
    public static S104Ink GetInk(PaletteType palette) =>
        Inks.TryGetValue(palette, out var ink) ? ink : Inks[PaletteType.Day];

    /// <summary>
    /// The colour palette for the station gauges' drawing instructions: the
    /// <see cref="InkToken"/>, <see cref="SymbolFillToken"/> and
    /// <see cref="LabelHaloToken"/> colours of <paramref name="palette"/>.
    /// </summary>
    /// <param name="palette">The palette.</param>
    public static ColorPalette GetColorPalette(PaletteType palette)
    {
        var ink = GetInk(palette);
        return new ColorPalette(
            $"S-104 {palette}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [InkToken] = ink.Ink.ToHex(),
                [SymbolFillToken] = ink.SymbolFill.ToHex(),
                [LabelHaloToken] = ink.LabelHalo.ToHex(),
            });
    }

    /// <summary>
    /// The S-100 SVG for a station symbol (<see cref="RisingSymbol"/> and the
    /// others), or <see langword="null"/> for an unknown name. Colours are
    /// palette classes resolved against <see cref="GetColorPalette"/>.
    /// </summary>
    /// <param name="name">The symbol name.</param>
    public static string? GetSymbol(string name) =>
        Symbols.TryGetValue(name, out var svg) ? svg : null;

    /// <summary>
    /// Returns <see langword="null"/>: the SoundCharts S-104 portrayal draws
    /// isolines and no colour fill, so the ENC depth and drying colours under the
    /// surface stay readable.
    /// </summary>
    /// <param name="settings">Unused.</param>
    public CoverageColorScheme? ResolveColorScheme(MarinerSettings settings) => null;

    /// <summary>
    /// Empty: S-104 isoline levels depend on each dataset's range, so they come
    /// from <see cref="SelectIsolineInterval"/> and <see cref="IsolineLevels"/>.
    /// </summary>
    public IReadOnlyList<ContourStyle> Contours => [];

    /// <summary>
    /// The isoline interval for a dataset whose values over all time steps run
    /// from <paramref name="minimum"/> to <paramref name="maximum"/> metres (fill
    /// values excluded). Chosen once per dataset so the lines keep their density
    /// while the time step changes.
    /// </summary>
    /// <param name="minimum">The dataset's lowest value.</param>
    /// <param name="maximum">The dataset's highest value.</param>
    public static double SelectIsolineInterval(double minimum, double maximum) =>
        maximum - minimum < FineIntervalRangeLimit ? FineIsolineInterval : IsolineInterval;

    /// <summary>
    /// The multiples of <paramref name="interval"/> from
    /// <paramref name="minimum"/> to <paramref name="maximum"/>, inclusive.
    /// </summary>
    /// <param name="minimum">The lowest value.</param>
    /// <param name="maximum">The highest value.</param>
    /// <param name="interval">The interval, greater than zero.</param>
    public static IReadOnlyList<double> IsolineLevels(double minimum, double maximum, double interval)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval);
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum)
            return [];

        var levels = new List<double>();
        for (long k = (long)Math.Ceiling(minimum / interval); k * interval <= maximum; k++)
            levels.Add(k * interval);
        return levels;
    }

    /// <summary>
    /// Formats an isoline level: metres to one decimal place (two for the
    /// <see cref="FineIsolineInterval"/>), "−" (U+2212) for negatives, no "+",
    /// and a bare "0" for zero: <c>1.0</c>, <c>0.5</c>, <c>0</c>, <c>−0.5</c>.
    /// </summary>
    /// <param name="level">The level in metres.</param>
    /// <param name="interval">The dataset's isoline interval.</param>
    public static string FormatIsolineLabel(double level, double interval)
    {
        string format = interval < IsolineInterval ? "0.00" : "0.0";
        double rounded = Math.Round(level, interval < IsolineInterval ? 2 : 1, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            return "0";
        string text = Math.Abs(rounded).ToString(format, CultureInfo.InvariantCulture);
        return rounded < 0 ? Minus + text : text;
    }

    /// <summary>
    /// Formats a station's height for its value label: the sign always shown,
    /// two decimals, metres without a unit: <c>+1.42</c>, <c>−0.12</c> (U+2212),
    /// <c>+0.00</c>.
    /// </summary>
    /// <param name="height">The height in metres.</param>
    public static string FormatStationValue(double height)
    {
        double rounded = Math.Round(height, 2, MidpointRounding.AwayFromZero);
        string text = Math.Abs(rounded).ToString("0.00", CultureInfo.InvariantCulture);
        return (rounded < 0 ? Minus : '+') + text;
    }
}
