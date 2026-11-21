using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Navigation.Tests;

/// <summary>
/// Currents in the ETAs every check uses (#1033), on
/// <see cref="Fixture.Leg(double, double?, double, double?)"/>: 6 NM due
/// east along the equator, an hour at 6 kn.
/// </summary>
public class ScheduleCurrentsTests
{
    private static readonly DateTimeOffset Departure = Fixture.Departure;

    private static PassagePlan Plan() => Fixture.Plan(Fixture.Leg(speed: 6));

    [Theory]
    [InlineData(90, 45)] // 2 kn fair: 8 kn over the ground
    [InlineData(270, 90)] // 2 kn foul: 4 kn
    [InlineData(0, 60)] // across the track: no change
    public void A_current_along_the_track_changes_the_ETAs(double setDegrees, double minutes)
    {
        var schedule = PassageSchedule.Compute(Plan().Legs, Departure, FakeCurrents.Constant(2, setDegrees));

        Assert.True(schedule.CurrentsApplied);
        Assert.Equal(minutes, schedule.Duration!.Value.TotalMinutes, 0.5);
        Assert.Equal(minutes / 2, (schedule.EtaAt(0, schedule.Legs[0].DistanceMeters / 2)!.Value - Departure).TotalMinutes, 0.5);
        Assert.Equal(["S1"], schedule.CurrentSources.Select(s => s.Id));
        Assert.Empty(schedule.CurrentGaps);
    }

    [Fact]
    public void Without_a_source_the_speed_is_over_the_ground()
    {
        var schedule = PassageSchedule.Compute(Plan().Legs, Departure);

        Assert.False(schedule.CurrentsApplied);
        Assert.Equal(60, schedule.Duration!.Value.TotalMinutes, 0.5);
    }

    [Fact]
    public void Where_no_current_covers_the_ETA_the_speed_stands_and_the_gap_is_kept()
    {
        // Current only on the first half of the leg (west of 0.05°).
        var fair = FakeCurrents.Constant(2, 90);
        var half = new FakeCurrents(p => p.Longitude < 0.05 ? fair.Locate(p) : null, _ => null);

        var schedule = PassageSchedule.Compute(Plan().Legs, Departure, half);

        // 3 NM at 8 kn, then 3 NM at 6 kn: 22.5 + 30 minutes.
        Assert.Equal(52.5, schedule.Duration!.Value.TotalMinutes, 1.0);
        var gap = Assert.Single(schedule.CurrentGaps);
        Assert.Equal(0, gap.LegIndex);
        Assert.InRange(gap.StartAlongLegMeters, 5_000, 5_700);
        Assert.Equal(schedule.Legs[0].DistanceMeters, gap.EndAlongLegMeters, 1);
    }

    [Fact]
    public void Speeds_through_the_water_reach_every_checks_ETAs_and_report_the_gaps()
    {
        var half = new FakeCurrents(p => p.Longitude < 0.05 ? FakeCurrents.Constant(2, 90).Locate(p) : null, _ => null);
        var checker = new RouteChecker(new RouteCheckSources { Currents = half });

        var over = checker.Check(Plan(), new RouteCheckSettings { At = Departure }, TestContext.Current.CancellationToken);
        var through = checker.Check(Plan(), new RouteCheckSettings { At = Departure, SpeedsThroughWater = true }, TestContext.Current.CancellationToken);

        Assert.Equal(60, over.Schedule.Duration!.Value.TotalMinutes, 0.5);
        Assert.DoesNotContain(over.Findings, f => f.Code == RouteFindingCodes.NoCurrentCoverage);

        Assert.Equal(52.5, through.Schedule.Duration!.Value.TotalMinutes, 1.0);
        var finding = Assert.Single(through.Findings, f => f.Code == RouteFindingCodes.NoCurrentCoverage);
        Assert.Equal(RouteFindingGroup.Info, finding.Group);
        Assert.Equal("No current at the ETA", finding.Title);
        Assert.Contains("speed taken as over the ground", finding.Meta);
        Assert.InRange(finding.Location!.StartDistanceMeters, 5_000, 5_700);
    }

    [Fact]
    public void The_timing_checks_apply_currents_also_moves_the_other_checks_ETAs()
    {
        var checker = new RouteChecker(new RouteCheckSources { Currents = FakeCurrents.Constant(2, 270) });

        var report = checker.Check(Plan(), new RouteCheckSettings { At = Departure, Timing = new PassageTimingSettings { ApplyCurrents = true } }, TestContext.Current.CancellationToken);

        Assert.Equal(90, report.Schedule.Duration!.Value.TotalMinutes, 0.5);
    }

    [Fact]
    public void A_reading_names_the_source_it_came_from()
    {
        var series = FakeCurrents.Constant(1, 90).Locate(new GeoPosition(0, 0))!;

        var reading = series.Read(Departure);

        Assert.Equal(new RouteCurrent(1, 90), reading!.Current);
        Assert.Equal("S1", reading.Source.Id);
    }
}
