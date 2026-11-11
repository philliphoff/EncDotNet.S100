using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather.Tests;

/// <summary>
/// Issue #1231: Overland's freezing spray predictor and classes, and the
/// combined source that derives it from the wind, air and sea (or skin)
/// temperature of one grid set.
/// </summary>
public sealed class FreezingSprayTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const float Zero = (float)SeaTemperatureStyle.CelsiusZero;

    [Theory]
    // PPR = Va (Tf − Ta) / (1 + 0.3 (Tw − Tf)), Tf = −1.7 °C.
    [InlineData(15, -10, 2, 59.005, FreezingSprayClass.Heavy)]
    [InlineData(10, -5, 0, 21.854, FreezingSprayClass.Light)]
    [InlineData(20, -15, -1, 219.835, FreezingSprayClass.Extreme)]
    [InlineData(12, -8, 1, 41.768, FreezingSprayClass.Moderate)]
    [InlineData(15, 0, 2, -12.085, FreezingSprayClass.None)]
    [InlineData(0, -20, 2, 0, FreezingSprayClass.None)]
    public void The_predictor_is_Overlands_and_classes_by_its_limits(double wind, double air, double water, double expected, FreezingSprayClass expectedClass)
    {
        double predictor = FreezingSpray.Predictor(Speed.FromMetresPerSecond(wind), air, water);

        Assert.Equal(expected, predictor, 3);
        Assert.Equal(expectedClass, FreezingSpray.Classify(predictor));
    }

    [Fact]
    public void Water_colder_than_minus_2_degrees_is_ice_or_land_and_has_no_predictor()
    {
        Assert.True(double.IsNaN(FreezingSpray.Predictor(Speed.FromMetresPerSecond(15), -10, -2.5)));
        Assert.Null(FreezingSpray.Classify(double.NaN));
        Assert.Equal("Moderate", FreezingSpray.GetLabel(FreezingSprayClass.Moderate));
    }

    [Fact]
    public void A_GFS_like_set_derives_the_predictor_from_wind_components_air_and_skin_temperature_over_water_only()
    {
        // Three cells: open water, land (the mask), and skin colder than −2 °C (sea ice).
        var set = new WeatherGridSet(TestGrids.Origin, TestGrids.Regular(1, 3),
        [
            TestGrids.Parameter(WeatherParameters.WindU, (0, [0f, 0f, 0f])),
            TestGrids.Parameter(WeatherParameters.WindV, (0, [15f, 15f, 15f])),
            TestGrids.Parameter(WeatherParameters.AirTemperature, (0, [Zero - 10, Zero - 10, Zero - 10])),
            TestGrids.Parameter(WeatherParameters.SurfaceTemperature, (0, [Zero + 2, Zero + 2, Zero - 3])),
            TestGrids.Parameter(WeatherParameters.LandCover, (0, [0f, 1f, 0f])),
        ]);

        var sources = WeatherCoverageSource.CreateForGroups(set);
        var spray = Assert.Single(sources, s => s.IsCombined);
        var predictor = spray.Sample(GridRegion.Full, Ct).GetValues(WeatherParameters.FreezingSprayPredictor);

        Assert.Equal(WeatherParameters.FreezingSprayGroup, spray.Name);
        Assert.Equal([WeatherParameters.FreezingSprayPredictor], spray.FieldNames);
        Assert.Equal(59.005f, predictor[0], 0.01f);
        Assert.True(float.IsNaN(predictor[1]));
        Assert.True(float.IsNaN(predictor[2]));

        // Only the spray fill draws the combined source; the wind and
        // temperatures are drawn by their own groups.
        Assert.Equal([FreezingSprayStyle.Key], WeatherCoverageStyle.CreateFor(spray).Select(s => s.LayerKey));
        Assert.DoesNotContain(sources.Where(s => !s.IsCombined).SelectMany(WeatherCoverageStyle.CreateFor), s => s is FreezingSprayStyle);
        Assert.Contains(sources.Single(s => s.Name == WeatherParameters.AirTemperatureGroup) is var air ? WeatherCoverageStyle.CreateFor(air) : [],
            s => s is AirTemperatureIsothermStyle);
    }

    [Fact]
    public void A_sea_temperature_is_preferred_to_the_skin_temperature_and_needs_no_land_mask()
    {
        var set = new WeatherGridSet(TestGrids.Origin, TestGrids.Regular(1, 1),
        [
            TestGrids.Parameter(WeatherParameters.WindSpeed, (0, [10f])),
            TestGrids.Parameter(WeatherParameters.AirTemperature, (0, [Zero - 5])),
            TestGrids.Parameter(WeatherParameters.SeaSurfaceTemperature, (0, [Zero])),
            TestGrids.Parameter(WeatherParameters.SurfaceTemperature, (0, [Zero + 20])),
            TestGrids.Parameter(WeatherParameters.LandCover, (0, [1f])),
        ]);

        var spray = WeatherCoverageSource.CreateFreezingSpray(set)!;

        Assert.Equal(
            [WeatherParameters.WindSpeed, WeatherParameters.AirTemperature, WeatherParameters.SeaSurfaceTemperature],
            spray.DerivedFields.Single().Inputs);
        Assert.Equal(21.854f, spray.Sample(GridRegion.Full, Ct).GetValues(WeatherParameters.FreezingSprayPredictor)[0], 0.01f);
    }

    [Theory]
    [InlineData(WeatherParameters.AirTemperature)]
    [InlineData(WeatherParameters.SurfaceTemperature)]
    [InlineData(WeatherParameters.WindV)]
    public void Without_every_input_there_is_no_freezing_spray_source(string missing)
    {
        WeatherParameter[] all =
        [
            TestGrids.Parameter(WeatherParameters.WindU, (0, [0f])),
            TestGrids.Parameter(WeatherParameters.WindV, (0, [15f])),
            TestGrids.Parameter(WeatherParameters.AirTemperature, (0, [Zero - 10])),
            TestGrids.Parameter(WeatherParameters.SurfaceTemperature, (0, [Zero + 2])),
        ];
        var set = new WeatherGridSet(TestGrids.Origin, TestGrids.Regular(1, 1), [.. all.Where(p => p.Id != missing)]);

        Assert.Null(WeatherCoverageSource.CreateFreezingSpray(set));
        Assert.DoesNotContain(WeatherCoverageSource.CreateForGroups(set), s => s.IsCombined);
    }
}
