using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.NetCdf.Tests;

/// <summary>The CF unit conversions of the precipitation, CAPE and cloud cover rows (#1230).</summary>
public class CfUnitsTests
{
    [Theory]
    [InlineData("kg m-2 s-1", 1)]
    [InlineData("kg m**-2 s**-1", 1)]
    [InlineData("mm/s", 1)]
    [InlineData("mm h-1", 1 / 3600.0)]
    [InlineData("mm/hr", 1 / 3600.0)]
    [InlineData("m s-1", 1000)]
    public void Precipitation_rates_convert_to_kilograms_per_square_metre_a_second(string units, double factor)
    {
        Assert.Equal(factor, CfUnits.FactorTo(units, WeatherUnit.KilogramsPerSquareMetrePerSecond)!.Value, 12);
    }

    [Theory]
    [InlineData("J kg-1")]
    [InlineData("J/kg")]
    [InlineData("m2 s-2")]
    [InlineData("m**2 s**-2")]
    public void CAPE_reads_in_joules_per_kilogram(string units)
    {
        Assert.Equal(1, CfUnits.FactorTo(units, WeatherUnit.JoulesPerKilogram));
    }

    [Theory]
    [InlineData("%", 1)]
    [InlineData("percent", 1)]
    [InlineData("1", 100)]
    [InlineData("fraction", 100)]
    public void Cloud_cover_reads_in_percent(string units, double factor)
    {
        Assert.Equal(factor, CfUnits.FactorTo(units, WeatherUnit.Percent));
    }

    [Theory]
    [InlineData("K", WeatherUnit.JoulesPerKilogram)]
    [InlineData("m", WeatherUnit.KilogramsPerSquareMetrePerSecond)]
    [InlineData("okta", WeatherUnit.Percent)]
    public void Other_units_have_no_conversion(string units, WeatherUnit target)
    {
        Assert.Null(CfUnits.ConversionTo(units, target));
    }
}
