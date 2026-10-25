using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// The SoundCharts portrayal of S-104 water level: station gauges for
/// fixed-station series (dcf1 / dcf8) and labelled isolines for gridded
/// surfaces (dcf2), in one ink colour per Day / Dusk / Night palette.
/// </summary>
/// <remarks>
/// <para>
/// S-104 Ed 2.0.0 §9 defines <b>no</b> portrayal: the data exists to adjust
/// depths in the ENC or bathymetry (S-98 Annex C), and the edition removed the
/// earlier portrayal catalogue. Everything this style describes is
/// SoundCharts' own symbology, and the viewer, CLI and MCP tools label it so
/// (<see cref="EncDotNet.S100.Datasets.Pipelines.SpecConventions.S104Portrayal"/>).
/// It is defined in code, so it is an <see cref="ICoverageStyle"/> and not an
/// <see cref="IPortrayalCatalogue"/>: it has no catalogue specification or
/// edition. Replace it with an official IHO portrayal catalogue if one is
/// published.
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
///   ordinary S-100 symbols (<see cref="Symbols"/>) and text (built with
///   <see cref="StationSymbols"/> and <see cref="StationLabels"/>), so they follow
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
public class S104CoverageStyle : ICoverageStyle
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

    /// <summary>
    /// The version of this style, folded into the tile disk-cache
    /// <see cref="ContentScope"/>. Bump it whenever the symbols, the palette or
    /// anything else that changes the drawn pixels without changing the drawing
    /// instructions changes, so warm tiles drawn with the old style are dropped.
    /// </summary>
    public const int StyleVersion = 1;

    /// <summary>Colour token of the ink (stroke, mark, label; isoline lines and labels) in <see cref="Palette"/>.</summary>
    public const string InkToken = "S104INK";

    /// <summary>Colour token of the station square fill in <see cref="Palette"/>.</summary>
    public const string SymbolFillToken = "S104FILL";

    /// <summary>Colour token of the station label halo in <see cref="Palette"/>.</summary>
    public const string LabelHaloToken = "S104HALO";

    /// <summary>Colour token of the isoline label halo in <see cref="Palette"/>: the palette's water ground.</summary>
    public const string IsolineLabelHaloToken = "S104ISOHALO";

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

    /// <summary>
    /// The S-104 colours: the <see cref="InkToken"/>, <see cref="SymbolFillToken"/>,
    /// <see cref="LabelHaloToken"/> and <see cref="IsolineLabelHaloToken"/> of each
    /// Day / Dusk / Night palette. The station gauges' drawing instructions and
    /// symbols resolve against its <see cref="ApplicationPalette.GetColorPalette"/>.
    /// </summary>
    public static ApplicationPalette Palette { get; } = new(
        "S-104",
        new Dictionary<string, PaletteColors>(StringComparer.Ordinal)
        {
            [InkToken] = PaletteColors.FromHex(day: "#0F5E73", dusk: "#9FD3DE", night: "#3E7C88"),
            [SymbolFillToken] = PaletteColors.FromHex(day: "#FFFFFF", dusk: "#3B4650", night: "#0B0F14"),
            [LabelHaloToken] = PaletteColors.FromHex(day: "#FFFFFF", dusk: "#3B4650", night: "#0B0F14"),
            [IsolineLabelHaloToken] = PaletteColors.FromHex(day: "#F6FBFE", dusk: "#3B4650", night: "#0B0F14"),
        });

    /// <summary>
    /// The station symbols (<see cref="RisingSymbol"/> and the others), embedded
    /// from the project's <c>Symbols</c> folder. Colours are palette classes
    /// resolved against <see cref="Palette"/>.
    /// </summary>
    /// <remarks>
    /// S-100 SVG symbols, pivot at the origin, colours by palette class
    /// (<c>sTOKEN</c> stroke, <c>fTOKEN</c> fill). Coordinates are the design's
    /// pixels; the 18 px viewBox is sized in millimetres so the shared symbol
    /// rule (SVG millimetres at 96 DPI, times the S-100 symbol factor 0.6) draws
    /// one design pixel as one display pixel: 18 × 0.0254 / 96 × 1000 / 0.6 mm.
    /// The square is 16 px with a 1.6 px stroke; marks are 1.9 px strokes with
    /// round caps and joins.
    /// </remarks>
    public static SymbolSet Symbols { get; } =
        new(typeof(S104CoverageStyle).Assembly, "EncDotNet.S100.Datasets.S104.Symbols");

    /// <summary>
    /// Builds the station symbol instructions: viewing group 0, priority 0,
    /// under radar, at every scale. The sub-layer's plane places them among the
    /// other products.
    /// </summary>
    public static InstructionBuilder StationSymbols { get; } = new();

    /// <summary>
    /// Builds the station value labels: as <see cref="StationSymbols"/>, drawn at
    /// 1:<see cref="StationLabelMaxScaleDenominator"/> and larger, as an S-101
    /// label with SCAMIN is.
    /// </summary>
    public static InstructionBuilder StationLabels { get; } =
        StationSymbols with { ScaleMinimum = StationLabelMaxScaleDenominator };

    /// <summary>
    /// The station value label: bold monospaced <see cref="InkToken"/> text on a
    /// <see cref="LabelHaloToken"/> halo, left-aligned
    /// <see cref="StationLabelOffsetMm"/> right of the station.
    /// </summary>
    public static TextInstructionStyle StationLabelStyle { get; } = new()
    {
        FontSize = StationLabelFontSize,
        FontColor = InkToken,
        FontWeight = TextFontWeight.Bold,
        FontProportion = TextFontProportion.MonoSpaced,
        HaloColor = LabelHaloToken,
        HaloWidth = StationLabelHaloMm,
        HorizontalAlignment = TextHorizontalAlignment.Start,
        VerticalAlignment = TextVerticalAlignment.Center,
        OffsetX = StationLabelOffsetMm,
    };

    /// <summary>
    /// The style's part of the tile disk-cache content scope: the style name and
    /// <see cref="StyleVersion"/> plus the toolkit version
    /// (<see cref="ApplicationContentScope.For"/>). The processor prefixes the
    /// dataset's content hash.
    /// </summary>
    public static string ContentScope { get; } = ApplicationContentScope.For("S-104", StyleVersion);

    private PaletteType _activePaletteType = PaletteType.Day;

    /// <summary>
    /// Gets the currently active colour palette: <see cref="Palette"/>'s colours
    /// for the palette last passed to <see cref="SwitchPaletteAsync"/> (Day
    /// until then).
    /// </summary>
    public ColorPalette ActivePalette => Palette.GetColorPalette(_activePaletteType);

    /// <summary>The palette last passed to <see cref="SwitchPaletteAsync"/>; Day until then.</summary>
    public PaletteType ActivePaletteType => _activePaletteType;

    /// <inheritdoc/>
    public ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default)
    {
        _activePaletteType = type;
        return ValueTask.CompletedTask;
    }

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
