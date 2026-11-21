using EncDotNet.S100.DataModel;
using EncDotNet.S100.Geodesy;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The weather check's tropical cyclone rule (#1299) on a synthetic storm:
/// a route due east along 25° N from 80° W to 76° W in four 1° legs at
/// 10 kn (about 5.4 h a leg), past a storm whose centre sits 30 NM north of
/// waypoint 3 (25.5° N 78° W), with round 64, 50 and 34 kt radii of 20, 40
/// and 80 NM and a round 45 NM cone.
/// </summary>
/// <remarks>
/// So the route is inside the 34 kt radius on legs 1 to 4 (within 74 NM of
/// waypoint 3), inside the 50 kt radius on legs 2 and 3 (within 26.5 NM),
/// never inside the 64 kt one, and comes closest, 30 NM, at waypoint 3,
/// about 10.9 h after departure.
/// </remarks>
public class CycloneCheckTests
{
    private static readonly DateTimeOffset Departure = Fixture.Departure;

    private static readonly GeoPosition Centre = new(25.5, -78);

    private static PassagePlan Plan(DateTimeOffset? departure = null) => new(
        [
            Fixture.Leg([new(25, -80), new(25, -79)], speed: 10, name: "Leg 1 → 2"),
            Fixture.Leg([new(25, -79), new(25, -78)], speed: 10, name: "Leg 2 → 3"),
            Fixture.Leg([new(25, -78), new(25, -77)], speed: 10, name: "Leg 3 → 4"),
            Fixture.Leg([new(25, -77), new(25, -76)], speed: 10, name: "Leg 4 → 5"),
        ],
        departure ?? Departure);

    private static RouteCheckSettings Settings(TropicalCycloneCheckSettings? cyclones = null, WeatherLimits? limits = null, DepartureRange? window = null) =>
        new() { At = Departure, TropicalCyclones = cyclones, WeatherLimits = limits, DepartureWindow = window };

    private static RouteCheckOutcome<WeatherCheckResult> Run(
        FakeCyclone? storm = null,
        RouteCheckSettings? settings = null,
        PassagePlan? plan = null,
        IRouteWeatherSource? weather = null) =>
        Checks.Run(new WeatherCheck(), plan ?? Plan(), settings ?? Settings(), new RouteCheckSources
        {
            Cyclones = [storm ?? FakeCyclone.Stationary(Centre, Departure.AddHours(-24), Departure, Departure.AddHours(48))],
            Weather = weather,
        });

    private static double Nm(double metres) => metres / PassageGeodesy.MetersPerNauticalMile;

    private static RouteFinding Single(RouteCheckOutcome<WeatherCheckResult> outcome, RouteFindingCode code) =>
        Assert.Single(outcome.Findings, f => f.Code == code);

    [Fact]
    public void At_the_defaults_the_route_meets_the_34_and_50_kt_radii_the_cone_and_reports_the_closest_approach()
    {
        var outcome = Run();

        Assert.Equal(RouteCheckState.Ran, outcome.State);
        Assert.Null(outcome.Reason);
        Assert.Equal(WeatherVerdict.InsideCycloneRadius, outcome.Detail!.Verdict);
        Assert.Equal(
            ["weather.tc_cone", "weather.tc_cpa", "weather.tc_radius_34", "weather.tc_radius_50"],
            outcome.Findings.Select(f => f.Code.Value).Order(StringComparer.Ordinal));

        var r34 = Single(outcome, RouteFindingCodes.CycloneRadius34);
        var d34 = Assert.IsType<TropicalCycloneDetails>(r34.Details);
        Assert.Equal(RouteFindingGroup.Check, r34.Group);
        Assert.Equal(RouteFindingCategory.Weather, r34.Category);
        Assert.Equal((0, 3), (d34.FirstLegIndex, d34.LastLegIndex));
        Assert.InRange(Nm(d34.Length.TotalMetres), 146, 150);
        Assert.Equal("weather.tc_radius_34.AL992026.0", r34.Id);
        Assert.Equal("Inside the 34 kt radius", r34.Title);
        Assert.StartsWith("Legs 1 → 5 · ", r34.Meta, StringComparison.Ordinal);
        Assert.EndsWith(" · ZELDA adv 17", r34.Meta, StringComparison.Ordinal);

        // 54.4 NM a leg: the radius starts 74 NM before waypoint 3, 34.7 NM
        // along the route, about 3.5 h out, and ends 74 NM after it, 18.3 h out.
        Assert.InRange((d34.StartTime - Departure).TotalHours, 3.3, 3.7);
        Assert.InRange((d34.EndTime!.Value - Departure).TotalHours, 18.1, 18.5);

        var r50 = Single(outcome, RouteFindingCodes.CycloneRadius50);
        var d50 = Assert.IsType<TropicalCycloneDetails>(r50.Details);
        Assert.Equal(RouteFindingGroup.MustReview, r50.Group);
        Assert.Equal((1, 2), (d50.FirstLegIndex, d50.LastLegIndex));
        Assert.Equal(TropicalCycloneRadius.Knots50, d50.Radius);
        Assert.InRange((d50.StartTime - Departure).TotalHours, 8.1, 8.5);
        Assert.InRange((d50.EndTime!.Value - Departure).TotalHours, 13.3, 13.7);

        var cone = Single(outcome, RouteFindingCodes.CycloneCone);
        Assert.Equal(RouteFindingGroup.Check, cone.Group);
        Assert.Equal("Inside the forecast cone", cone.Title);

        var cpa = Single(outcome, RouteFindingCodes.CycloneClosestApproach);
        var dc = Assert.IsType<TropicalCycloneDetails>(cpa.Details);
        Assert.Equal(RouteFindingGroup.Info, cpa.Group);
        Assert.InRange(Nm(dc.ClosestDistance!.Value.TotalMetres), 29.8, 30.3);
        Assert.InRange((dc.StartTime - Departure).TotalHours, 10.7, 11.1);
        Assert.Equal(Centre, dc.StormAtClosest!.Centre);
        Assert.Equal("Closest approach 30 NM", cpa.Title);
        Assert.True(cpa.Location!.IsPoint);

        var storm = Assert.Single(outcome.Detail.Cyclones);
        Assert.Equal(TropicalCycloneRadius.Knots50, storm.StrongestRadius);
        Assert.True(storm.InCone);
        Assert.False(storm.BeyondForecast);
    }

