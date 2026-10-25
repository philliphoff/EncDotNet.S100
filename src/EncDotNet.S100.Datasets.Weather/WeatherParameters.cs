using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The canonical weather parameter table (design note weather-sources.md
/// §3.5). GRIB2 and NetCDF-CF readers map their own codes to these ids;
/// anything else is an unknown parameter, listed but not drawn.
/// </summary>
/// <remarks>
/// <para>
/// GRIB2 codes are (discipline, category, number) from WMO code table 4.2:
/// discipline 0 category 2 is momentum, discipline 0 category 3 is mass and
/// discipline 10 category 0 is oceanographic waves. CF names are from the CF
/// standard name table.
/// </para>
/// <para>
/// Where a row lists several codes or names, they're alternatives in order of
/// preference: wave direction is the mean direction of combined wind waves
/// and swell (10,0,14) before the primary wave direction (10,0,10), and wave
/// period is the peak period (10,0,34) before the mean period of combined wind
/// waves and swell (10,0,15) and the primary wave mean period (10,0,11).
/// </para>
/// </remarks>
public static class WeatherParameters
{
    /// <summary>Canonical id of the eastward (U) wind component at 10 m.</summary>
    public const string WindU = "wind.u";

    /// <summary>Canonical id of the northward (V) wind component at 10 m.</summary>
    public const string WindV = "wind.v";

    /// <summary>Canonical id of the wind speed at 10 m.</summary>
    public const string WindSpeed = "wind.speed";

    /// <summary>Canonical id of the direction the wind blows from at 10 m, in degrees true.</summary>
    public const string WindFromDirection = "wind.from_direction";

    /// <summary>Canonical id of the wind gust speed.</summary>
    public const string WindGust = "wind.gust";

    /// <summary>Canonical id of the significant height of combined wind waves and swell.</summary>
    public const string WaveHeight = "wave.hs";

    /// <summary>Canonical id of the direction waves come from, in degrees true.</summary>
    public const string WaveDirection = "wave.dir";

    /// <summary>Canonical id of the wave period (peak, or mean when no peak period is given).</summary>
    public const string WavePeriod = "wave.period";

    /// <summary>Canonical id of the significant height of wind waves (wind sea).</summary>
    public const string WindSeaHeight = "wave.wind_sea.hs";

    /// <summary>Canonical id of the significant height of swell.</summary>
    public const string SwellHeight = "wave.swell.hs";

    /// <summary>Canonical id of the air pressure reduced to mean sea level.</summary>
    public const string PressureMsl = "pressure.msl";

    /// <summary>Group id of the 10 m wind: U, V, speed and direction.</summary>
    public const string WindGroup = "wind";

    /// <summary>Group id of the wind gust.</summary>
    public const string WindGustGroup = "wind.gust";

    /// <summary>Group id of the combined sea state: height, direction and period.</summary>
    public const string WaveGroup = "wave";

    /// <summary>Group id of the wind-sea height.</summary>
    public const string WindSeaGroup = "wave.wind_sea";

    /// <summary>Group id of the swell height.</summary>
    public const string SwellGroup = "wave.swell";

    /// <summary>Group id of the mean sea level pressure.</summary>
    public const string PressureGroup = "pressure";

    private static readonly WeatherLevel TenMetres = WeatherLevel.AboveGround(Length.FromMetres(10));

    /// <summary>Every row of the table, in the order of design note §3.5.</summary>
    public static IReadOnlyList<WeatherParameterDefinition> All { get; } =
    [
        new(WindU, "Wind, eastward component", WeatherUnit.MetresPerSecond, WindGroup, TenMetres,
            [new(0, 2, 2)], ["eastward_wind"]),
        new(WindV, "Wind, northward component", WeatherUnit.MetresPerSecond, WindGroup, TenMetres,
            [new(0, 2, 3)], ["northward_wind"]),
        new(WindSpeed, "Wind speed", WeatherUnit.MetresPerSecond, WindGroup, TenMetres,
            [new(0, 2, 1)], ["wind_speed"]),
        new(WindFromDirection, "Wind direction (from)", WeatherUnit.DegreesTrue, WindGroup, TenMetres,
            [new(0, 2, 0)], ["wind_from_direction"]),
        new(WindGust, "Wind gust", WeatherUnit.MetresPerSecond, WindGustGroup, null,
            [new(0, 2, 22)], ["wind_speed_of_gust"]),
        new(WaveHeight, "Significant wave height", WeatherUnit.Metres, WaveGroup, null,
            [new(10, 0, 3)], ["sea_surface_wave_significant_height"]),
        new(WaveDirection, "Wave direction (from)", WeatherUnit.DegreesTrue, WaveGroup, null,
            [new(10, 0, 14), new(10, 0, 10)],
            ["sea_surface_wave_from_direction", "sea_surface_wave_from_direction_at_variance_spectral_density_maximum"]),
        new(WavePeriod, "Wave period", WeatherUnit.Seconds, WaveGroup, null,
            [new(10, 0, 34), new(10, 0, 15), new(10, 0, 11)],
            ["sea_surface_wave_period_at_variance_spectral_density_maximum", "sea_surface_wave_mean_period"]),
        new(WindSeaHeight, "Wind-sea significant height", WeatherUnit.Metres, WindSeaGroup, null,
            [new(10, 0, 5)], ["sea_surface_wind_wave_significant_height"]),
        new(SwellHeight, "Swell significant height", WeatherUnit.Metres, SwellGroup, null,
            [new(10, 0, 8)], ["sea_surface_swell_wave_significant_height"]),
        new(PressureMsl, "Mean sea level pressure", WeatherUnit.Pascals, PressureGroup, WeatherLevel.MeanSeaLevel,
            [new(0, 3, 1)], ["air_pressure_at_mean_sea_level", "air_pressure_at_sea_level"]),
    ];

    /// <summary>Finds the row with canonical id <paramref name="id"/>.</summary>
    /// <param name="id">A canonical id, for example <c>"wind.u"</c>.</param>
    /// <returns>The row, or <see langword="null"/> when the id isn't in the table.</returns>
    public static WeatherParameterDefinition? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var definition in All)
        {
            if (string.Equals(definition.Id, id, StringComparison.Ordinal))
                return definition;
        }

        return null;
    }

    /// <summary>Finds the row a GRIB2 parameter code maps to.</summary>
    /// <param name="code">The GRIB2 (discipline, category, number).</param>
    /// <returns>The row, or <see langword="null"/> when the code isn't in the table.</returns>
    /// <remarks>
    /// The level isn't checked here: a reader also compares the message's
    /// level with <see cref="WeatherParameterDefinition.Level"/>.
    /// </remarks>
    public static WeatherParameterDefinition? FindByGribCode(GribParameterCode code)
    {
        foreach (var definition in All)
        {
            foreach (var candidate in definition.GribCodes)
            {
                if (candidate == code)
                    return definition;
            }
        }

        return null;
    }

    /// <summary>Finds the row a CF <c>standard_name</c> maps to.</summary>
    /// <param name="standardName">The variable's <c>standard_name</c> attribute.</param>
    /// <returns>The row, or <see langword="null"/> when the name isn't in the table.</returns>
    public static WeatherParameterDefinition? FindByCfStandardName(string standardName)
    {
        ArgumentNullException.ThrowIfNull(standardName);
        var trimmed = standardName.Trim();
        foreach (var definition in All)
        {
            foreach (var candidate in definition.CfStandardNames)
            {
                if (string.Equals(candidate, trimmed, StringComparison.Ordinal))
                    return definition;
            }
        }

        return null;
    }
}
