namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Labels for <see cref="WeatherUnit"/> values, in the wording that
/// <see cref="EncDotNet.S100.Pipelines.Coverage.CoverageValueField.Units"/>
/// uses for the S-100 coverage products (<c>"metres"</c>, <c>"degrees"</c>).
/// </summary>
public static class WeatherUnits
{
    /// <summary>Returns the display label of <paramref name="unit"/>.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>
    /// <c>"metres per second"</c>, <c>"degrees"</c>, <c>"metres"</c>,
    /// <c>"seconds"</c>, <c>"pascals"</c>, or an empty string for
    /// <see cref="WeatherUnit.Other"/>.
    /// </returns>
    public static string GetLabel(WeatherUnit unit) => unit switch
    {
        WeatherUnit.MetresPerSecond => "metres per second",
        WeatherUnit.DegreesTrue => "degrees",
        WeatherUnit.Metres => "metres",
        WeatherUnit.Seconds => "seconds",
        WeatherUnit.Pascals => "pascals",
        _ => string.Empty,
    };
}
