using System.Globalization;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The time interval a step's values are a statistic over, for example the
/// precipitation accumulated in the 3 hours to 12:00Z (GRIB2 product template
/// 4.8). The step is valid at <see cref="End"/>.
/// </summary>
/// <param name="Start">The start of the interval, in UTC.</param>
/// <param name="End">The end of the interval, in UTC: the step's valid time.</param>
/// <param name="Statistic">What the values are over the interval.</param>
public readonly record struct WeatherInterval(DateTime Start, DateTime End, WeatherStatistic Statistic)
{
    /// <summary>The length of the interval.</summary>
    public TimeSpan Length => End - Start;

    /// <summary>
    /// Describes the period as a mariner reads it: <c>"in the 3 h to 12:00Z"</c>,
    /// with the date when the interval ends on another day than
    /// <paramref name="validDate"/>, and minutes only when the interval isn't
    /// whole hours (<c>"in the 30 min to 12:30Z"</c>).
    /// </summary>
    /// <param name="validDate">The date the reader already sees, or <see langword="null"/> to always give the date.</param>
    /// <returns>The phrase, starting with <c>"in the"</c>.</returns>
    public string Describe(DateTime? validDate = null)
    {
        var end = End.ToString(End.Date == validDate?.Date ? "HH:mm'Z'" : "yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture);
        return $"in the {DescribeLength(Length)} to {end}";
    }

    /// <summary>A length as <c>"3 h"</c>, <c>"1 h 30 min"</c> or <c>"45 min"</c>.</summary>
    /// <param name="length">The length.</param>
    public static string DescribeLength(TimeSpan length)
    {
        int hours = (int)length.TotalHours;
        int minutes = length.Minutes;
        return (hours, minutes) switch
        {
            (0, _) => string.Create(CultureInfo.InvariantCulture, $"{minutes} min"),
            (_, 0) => string.Create(CultureInfo.InvariantCulture, $"{hours} h"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{hours} h {minutes} min"),
        };
    }
}
