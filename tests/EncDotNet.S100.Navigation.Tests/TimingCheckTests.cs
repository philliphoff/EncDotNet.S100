using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// The timing check (#1561) on <see cref="Fixture.Leg(double, double?, double, double?)"/>:
/// 6 NM due east along the equator, so an hour at 6 kn, with a fake current
/// station whose current is the same everywhere.
/// </summary>
public class TimingCheckTests
{
    private static readonly DateTimeOffset Departure = Fixture.Departure;

    private static PassagePlan Plan(double? speed = 6) => Fixture.Plan(Fixture.Leg(speed: speed));

    private static RouteCheckOutcome<PassageTimingResult> Run(PassageTimingSettings? timing, IRouteCurrentSource? currents = null, PassagePlan? plan = null, DepartureRange? window = null, IWaterLevelSource? water = null) =>
        Checks.Run(
            new TimingCheck(),
            plan ?? Plan(),
            new RouteCheckSettings { At = Departure, Timing = timing, DepartureWindow = window },
            new RouteCheckSources { Currents = currents, WaterLevel = water });

    [Fact]
    public void Without_timing_settings_the_check_is_skipped()
    {
        var outcome = Run(null, FakeCurrents.Constant(2, 90));

        Assert.Equal(RouteCheckState.Skipped, outcome.State);
        Assert.Equal(TimingReasons.NotRequested, outcome.Reason);
    }

    [Fact]
    public void A_leg_without_a_speed_skips_the_check()
    {
        var outcome = Run(new PassageTimingSettings(), plan: Plan(speed: null));

        Assert.Equal(TimingReasons.SpeedMissing, outcome.Reason);
    }

    [Theory]
    [InlineData(90, 45)]
    [InlineData(270, 90)]
    public void Applied_currents_change_the_running_time(double setDegrees, double minutes)
    {
        var outcome = Run(new PassageTimingSettings { ApplyCurrents = true }, FakeCurrents.Constant(2, setDegrees));

        Assert.Equal(RouteCheckState.Ran, outcome.State);
        var planned = outcome.Detail!.Planned;
        Assert.True(outcome.Detail.CurrentsApplied);
        Assert.Equal(minutes, planned.RunningTime.TotalMinutes, 0.5);
        Assert.Equal(60, planned.Legs[0].RunningTimeAtSpeed.TotalMinutes, 0.5);
        Assert.Equal(setDegrees == 90 ? 2 : -2, planned.Legs[0].MeanAlongCurrentKnots!.Value, 2);
        Assert.Equal(["S1"], outcome.Detail.CurrentSources.Select(s => s.Id));
    }

    [Fact]
    public void Without_apply_currents_the_speed_stays_over_the_ground()
    {
        var outcome = Run(new PassageTimingSettings(), FakeCurrents.Constant(2, 90));

        Assert.False(outcome.Detail!.CurrentsApplied);
        Assert.Equal(60, outcome.Detail.Planned.RunningTime.TotalMinutes, 0.5);
        Assert.Equal(2, outcome.Detail.Planned.Legs[0].MeanAlongCurrentKnots!.Value, 2);
    }

    [Fact]
    public void Applying_currents_without_a_source_is_partial()
    {
        var outcome = Run(new PassageTimingSettings { ApplyCurrents = true });

        Assert.Equal(RouteCheckState.Partial, outcome.State);
        Assert.Equal(TimingReasons.CurrentsMissing, outcome.Reason);
        Assert.Equal(60, outcome.Detail!.Planned.RunningTime.TotalMinutes, 0.5);
    }

    [Fact]
    public void A_slack_gate_finds_the_departure_that_arrives_at_slack()
    {
        // Slack an hour after the planned departure, when the boat reaches the
        // arrival waypoint (6.01 NM at 6 kn).
        var currents = FakeCurrents.Tidal(Departure.AddHours(1));
        var timing = new PassageTimingSettings
        {
            Gates = [new TimingGate { Name = "Pass", WaypointIndex = 1, Station = "S1", SlackWithin = TimeSpan.FromMinutes(12) }],
        };

        var outcome = Run(timing, currents, window: DepartureRange.Between(Departure.AddHours(-2), Departure.AddHours(2), TimeSpan.FromMinutes(5)));

        var result = outcome.Detail!;
        var gate = Assert.Single(result.Planned.Gates);
        Assert.Equal(60, (gate.Eta - Departure).TotalMinutes, 0.5);
        Assert.Equal(Departure.AddHours(1), gate.NearestSlack);
        Assert.True(result.Planned.MeetsConstraints);
        var window = Assert.Single(result.Windows);
        Assert.Equal(Departure.AddMinutes(-10), window.From);
        Assert.Equal(Departure.AddMinutes(10), window.To);
        Assert.Equal(Departure, window.Best);
        Assert.Equal(49, result.Departures!.Count);
    }

