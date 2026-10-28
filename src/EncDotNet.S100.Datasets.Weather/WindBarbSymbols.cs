using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Picks the wind barb symbol for a speed at a position: the barb for the
/// speed (<see cref="WindBarb.FromKnots"/>), from the southern-hemisphere set
/// south of the equator and the northern set elsewhere. The symbols are served
/// by <see cref="WeatherCoverageStyle.Symbols"/>.
/// </summary>
/// <remarks>
/// <para>
/// By convention the feathers of a barb go on the clockwise side of the staff
/// in the northern hemisphere and on the anticlockwise side in the southern
/// hemisphere, so a southern barb is the mirror image of a northern one
/// (<see cref="WindBarb.SouthernSymbolPrefix"/>). The calm circle is the same
/// in both.
/// </para>
/// <para>
/// This is the one rule for every barb a viewer draws: the gridded barbs
/// (<see cref="WindBarbStyle"/>, through
/// <see cref="CoverageSymbolScheme.SouthernHemisphereBands"/>),
/// observed winds (the NDBC station glyph) and any single barb such as an
/// own-ship wind, so they all agree at the same latitude.
/// </para>
/// </remarks>
public static class WindBarbSymbols
{
    /// <summary>
    /// Whether a position at <paramref name="latitude"/> takes southern barbs:
    /// a latitude below zero. The equator itself is northern, as in
    /// <see cref="CoverageSymbolScheme.Resolve"/>.
    /// </summary>
    /// <param name="latitude">The latitude in degrees.</param>
    public static bool IsSouthern(double latitude) => latitude < 0;

    /// <summary>
    /// The barb symbol name for a wind of <paramref name="knots"/> at
    /// <paramref name="latitude"/>, e.g. <c>WXBARBN065</c> at 40°N,
    /// <c>WXBARBS065</c> at 40°S, or <see cref="WindBarb.CalmSymbol"/>.
    /// </summary>
    /// <param name="knots">The speed in knots; zero or more.</param>
    /// <param name="latitude">The latitude in degrees.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="knots"/> is negative or NaN.</exception>
    public static string For(double knots, double latitude) => WindBarb.FromKnots(knots).SymbolNameAt(latitude);

    /// <summary>The barb symbol name for a wind speed at a latitude; see <see cref="For(double, double)"/>.</summary>
    /// <param name="speed">The speed; zero or more.</param>
    /// <param name="latitude">The latitude in degrees.</param>
    public static string For(Speed speed, double latitude) => For(speed.TotalKnots, latitude);
}
