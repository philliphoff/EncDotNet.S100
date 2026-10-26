using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// A GRIB2 time range: a count of a unit from code table 4.4, for example a
/// forecast time of 3 hours.
/// </summary>
/// <param name="Unit">The unit.</param>
/// <param name="Value">The number of units; may be negative (a forecast time before the reference time).</param>
public readonly record struct GribTimeRange(GribTimeUnit Unit, int Value)
{
    /// <summary>Adds the range to <paramref name="time"/>, calendar-aware for months and longer units.</summary>
    /// <param name="time">The time to start from, usually the reference time.</param>
    /// <returns>The time <see cref="Value"/> units after <paramref name="time"/>.</returns>
    /// <exception cref="UnsupportedGribException">The unit isn't one of code table 4.4's defined units.</exception>
    public DateTime AddTo(DateTime time) => Unit switch
    {
        GribTimeUnit.Second => time.AddSeconds(Value),
        GribTimeUnit.Minute => time.AddMinutes(Value),
        GribTimeUnit.Hour => time.AddHours(Value),
        GribTimeUnit.ThreeHours => time.AddHours(3.0 * Value),
        GribTimeUnit.SixHours => time.AddHours(6.0 * Value),
        GribTimeUnit.TwelveHours => time.AddHours(12.0 * Value),
        GribTimeUnit.Day => time.AddDays(Value),
        GribTimeUnit.Month => time.AddMonths(Value),
        GribTimeUnit.Year => time.AddYears(Value),
        GribTimeUnit.Decade => time.AddYears(10 * Value),
        GribTimeUnit.Normal => time.AddYears(30 * Value),
        GribTimeUnit.Century => time.AddYears(100 * Value),
        _ => throw new UnsupportedGribException(
            string.Create(CultureInfo.InvariantCulture, $"GRIB2 time unit {(byte)Unit} (code table 4.4) isn't supported.")),
    };

    /// <summary>Returns the range as, for example, <c>"3 h"</c>.</summary>
    public override string ToString()
    {
        var unit = Unit switch
        {
            GribTimeUnit.Second => "s",
            GribTimeUnit.Minute => "min",
            GribTimeUnit.Hour => "h",
            GribTimeUnit.ThreeHours => "× 3 h",
            GribTimeUnit.SixHours => "× 6 h",
            GribTimeUnit.TwelveHours => "× 12 h",
            GribTimeUnit.Day => "d",
            GribTimeUnit.Month => "mo",
            GribTimeUnit.Year => "y",
            _ => string.Create(CultureInfo.InvariantCulture, $"(unit {(byte)Unit})"),
        };
        return string.Create(CultureInfo.InvariantCulture, $"{Value} {unit}");
    }
}
