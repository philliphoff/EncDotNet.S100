using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// Converts a CF variable's <c>units</c> to the stored unit of a weather
/// parameter (<see cref="WeatherUnit"/>), for the UDUNITS spellings weather
/// files use.
/// </summary>
internal static class CfUnits
{
    private const double Knot = 1852.0 / 3600.0;

    /// <summary>
    /// The factor that converts values in <paramref name="units"/> to
    /// <paramref name="target"/>, or <see langword="null"/> when the units
    /// aren't a spelling of a unit of the same quantity. Missing units are
    /// taken to be the target unit.
    /// </summary>
    /// <param name="units">The variable's <c>units</c> attribute, or <see langword="null"/>.</param>
    /// <param name="target">The parameter's stored unit.</param>
    public static double? FactorTo(string? units, WeatherUnit target)
    {
        if (string.IsNullOrWhiteSpace(units))
            return 1;

        var key = Normalize(units);
        return target switch
        {
            WeatherUnit.Metres => key switch
            {
                "m" or "meter" or "meters" or "metre" or "metres" => 1,
                "cm" or "centimeter" or "centimeters" or "centimetre" or "centimetres" => 0.01,
                "ft" or "foot" or "feet" => 0.3048,
                _ => null,
            },
            WeatherUnit.MetresPerSecond => key switch
            {
                "m s-1" or "m/s" or "ms-1" or "m.s-1" or "m sec-1" or "meter second-1" or "meters second-1" or
                "metre second-1" or "metres second-1" or "meter/second" or "meters/second" or "metres/second" or
                "metres per second" or "meters per second" => 1,
                "knot" or "knots" or "kt" or "kts" or "kn" => Knot,
                "km h-1" or "km/h" or "kmh-1" or "km.h-1" or "kilometers per hour" or "kilometres per hour" => 1 / 3.6,
                _ => null,
            },
            WeatherUnit.DegreesTrue => key switch
            {
                "degree" or "degrees" or "deg" or "degree_true" or "degrees_true" or "degree true" or "degrees true" or
                "degree_t" or "degrees_t" or "°" or "degree from north" or "degrees from north" => 1,
                _ => null,
            },
            WeatherUnit.Seconds => key switch
            {
                "s" or "sec" or "secs" or "second" or "seconds" => 1,
                _ => null,
            },
            WeatherUnit.Pascals => key switch
            {
                "pa" or "pascal" or "pascals" => 1,
                "hpa" or "mbar" or "millibar" or "millibars" or "mb" => 100,
                _ => null,
            },
            _ => null,
        };
    }

    // Lower case, single spaces, "**" and "^" exponents written as UDUNITS does ("m s-1").
    private static string Normalize(string units)
    {
        var text = units.Trim().ToLowerInvariant().Replace("**", "", StringComparison.Ordinal).Replace("^", "", StringComparison.Ordinal);
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
