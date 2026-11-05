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
    /// aren't a spelling of a unit of the same quantity or the conversion
    /// needs an offset (<see cref="ConversionTo"/>). Missing units are taken
    /// to be the target unit.
    /// </summary>
    /// <param name="units">The variable's <c>units</c> attribute, or <see langword="null"/>.</param>
    /// <param name="target">The parameter's stored unit.</param>
    public static double? FactorTo(string? units, WeatherUnit target) =>
        ConversionTo(units, target) is { Offset: 0 } conversion ? conversion.Factor : null;

    /// <summary>
    /// The conversion of values in <paramref name="units"/> to
    /// <paramref name="target"/> (stored = value × factor + offset), or
    /// <see langword="null"/> when the units aren't a spelling of a unit of
    /// the same quantity. Only temperatures have an offset. Missing units are
    /// taken to be the target unit.
    /// </summary>
    /// <param name="units">The variable's <c>units</c> attribute, or <see langword="null"/>.</param>
    /// <param name="target">The parameter's stored unit.</param>
    public static CfUnitConversion? ConversionTo(string? units, WeatherUnit target)
    {
        if (string.IsNullOrWhiteSpace(units))
            return CfUnitConversion.Identity;

        var key = Normalize(units);
        if (target == WeatherUnit.Kelvin)
        {
            return key switch
            {
                "k" or "kelvin" or "kelvins" or "degk" or "deg_k" or "degree_k" or "degrees_k" or "degreek" or "degreesk" =>
                    CfUnitConversion.Identity,
                "degc" or "deg_c" or "degree_c" or "degrees_c" or "degreec" or "degreesc" or "°c" or "c" or "celsius" or
                "degree celsius" or "degrees celsius" or "degree_celsius" or "degrees_celsius" =>
                    new CfUnitConversion(1, CelsiusZero),
                "degf" or "deg_f" or "degree_f" or "degrees_f" or "degreef" or "degreesf" or "°f" or "fahrenheit" or
                "degree fahrenheit" or "degrees fahrenheit" or "degree_fahrenheit" or "degrees_fahrenheit" =>
                    new CfUnitConversion(5.0 / 9.0, (459.67 * 5.0) / 9.0),
                _ => null,
            };
        }

        double? factor = target switch
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
            WeatherUnit.Proportion => key switch
            {
                "1" or "fraction" or "proportion" => 1,
                "%" or "percent" => 0.01,
                _ => null,
            },
            _ => null,
        };
        return factor is { } f ? new CfUnitConversion(f, 0) : null;
    }

    /// <summary>0 °C in kelvin.</summary>
    public const double CelsiusZero = 273.15;

    // Lower case, single spaces, "**" and "^" exponents written as UDUNITS does ("m s-1").
    private static string Normalize(string units)
    {
        var text = units.Trim().ToLowerInvariant().Replace("**", "", StringComparison.Ordinal).Replace("^", "", StringComparison.Ordinal);
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>A linear unit conversion: stored = value × <paramref name="Factor"/> + <paramref name="Offset"/>.</summary>
/// <param name="Factor">The scale.</param>
/// <param name="Offset">The offset, in the stored unit; non-zero only for temperatures.</param>
internal readonly record struct CfUnitConversion(double Factor, double Offset)
{
    /// <summary>No conversion.</summary>
    public static CfUnitConversion Identity { get; } = new(1, 0);
}
