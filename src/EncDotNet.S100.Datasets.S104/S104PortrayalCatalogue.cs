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
///   1:<see cref="StationLabelMaxScaleDenominator"/> and larger scales.
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

    private const char Minus = '−';

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
