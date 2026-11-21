using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The weather check on a 60 NM leg due east along the equator from 0° to
/// 1° E, run at 10 kn from <see cref="Fixture.Departure"/>, so the vessel is
/// at longitude <c>x</c> about <c>6x</c> hours after departure.
/// </summary>
public class WeatherCheckTests
{
    private static readonly DateTimeOffset Departure = Fixture.Departure;

    private static readonly WeatherLimits Limits = new() { MaxWind = Speed.FromKnots(25), MaxWaveHeight = Length.FromMetres(3) };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PassageLeg Leg(double speed = 10) =>
        Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 1)], speed: speed, name: "Leg 1 → 2");

    private static PassagePlan Plan(PassageLeg? leg = null, WeatherLimits? vessel = null, DateTimeOffset? departure = null) =>
        new([leg ?? Leg()], departure ?? Departure) { VesselLimits = vessel };

    private static RouteCheckSettings Settings(WeatherLimits? limits = null, DepartureRange? window = null) =>
        new() { At = Departure, WeatherLimits = limits, DepartureWindow = window };

    private static RouteCheckOutcome<WeatherCheckResult> Run(IRouteWeatherSource weather, PassagePlan? plan = null, RouteCheckSettings? settings = null, WeatherCheck? check = null) =>
        Checks.Run(check ?? new WeatherCheck(), plan ?? Plan(), settings ?? Settings(Limits), new RouteCheckSources { Weather = weather });

    private static double Ms(double knots) => Speed.FromKnots(knots).TotalMetresPerSecond;

    [Fact]
    public void Without_weather_the_check_is_skipped()
    {
        var outcome = Checks.Run(new WeatherCheck(), Plan(), Settings(Limits), RouteCheckSources.Empty);

        Assert.Equal(RouteCheckState.Skipped, outcome.State);
        Assert.Equal(RouteCheckReasons.WeatherMissing, outcome.Reason);
    }

    [Fact]
    public void Without_a_limit_the_check_is_skipped()
    {
        var outcome = Run(FakeWeather.Uniform(40, 5, Departure, 12), settings: Settings());

        Assert.Equal(RouteCheckState.Skipped, outcome.State);
        Assert.Equal(RouteCheckReasons.WeatherLimitsMissing, outcome.Reason);
    }

    [Fact]
    public void Within_the_limits_the_route_has_no_findings()
    {
        var outcome = Run(FakeWeather.Uniform(15, 1.5, Departure - TimeSpan.FromHours(1), 12));

        Assert.Equal(RouteCheckState.Ran, outcome.State);
        Assert.Empty(outcome.Findings);
        var result = outcome.Detail!;
        Assert.Equal(WeatherVerdict.WithinLimits, result.Verdict);
        Assert.Equal(61, result.Samples.Count);
        Assert.All(result.Samples, s => Assert.All(s.Readings, r => Assert.Equal(WeatherSampleState.WithinLimit, r.State)));
        Assert.Equal(["gfs.grib2"], result.DatasetsUsed);
        Assert.Equal(15, Speed.FromMetresPerSecond(result.Maximum(WeatherQuantity.WindSpeed)!.Value.Reading.Value!.Value).TotalKnots, 6);
    }

    [Fact]
    public void A_stretch_above_the_wind_limit_is_one_check_finding_with_its_time_window()
    {
        // 30 kn from 08:00 to 11:00 (steps 08:00 to 10:00 plus the half step
        // either side), 15 kn otherwise.
        var steps = FakeWeatherSeries.Hourly(Departure - TimeSpan.FromHours(1), 12);
        var weather = new FakeWeather(
            (_, q) => [new FakeWeatherSeries(steps, t => q == WeatherQuantity.WindSpeed
                ? (t.Hour is >= 8 and <= 10 ? Ms(30) : Ms(15))
                : 1.0)]);

        var outcome = Run(weather);

        var finding = Assert.Single(outcome.Findings);
        Assert.Equal(RouteFindingCodes.WindAboveLimit, finding.Code);
        Assert.Equal(RouteFindingGroup.Check, finding.Group);
        Assert.Equal(RouteFindingCategory.Weather, finding.Category);
        Assert.Equal("weather.wind_above_limit.0", finding.Id);
        var details = Assert.IsType<WeatherDetails>(finding.Details);
        Assert.Null(details.Gap);
        Assert.Equal(WeatherQuantity.WindSpeed, details.Quantity);
        Assert.Equal(Ms(25), details.Limit!.Value, 6);
        Assert.Equal(WeatherLimitOrigin.Settings, details.LimitOrigin);
        Assert.Equal(Ms(30), details.Maximum!.Value, 4);
        Assert.Equal("gfs.grib2", details.DatasetId);

        // In the stretch from 07:30 to 10:30, give or take one sample (6 min).
        Assert.InRange(details.StartTime, Departure.AddMinutes(90 - 6), Departure.AddMinutes(90 + 6));
        Assert.InRange(details.EndTime!.Value, Departure.AddMinutes(270 - 6), Departure.AddMinutes(270 + 6));
        Assert.StartsWith("Wind above 25 kn for 3", finding.Title, StringComparison.Ordinal);
        Assert.Contains("max 30 kn at", finding.Meta, StringComparison.Ordinal);
        Assert.StartsWith("Leg 1 → 2", finding.Meta, StringComparison.Ordinal);
        Assert.False(finding.Location!.IsPoint);
        Assert.Equal(WeatherVerdict.AboveLimits, outcome.Detail!.Verdict);
    }

    [Fact]
    public void Wind_and_waves_above_their_limits_are_separate_findings()
    {
        var outcome = Run(FakeWeather.Uniform(30, 4, Departure - TimeSpan.FromHours(1), 12));

        Assert.Equal(
            [RouteFindingCodes.WavesAboveLimit, RouteFindingCodes.WindAboveLimit],
            outcome.Findings.Select(f => f.Code).OrderBy(c => c.Value));
        var waves = outcome.Findings.Single(f => f.Code == RouteFindingCodes.WavesAboveLimit);
        Assert.StartsWith("Waves above 3.0 m for 6", waves.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Where_the_forecast_ends_before_arrival_the_rest_of_the_route_is_not_checked()
    {
        // Steps 06:00 to 09:00: the vessel is past 09:30 (half a step after
        // the last) from about halfway, where nothing is checked.
        var outcome = Run(FakeWeather.Uniform(15, 1, Departure, 4));

        var result = outcome.Detail!;
        Assert.Equal(WeatherVerdict.ForecastEndsBeforeArrival, result.Verdict);
        Assert.Equal(
            [RouteFindingCodes.WeatherForecastEnds, RouteFindingCodes.WeatherForecastEnds],
            outcome.Findings.Select(f => f.Code));
        Assert.All(outcome.Findings, f => Assert.Equal(RouteFindingGroup.Check, f.Group));
        var wind = outcome.Findings.Single(f => ((WeatherDetails)f.Details!).Quantity == WeatherQuantity.WindSpeed);
        Assert.Equal("weather.forecast_ends.wind.0", wind.Id);
        Assert.Equal("Wind not checked: forecast ends before arrival", wind.Title);
        var details = (WeatherDetails)wind.Details!;
        Assert.Equal(WeatherSampleState.ForecastEnds, details.Gap);
        Assert.Equal(Departure.AddHours(3), details.ForecastEnd);
        Assert.InRange(details.StartTime, Departure.AddMinutes(210 - 6), Departure.AddMinutes(210 + 6));
        Assert.Contains("forecast ends 2024-06-01 09:00 UTC", wind.Meta, StringComparison.Ordinal);

        // Before the end every sample is checked; after it none is.
        var first = result.Samples.First(s => s.Reading(WeatherQuantity.WindSpeed)!.State == WeatherSampleState.ForecastEnds);
        Assert.All(result.Samples.TakeWhile(s => s != first), s => Assert.True(s.Reading(WeatherQuantity.WindSpeed)!.IsChecked));
        Assert.All(result.Samples.SkipWhile(s => s != first), s => Assert.Equal(WeatherSampleState.ForecastEnds, s.Reading(WeatherQuantity.WindSpeed)!.State));
        Assert.Null(first.Reading(WeatherQuantity.WindSpeed)!.Value);
    }

    [Fact]
    public void A_forecast_that_ends_before_departure_checks_nothing_rather_than_passing()
    {
        var outcome = Run(FakeWeather.Uniform(40, 6, Departure - TimeSpan.FromDays(2), 6));

        Assert.Equal(WeatherVerdict.ForecastEndsBeforeArrival, outcome.Detail!.Verdict);
        Assert.DoesNotContain(outcome.Findings, f => f.Code == RouteFindingCodes.WindAboveLimit);
        var wind = outcome.Findings.Single(f => f.Id == "weather.forecast_ends.wind.0");
        Assert.Equal(0, wind.Location!.StartDistanceMeters, 6);
    }

    [Fact]
    public void A_forecast_that_starts_after_the_vessel_passes_is_a_gap()
    {
        // Steps from 10:00: the first two thirds of the passage are before them.
        var outcome = Run(FakeWeather.Uniform(15, 1, Departure.AddHours(4), 12));

        var late = outcome.Findings.Where(f => f.Code == RouteFindingCodes.WeatherForecastStartsLate).ToList();
        Assert.Equal(2, late.Count);
        var details = (WeatherDetails)late[0].Details!;
        Assert.Equal(Departure.AddHours(4), details.ForecastStart);
        Assert.Equal(0, details.StartTime.Subtract(Departure).TotalMinutes, 0.1);
        Assert.Equal(WeatherVerdict.Incomplete, outcome.Detail!.Verdict);
    }

    [Fact]
    public void Outside_every_grid_and_without_a_value_are_gaps()
    {
        // No grid east of 0,5° E; west of it waves have no value (masked).
        var steps = FakeWeatherSeries.Hourly(Departure - TimeSpan.FromHours(1), 12);
        var weather = new FakeWeather((p, q) => p.Longitude > 0.5
            ? []
            : [new FakeWeatherSeries(steps, _ => q == WeatherQuantity.WindSpeed ? Ms(10) : null)]);

        var outcome = Run(weather);

        Assert.Equal(
            ["weather.no_coverage.waves.0", "weather.no_coverage.wind.0", "weather.no_data.waves.0"],
            outcome.Findings.Select(f => f.Id).Order(StringComparer.Ordinal));
        Assert.Equal("Waves not checked: no forecast value", outcome.Findings.Single(f => f.Code == RouteFindingCodes.WeatherNoData).Title);
        Assert.Equal("Wind not checked: outside forecast coverage", outcome.Findings.Single(f => f.Id == "weather.no_coverage.wind.0").Title);
        Assert.Equal(WeatherVerdict.Incomplete, outcome.Detail!.Verdict);
    }

    [Fact]
    public void The_best_series_that_reaches_the_time_is_read()
    {
        // A regional forecast (first) ends at 08:00; the global one runs on.
        var regional = new FakeWeatherSeries(FakeWeatherSeries.Hourly(Departure, 3), _ => Ms(10), "regional.grib2");
        var global = new FakeWeatherSeries(FakeWeatherSeries.Hourly(Departure, 12, TimeSpan.FromHours(3)), _ => Ms(30), "global.grib2");
        var weather = new FakeWeather((_, _) => [regional, global], WeatherQuantity.WindSpeed);

        var outcome = Run(weather, settings: Settings(new WeatherLimits { MaxWind = Speed.FromKnots(25) }));

        var finding = Assert.Single(outcome.Findings);
        Assert.Equal(RouteFindingCodes.WindAboveLimit, finding.Code);
        Assert.Equal("global.grib2", ((WeatherDetails)finding.Details!).DatasetId);
        Assert.InRange(((WeatherDetails)finding.Details!).StartTime, Departure.AddMinutes(150 - 6), Departure.AddMinutes(150 + 6));
        Assert.Equal(["global.grib2", "regional.grib2"], outcome.Detail!.DatasetsUsed);
    }

    [Fact]
    public void A_masked_value_gives_way_to_a_coarser_grid_with_one()
    {
        var fine = new FakeWeatherSeries(FakeWeatherSeries.Hourly(Departure, 12), _ => null, "coastal.nc");
        var coarse = new FakeWeatherSeries(FakeWeatherSeries.Hourly(Departure, 12), _ => 2.0, "ww3.grib2");
        var weather = new FakeWeather((_, _) => [fine, coarse], WeatherQuantity.SignificantWaveHeight);

        var outcome = Run(weather, settings: Settings(new WeatherLimits { MaxWaveHeight = Length.FromMetres(3) }));

        Assert.Empty(outcome.Findings);
        Assert.Equal(["ww3.grib2"], outcome.Detail!.DatasetsUsed);
    }

    [Fact]
    public void The_settings_replace_the_routes_limits_value_by_value()
    {
        var weather = FakeWeather.Uniform(30, 4, Departure, 12);

        // The route says 35 kn and 3 m; the settings say 25 kn only.
        var outcome = Run(weather,
            plan: Plan(vessel: new WeatherLimits { MaxWind = Speed.FromKnots(35), MaxWaveHeight = Length.FromMetres(3) }),
            settings: Settings(new WeatherLimits { MaxWind = Speed.FromKnots(25) }));

        Assert.Equal(
            [
                new WeatherLimitUsed(WeatherQuantity.WindSpeed, Ms(25), WeatherLimitOrigin.Settings),
                new WeatherLimitUsed(WeatherQuantity.SignificantWaveHeight, 3, WeatherLimitOrigin.Route),
            ],
            outcome.Detail!.Limits);
        Assert.Equal(WeatherLimitOrigin.Route, ((WeatherDetails)outcome.Findings.Single(f => f.Code == RouteFindingCodes.WavesAboveLimit).Details!).LimitOrigin);
    }

    [Fact]
    public void The_routes_own_limits_are_used_without_settings()
    {
        var outcome = Run(FakeWeather.Uniform(30, 1, Departure, 12), plan: Plan(vessel: new WeatherLimits { MaxWind = Speed.FromKnots(28) }), settings: Settings());

        var finding = Assert.Single(outcome.Findings);
        Assert.StartsWith("Wind above 28 kn", finding.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_limit_for_a_quantity_no_dataset_has_runs_the_check_in_part()
    {
        var outcome = Run(new FakeWeather((_, _) => [new FakeWeatherSeries(FakeWeatherSeries.Hourly(Departure, 12), _ => Ms(10))], WeatherQuantity.WindSpeed));

        Assert.Equal(RouteCheckState.Partial, outcome.State);
        Assert.Equal(RouteCheckReasons.WavesMissing, outcome.Reason);
        Assert.Empty(outcome.Findings);
        Assert.Equal([WeatherQuantity.WindSpeed], outcome.Detail!.Limits.Select(l => l.Quantity));
    }

    [Fact]
    public void Without_speeds_the_route_is_judged_at_the_departure()
    {
        var outcome = Run(FakeWeather.Uniform(30, 1, Departure, 2), plan: Plan(Fixture.Leg([new GeoPosition(0, 0), new GeoPosition(0, 1)], speed: null)));

        var finding = Assert.Single(outcome.Findings);
        Assert.All(outcome.Detail!.Samples, s => Assert.False(s.HasEta));
        Assert.Null(((WeatherDetails)finding.Details!).EndTime);
        Assert.StartsWith("Wind above 25 kn for 60.1 NM", finding.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_departure_change_reuses_the_located_series()
    {
        var weather = FakeWeather.Uniform(15, 1, Departure, 48);
        var check = new WeatherCheck();
        Run(weather, check: check);
        var located = weather.Locates;

        Run(weather, plan: Plan(departure: Departure.AddHours(3)), check: check);
        Run(weather, plan: Plan(), settings: Settings(new WeatherLimits { MaxWind = Speed.FromKnots(10) }), check: check);

        Assert.Equal(located, weather.Locates);
        Run(weather, plan: Plan(Leg(speed: 8)), check: check);
        Assert.True(weather.Locates > located);
    }

    [Fact]
    public void The_departure_sweep_finds_the_departures_that_avoid_the_wind()
    {
        // A gale in the steps 14:00 to 17:00, so at ETAs from 13:30 to 17:30.
        // The passage takes six hours: leaving 08:00 to 17:00 meets it.
        var steps = FakeWeatherSeries.Hourly(Departure, 28);
        var weather = new FakeWeather((_, q) => [new FakeWeatherSeries(steps, t => q == WeatherQuantity.WindSpeed
            ? (t >= Departure.AddHours(8) && t < Departure.AddHours(12) ? Ms(40) : Ms(10))
            : 1.0)]);

        var outcome = Run(weather, settings: Settings(Limits, DepartureRange.Between(Departure, Departure.AddHours(24), TimeSpan.FromHours(1))));

        var sweep = outcome.Detail!.Departures!;
        Assert.Equal(25, sweep.Options.Count);
        Assert.Equal(TimeSpan.FromHours(1), sweep.Step);
        Assert.True(sweep.Options[1].WithinLimits);
        Assert.Equal(WeatherVerdict.AboveLimits, sweep.Options[2].Verdict);
        Assert.Equal(WeatherVerdict.AboveLimits, sweep.Options[11].Verdict);
        Assert.True(sweep.Options[12].WithinLimits);
        Assert.InRange(sweep.Options[7].TimeAboveLimits, TimeSpan.FromHours(3.8), TimeSpan.FromHours(4.2));
        Assert.Equal(Ms(40), sweep.Options[7].MaxWindMetresPerSecond!.Value, 4);
        Assert.Equal(Ms(10), sweep.Options[0].MaxWindMetresPerSecond!.Value, 4);

        // Leaving at 06:00 the next day arrives after the forecast ends (09:00, plus half a step).
        Assert.Equal(WeatherVerdict.ForecastEndsBeforeArrival, sweep.Options[^1].Verdict);
        Assert.Equal((Departure, Departure.AddHours(1)), sweep.Windows()[0]);
        Assert.Equal(Departure.AddHours(12), sweep.Windows()[1].First);
        Assert.Equal(Departure.AddHours(12), sweep.NearestWithinLimits(Departure.AddHours(9))!.Departure);
    }

    [Fact]
    public void A_sweep_that_follows_the_data_follows_the_forecast()
    {
        var outcome = Run(FakeWeather.Uniform(10, 1, Departure, 13), settings: Settings(Limits, DepartureRange.WaterLevelData(TimeSpan.FromHours(1))));

        var sweep = outcome.Detail!.Departures!;
        Assert.Equal(Departure, sweep.Options[0].Departure);
        Assert.Equal(Departure.AddHours(12), sweep.Options[^1].Departure);
        Assert.Equal(WeatherVerdict.ForecastEndsBeforeArrival, sweep.Options[^1].Verdict);
        Assert.True(sweep.Options[0].WithinLimits);
    }

    [Fact]
    public void The_route_checker_runs_the_weather_check_and_reports_its_datasets()
    {
        var sources = new RouteCheckSources { Weather = FakeWeather.Uniform(30, 1, Departure, 12) };

        var report = new RouteChecker(sources).Check(Plan(), Settings(Limits), Token);

        Assert.Equal(new RouteCheckStatus("weather", RouteCheckState.Ran, null, 1), report.Status(WeatherCheck.CheckName));
        Assert.NotNull(report.Weather);
        Assert.Contains("gfs.grib2", report.DatasetsUsed);
        Assert.Equal(RouteCheckVerdictKind.Check, report.Verdict.Kind);
        Assert.Equal(RouteFindingCodes.WindAboveLimit, report.Verdict.Headline!.Code);
    }

    [Theory]
    [InlineData(5.5, 6.0, 0)]
    [InlineData(6.4, 6.0, 0)]
    [InlineData(6.5, 6.0, 0)]
    [InlineData(6.6, 6.0, 1)]
    [InlineData(8.5, 6.0, 2)]
    [InlineData(9.4, 6.0, 3)]
    public void A_time_reads_the_nearest_step_within_half_a_step_of_the_ends(double hour, double first, int expected)
    {
        var midnight = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var series = new FakeWeatherSeries(FakeWeatherSeries.Hourly(midnight.AddHours(first), 4), _ => 1);
        var time = midnight.AddHours(hour);

        Assert.Equal(expected, WeatherCheck.Nearest(series, time));
    }

    [Theory]
    [InlineData(5.4)]
    [InlineData(9.6)]
    public void A_time_beyond_half_a_step_of_the_ends_reads_nothing(double hour)
    {
        var midnight = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var series = new FakeWeatherSeries(FakeWeatherSeries.Hourly(midnight.AddHours(6), 4), _ => 1);

        Assert.Null(WeatherCheck.Nearest(series, midnight.AddHours(hour)));
    }
}
