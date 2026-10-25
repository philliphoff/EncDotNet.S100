namespace EncDotNet.S100.Datasets.Weather;

/// <summary>The kind of vertical level a weather parameter is given at.</summary>
public enum WeatherLevelKind
{
    /// <summary>A level the model doesn't classify; see <see cref="WeatherLevel.Description"/>.</summary>
    Other,

    /// <summary>The ground or water surface (GRIB2 code table 4.5, fixed surface type 1).</summary>
    Surface,

    /// <summary>A height above the ground or sea surface (GRIB2 code table 4.5, fixed surface type 103).</summary>
    HeightAboveGround,

    /// <summary>Mean sea level (GRIB2 code table 4.5, fixed surface type 101).</summary>
    MeanSeaLevel,
}
