using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Timeline's clock: Live and Pinned modes, now on every timeline, the
/// readout's offset and the status line (#685, #706, #713).
/// </summary>
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

    private static (GlobalTimeService Service, TimelineViewModel Timeline, FakeTimeProvider Clock) Create(
        DateTime now, IForecastRunRefresher? refresher = null)
    {
        var service = new GlobalTimeService();
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        var timeline = new TimelineViewModel(service, timeFormat: null, clock, action => action(), refresher);
        return (service, timeline, clock);
    }

    [Fact]
    public void A_forecast_covering_now_starts_live_at_now()
    {
        var (service, timeline, _) = Create(Run.AddHours(9).AddMinutes(10));

        service.ApplySnapshot(RunSnapshot(Run, current: null));

        Assert.Equal(TimeMode.Live, service.Mode);
        Assert.True(timeline.IsLive);
        Assert.Equal("LIVE", timeline.ModeLabel);
        // Live is now itself, not the step nearest it (#713 A1).
        Assert.Equal(Run.AddHours(9).AddMinutes(10), service.CurrentTime);
        Assert.Equal("now", timeline.OffsetText);
        Assert.StartsWith("LIVE · ", timeline.StampText, StringComparison.Ordinal);
        Assert.Equal((9 + 10 / 60.0) / 48, timeline.NowPosition, 3);
        Assert.False(timeline.HasStatusMessage);
        Assert.False(timeline.NowCommand.CanExecute(null));
        Assert.Equal(["cbofs 12:00Z"], timeline.Runs);
        // The range line reads the run in the user's setting (Local here, #730); agents get UTC.
        var local = LayerTimes.Clock(Run, TimeFormat.Local, TimeZoneInfo.Utc, System.Globalization.CultureInfo.CurrentCulture);
        Assert.Contains($" · 48 h · cbofs {local} · hourly", timeline.RangeLabel, StringComparison.Ordinal);
        Assert.Equal(["cbofs 12:00Z"], timeline.Runs);
    }

    [Fact]
    public void Live_follows_the_clock_past_every_forecast_and_offers_to_check_for_new_runs()
    {
        var refresher = new FakeRefresher();
        var (service, timeline, clock) = Create(Run.AddHours(40), refresher);
        service.ApplySnapshot(RunSnapshot(Run, current: null));

        clock.Advance(TimeSpan.FromHours(11));

        // The view time is not clamped: Live sits past the data (#713 A3, A4).
        Assert.Equal(TimeMode.Live, service.Mode);
        Assert.Equal(Run.AddHours(51), service.CurrentTime);
        Assert.True(timeline.IsForecastEnded);
        Assert.Equal(1.0, timeline.NowPosition, 3);
        Assert.Equal(0.45, timeline.BandOpacity);
        Assert.Equal("Every forecast ended 3 h ago", timeline.StatusMessage);
        Assert.True(timeline.IsStatusError);
        Assert.Equal("Check for new runs", timeline.StatusActionText);

        timeline.StatusActionCommand!.Execute(null);
        Assert.Equal(1, refresher.Refreshes);
    }

    [Fact]
    public void Library_tiles_without_a_run_in_their_name_still_count_as_forecasts()
    {
        var (service, timeline, clock) = Create(Run.AddHours(40));
        var snapshot = RunSnapshot(Run, current: null);
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = snapshot.Minimum,
            Maximum = snapshot.Maximum,
            Current = snapshot.Current,
            Samples = snapshot.Samples,
            CoverageSegments = snapshot.CoverageSegments,
            Datasets = [new MapsuiMapTimedDataset("111US00_CBOFS_US4MD1DD", Run, Run.AddHours(48)) { ProductSpec = "S-111" }],
        });

        clock.Advance(TimeSpan.FromHours(11));

        Assert.True(timeline.IsForecastTimeline);
        Assert.Empty(timeline.Runs);
        Assert.Equal("Every forecast ended 3 h ago", timeline.StatusMessage);
        Assert.Contains(" · 48 h · hourly", timeline.RangeLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_forecast_sources_there_is_nothing_to_check()
    {
        var (service, timeline, clock) = Create(Run.AddHours(40));
        service.ApplySnapshot(RunSnapshot(Run, current: null));
        clock.Advance(TimeSpan.FromHours(11));

        Assert.True(timeline.HasStatusMessage);
        Assert.False(timeline.HasStatusAction);
    }

    [Fact]
    public void Picking_a_time_pins_it_and_Go_live_returns_to_now()
    {
        var (service, timeline, clock) = Create(Run.AddHours(9).AddMinutes(10));
        service.ApplySnapshot(RunSnapshot(Run, current: null));

        // The slider echoing its own position is not a choice of time.
        timeline.SliderValue = timeline.SliderValue;
        Assert.Equal(TimeMode.Live, service.Mode);

        timeline.PreviousStepCommand.Execute(null);
        Assert.Equal(TimeMode.Pinned, service.Mode);
        Assert.Equal("PINNED", timeline.ModeLabel);
        Assert.Equal(Run.AddHours(9), service.CurrentTime);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(Run.AddHours(9), service.CurrentTime);
        Assert.Equal("2 h 10 ago", timeline.OffsetText);
        Assert.EndsWith(" · 2 h 10 ago", timeline.StampText, StringComparison.Ordinal);

        Assert.True(timeline.NowCommand.CanExecute(null));
        timeline.NowCommand.Execute(null);
        Assert.Equal(TimeMode.Live, service.Mode);
        Assert.Equal(Run.AddHours(11).AddMinutes(10), service.CurrentTime);
    }

    [Fact]
    public void A_replaced_run_keeps_Live()
    {
        var (service, _, clock) = Create(Run.AddHours(9));
        service.ApplySnapshot(RunSnapshot(Run, current: null));
        clock.Advance(TimeSpan.FromHours(6));

        // The 18:00Z run arrives with the session's clock where it was.
        service.ApplySnapshot(RunSnapshot(Run.AddHours(6), current: Run.AddHours(6)));

        Assert.Equal(TimeMode.Live, service.Mode);
        Assert.Equal(Run.AddHours(15), service.CurrentTime);
    }

    [Fact]
    public void A_pinned_time_stays_when_new_data_does_not_cover_it_and_offers_the_nearest_data()
    {
        var (service, timeline, _) = Create(Run.AddHours(10));
        service.ApplySnapshot(RunSnapshot(Run, current: null));
        service.SetCurrentTime(Run.AddHours(1));

        // The 18:00Z run starts after the chosen 13:00Z; the pinned time stays (#713).
        service.ApplySnapshot(RunSnapshot(Run.AddHours(6), current: Run.AddHours(1)));

        Assert.Equal(TimeMode.Pinned, service.Mode);
        Assert.Equal(Run.AddHours(1), service.CurrentTime);
        Assert.Equal("No data at this time for 1 of 1 layers", timeline.StatusMessage);
        Assert.True(timeline.IsStatusWarning);
        Assert.Equal("Next data ›", timeline.StatusActionText);

        timeline.StatusActionCommand!.Execute(null);
        Assert.Equal(Run.AddHours(6), service.CurrentTime);
        Assert.False(timeline.HasStatusMessage);
    }

    [Fact]
    public void Every_timeline_shows_now_but_historical_data_starts_pinned()
    {
        var (service, timeline, _) = Create(Run.AddDays(30));

        service.ApplySnapshot(RunSnapshot(Run, current: null, name: "S104_tides_{0}x"));

        Assert.False(timeline.IsForecastTimeline);
        Assert.Equal(TimeMode.Pinned, service.Mode);
        Assert.Equal(Run, service.CurrentTime);
        Assert.True(timeline.IsNowInRange);
        Assert.Equal(1.0, timeline.NowPosition, 3);
        Assert.Equal("4 wk ago", timeline.OffsetText);
        Assert.True(timeline.NowCommand.CanExecute(null));
        Assert.DoesNotContain("cbofs", timeline.RangeLabel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "now")]
    [InlineData(30, "now")]
    [InlineData(11 * 3600 + 30 * 60, "in 11 h 30")]
    [InlineData(-5 * 3600, "5 h ago")]
    [InlineData(52 * 3600, "in 2 d 4 h")]
    [InlineData(-25 * 60, "25 min ago")]
    [InlineData(-289 * 24 * 3600, "9 mo ago")]
    public void Offsets_read_naturally(int seconds, string expected) =>
        Assert.Equal(expected, TimelineViewModel.Offset(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Steps_work_however_many_samples_there_are()
    {
        // 6-minute water levels over two days.
        var samples = Enumerable.Range(0, 480).Select(i => Run.AddMinutes(6 * i)).ToArray();
        var (service, timeline, _) = Create(Run.AddDays(30));
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[100],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets = [new MapsuiMapTimedDataset("104US00_levels", samples[0], samples[^1]) { Samples = samples }],
        });

        // The default 1 h step lands on whole hours.
        Assert.True(timeline.NextStepCommand.CanExecute(null));
        timeline.NextStepCommand.Execute(null);
        Assert.Equal(Run.AddHours(11), service.CurrentTime);

        // "Sample of" walks the 6-minute samples.
        timeline.SetStepCommand.Execute(nameof(TimelineStepKind.Sample));
        timeline.NextStepCommand.Execute(null);
        Assert.Equal(samples[111], service.CurrentTime);
        timeline.PreviousStepCommand.Execute(null);
        timeline.PreviousStepCommand.Execute(null);
        Assert.Equal(samples[109], service.CurrentTime);
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

    private sealed class FakeRefresher : IForecastRunRefresher
    {
        public int Refreshes { get; private set; }

        public bool HasForecastSources => true;

        public void RefreshForecastSources() => Refreshes++;
    }
}
