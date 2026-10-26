using System.Globalization;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// A field of a GRIB2 file that <see cref="GribWeatherDataset"/> left out of
/// its <see cref="Weather.WeatherGridSet"/>, with why: an unsupported template
/// (for example JPEG 2000), a different grid, an older model run, or a repeat
/// of another field's parameter, level and valid time. Hosts list these in
/// <c>info</c> output.
/// </summary>
/// <param name="Field">The field.</param>
/// <param name="Reason">Why it was left out, as a sentence.</param>
public sealed record GribIgnoredField(GribField Field, string Reason)
{
    /// <summary>
    /// A short name for the field: the NCEP short name when it has one (or the
    /// GRIB2 code), its level and valid time, for example
    /// <c>"HTSGW (10,0,3), surface, valid 2026-10-25 03:00Z"</c>.
    /// </summary>
    public string Name
    {
        get
        {
            var valid = Field.ValidTime is { } time
                ? time.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture)
                : "unknown time";
            return string.Create(CultureInfo.InvariantCulture, $"{GribShortNames.Describe(Field.Parameter)}, {Field.ToWeatherLevel().Description}, valid {valid}");
        }
    }
}