    [Fact]
    public void A_slack_gate_needs_a_station()
    {
        var timing = new PassageTimingSettings
        {
            Gates = [new TimingGate { Position = new GeoPosition(0, 0.05), SlackWithin = TimeSpan.FromMinutes(10) }],
        };

        var outcome = Run(timing, FakeCurrents.Constant(2, 90, station: false));

        var constraint = Assert.Single(outcome.Detail!.Planned.Constraints);
        Assert.Equal(TimingConstraintCodes.GateSlack, constraint.Code);
        Assert.False(constraint.Holds);
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(270, false)]
    public void A_fair_only_gate_holds_with_the_current(double setDegrees, bool holds)
    {
        var timing = new PassageTimingSettings { Gates = [new TimingGate { Position = new GeoPosition(0, 0.05), FairOnly = true }] };

        var outcome = Run(timing, FakeCurrents.Constant(1.5, setDegrees));

        var constraint = Assert.Single(outcome.Detail!.Planned.Constraints);
        Assert.Equal(TimingConstraintCodes.GateFair, constraint.Code);
        Assert.Equal(holds, constraint.Holds);
    }

    [Fact]
    public void A_gate_current_above_its_limit_fails()
    {
        var timing = new PassageTimingSettings { Gates = [new TimingGate { WaypointIndex = 0, MaxCurrent = Speed.FromKnots(1) }] };

        var constraint = Assert.Single(Run(timing, FakeCurrents.Constant(2, 0)).Detail!.Planned.Constraints);

        Assert.Equal(TimingConstraintCodes.GateMaxCurrent, constraint.Code);
        Assert.False(constraint.Holds);
        Assert.Equal(2, constraint.Value!.Value, 2);
    }

    [Theory]
    [InlineData(270, 1, false)]
    [InlineData(270, 3, true)]
    [InlineData(90, 1, true)]
    public void A_leg_limit_judges_the_current_against_the_track(double setDegrees, double maxOpposing, bool holds)
    {
        var timing = new PassageTimingSettings { LegLimits = [new LegCurrentLimit { MaxOpposing = Speed.FromKnots(maxOpposing) }] };

        var constraint = Assert.Single(Run(timing, FakeCurrents.Constant(2, setDegrees)).Detail!.Planned.Constraints);

        Assert.Equal(TimingConstraintCodes.LegOpposingCurrent, constraint.Code);
        Assert.Equal(holds, constraint.Holds);
        Assert.Equal(0, constraint.LegIndex);
    }

    [Fact]
    public void A_leg_without_current_data_is_not_judged()
    {
        var timing = new PassageTimingSettings { LegLimits = [new LegCurrentLimit { MaxOpposing = Speed.FromKnots(1) }] };

        var constraint = Assert.Single(Run(timing, new FakeCurrents(_ => null, _ => null)).Detail!.Planned.Constraints);

        Assert.True(constraint.Holds);
        Assert.Null(constraint.Value);
    }

    [Theory]
    [InlineData(50, false)]
    [InlineData(70, true)]
    public void Arrive_by_judges_the_eta(int minutes, bool holds)
    {
        var timing = new PassageTimingSettings { ArriveBy = Departure.AddMinutes(minutes) };

        var constraint = Assert.Single(Run(timing).Detail!.Planned.Constraints);

        Assert.Equal(TimingConstraintCodes.ArriveBy, constraint.Code);
        Assert.Equal(holds, constraint.Holds);
    }

