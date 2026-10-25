namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The unit a weather parameter's values are stored in (design note
/// weather-sources.md §3.5). Readers convert to this unit when they decode a
/// step, so every grid of one canonical parameter shares it.
/// </summary>
public enum WeatherUnit
{
    /// <summary>A unit the parameter table doesn't know; used for unknown parameters.</summary>
    Other,

    /// <summary>Metres per second (wind components, speed and gusts).</summary>
    MetresPerSecond,

    /// <summary>Degrees clockwise from true north (wind and wave "from" directions).</summary>
    DegreesTrue,

    /// <summary>Metres (wave heights).</summary>
    Metres,

    /// <summary>Seconds (wave periods).</summary>
    Seconds,

    /// <summary>Pascals (air pressure).</summary>
    Pascals,
}
