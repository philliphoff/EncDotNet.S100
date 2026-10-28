using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The feathers of a WMO wind barb for one speed: pennants (50 kn), full
/// barbs (10 kn) and half barbs (5 kn), with the speed rounded to the nearest
/// 5 kn.
/// </summary>
/// <remarks>
/// <para>
/// A barb's staff points into the wind, from the station toward the
/// direction the wind blows from. Its feathers sit at the outer end, largest
/// first: pennants, then full barbs, then at most one half barb. A half barb
/// on its own (5 kn) is drawn one feather spacing in from the end, so it
/// isn't read as a full barb. Below 2.5 kn the wind is calm and drawn as a
/// circle with no staff (WMO Manual on Codes; WMO-No. 485).
/// </para>
/// <para>
/// <see cref="WindBarbStyle"/> draws one symbol per 5 kn band, named by
/// <see cref="SymbolName"/>; the symbols are generated from this
/// decomposition by <c>tools/GenerateWeatherSymbols</c>.
/// </para>
/// </remarks>
/// <param name="Knots">The speed the barb stands for: a multiple of 5 kn from 0 (calm) to <see cref="MaxKnots"/>.</param>
/// <param name="Pennants">The number of pennants (flags), 50 kn each.</param>
/// <param name="FullBarbs">The number of full barbs, 10 kn each.</param>
/// <param name="HalfBarbs">The number of half barbs (0 or 1), 5 kn each.</param>
public readonly record struct WindBarb(int Knots, int Pennants, int FullBarbs, int HalfBarbs)
{
    /// <summary>The speed step between barbs, in knots: 5.</summary>
    public const int StepKnots = 5;

    /// <summary>The highest barb, in knots: 200. Faster winds are drawn with it.</summary>
    public const int MaxKnots = 200;

    /// <summary>The speed below which the wind is calm, in knots: 2.5.</summary>
    public const double CalmBelowKnots = StepKnots / 2.0;

    /// <summary>The symbol name of the calm circle.</summary>
    public const string CalmSymbol = "WXCALM";

    /// <summary>
    /// The symbol name prefix of the northern-hemisphere barbs, followed by the
    /// speed in knots to three digits: <c>WXBARBN005</c> to <c>WXBARBN200</c>.
    /// </summary>
    public const string NorthernSymbolPrefix = "WXBARBN";

    /// <summary>
    /// The symbol name prefix of the southern-hemisphere barbs, followed by the
    /// speed in knots to three digits: <c>WXBARBS005</c> to <c>WXBARBS200</c>.
    /// Each is the mirror image of its northern barb, with the feathers on the
    /// anticlockwise side of the staff.
    /// </summary>
    public const string SouthernSymbolPrefix = "WXBARBS";

    /// <summary>Whether this is the calm symbol (0 kn): no staff and no feathers.</summary>
    public bool IsCalm => Knots == 0;

    /// <summary>
    /// The northern-hemisphere symbol name: <see cref="CalmSymbol"/>, or
    /// <see cref="NorthernSymbolPrefix"/> and the speed, e.g. <c>WXBARBN065</c>.
    /// </summary>
    public string SymbolName => IsCalm
        ? CalmSymbol
        : NorthernSymbolPrefix + Knots.ToString("000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The southern-hemisphere symbol name: <see cref="CalmSymbol"/> (calm has
    /// no side), or <see cref="SouthernSymbolPrefix"/> and the speed, e.g.
    /// <c>WXBARBS065</c>.
    /// </summary>
    public string SouthernSymbolName => IsCalm
        ? CalmSymbol
        : SouthernSymbolPrefix + Knots.ToString("000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The symbol name for a station at <paramref name="latitude"/>:
    /// <see cref="SouthernSymbolName"/> south of the equator
    /// (<see cref="WindBarbSymbols.IsSouthern"/>), <see cref="SymbolName"/> otherwise.
    /// </summary>
    /// <param name="latitude">The station's latitude in degrees.</param>
    public string SymbolNameAt(double latitude) =>
        WindBarbSymbols.IsSouthern(latitude) ? SouthernSymbolName : SymbolName;

    /// <summary>
    /// Every barb from calm to <see cref="MaxKnots"/>, in steps of
    /// <see cref="StepKnots"/>: 41 barbs, the first of them calm.
    /// </summary>
    public static IReadOnlyList<WindBarb> All { get; } =
        Enumerable.Range(0, MaxKnots / StepKnots + 1).Select(i => ForRoundedKnots(i * StepKnots)).ToArray();

    /// <summary>
    /// The barb for a wind speed in knots: rounded to the nearest
    /// <see cref="StepKnots"/> (halves round up, so 2.5 kn is a 5 kn barb),
    /// calm below <see cref="CalmBelowKnots"/>, and the
    /// <see cref="MaxKnots"/> barb for anything faster.
    /// </summary>
    /// <param name="knots">The speed in knots; zero or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="knots"/> is negative or NaN.</exception>
    public static WindBarb FromKnots(double knots)
    {
        if (!(knots >= 0))
            throw new ArgumentOutOfRangeException(nameof(knots), knots, "A wind speed must be zero or more.");

        double steps = Math.Floor((knots + CalmBelowKnots) / StepKnots);
        int rounded = (int)Math.Min(steps * StepKnots, MaxKnots);
        return ForRoundedKnots(rounded);
    }

    /// <summary>The barb for a wind speed; see <see cref="FromKnots"/>.</summary>
    /// <param name="speed">The speed; zero or more.</param>
    public static WindBarb FromSpeed(Speed speed) => FromKnots(speed.TotalKnots);

    private static WindBarb ForRoundedKnots(int knots)
    {
        int pennants = knots / 50;
        int remainder = knots % 50;
        return new WindBarb(knots, pennants, remainder / 10, remainder % 10 / StepKnots);
    }
}
