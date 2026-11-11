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
/// discipline 0 category 0 is temperature, discipline 0 category 19 is
/// physical atmospheric properties (visibility), discipline 0 category 1 is
/// moisture (precipitation rate), discipline 0 category 6 is cloud,
/// discipline 0 category 7 is thermodynamic stability (CAPE), discipline 2 category 0 is
/// land surface vegetation (the land cover mask),
/// discipline 10 category 0 is oceanographic waves and discipline 10
/// category 3 is surface properties (water temperature); discipline 10
/// category 1 is currents. CF names are from
/// the CF standard name table.
/// </para>
/// <para>
/// Where a row lists several codes or names, they're alternatives in order of
/// preference: wave direction is the mean direction of combined wind waves
/// and swell (10,0,14) before the primary wave direction (10,0,10), and wave
/// period is the peak period (10,0,34) before the mean period of combined wind
/// waves and swell (10,0,15) and the primary wave mean period (10,0,11).
/// Mean sea level pressure is NCEP's PRMSL (0,3,1) before ECMWF's <c>msl</c>,
/// which is plain pressure (0,3,0) at mean sea level: the row's level keeps
/// surface pressure, also (0,3,0), out. Sea surface temperature takes the
/// plain CF name first, then the GHRSST foundation, subskin and skin
/// temperatures that satellite analyses such as MUR and the NOAA Geo-polar
/// Blended analysis carry; GFS's surface <c>TMP</c> (0,0,0) is left out, as
/// it is land skin temperature too. The current components take the plain
/// CF names first, then the <c>surface_</c> ones HF radar products carry.
/// Current direction is the direction the water flows <em>to</em>, the
/// oceanographic convention, unlike the wind and wave directions.
/// </para>
/// <para>
/// The current rows have no level: a model gives currents at its top layer
/// (0 m for HYCOM, about 0.5 m for Copernicus, 15 m for OSCAR), and each
/// reader keeps the level it finds, so a 15 m current is labelled as one.
/// </para>
/// <para>
/// GRIB2 temperature (0,0,0) is two rows told apart by level: at 2 m above
/// ground it's the air temperature, at the surface the skin temperature,
/// which over the sea is the model's sea surface temperature and over land
/// the ground's. The skin temperature and the land cover mask aren't drawn:
/// they stand in for the sea temperature in the freezing spray risk
/// (<see cref="FreezingSpray"/>) when a file such as GFS has no sea
/// temperature of its own.
/// </para>
/// <para>
/// The visibility row has no level either: GFS, HRRR and the NBM give it
/// only at the surface.
/// </para>
/// <para>
/// Precipitation rate and CAPE are read at the surface, and cloud cover for
/// the entire atmosphere: GFS also gives CAPE for layers above the ground
/// (180–0 hPa and others) and cloud cover for each pressure level and cloud
/// layer, which the levels keep out. GFS gives the precipitation rate and
/// cloud cover twice at each valid time, as the value at that time and as an
/// average over the hours before it; a reader keeps the first message, the
/// value at that time, which GFS writes first.
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

    /// <summary>Canonical id of the direction wind waves (wind sea) come from, in degrees true.</summary>
    public const string WindSeaDirection = "wave.wind_sea.dir";

    /// <summary>Canonical id of the significant height of swell.</summary>
    public const string SwellHeight = "wave.swell.hs";

    /// <summary>Canonical id of the air pressure reduced to mean sea level.</summary>
    public const string PressureMsl = "pressure.msl";

    /// <summary>Canonical id of the sea surface temperature, in kelvin.</summary>
    public const string SeaSurfaceTemperature = "sea.temperature";

    /// <summary>Canonical id of the eastward (U) component of the sea water velocity.</summary>
    public const string CurrentU = "current.u";

    /// <summary>Canonical id of the northward (V) component of the sea water velocity.</summary>
    public const string CurrentV = "current.v";

    /// <summary>Canonical id of the current speed.</summary>
    public const string CurrentSpeed = "current.speed";

    /// <summary>Canonical id of the direction the current flows to, in degrees true.</summary>
    public const string CurrentToDirection = "current.to_direction";

    /// <summary>Canonical id of the horizontal visibility at the surface, in metres.</summary>
    public const string Visibility = "visibility";

    /// <summary>Canonical id of the air temperature at 2 m above ground, in kelvin.</summary>
    public const string AirTemperature = "air.temperature";

    /// <summary>Canonical id of the surface (skin) temperature, in kelvin: the sea surface temperature over water.</summary>
    public const string SurfaceTemperature = "surface.temperature";

    /// <summary>Canonical id of the land cover mask: 1 over land, 0 over water.</summary>
    public const string LandCover = "land.cover";

    /// <summary>Canonical id of the precipitation rate at the surface, in kg m⁻² s⁻¹ (millimetres of water a second).</summary>
    public const string PrecipitationRate = "precipitation.rate";

    /// <summary>Canonical id of the convective available potential energy (CAPE) of a parcel from the surface, in J/kg.</summary>
    public const string Cape = "cape";

    /// <summary>Canonical id of the total cloud cover of the entire atmosphere, in percent.</summary>
    public const string CloudCover = "cloud.cover";

    /// <summary>
    /// Id of the freezing spray predictor, a field derived from the wind,
    /// air temperature and sea temperature (<see cref="FreezingSpray"/>), not
    /// a row of the table.
    /// </summary>
    public const string FreezingSprayPredictor = "spray.predictor";

    /// <summary>Group id of the 10 m wind: U, V, speed and direction.</summary>
    public const string WindGroup = "wind";

    /// <summary>Group id of the wind gust.</summary>
    public const string WindGustGroup = "wind.gust";

    /// <summary>Group id of the combined sea state: height, direction and period.</summary>
    public const string WaveGroup = "wave";

    /// <summary>Group id of the wind sea: height and direction.</summary>
    public const string WindSeaGroup = "wave.wind_sea";

    /// <summary>Group id of the swell height.</summary>
    public const string SwellGroup = "wave.swell";

    /// <summary>Group id of the mean sea level pressure.</summary>
    public const string PressureGroup = "pressure";

    /// <summary>Group id of the sea surface temperature.</summary>
    public const string SeaTemperatureGroup = "sea.temperature";

    /// <summary>Group id of the surface current: U, V, speed and direction.</summary>
    public const string CurrentGroup = "current";

    /// <summary>Group id of the visibility.</summary>
    public const string VisibilityGroup = "visibility";

    /// <summary>Group id of the 2 m air temperature.</summary>
    public const string AirTemperatureGroup = "air.temperature";

    /// <summary>Group id of the surface (skin) temperature.</summary>
    public const string SurfaceTemperatureGroup = "surface.temperature";

    /// <summary>Group id of the land cover mask.</summary>
    public const string LandCoverGroup = "land.cover";

    /// <summary>Group id of the precipitation rate.</summary>
    public const string PrecipitationGroup = "precipitation";

    /// <summary>Group id of the CAPE.</summary>
    public const string CapeGroup = "cape";

    /// <summary>Group id of the total cloud cover.</summary>
    public const string CloudGroup = "cloud";

    /// <summary>
    /// Name of the source that combines the wind, air temperature and sea
    /// temperature to derive <see cref="FreezingSprayPredictor"/>
    /// (<see cref="WeatherCoverageSource.CreateFreezingSpray"/>).
    /// </summary>
    public const string FreezingSprayGroup = "spray";

    private static readonly WeatherLevel TwoMetres = WeatherLevel.AboveGround(Length.FromMetres(2));

    private static readonly WeatherLevel TenMetres = WeatherLevel.AboveGround(Length.FromMetres(10));

    /// <summary>
    /// Every row of the table, in the order of design note §3.5, with the
    /// wind-sea direction (NCEP's WVDIR, issue #1074) after the wind-sea height,
    /// then the sea surface temperature (issue #1227), the surface current
    /// (issue #1228), the visibility (issue #1229), and the air and skin
    /// temperatures and land cover the freezing spray risk reads (issue #1231),
    /// and the precipitation rate, CAPE and total cloud cover (issue #1230).
    /// </summary>
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
        new(WindSeaDirection, "Wind-sea direction (from)", WeatherUnit.DegreesTrue, WindSeaGroup, null,
            [new(10, 0, 4)], ["sea_surface_wind_wave_from_direction"]),
        new(SwellHeight, "Swell significant height", WeatherUnit.Metres, SwellGroup, null,
            [new(10, 0, 8)], ["sea_surface_swell_wave_significant_height"]),
        new(PressureMsl, "Mean sea level pressure", WeatherUnit.Pascals, PressureGroup, WeatherLevel.MeanSeaLevel,
            [new(0, 3, 1), new(0, 3, 0)], ["air_pressure_at_mean_sea_level", "air_pressure_at_sea_level"]),
        new(SeaSurfaceTemperature, "Sea surface temperature", WeatherUnit.Kelvin, SeaTemperatureGroup, null,
            [new(10, 3, 0)],
            ["sea_surface_temperature", "sea_surface_foundation_temperature", "sea_surface_subskin_temperature", "sea_surface_skin_temperature"]),
        new(CurrentU, "Current, eastward component", WeatherUnit.MetresPerSecond, CurrentGroup, null,
            [new(10, 1, 2)], ["eastward_sea_water_velocity", "surface_eastward_sea_water_velocity"]),
        new(CurrentV, "Current, northward component", WeatherUnit.MetresPerSecond, CurrentGroup, null,
            [new(10, 1, 3)], ["northward_sea_water_velocity", "surface_northward_sea_water_velocity"]),
        new(CurrentSpeed, "Current speed", WeatherUnit.MetresPerSecond, CurrentGroup, null,
            [new(10, 1, 1)], ["sea_water_speed"]),
        new(CurrentToDirection, "Current direction (to)", WeatherUnit.DegreesTrue, CurrentGroup, null,
            [new(10, 1, 0)], ["sea_water_velocity_to_direction", "direction_of_sea_water_velocity"]),
        new(Visibility, "Visibility", WeatherUnit.Metres, VisibilityGroup, null,
            [new(0, 19, 0)], ["visibility_in_air"]),
        new(AirTemperature, "Air temperature at 2 m", WeatherUnit.Kelvin, AirTemperatureGroup, TwoMetres,
            [new(0, 0, 0)], ["air_temperature"]),
        new(SurfaceTemperature, "Surface temperature", WeatherUnit.Kelvin, SurfaceTemperatureGroup, WeatherLevel.Surface,
            [new(0, 0, 0)], ["surface_temperature"]),
        new(LandCover, "Land cover", WeatherUnit.Proportion, LandCoverGroup, WeatherLevel.Surface,
            [new(2, 0, 0)], ["land_binary_mask"]),
        new(PrecipitationRate, "Precipitation rate", WeatherUnit.KilogramsPerSquareMetrePerSecond, PrecipitationGroup, WeatherLevel.Surface,
            [new(0, 1, 7)], ["precipitation_flux"]),
        new(Cape, "Convective available potential energy", WeatherUnit.JoulesPerKilogram, CapeGroup, WeatherLevel.Surface,
            [new(0, 7, 6)], ["atmosphere_convective_available_potential_energy", "atmosphere_convective_available_potential_energy_wrt_surface"]),
        new(CloudCover, "Total cloud cover", WeatherUnit.Percent, CloudGroup, WeatherLevel.EntireAtmosphere,
            [new(0, 6, 1)], ["cloud_area_fraction"]),
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

    /// <summary>Finds the first row a GRIB2 parameter code maps to.</summary>
    /// <param name="code">The GRIB2 (discipline, category, number).</param>
    /// <returns>The row, or <see langword="null"/> when the code isn't in the table.</returns>
    /// <remarks>
    /// The level isn't checked here, and temperature (0,0,0) has two rows;
    /// readers use <see cref="FindByGribCode(GribParameterCode, WeatherLevel)"/>.
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

    /// <summary>
    /// Finds the row a GRIB2 parameter code at a level maps to: the row with
    /// that code and that level, else the first row with the code (whose
    /// level the reader then finds doesn't match, or which takes any level).
    /// </summary>
    /// <param name="code">The GRIB2 (discipline, category, number).</param>
    /// <param name="level">The message's level.</param>
    /// <returns>The row, or <see langword="null"/> when the code isn't in the table.</returns>
    public static WeatherParameterDefinition? FindByGribCode(GribParameterCode code, WeatherLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        WeatherParameterDefinition? first = null;
        foreach (var definition in All)
        {
            if (!definition.GribCodes.Contains(code))
                continue;
            if (definition.Level == level)
                return definition;
            first ??= definition;
        }

        return first;
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
