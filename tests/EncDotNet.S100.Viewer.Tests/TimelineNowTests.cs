using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The Timeline's Now marker and button for forecasts (#685, handoff D1–D6).</summary>
public sealed class TimelineNowTests
{
    private static readonly DateTime Run = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A cbofs run: hourly steps from the run time to 48 h after it.</summary>
    private static MapsuiMapTimeSnapshot RunSnapshot(DateTime run, DateTime? current, string name = "111US00_CBOFS_{0}_US4VA1DD")
    {
        var samples = Enumerable.Range(0, 49).Select(h => run.AddHours(h)).ToArray();
        return new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = current ?? samples[0],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets = [new MapsuiMapTimedDataset(string.Format(name, run.ToString("yyyyMMdd'T'HH") + "Z"), samples[0], samples[^1])],
        };
    }

    private static (GlobalTimeService Service, TimelineViewModel Timeline, FakeTimeProvider Clock) Create(DateTime now)
    {
        var service = new GlobalTimeService();
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        var timeline = new TimelineViewModel(service, timeFormat: null, clock, action => action());
        return (service, timeline, clock);
    }

    [Fact]
    public void A_forecast_starts_at_now_and_marks_it_on_the_axis()
    {
        var (service, timeline, _) = Create(Run.AddHours(9).AddMinutes(10));

        service.ApplySnapshot(RunSnapshot(Run, current: null));

        Assert.True(timeline.IsForecastTimeline);
        Assert.Equal(["cbofs 12:00Z"], timeline.Runs);
        // D6: the first forecast starts at the step nearest now, not at step one.
        Assert.Equal(Run.AddHours(9), service.CurrentTime);
        Assert.True(timeline.IsNowInRange);
        Assert.Equal((9 + 10 / 60.0) / 48, timeline.NowPosition, 3);
        Assert.False(timeline.IsForecastEnded);
        Assert.Equal(string.Empty, timeline.NowEdgeLabel);
        Assert.Equal(1.0, timeline.BandOpacity);
        Assert.Contains(" · 48 h · cbofs 12:00Z · hourly", timeline.RangeLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Now_jumps_to_the_step_nearest_the_current_time()
    {
        var (service, timeline, _) = Create(Run.AddHours(20).AddMinutes(40));
        service.ApplySnapshot(RunSnapshot(Run, current: null));
        service.SetCurrentTime(Run.AddHours(2));

        Assert.True(timeline.NowCommand.CanExecute(null));
        timeline.NowCommand.Execute(null);

        Assert.Equal(Run.AddHours(21), service.CurrentTime);
    }

    [Fact]
    public void Once_every_run_has_ended_the_timeline_says_so_and_Now_is_disabled()
    {
        var (service, timeline, clock) = Create(Run.AddHours(40));
        service.ApplySnapshot(RunSnapshot(Run, current: null));

        // The minute clock moves past the end of the 48 h window.
        clock.Advance(TimeSpan.FromHours(11));

        Assert.True(timeline.IsForecastEnded);
        Assert.False(timeline.IsNowInRange);
        Assert.True(double.IsNaN(timeline.NowPosition));
        Assert.Equal("NOW 3 H LATER ›", timeline.NowEdgeLabel);
        Assert.Equal("forecast ended 3 h ago", timeline.ForecastEndedText);
        Assert.Equal(0.45, timeline.BandOpacity);
        Assert.False(timeline.NowCommand.CanExecute(null));
        Assert.EndsWith("Refresh the Library to look for newer runs", timeline.RangeLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void A_replaced_run_keeps_the_chosen_time_when_it_covers_it_else_moves_to_now()
    {
        var (service, timeline, _) = Create(Run.AddHours(9));
        service.ApplySnapshot(RunSnapshot(Run, current: null));
        service.SetCurrentTime(Run.AddHours(30));

        // The 18:00Z run covers 30 h after 12:00Z: the time stays.
        service.ApplySnapshot(RunSnapshot(Run.AddHours(6), current: Run.AddHours(30)));
        Assert.Equal(Run.AddHours(30), service.CurrentTime);
        Assert.Equal(["cbofs 18:00Z"], timeline.Runs);

        // The user looks back at 19:00Z; the next run (00:00Z) starts later, so the
        // session clamps to its start — and now (21:00Z) lies before it, so it stays.
        service.SetCurrentTime(Run.AddHours(7));
        service.ApplySnapshot(RunSnapshot(Run.AddHours(12), current: Run.AddHours(12)));
        Assert.Equal(Run.AddHours(12), service.CurrentTime);
        Assert.True(timeline.IsNowBeforeRange);
        Assert.Equal("‹ NOW 3 H EARLIER", timeline.NowEdgeLabel);
    }

    [Fact]
    public void A_run_that_no_longer_covers_the_chosen_time_moves_to_now()
    {
        var (service, timeline, _) = Create(Run.AddHours(10));
        service.ApplySnapshot(RunSnapshot(Run, current: null));
        service.SetCurrentTime(Run.AddHours(1));

        // The 18:00Z run starts after the chosen 13:00Z: the session clamps to 18:00Z, and the timeline moves to now (22:00Z).
        service.ApplySnapshot(RunSnapshot(Run.AddHours(6), current: Run.AddHours(6)));

        Assert.Equal(Run.AddHours(10), service.CurrentTime);
        Assert.True(timeline.IsNowInRange);
    }

    [Fact]
    public void Now_keeps_following_the_clock_until_the_user_picks_a_time()
    {
        var (service, timeline, clock) = Create(Run.AddHours(9).AddMinutes(10));
        service.ApplySnapshot(RunSnapshot(Run, current: null));
        Assert.True(service.IsFollowingNow);

        // An hour later the view has moved on with no user action (#706).
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(Run.AddHours(10), service.CurrentTime);

        // The slider echoing its own position is not a choice of time.
        timeline.SliderValue = timeline.SliderValue;
        Assert.True(service.IsFollowingNow);

        timeline.PreviousStepCommand.Execute(null);
        Assert.False(service.IsFollowingNow);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(Run.AddHours(9), service.CurrentTime);

        // Now resumes following.
        timeline.NowCommand.Execute(null);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(Run.AddHours(13), service.CurrentTime);
        Assert.True(service.IsFollowingNow);
    }

    [Fact]
    public void A_replaced_run_keeps_following_now()
    {
        var (service, timeline, clock) = Create(Run.AddHours(9));
        service.ApplySnapshot(RunSnapshot(Run, current: null));
        clock.Advance(TimeSpan.FromHours(6));

        // The 18:00Z run arrives with the session clamped to its first step.
        service.ApplySnapshot(RunSnapshot(Run.AddHours(6), current: Run.AddHours(6)));

        Assert.Equal(Run.AddHours(15), service.CurrentTime);
        Assert.True(service.IsFollowingNow);
    }

    [Fact]
    public void Steps_work_however_many_samples_there_are()
    {
        // 6-minute water levels over two days: far more than the 50 the slider snaps to.
        var samples = Enumerable.Range(0, 480).Select(i => Run.AddMinutes(6 * i)).ToArray();
        var (service, timeline, _) = Create(Run.AddHours(9));
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[100],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets = [new MapsuiMapTimedDataset("104US00_levels", samples[0], samples[^1])],
        });
        Assert.False(timeline.IsSnapToTickEnabled);

        Assert.True(timeline.NextStepCommand.CanExecute(null));
        timeline.NextStepCommand.Execute(null);
        Assert.Equal(samples[101], service.CurrentTime);

        Assert.True(timeline.PreviousStepCommand.CanExecute(null));
        timeline.PreviousStepCommand.Execute(null);
        timeline.PreviousStepCommand.Execute(null);
        Assert.Equal(samples[99], service.CurrentTime);
    }

    [Fact]
    public void Timelines_without_forecast_runs_show_no_Now()
    {
        var (service, timeline, _) = Create(Run.AddHours(9));

        service.ApplySnapshot(RunSnapshot(Run, current: null, name: "S104_tides_{0}x"));

        Assert.False(timeline.IsForecastTimeline);
        Assert.Equal(Run, service.CurrentTime);
        Assert.True(double.IsNaN(timeline.NowPosition));
        Assert.DoesNotContain("cbofs", timeline.RangeLabel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("111US00_CBOFS_20260930T18Z_US4VA1DD", "cbofs 18:00Z")]
    [InlineData("111US00_RTOFS_EAST_20261001T00Z_US2GOMBD", "rtofs_east 00:00Z")]
    [InlineData("111US00_NYOFS_20261001T11Z", "nyofs 11:00Z")]
    [InlineData("104US004SC1BO_20251217T12Z", "S-104 12:00Z")]
    [InlineData("102US004SC1EV262247", null)]
    [InlineData("US5AK1AM", null)]
    public void Run_names_come_from_dataset_names(string name, string? expected)
    {
        Assert.Equal(expected, ForecastRunNames.Describe(name));
    }
}
