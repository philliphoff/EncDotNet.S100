using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// <c>check_route</c>'s weather check (#1093) over the GFS-Wave crop, which
/// has the wave model's wind and its significant wave height: at file index
/// 566 (35.75°N, 74.75°W) about 20 kn and 2.54 m at f003.
/// </summary>
public class CheckRouteWeatherTests
{
    private static readonly DateTimeOffset RunTime = new(Run);

    private static GeoPolyline Polyline
    {
        get
        {
            var (lat, lon) = CropPoint(566);
            return new GeoPolyline([new GeoPoint(lat, lon), new GeoPoint(lat, lon + 0.1)]);
        }
    }

    private static FakeDatasetCatalog Catalog()
    {
        var catalog = new FakeDatasetCatalog();
        using var stream = File.OpenRead(Fixture(WaveFilter));
        catalog.Add(LoadedDatasetProjector.Project(new DatasetId("gfswave"), "GRIB2", stream)!);
        return catalog;
    }

    private static async Task<CheckRouteResult> Ok(CheckRouteTool tool, CheckRouteRequest request)
    {
        var result = await tool.InvokeAsync(request, TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    [Fact]
    public async Task Limits_check_the_forecast_along_the_route()
    {
        var value = await Ok(CheckRouteTools.Create(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, SpeedKnots: 6, Departure: RunTime.AddHours(3), MaxWindKnots: 15, MaxWaveHeightMeters: 2));

        var check = value.Checks.Single(c => c.Name == "weather");
        Assert.Equal("ran", check.State);
        Assert.Equal(2, check.FindingCount);
        var weather = value.Weather!;
        Assert.Equal("above_limits", weather.Verdict);
        Assert.Equal(
            [new CheckRouteWeatherLimit("wind_speed", 15, "kn", "settings"), new CheckRouteWeatherLimit("significant_wave_height", 2, "m", "settings")],
            weather.Limits);
        Assert.Equal(Math.Round(Speed.FromMetresPerSecond(FilterAt566.WindSpeed[1]).TotalKnots, 2), weather.MaxWind!.Value);
        Assert.Equal(Math.Round(FilterAt566.Height[1], 2), weather.MaxWaveHeight!.Value);
        Assert.Equal(RunTime.AddHours(3), weather.MaxWaveHeight.StepTime);
        Assert.Equal(["gfswave"], weather.Datasets);
        Assert.Equal(0, weather.NotCheckedCount);
        Assert.Null(weather.Window);
        Assert.Contains("gfswave", value.Datasets);
        Assert.EndsWith(CheckRouteWeather.DisclaimerText, value.Disclaimer, StringComparison.Ordinal);

        var waves = value.Findings.Single(f => f.Code == RouteFindingCodes.WavesAboveLimit.Value);
        Assert.Equal("weather", waves.Category);
        Assert.Equal("check", waves.Group);
        var details = waves.Details!.Weather!;
        Assert.Equal("significant_wave_height", details.Quantity);
        Assert.Equal("above_limit", details.State);
        Assert.Equal("m", details.Unit);
        Assert.Equal(2, details.Limit);
        Assert.Equal("settings", details.LimitOrigin);
        Assert.Equal("gfswave", details.DatasetId);
        Assert.Equal(RunTime, details.ModelRun);
        Assert.NotNull(details.EndTime);
    }

    [Fact]
    public async Task Without_limits_the_weather_check_is_skipped()
    {
        var value = await Ok(CheckRouteTools.Create(Catalog()), new CheckRouteRequest(Polyline: Polyline, SpeedKnots: 6, Departure: RunTime));

        var check = value.Checks.Single(c => c.Name == "weather");
        Assert.Equal("skipped", check.State);
        Assert.Equal(RouteCheckReasons.WeatherLimitsMissing, check.Reason);
        Assert.Null(value.Weather);
    }

    [Fact]
    public async Task A_departure_after_the_forecast_is_not_checked()
    {
        var value = await Ok(CheckRouteTools.Create(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, SpeedKnots: 6, Departure: RunTime.AddDays(2), MaxWindKnots: 40));

        Assert.Equal("forecast_ends_before_arrival", value.Weather!.Verdict);
        var gap = value.Findings.Single(f => f.Code == RouteFindingCodes.WeatherForecastEnds.Value);
        Assert.Equal("forecast_ends", gap.Details!.Weather!.State);
        Assert.Equal(RunTime.AddHours(6), gap.Details.Weather.ForecastEnd);
        Assert.Equal(value.Weather.SampleCount, value.Weather.NotCheckedCount);
    }

    [Fact]
    public async Task The_window_sweeps_departures_across_the_forecast()
    {
        var value = await Ok(CheckRouteTools.Create(Catalog()), new CheckRouteRequest(
            Polyline: Polyline, SpeedKnots: 6, Departure: RunTime, MaxWaveHeightMeters: 2.6, IncludeWindow: true, WindowStepMinutes: 60));

        var window = value.Weather!.Window!;
        Assert.Equal(60, window.StepMinutes);
        Assert.Equal(RunTime, window.Departures[0].Departure);
        Assert.Equal("within_limits", window.Departures[0].Verdict);

        // f006 is 2.62 m, above 2.6 m: departures read at it aren't within.
        Assert.Contains(window.Departures, d => d.Verdict == "above_limits");
        Assert.Equal(RunTime, window.Windows[0].First);
    }

    [Fact]
    public async Task A_routes_own_limits_are_used_and_the_request_replaces_them()
    {
        var (lat, lon) = CropPoint(566);
        var routes = new FakeRoutes(new PassageRouteSnapshot(
            "r1",
            null,
            [new PassageRouteLeg([new GeoPosition(lat, lon), new GeoPosition(lat, lon + 0.1)], LegGeometry.Rhumb, null, 6, null, null, null)],
            RunTime.AddHours(3),
            new WeatherLimits { MaxWind = Speed.FromKnots(30), MaxWaveHeight = Length.FromMetres(2) }));

        var own = await Ok(CheckRouteTools.Create(Catalog(), routes: routes, time: TimeProvider.System), new CheckRouteRequest());
        Assert.Equal(["route", "route"], own.Weather!.Limits.Select(l => l.Origin));
        Assert.Equal([RouteFindingCodes.WavesAboveLimit.Value], own.Findings.Where(f => f.Category == "weather").Select(f => f.Code));

        var replaced = await Ok(CheckRouteTools.Create(Catalog(), routes: routes, time: TimeProvider.System), new CheckRouteRequest(MaxWindKnots: 15));
        Assert.Equal(["settings", "route"], replaced.Weather!.Limits.Select(l => l.Origin));
        Assert.Equal(2, replaced.Findings.Count(f => f.Category == "weather"));
    }

    [Theory]
    [InlineData(0.0, null, "maxWindKnots")]
    [InlineData(-5.0, null, "maxWindKnots")]
    [InlineData(null, 0.0, "maxWaveHeightMeters")]
    public async Task Limits_must_be_positive(double? wind, double? wave, string parameter)
    {
        var result = await CheckRouteTools.Create(Catalog()).InvokeAsync(
            new CheckRouteRequest(Polyline: Polyline, MaxWindKnots: wind, MaxWaveHeightMeters: wave),
            TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Contains(parameter, error!.Message, StringComparison.Ordinal);
    }

    private sealed class FakeRoutes(PassageRouteSnapshot route) : IPassageRouteProvider
    {
        public Task<ToolResult<PassageRouteSnapshot>> GetRouteAsync(string? routeId, CancellationToken cancellationToken) =>
            Task.FromResult(ToolResult<PassageRouteSnapshot>.Ok(route));
    }
}
