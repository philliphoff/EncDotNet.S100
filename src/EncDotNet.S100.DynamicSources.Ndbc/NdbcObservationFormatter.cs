using System.Globalization;

namespace EncDotNet.S100.DynamicSources.Ndbc;

/// <summary>
/// Formats an <see cref="NdbcObservation"/> for a pick report: every value
/// present, with its unit, and the observation time.
/// </summary>
/// <remarks>
/// Wind speeds are given in knots and in the metres per second NDBC reports;
/// wave height in metres and feet. The labels are English: the library is
/// host-agnostic and has no resources of its own.
/// </remarks>
public static class NdbcObservationFormatter
{
    /// <summary>Describes <paramref name="observation"/>.</summary>
    /// <param name="observation">The observation.</param>
    /// <param name="stationName">The station's name, or <see langword="null"/> to title it by id.</param>
    /// <param name="stationType">The station's type from the station table, e.g. <c>buoy</c>, or <see langword="null"/>.</param>
    /// <param name="stale">Whether the observation is stale.</param>
    /// <param name="now">The time the observation's age is measured at.</param>
    /// <param name="formatProvider">The culture for numbers.</param>
    public static DynamicFeatureDescription Describe(
        NdbcObservation observation,
        string? stationName,
        string? stationType,
        bool stale,
        DateTimeOffset now,
        IFormatProvider? formatProvider = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var c = formatProvider ?? CultureInfo.InvariantCulture;
        var rows = new List<DynamicFeatureProperty>(16)
        {
            new("Station", stationType is { Length: > 0 }
                ? string.Format(c, "{0} ({1})", observation.StationId, DescribeType(stationType))
                : observation.StationId),
            new("Observed", FormatTime(observation.Time, now, c) + (stale ? " — stale" : string.Empty)),
        };

        if (observation.WindSpeed is { } speed)
        {
            string direction = observation.WindFromDirection is { } from
                ? string.Format(c, "{0:000}° ", from.TotalDegrees)
                : string.Empty;
            rows.Add(new("Wind", string.Format(c, "{0}{1:0.0} kn ({2:0.0} m/s)", direction, speed.TotalKnots, speed.TotalMetresPerSecond)));
        }
        else if (observation.WindFromDirection is { } fromOnly)
        {
            rows.Add(new("Wind direction", string.Format(c, "{0:000}°", fromOnly.TotalDegrees)));
        }

        if (observation.WindGust is { } gust)
            rows.Add(new("Gust", string.Format(c, "{0:0.0} kn ({1:0.0} m/s)", gust.TotalKnots, gust.TotalMetresPerSecond)));

        if (observation.WaveHeight is { } height)
        {
            string value = string.Format(c, "{0:0.0} m ({1:0.0} ft)", height.TotalMetres, height.TotalMetres / 0.3048);
            if (observation.WaveTime is { } waveTime && waveTime != observation.Time)
                value += string.Format(c, " at {0:HH:mm} UTC", waveTime.UtcDateTime);
            rows.Add(new("Wave height", value));
        }

        Add(rows, "Dominant wave period", observation.DominantWavePeriodSeconds, "{0:0.#} s", c);
        Add(rows, "Average wave period", observation.AverageWavePeriodSeconds, "{0:0.#} s", c);
        if (observation.MeanWaveDirection is { } mwd)
            rows.Add(new("Wave direction", string.Format(c, "{0:000}° (from)", mwd.TotalDegrees)));
        Add(rows, "Pressure", observation.PressureHectopascals, "{0:0.0} hPa", c);
        Add(rows, "Pressure tendency", observation.PressureTendencyHectopascals, "{0:+0.0;-0.0;0.0} hPa in 3 h", c);
        Add(rows, "Air temperature", observation.AirTemperatureCelsius, "{0:0.0} °C", c);
        Add(rows, "Water temperature", observation.WaterTemperatureCelsius, "{0:0.0} °C", c);
        Add(rows, "Dew point", observation.DewPointCelsius, "{0:0.0} °C", c);
        Add(rows, "Visibility", observation.VisibilityNauticalMiles, "{0:0.#} nmi", c);
        Add(rows, "Tide", observation.TideFeet, "{0:+0.00;-0.00;0.00} ft (MLLW)", c);
        rows.Add(new("Source", "NOAA National Data Buoy Center. Not for navigation."));

        return new DynamicFeatureDescription(
            string.IsNullOrWhiteSpace(stationName) ? observation.StationId : stationName,
            rows);
    }

    /// <summary>Formats an observation time as UTC with its age, e.g. <c>2026-10-27 14:50 UTC (35 min ago)</c>.</summary>
    /// <param name="time">The observation time.</param>
    /// <param name="now">The time the age is measured at.</param>
    /// <param name="formatProvider">The culture for numbers.</param>
    public static string FormatTime(DateTimeOffset time, DateTimeOffset now, IFormatProvider? formatProvider = null)
    {
        var c = formatProvider ?? CultureInfo.InvariantCulture;
        var age = now - time;
        string ago = age < TimeSpan.Zero ? "in the future"
            : age < TimeSpan.FromMinutes(1) ? "just now"
            : age < TimeSpan.FromHours(1) ? string.Format(c, "{0:0} min ago", Math.Floor(age.TotalMinutes))
            : string.Format(c, "{0} h {1:00} min ago", (int)age.TotalHours, age.Minutes);
        return string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm} UTC ({1})", time.UtcDateTime, ago);
    }

    private static string DescribeType(string type) => type.ToLowerInvariant() switch
    {
        "buoy" => "buoy",
        "fixed" => "fixed station",
        "dart" => "DART buoy",
        "tao" => "TAO buoy",
        var other => other,
    };

    private static void Add(List<DynamicFeatureProperty> rows, string label, double? value, string format, IFormatProvider c)
    {
        if (value is { } v)
            rows.Add(new(label, string.Format(c, format, v)));
    }
}
