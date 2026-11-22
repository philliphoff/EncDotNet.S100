namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// What a value over a time interval is (<see cref="WeatherInterval"/>): GRIB2
/// code table 4.10, or CF <c>cell_methods</c>, reduced to the kinds a reader
/// tells apart.
/// </summary>
public enum WeatherStatistic
{
    /// <summary>Another statistic, such as a standard deviation or a difference.</summary>
    Other,

    /// <summary>The average over the interval (GRIB2 0, CF <c>time: mean</c>).</summary>
    Average,

    /// <summary>The total over the interval, such as precipitation (GRIB2 1, CF <c>time: sum</c>).</summary>
    Accumulation,

    /// <summary>The maximum over the interval, such as a gust (GRIB2 2, CF <c>time: maximum</c>).</summary>
    Maximum,

    /// <summary>The minimum over the interval (GRIB2 3, CF <c>time: minimum</c>).</summary>
    Minimum,
}