    [Theory]
    [InlineData(TropicalCycloneRadius.Knots50, new[] { "weather.tc_cone", "weather.tc_cpa", "weather.tc_radius_50" })]
    [InlineData(TropicalCycloneRadius.Knots64, new[] { "weather.tc_cone", "weather.tc_cpa" })]
    public void Check_against_leaves_out_the_radii_outside_it(TropicalCycloneRadius radius, string[] codes)
    {
        var outcome = Run(settings: Settings(new TropicalCycloneCheckSettings { CheckAgainst = radius }));

        Assert.Equal(codes, outcome.Findings.Select(f => f.Code.Value).Order(StringComparer.Ordinal));
        Assert.Equal(
            radius == TropicalCycloneRadius.Knots64 ? WeatherVerdict.WithinLimits : WeatherVerdict.InsideCycloneRadius,
            outcome.Detail!.Verdict);
    }

    [Fact]
    public void Turning_off_the_closest_approach_leaves_it_out()
    {
        var outcome = Run(settings: Settings(new TropicalCycloneCheckSettings { ReportClosestApproach = false }));

        Assert.DoesNotContain(outcome.Findings, f => f.Code == RouteFindingCodes.CycloneClosestApproach);
        Assert.NotNull(Assert.Single(outcome.Detail!.Cyclones).ClosestApproach);
    }

    [Theory]
    [InlineData(TropicalCycloneRadius.Knots34, TropicalCycloneRadius.Knots34, RouteFindingGroup.MustReview, RouteFindingGroup.MustReview)]
    [InlineData(TropicalCycloneRadius.Knots34, TropicalCycloneRadius.Knots64, RouteFindingGroup.Check, RouteFindingGroup.Check)]
    [InlineData(TropicalCycloneRadius.Knots34, null, RouteFindingGroup.Check, RouteFindingGroup.Check)]
    [InlineData(TropicalCycloneRadius.Knots50, TropicalCycloneRadius.Knots34, null, RouteFindingGroup.MustReview)]
    public void Must_review_inside_sets_the_group_of_each_radius(TropicalCycloneRadius checkAgainst, TropicalCycloneRadius? mustReview, RouteFindingGroup? group34, RouteFindingGroup group50)
    {
        var outcome = Run(settings: Settings(new TropicalCycloneCheckSettings { CheckAgainst = checkAgainst, MustReviewInside = mustReview }));

        Assert.Equal(group34, outcome.Findings.SingleOrDefault(f => f.Code == RouteFindingCodes.CycloneRadius34)?.Group);
        Assert.Equal(group50, Single(outcome, RouteFindingCodes.CycloneRadius50).Group);
        Assert.Equal(RouteFindingGroup.Check, Single(outcome, RouteFindingCodes.CycloneCone).Group);
    }

