using EncDotNet.S100.Datasets.Grib.Tests.Encoding;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Grib.Tests;

public class ProductTests
{
    private static readonly DateTime Run = new(2026, 10, 25, 6, 0, 0, DateTimeKind.Utc);

    private static GribField Index(TestField field)
    {
        // The index outlives the file: only decoding needs it open.
        using var grib = GribFile.Open(GribTestEncoder.Message(field));
        return grib.Fields[0];
    }

    private static TestField Field() => new() { ReferenceTime = Run, Values = new double[35] };

    [Theory]
    [InlineData(1, 3, 3 * 60)]
    [InlineData(0, 90, 90)]
    [InlineData(2, 2, 2 * 24 * 60)]
    [InlineData(10, 2, 6 * 60)]
    [InlineData(13, 1800, 30)]
    [InlineData(1, -6, -6 * 60)]
    public void Template_4_0_valid_time_is_reference_plus_forecast_time(byte unit, int value, int minutes)
    {
        var field = Index(Field() with { TimeUnit = unit, ForecastTime = value });

        Assert.Equal(0, field.Product.TemplateNumber);
        Assert.Equal(new GribTimeRange((GribTimeUnit)unit, value), field.Product.ForecastTime);
        Assert.Equal(Run, field.ReferenceTime);
        Assert.Equal(Run.AddMinutes(minutes), field.ValidTime);
        Assert.Empty(field.Product.StatisticalProcesses);
        Assert.Null(field.Product.IntervalEnd);
    }

    [Fact]
    public void Template_4_8_is_valid_at_the_end_of_its_interval()
    {
        var end = Run.AddHours(9);
        var field = Index(Field() with
        {
            ProductTemplate = 8,
            ForecastTime = 6,
            IntervalEnd = end,
            StatisticalProcess = GribStatisticalProcess.Maximum,
            StatisticalLength = 3,
            Category = 2,
            Number = 22,
            SurfaceValue = 10,
        });

        Assert.Equal(8, field.Product.TemplateNumber);
        Assert.Equal(end, field.Product.IntervalEnd);
        Assert.Equal(end, field.ValidTime);
        Assert.Equal(new GribTimeRange(GribTimeUnit.Hour, 6), field.Product.ForecastTime);
        Assert.Equal(0, field.Product.MissingInStatisticalProcess);
        var process = Assert.Single(field.Product.StatisticalProcesses);
        Assert.Equal(new GribStatisticalProcess(2, 2, new GribTimeRange(GribTimeUnit.Hour, 3), new GribTimeRange(GribTimeUnit.Hour, 0)), process);

        // The maximum 10 m gust (ECMWF serves 10fg this way) maps to wind.gust.
        Assert.Equal(WeatherParameters.WindGust, field.FindWeatherParameter()?.Id);
    }

    [Fact]
    public void Levels_map_to_weather_levels()
    {
        Assert.Equal(WeatherLevel.Surface, Index(Field() with { SurfaceType = 1, SurfaceValue = 0 }).ToWeatherLevel());
        Assert.Equal(WeatherLevel.MeanSeaLevel, Index(Field() with { SurfaceType = 101, SurfaceValue = 0 }).ToWeatherLevel());
        Assert.Equal(WeatherLevel.AboveGround(Length.FromMetres(10)), Index(Field() with { SurfaceType = 103, SurfaceValue = 10 }).ToWeatherLevel());
        Assert.Equal(WeatherLevel.AboveGround(Length.FromMetres(2.5)), Index(Field() with { SurfaceType = 103, SurfaceScale = 1, SurfaceValue = 25 }).ToWeatherLevel());
        Assert.Equal("850 hPa", Index(Field() with { SurfaceType = 100, SurfaceValue = 85000 }).ToWeatherLevel().Description);

        // NCEP's swell partitions: local fixed surface type 241, "1 in sequence".
        var swell = Index(Field() with { Discipline = 10, Category = 0, Number = 8, SurfaceType = 241, SurfaceValue = 1 });
        Assert.Equal(WeatherLevelKind.Other, swell.ToWeatherLevel().Kind);
        Assert.Equal("GRIB2 fixed surface type 241 = 1", swell.ToWeatherLevel().Description);
        Assert.Equal(WeatherParameters.SwellHeight, swell.FindWeatherParameter()?.Id);
    }

    [Fact]
    public void Wind_at_another_level_isnt_the_canonical_10_m_wind()
    {
        // GFS also has UGRD at 10 mb and GFS-Wave UGRD at the surface.
        Assert.Null(Index(Field() with { SurfaceType = 100, SurfaceValue = 1000 }).FindWeatherParameter());
        Assert.Null(Index(Field() with { SurfaceType = 1, SurfaceValue = 0 }).FindWeatherParameter());
        Assert.Equal(WeatherParameters.WindU, Index(Field()).FindWeatherParameter()?.Id);
    }

    [Fact]
    public void Unknown_parameters_have_no_weather_row()
    {
        var field = Index(Field() with { Category = 1, Number = 8, SurfaceType = 1 });

        Assert.Equal(new GribParameterCode(0, 1, 8), field.Parameter);
        Assert.Null(field.FindWeatherParameter());
        Assert.True(field.IsSupported);
    }

    [Theory]
    [InlineData(1, "4.1 (individual ensemble forecast)")]
    [InlineData(11, "4.11 (individual ensemble forecast over a time interval)")]
    [InlineData(15, "4.15")]
    public void Other_product_templates_keep_the_parameter_but_arent_decoded(int template, string named)
    {
        var field = Index(Field() with { ProductTemplate = template, Category = 2, Number = 3 });

        Assert.Equal(new GribParameterCode(0, 2, 3), field.Parameter);
        Assert.Null(field.ValidTime);
        Assert.Null(field.Product.FirstSurface);
        Assert.False(field.IsSupported);
        Assert.Contains(named, field.UnsupportedReason);
    }

    [Fact]
    public void Inventory_line_names_the_templates()
    {
        var field = Index(Field() with { Packing = new ComplexTestPacking(SpatialDifferencingOrder: 2) });

        Assert.Equal("#0 0,2,2 type 103 = 10 ref 2026-10-25T06:00Z valid 2026-10-25T09:00Z 3.0 4.0 5.3", field.ToString());
    }
}
