using System.Globalization;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.DynamicSources.Nmea;

/// <summary>
/// Formats an <see cref="OwnShipWeather"/>: the pick-report rows with units,
/// and the short chart labels <see cref="OwnShipWeatherPortrayal"/> draws.
/// </summary>
/// <remarks>
/// The labels are English: the library is host-agnostic and has no resources
/// of its own.
/// </remarks>
public static class NmeaWeatherFormatter
{
    /// <summary>The pick report's title.</summary>
    public const string Title = "Own-ship weather";

    /// <summary>Describes <paramref name="weather"/> for a pick report.</summary>
    /// <param name="weather">The weather.</param>
    /// <param name="now">The time the values' age is measured at.</param>
    /// <param name="formatProvider">The culture for numbers.</param>
    public static DynamicFeatureDescription Describe(OwnShipWeather weather, DateTimeOffset now, IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(weather);
        var c = formatProvider ?? CultureInfo.InvariantCulture;
        var rows = new List<DynamicFeatureProperty>(10);

        if (weather.TrueWind is { } tw)
        {
            rows.Add(new("True wind", string.Format(c, "{0:000}° {1}", tw.FromDirection.TotalDegrees, FormatSpeed(tw.Speed, c))));
            rows.Add(new("True wind from", DescribeSource(weather.TrueWindSource)));
        }

        if (weather.RelativeWindAngle is { } angle && weather.RelativeWindSpeed is { } speed)
        {
            string value = string.Format(c, "{0} {1}", FormatRelativeAngle(angle, c), FormatSpeed(speed, c));
            if (weather.IsRelativeOnly)
                value += weather.Heading is null
                    ? " (no true wind: heading unknown)"
                    : " (no true wind: speed unknown)";
            rows.Add(new("Relative wind", value));
        }

        if (weather.Heading is { } heading)
            rows.Add(new("Heading", string.Format(c, "{0:000}° T", heading.TotalDegrees)));
        if (weather.WaterTemperatureCelsius is { } water)
            rows.Add(new("Water temperature", string.Format(c, "{0:0.0} °C", water)));
        if (weather.AirTemperatureCelsius is { } air)
            rows.Add(new("Air temperature", string.Format(c, "{0:0.0} °C", air)));
        if (weather.PressureHectopascals is { } pressure)
            rows.Add(new("Pressure", string.Format(c, "{0:0.0} hPa", pressure)));

        rows.Add(new("Updated", FormatTime(weather.Time, now, c)));
        rows.Add(new("Source", "Own-ship NMEA 0183 instruments. Not for navigation."));
        return new DynamicFeatureDescription(Title, rows);
    }

    /// <summary>
    /// The chart's wind label: <c>225° 14 kn</c> for true wind, or
    /// <c>Rel. wind 045° 12 kn</c> when only the relative wind is known;
    /// <see langword="null"/> without wind.
    /// </summary>
    /// <param name="weather">The weather.</param>
    public static string? WindLabel(OwnShipWeather weather)
    {
        ArgumentNullException.ThrowIfNull(weather);
        var c = CultureInfo.InvariantCulture;
        if (weather.TrueWind is { } tw)
            return string.Format(c, "{0:000}° {1:0} kn", tw.FromDirection.TotalDegrees, tw.Speed.TotalKnots);
        if (weather.RelativeWindAngle is { } angle && weather.RelativeWindSpeed is { } speed)
            return string.Format(c, "Rel. wind {0:000}° {1:0} kn", angle.TotalDegrees, speed.TotalKnots);
        return null;
    }

    /// <summary>The chart's water temperature label, e.g. <c>Water 18.4 °C</c>; <see langword="null"/> without one.</summary>
    /// <param name="weather">The weather.</param>
    public static string? WaterLabel(OwnShipWeather weather)
    {
        ArgumentNullException.ThrowIfNull(weather);
        return weather.WaterTemperatureCelsius is { } water
            ? string.Format(CultureInfo.InvariantCulture, "Water {0:0.0} °C", water)
            : null;
    }

    /// <summary>What a <see cref="TrueWindSource"/> means, for a pick report.</summary>
    /// <param name="source">The source.</param>
    public static string DescribeSource(TrueWindSource source) => source switch
    {
        TrueWindSource.Mwd => "Instruments (MWD)",
        TrueWindSource.DerivedOverGround => "Relative wind, heading and motion over the ground",
        TrueWindSource.MwvTrue => "Instruments (MWV true angle) and heading",
        TrueWindSource.DerivedThroughWater => "Relative wind, heading and speed through the water",
        _ => "None",
    };

    // 045° starboard, 315° → 045° port; 0° and 180° need no side.
    private static string FormatRelativeAngle(Angle angle, IFormatProvider c)
    {
        double d = angle.TotalDegrees;
        if (d is 0 or 180)
            return string.Format(c, "{0:000}°", d);
        return d < 180
            ? string.Format(c, "{0:000}° starboard", d)
            : string.Format(c, "{0:000}° port", 360 - d);
    }

    private static string FormatSpeed(Speed speed, IFormatProvider c) =>
        string.Format(c, "{0:0.0} kn ({1:0.0} m/s)", speed.TotalKnots, speed.TotalMetresPerSecond);

    private static string FormatTime(DateTimeOffset time, DateTimeOffset now, IFormatProvider c)
    {
        var age = now - time;
        string ago = age < TimeSpan.Zero ? "in the future"
            : age < TimeSpan.FromSeconds(2) ? "just now"
            : age < TimeSpan.FromMinutes(1) ? string.Format(c, "{0:0} s ago", Math.Floor(age.TotalSeconds))
            : string.Format(c, "{0:0} min ago", Math.Floor(age.TotalMinutes));
        return string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss} UTC ({1})", time.UtcDateTime, ago);
    }
}