    [Fact]
    public void Must_review_inside_cannot_sit_outside_check_against()
    {
        var settings = new TropicalCycloneCheckSettings { CheckAgainst = TropicalCycloneRadius.Knots64, MustReviewInside = TropicalCycloneRadius.Knots34 };

        Assert.Equal(TropicalCycloneRadius.Knots64, settings.EffectiveMustReviewInside);
        Assert.Equal([TropicalCycloneRadius.Knots64], settings.TestedRadii);
        Assert.Equal(RouteFindingGroup.MustReview, settings.GroupOf(TropicalCycloneRadius.Knots64));
        Assert.Null((settings with { MustReviewInside = null }).EffectiveMustReviewInside);
    }

    [Fact]
    public void Legs_after_the_last_forecast_point_are_not_checked()
    {
        // The forecast ends 8 h out, on leg 2.
        var storm = FakeCyclone.Stationary(Centre, Departure.AddHours(-24), Departure, Departure.AddHours(8));

        var outcome = Run(storm);

        var beyond = Single(outcome, RouteFindingCodes.CycloneBeyondForecast);
        var details = Assert.IsType<TropicalCycloneDetails>(beyond.Details);
        Assert.Equal(RouteFindingGroup.Check, beyond.Group);
        Assert.Equal((1, 3), (details.FirstLegIndex, details.LastLegIndex));
        Assert.Equal(Departure.AddHours(8), details.ForecastEnd);
        Assert.InRange((details.StartTime - Departure).TotalHours, 7.9, 8.1);
        Assert.Equal("ZELDA not checked: beyond the forecast", beyond.Title);
        Assert.True(Assert.Single(outcome.Detail!.Cyclones).BeyondForecast);

        // The 34 kt stretch on leg 1 still counts; the 50 kt one is beyond the forecast.
        Assert.Equal(WeatherVerdict.InsideCycloneRadius, outcome.Detail.Verdict);
        var r34 = Assert.IsType<TropicalCycloneDetails>(Single(outcome, RouteFindingCodes.CycloneRadius34).Details);
        Assert.InRange((r34.EndTime!.Value - Departure).TotalHours, 7.9, 8.1);
        Assert.DoesNotContain(outcome.Findings, f => f.Code == RouteFindingCodes.CycloneRadius50);
    }

    [Fact]
    public void Each_quadrant_has_its_own_extent()
    {
        // The route is south of the centre; with the 34 kt and 50 kt radii
        // only to the north, it stays outside them.
        var storm = FakeCyclone.Stationary(Centre, Departure.AddHours(-24), Departure, Departure.AddHours(48), southern: 0);

        var outcome = Run(storm);

        Assert.DoesNotContain(outcome.Findings, f => f.Code.Value.StartsWith("weather.tc_radius", StringComparison.Ordinal));
        Assert.Contains(outcome.Findings, f => f.Code == RouteFindingCodes.CycloneClosestApproach);
    }

    [Fact]
    public void A_storm_far_from_the_route_is_left_out()
    {
        var far = FakeCyclone.Stationary(new GeoPosition(40, -40), Departure.AddHours(-24), Departure, Departure.AddHours(2));

        var outcome = Run(far);

        Assert.Equal(RouteCheckState.Ran, outcome.State);
        Assert.Empty(outcome.Findings);
        Assert.Empty(outcome.Detail!.Cyclones);
        Assert.Equal(WeatherVerdict.WithinLimits, outcome.Detail.Verdict);
    }

    [Fact]
    public void With_grids_but_no_limits_only_the_cyclone_rule_runs()
    {
        var outcome = Run(weather: FakeWeather.Uniform(40, 5, Departure, 30));

        Assert.Equal(RouteCheckState.Partial, outcome.State);
        Assert.Equal(RouteCheckReasons.WeatherLimitsMissing, outcome.Reason);
        Assert.Empty(outcome.Detail!.Limits);
        Assert.Contains(outcome.Findings, f => f.Code == RouteFindingCodes.CycloneRadius50);
    }

    [Fact]
    public void Cyclone_and_grid_findings_come_from_one_run()
    {
        var limits = new WeatherLimits { MaxWind = Speed.FromKnots(25) };
        var outcome = Run(weather: FakeWeather.Uniform(30, 1, Departure, 30), settings: Settings(limits: limits));

        Assert.Equal(RouteCheckState.Ran, outcome.State);
        Assert.Contains(outcome.Findings, f => f.Code == RouteFindingCodes.WindAboveLimit);
        Assert.Contains(outcome.Findings, f => f.Code == RouteFindingCodes.CycloneRadius50);
        Assert.Equal(WeatherVerdict.InsideCycloneRadius, outcome.Detail!.Verdict);
    }

