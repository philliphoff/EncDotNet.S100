using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.DynamicSources.Ndbc;

/// <summary>
/// One NDBC standard meteorological observation: a row of a
/// <c>realtime2/{station}.txt</c> file, or the latest values of several rows
/// combined by <see cref="Combine"/>. A missing value (<c>MM</c> in the file)
/// is <see langword="null"/>.
/// </summary>
/// <remarks>
/// Units follow the file (NDBC "Measurement Descriptions and Units"): wind in
/// metres per second averaged over 8 minutes (buoys) or 2 minutes (C-MAN),
/// gust the peak 5 or 8 second speed, wave height the significant wave height,
/// periods in seconds, directions in degrees true, pressure at sea level in
/// hectopascals, temperatures in degrees Celsius, visibility in nautical miles
/// and tide in feet above mean lower low water.
/// </remarks>
public sealed record NdbcObservation
{
    /// <summary>The station identifier, e.g. <c>44013</c>.</summary>
    public required string StationId { get; init; }

    /// <summary>The observation time, UTC (the row's <c>YY MM DD hh mm</c>).</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>The direction the wind blows from, degrees true (<c>WDIR</c>).</summary>
    public Angle? WindFromDirection { get; init; }

    /// <summary>The average wind speed (<c>WSPD</c>).</summary>
    public Speed? WindSpeed { get; init; }

    /// <summary>The peak gust speed (<c>GST</c>).</summary>
    public Speed? WindGust { get; init; }

    /// <summary>The significant wave height (<c>WVHT</c>).</summary>
    public Length? WaveHeight { get; init; }

    /// <summary>The dominant wave period in seconds (<c>DPD</c>).</summary>
    public double? DominantWavePeriodSeconds { get; init; }

    /// <summary>The average wave period in seconds (<c>APD</c>).</summary>
    public double? AverageWavePeriodSeconds { get; init; }

    /// <summary>The direction the dominant waves come from, degrees true (<c>MWD</c>).</summary>
    public Angle? MeanWaveDirection { get; init; }

    /// <summary>Sea-level pressure in hectopascals (<c>PRES</c>).</summary>
    public double? PressureHectopascals { get; init; }

    /// <summary>Air temperature in degrees Celsius (<c>ATMP</c>).</summary>
    public double? AirTemperatureCelsius { get; init; }

    /// <summary>Sea surface temperature in degrees Celsius (<c>WTMP</c>).</summary>
    public double? WaterTemperatureCelsius { get; init; }

    /// <summary>Dew point in degrees Celsius (<c>DEWP</c>).</summary>
    public double? DewPointCelsius { get; init; }

    /// <summary>Visibility in nautical miles (<c>VIS</c>).</summary>
    public double? VisibilityNauticalMiles { get; init; }

    /// <summary>Pressure tendency over three hours in hectopascals (<c>PTDY</c>).</summary>
    public double? PressureTendencyHectopascals { get; init; }

    /// <summary>Water level above mean lower low water, in feet (<c>TIDE</c>).</summary>
    public double? TideFeet { get; init; }

    /// <summary>
    /// The time of the row the wave values came from, when <see cref="Combine"/>
    /// took them from an older row than <see cref="Time"/>; otherwise
    /// <see langword="null"/>. Buoys report waves less often than wind (44013: waves at :20 and :50, wind every 10 minutes).
    /// </summary>
    public DateTimeOffset? WaveTime { get; init; }

    /// <summary>Whether any value is present.</summary>
    public bool HasAnyValue =>
        WindFromDirection is not null || WindSpeed is not null || WindGust is not null
        || WaveHeight is not null || DominantWavePeriodSeconds is not null || AverageWavePeriodSeconds is not null
        || MeanWaveDirection is not null || PressureHectopascals is not null || AirTemperatureCelsius is not null
        || WaterTemperatureCelsius is not null || DewPointCelsius is not null || VisibilityNauticalMiles is not null
        || PressureTendencyHectopascals is not null || TideFeet is not null;

    /// <summary>
    /// The station's latest values: the newest row, with each value it lacks
    /// taken from the newest older row that has it, no more than
    /// <paramref name="window"/> older. The wind values travel together, as do
    /// the wave values, so a speed is never paired with another row's direction.
    /// </summary>
    /// <param name="rowsNewestFirst">The rows, newest first, as a <c>realtime2</c> file lists them.</param>
    /// <param name="window">How much older than the newest row a value may be.</param>
    /// <returns>The combined observation, or <see langword="null"/> when there are no rows.</returns>
    public static NdbcObservation? Combine(IReadOnlyList<NdbcObservation> rowsNewestFirst, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(rowsNewestFirst);
        if (rowsNewestFirst.Count == 0)
            return null;

        var latest = rowsNewestFirst[0];
        var result = latest;
        bool haveWind = latest.WindSpeed is not null || latest.WindFromDirection is not null;
        bool haveWave = latest.WaveHeight is not null;

        for (int i = 1; i < rowsNewestFirst.Count; i++)
        {
            var row = rowsNewestFirst[i];
            if (latest.Time - row.Time > window)
                break;
            if (row.Time > latest.Time)
                continue;

            if (!haveWind && (row.WindSpeed is not null || row.WindFromDirection is not null))
            {
                result = result with { WindFromDirection = row.WindFromDirection, WindSpeed = row.WindSpeed, WindGust = row.WindGust };
                haveWind = true;
            }

            if (!haveWave && row.WaveHeight is not null)
            {
                result = result with
                {
                    WaveHeight = row.WaveHeight,
                    DominantWavePeriodSeconds = row.DominantWavePeriodSeconds,
                    AverageWavePeriodSeconds = row.AverageWavePeriodSeconds,
                    MeanWaveDirection = row.MeanWaveDirection,
                    WaveTime = row.Time,
                };
                haveWave = true;
            }

            result = result with
            {
                PressureHectopascals = result.PressureHectopascals ?? row.PressureHectopascals,
                AirTemperatureCelsius = result.AirTemperatureCelsius ?? row.AirTemperatureCelsius,
                WaterTemperatureCelsius = result.WaterTemperatureCelsius ?? row.WaterTemperatureCelsius,
                DewPointCelsius = result.DewPointCelsius ?? row.DewPointCelsius,
                VisibilityNauticalMiles = result.VisibilityNauticalMiles ?? row.VisibilityNauticalMiles,
                PressureTendencyHectopascals = result.PressureTendencyHectopascals ?? row.PressureTendencyHectopascals,
                TideFeet = result.TideFeet ?? row.TideFeet,
            };
        }

        return result;
    }
}