    [Theory]
    [InlineData(1.0, null, true)]
    [InlineData(2.0, null, false)]
    [InlineData(null, 1.0, false)]
    public void Arrival_tide_reads_the_water_level_at_the_eta(double? min, double? max, bool holds)
    {
        // The water is 1.5 m an hour after departure, at the arrival.
        var water = new FakeWater((_, _) =>
        [
            new FakeSeries(Departure.UtcDateTime.AddHours(-12), Departure.UtcDateTime.AddHours(12), t => t >= Departure.UtcDateTime.AddMinutes(55) ? 1.5 : 0),
        ]);
        var timing = new PassageTimingSettings { ArrivalTide = new ArrivalTideRange { MinMeters = min, MaxMeters = max } };

        var constraint = Assert.Single(Run(timing, water: water).Detail!.Planned.Constraints);

        Assert.Equal(TimingConstraintCodes.ArrivalTide, constraint.Code);
        Assert.Equal(holds, constraint.Holds);
        Assert.Equal(1.5, constraint.Value);
    }

    [Fact]
    public void The_checker_reports_timing_by_its_name()
    {
        var checker = new RouteChecker(new RouteCheckSources { Currents = FakeCurrents.Constant(2, 90) }, additionalChecks: [new TimingCheck()]);

        var report = checker.Check(Plan(), new RouteCheckSettings { At = Departure, Timing = new PassageTimingSettings { ApplyCurrents = true } }, TestContext.Current.CancellationToken);

        Assert.Equal(RouteCheckState.Ran, report.Status(TimingCheck.CheckName)!.State);
        Assert.Equal(45, report.Detail<PassageTimingResult>(TimingCheck.CheckName)!.Planned.RunningTime.TotalMinutes, 0.5);
    }
}

/// <summary>A current source whose one station covers everywhere.</summary>
internal sealed class FakeCurrents(Func<GeoPosition, IRouteCurrentSeries?> locate, Func<string, IRouteCurrentStation?> find) : IRouteCurrentSource
{
    public IRouteCurrentSeries? Locate(GeoPosition position) => locate(position);

    public IRouteCurrentStation? FindStation(string idOrName) => find(idOrName);

    /// <summary>A steady current everywhere, from station <c>S1</c> (or a plain series).</summary>
    public static FakeCurrents Constant(double knots, double setDegrees, bool station = true)
    {
        var s = new FakeStation(_ => new RouteCurrent(knots, setDegrees), (_, _) => []);
        IRouteCurrentSeries series = station ? s : new PlainSeries(s);
        return new FakeCurrents(_ => series, id => station && id == "S1" ? s : null);
    }

    /// <summary>
    /// A 12-hour east-west tidal stream of up to 2 kn, slack at <paramref name="slack"/>
    /// and every 6 hours either side.
    /// </summary>
    public static FakeCurrents Tidal(DateTimeOffset slack)
    {
        var period = TimeSpan.FromHours(12);
        var s = new FakeStation(
            t =>
            {
                var v = 2 * Math.Sin(2 * Math.PI * (t - slack).TotalHours / period.TotalHours);
                return new RouteCurrent(Math.Abs(v), v >= 0 ? 90 : 270);
            },
            (from, to) =>
            {
                var result = new List<DateTimeOffset>();
                var k = Math.Ceiling((from - slack).TotalHours / 6);
                for (var t = slack.AddHours(k * 6); t <= to; t = t.AddHours(6))
                {
                    result.Add(t);
                }

                return result;
            });
        return new FakeCurrents(_ => s, id => id == "S1" ? s : null);
    }

    private sealed class FakeStation(Func<DateTimeOffset, RouteCurrent?> at, Func<DateTimeOffset, DateTimeOffset, IReadOnlyList<DateTimeOffset>> slacks) : IRouteCurrentStation
    {
        public RouteCurrentSourceInfo Source { get; } = new("S1", "Station one", RouteCurrentOrigin.Prediction, null);

        public GeoPosition Position { get; } = new(0, 0.05);

        public RouteCurrent? At(DateTimeOffset time) => at(time);

        public IReadOnlyList<DateTimeOffset> SlacksBetween(DateTimeOffset from, DateTimeOffset to) => slacks(from, to);
    }

    private sealed class PlainSeries(IRouteCurrentSeries inner) : IRouteCurrentSeries
    {
        public RouteCurrentSourceInfo Source => inner.Source;

        public RouteCurrent? At(DateTimeOffset time) => inner.At(time);
    }
}
