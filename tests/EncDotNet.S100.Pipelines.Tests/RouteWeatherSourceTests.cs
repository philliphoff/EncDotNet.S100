using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1093: the route checks' weather source over loaded GRIB2 grids:
/// stored wind speed, wind from its U and V components, waves, and the
/// weather check run over them. Values are ecCodes'.
/// </summary>
public sealed class RouteWeatherSourceTests
{
    private static readonly DateTimeOffset RunTime = new(Run);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GeoPosition Point566
    {
        get
        {
            var (lat, lon) = CropPoint(566);
            return new GeoPosition(lat, lon);
        }
    }

    private static LoadedDataset Load(string name, string id)
    {
        using var stream = File.OpenRead(Fixture(name));
        return LoadedDatasetProjector.Project(new DatasetId(id), "GRIB2", stream)
            ?? throw new InvalidOperationException($"{name} didn't load.");
    }

    private static RouteCheckSources Sources(params LoadedDataset[] datasets) =>
        CatalogRouteCheckSources.FromDatasets(datasets, new ProjNetCrsTransformFactory());

    [Fact]
    public void Without_weather_grids_the_sources_have_no_weather()
    {
        Assert.Null(Sources().Weather);
    }

    [Fact]
    public void A_wave_model_file_gives_its_stored_wind_and_its_waves_at_the_nearest_node()
    {
        var weather = Sources(Load(WaveFilter, "gfswave")).Weather!;

        Assert.True(weather.Carries(WeatherQuantity.WindSpeed));
        Assert.True(weather.Carries(WeatherQuantity.SignificantWaveHeight));
        var waves = Assert.Single(weather.Locate(Point566, WeatherQuantity.SignificantWaveHeight));
        Assert.Equal("gfswave", waves.Source.DatasetId);
        Assert.Equal(RunTime, waves.Source.ModelRun);
        Assert.Equal([RunTime, RunTime.AddHours(3), RunTime.AddHours(6)], waves.Steps);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(FilterAt566.Height[i], waves.ValueAt(i)!.Value, 1e-4);
        }

        var wind = Assert.Single(weather.Locate(new GeoPosition(Point566.Latitude + 0.1, Point566.Longitude - 0.1), WeatherQuantity.WindSpeed));
        Assert.Equal(FilterAt566.WindSpeed[1], wind.ValueAt(1)!.Value, 1e-4);
    }

    [Fact]
    public void Wind_from_its_components_is_their_speed()
    {
        var weather = Sources(Load(Wind10m, "gfs")).Weather!;

        Assert.False(weather.Carries(WeatherQuantity.SignificantWaveHeight));
        var wind = Assert.Single(weather.Locate(Point566, WeatherQuantity.WindSpeed));
        for (var i = 0; i < 3; i++)
        {
            var speed = Math.Sqrt((Wind10mAt566.U[i] * Wind10mAt566.U[i]) + (Wind10mAt566.V[i] * Wind10mAt566.V[i]));
            Assert.Equal(speed, wind.ValueAt(i)!.Value, 1e-4);
        }
    }

    [Fact]
    public void Outside_the_grid_nothing_is_located()
    {
        var weather = Sources(Load(WaveFilter, "gfswave")).Weather!;

        Assert.Empty(weather.Locate(new GeoPosition(-30, 120), WeatherQuantity.SignificantWaveHeight));
    }

    [Fact]
    public void Two_files_are_both_located_and_the_check_reads_them()
    {
        var sources = Sources(Load(Wind10m, "gfs"), Load(WaveFilter, "gfswave"));
        var start = Point566;
        var plan = new PassagePlan(
            [
                new PassageLeg
                {
                    Points = [start, new GeoPosition(start.Latitude, start.Longitude + 0.1)],
                    Speed = Speed.FromKnots(6),
                },
            ],
            RunTime.AddHours(3));
        var settings = new RouteCheckSettings
        {
            At = RunTime,
            WeatherLimits = new WeatherLimits { MaxWind = Speed.FromKnots(15), MaxWaveHeight = Length.FromMetres(2) },
        };

        var report = new RouteChecker(sources).Check(plan, settings, Ct);

        Assert.Equal(new RouteCheckStatus("weather", RouteCheckState.Ran, null, 2), report.Status(WeatherCheck.CheckName));
        Assert.Equal(
            [RouteFindingCodes.WavesAboveLimit, RouteFindingCodes.WindAboveLimit],
            report.Findings.Where(f => f.Category == RouteFindingCategory.Weather).Select(f => f.Code).OrderBy(c => c.Value));
        var waves = (WeatherDetails)report.Findings.Single(f => f.Code == RouteFindingCodes.WavesAboveLimit).Details!;
        Assert.Equal(FilterAt566.Height[1], waves.Maximum!.Value, 1e-4);
        Assert.Equal(RunTime.AddHours(3), waves.MaximumStepTime);
        Assert.Equal("gfswave", waves.DatasetId);
        Assert.Contains("gfs", report.DatasetsUsed);
        Assert.Contains("gfswave", report.DatasetsUsed);
    }
}