    [Fact]
    public void The_storm_is_read_at_each_samples_eta()
    {
        // Moving north at 20 kn: at departure it is 30 NM from waypoint 3,
        // but the vessel only gets there 10.9 h later, when the storm is
        // 248 NM north. Nothing is inside a radius.
        var storm = FakeCyclone.Moving(Centre, Departure, northKnots: 20, Departure.AddHours(-24), Departure.AddHours(48));

        var outcome = Run(storm);

        Assert.DoesNotContain(outcome.Findings, f => f.Code.Value.StartsWith("weather.tc_radius", StringComparison.Ordinal));
        var cpa = Assert.Single(outcome.Detail!.Cyclones).ClosestApproach!;
        Assert.True(Nm(cpa.DistanceMeters) > 100);

        // Leaving 10.9 h earlier, the vessel is at waypoint 3 as the storm is.
        var early = Run(storm, plan: Plan(Departure.AddHours(-10.9)));
        Assert.Contains(early.Findings, f => f.Code == RouteFindingCodes.CycloneRadius50);
    }

    [Fact]
    public void The_departure_sweep_shows_which_departures_keep_the_route_outside_the_radius()
    {
        var storm = FakeCyclone.Moving(Centre, Departure, northKnots: 20, Departure.AddHours(-24), Departure.AddHours(48));
        var window = DepartureRange.Between(Departure.AddHours(-16), Departure.AddHours(4), TimeSpan.FromHours(1));

        var outcome = Run(storm, settings: Settings(window: window));

        var sweep = outcome.Detail!.Departures!;
        Assert.Equal(21, sweep.Options.Count);
        // Leaving 11 h before, the storm crosses the route just ahead of the vessel.
        var atCpa = sweep.Options.Single(o => o.Departure == Departure.AddHours(-11));
        Assert.Equal(TropicalCycloneRadius.Knots64, atCpa.CycloneRadius);
        Assert.Equal(WeatherVerdict.InsideCycloneRadius, atCpa.Verdict);
        var late = sweep.Options[^1];
        Assert.True(late.WithinLimits);
        Assert.Null(late.CycloneRadius);
        Assert.True(Nm(late.CycloneClosestMeters!.Value) > 80);
        Assert.Equal(Departure.AddHours(4), sweep.Windows()[^1].Last);
    }

    [Fact]
    public void Without_grids_or_cyclones_the_check_is_skipped()
    {
        var outcome = Checks.Run(new WeatherCheck(), Plan(), Settings(), new RouteCheckSources { Cyclones = [] });

        Assert.Equal(RouteCheckState.Skipped, outcome.State);
        Assert.Equal(RouteCheckReasons.WeatherMissing, outcome.Reason);
    }
}

/// <summary>
/// A synthetic storm with round wind radii (64, 50 and 34 kt of 20, 40 and
/// 80 NM) and a round 45 NM cone around its centre at the synoptic time.
/// </summary>
internal sealed class FakeCyclone(
    Func<DateTimeOffset, GeoPosition> centre,
    DateTimeOffset trackStart,
    DateTimeOffset forecastStart,
    DateTimeOffset forecastEnd,
    double southern) : IRouteCyclone
{
    public RouteCycloneInfo Info { get; } = new("al992026_017", "AL992026", "ZELDA", "17", forecastStart);

    public DateTimeOffset ForecastStart => forecastStart;

    public DateTimeOffset ForecastEnd => forecastEnd;

    public IReadOnlyList<GeoPosition> Track =>
        [.. Enumerable.Range(0, (int)((forecastEnd - trackStart).TotalHours / 6) + 1).Select(i => centre(trackStart.AddHours(6 * i)))];

    public static FakeCyclone Stationary(GeoPosition at, DateTimeOffset trackStart, DateTimeOffset forecastStart, DateTimeOffset forecastEnd, double southern = 1) =>
        new(_ => at, trackStart, forecastStart, forecastEnd, southern);

    public static FakeCyclone Moving(GeoPosition at, DateTimeOffset when, double northKnots, DateTimeOffset trackStart, DateTimeOffset forecastEnd) =>
        new(t => new GeoPosition(at.Latitude + ((t - when).TotalHours * northKnots / 60), at.Longitude), trackStart, when, forecastEnd, 1);

    public RouteCycloneAt? At(DateTimeOffset time)
    {
        if (time < trackStart || time > forecastEnd)
        {
            return null;
        }

        RouteCycloneRadii Radii(TropicalCycloneRadius r, double nm) => new(r, nm, nm * southern, nm * southern, nm);
        return new RouteCycloneAt(
            time,
            centre(time),
            time < forecastStart ? RouteCycloneBasis.BestTrack : RouteCycloneBasis.Estimated,
            [Radii(TropicalCycloneRadius.Knots34, 80), Radii(TropicalCycloneRadius.Knots50, 40), Radii(TropicalCycloneRadius.Knots64, 20)],
            "H");
    }

    public bool InCone(GeoPosition position) =>
        Wgs84Geodesy.GeodesicDistanceMetres(centre(forecastStart), position) <= 45 * PassageGeodesy.MetersPerNauticalMile;
}
