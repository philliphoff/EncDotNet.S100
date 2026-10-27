using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather.Tests;

public class WeatherParametersTests
{
    [Theory]
    [InlineData("wind.u", 0, 2, 2, "eastward_wind", WeatherUnit.MetresPerSecond)]
    [InlineData("wind.v", 0, 2, 3, "northward_wind", WeatherUnit.MetresPerSecond)]
    [InlineData("wind.speed", 0, 2, 1, "wind_speed", WeatherUnit.MetresPerSecond)]
    [InlineData("wind.from_direction", 0, 2, 0, "wind_from_direction", WeatherUnit.DegreesTrue)]
    [InlineData("wind.gust", 0, 2, 22, "wind_speed_of_gust", WeatherUnit.MetresPerSecond)]
    [InlineData("wave.hs", 10, 0, 3, "sea_surface_wave_significant_height", WeatherUnit.Metres)]
    [InlineData("wave.dir", 10, 0, 14, "sea_surface_wave_from_direction", WeatherUnit.DegreesTrue)]
    [InlineData("wave.period", 10, 0, 34, "sea_surface_wave_period_at_variance_spectral_density_maximum", WeatherUnit.Seconds)]
    [InlineData("wave.wind_sea.hs", 10, 0, 5, "sea_surface_wind_wave_significant_height", WeatherUnit.Metres)]
    [InlineData("wave.wind_sea.dir", 10, 0, 4, "sea_surface_wind_wave_from_direction", WeatherUnit.DegreesTrue)]
    [InlineData("wave.swell.hs", 10, 0, 8, "sea_surface_swell_wave_significant_height", WeatherUnit.Metres)]
    [InlineData("pressure.msl", 0, 3, 1, "air_pressure_at_mean_sea_level", WeatherUnit.Pascals)]
    public void Table_maps_each_canonical_id(string id, byte discipline, byte category, byte number, string standardName, WeatherUnit unit)
    {
        var definition = WeatherParameters.Find(id);

        Assert.NotNull(definition);
        Assert.Equal(unit, definition.Unit);
        Assert.Equal(new GribParameterCode(discipline, category, number), definition.GribCodes[0]);
        Assert.Equal(standardName, definition.CfStandardNames[0]);
        Assert.Same(definition, WeatherParameters.FindByGribCode(new GribParameterCode(discipline, category, number)));
        Assert.Same(definition, WeatherParameters.FindByCfStandardName(standardName));
    }

    [Theory]
    [InlineData(10, 0, 10, "wave.dir")]
    [InlineData(10, 0, 15, "wave.period")]
    [InlineData(10, 0, 11, "wave.period")]
    [InlineData(0, 3, 0, "pressure.msl")]
    public void Alternative_GRIB2_codes_map_to_the_same_id(byte discipline, byte category, byte number, string id)
    {
        Assert.Equal(id, WeatherParameters.FindByGribCode(new GribParameterCode(discipline, category, number))?.Id);
    }

    [Fact]
    public void Ids_codes_and_names_are_unique()
    {
        var all = WeatherParameters.All;

        Assert.Equal(12, all.Count);
        Assert.Equal(all.Count, all.Select(d => d.Id).Distinct().Count());
        var codes = all.SelectMany(d => d.GribCodes).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        var names = all.SelectMany(d => d.CfStandardNames).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Wind_components_are_at_10_metres_and_pressure_at_mean_sea_level()
    {
        Assert.Equal(TestGrids.TenMetres, WeatherParameters.Find(WeatherParameters.WindU)?.Level);
        Assert.Equal("10 m above ground", WeatherParameters.Find(WeatherParameters.WindU)?.Level?.Description);
        Assert.Equal(WeatherLevel.MeanSeaLevel, WeatherParameters.Find(WeatherParameters.PressureMsl)?.Level);
        Assert.Null(WeatherParameters.Find(WeatherParameters.WaveHeight)?.Level);
        Assert.Equal(WeatherParameters.WindSeaGroup, WeatherParameters.Find(WeatherParameters.WindSeaDirection)?.Group);
    }

    [Fact]
    public void Unknown_codes_and_names_find_nothing()
    {
        Assert.Null(WeatherParameters.Find("wind.w"));
        Assert.Null(WeatherParameters.FindByGribCode(new GribParameterCode(0, 1, 8)));
        Assert.Null(WeatherParameters.FindByCfStandardName("sea_water_temperature"));
    }

    [Fact]
    public void Values_read_as_quantity_types()
    {
        Assert.Equal(Speed.FromMetresPerSecond(5), TestGrids.Definition(WeatherParameters.WindU).ToSpeed(5f));
        Assert.Equal(Length.FromMetres(2.5), TestGrids.Definition(WeatherParameters.WaveHeight).ToLength(2.5f));
        Assert.Equal(Angle.FromDegrees(270), TestGrids.Definition(WeatherParameters.WaveDirection).ToAngle(270f));
        Assert.Equal(TimeSpan.FromSeconds(8), TestGrids.Definition(WeatherParameters.WavePeriod).ToPeriod(8f));
    }

    [Fact]
    public void Missing_values_read_as_null()
    {
        Assert.Null(TestGrids.Definition(WeatherParameters.WindGust).ToSpeed(float.NaN));
        Assert.Null(TestGrids.Definition(WeatherParameters.SwellHeight).ToLength(float.NaN));
    }

    [Fact]
    public void Reading_a_value_as_the_wrong_quantity_throws()
    {
        Assert.Throws<InvalidOperationException>(() => TestGrids.Definition(WeatherParameters.WaveHeight).ToSpeed(1f));
        Assert.Throws<InvalidOperationException>(() => TestGrids.Definition(WeatherParameters.PressureMsl).ToLength(1f));
    }

    [Fact]
    public void Unknown_parameter_cannot_take_a_canonical_id()
    {
        Assert.Throws<ArgumentException>(
            () => new WeatherParameter(WeatherParameters.WindU, "m s-1", TestGrids.TenMetres, []));

        var unknown = new WeatherParameter("grib2:0,1,8", "kg m-2", WeatherLevel.Surface, []);
        Assert.False(unknown.IsKnown);
        Assert.Equal(WeatherUnit.Other, unknown.Unit);
        Assert.Equal("kg m-2", unknown.Units);
    }
}
