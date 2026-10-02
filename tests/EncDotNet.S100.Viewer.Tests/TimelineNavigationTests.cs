using System.Globalization;
using Avalonia.Input;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The Timeline's navigation: steps, data jumps, labels, zoom, presets and keys (#708).</summary>
public sealed class TimelineNavigationTests
{
    private static readonly DateTime T0 = new(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc);

    private static MapsuiMapTimedDataset Dataset(string name, string spec, IReadOnlyList<DateTime> samples) =>
        new(name, samples[0], samples[^1])
        {
            ProductSpec = spec,
            Samples = samples,
            Coverage = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
        };

    private static DateTime[] Hourly(DateTime start, int count, int every = 1) =>
        [.. Enumerable.Range(0, count).Select(h => start.AddHours(h * every))];

    /// <summary>Four week-long Rotterdam-like clusters separated by long gaps.</summary>
    private static (GlobalTimeService Service, TimelineViewModel Timeline, FakeTimeProvider Clock, MapsuiMapTimedDataset[] Runs) Rotterdam(DateTime now)
    {
        var week = TimeSpan.FromDays(7);
        var runs = new[] { T0, T0 + 7 * week, T0 + 16 * week, T0 + 27 * week }
            .Select((s, i) => Dataset($"111NL00RTM0{i + 1}", "S-111", Hourly(s, 168)))
            .ToArray();
        var samples = runs.SelectMany(r => r.Samples).Order().ToArray();
        var service = new GlobalTimeService();
        var clock = new FakeTimeProvider(new DateTimeOffset(now));
        var timeline = new TimelineViewModel(service, null, clock, action => action());
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = [.. runs.Select(r => new MapsuiMapTimeSegment(r.First, r.Last))],
            Datasets = runs,
        });
        return (service, timeline, clock, runs);
    }

    [Fact]
    public void Rotterdam_shows_every_cluster_and_the_gap_to_now_with_labels()
    {
        var (_, timeline, _, _) = Rotterdam(T0.AddDays(240));

        Assert.Equal(4, timeline.CoverageBands.Count);
        // Three gaps between the clusters and one up to now.
        Assert.Equal(4, timeline.Gaps.Count);
        var gapLabels = timeline.AxisLabels.Where(l => l.Kind == AxisLabelKind.Gap).Select(l => l.Text).ToArray();
        Assert.Equal(4, gapLabels.Length);
        Assert.All(gapLabels, l => Assert.Matches(@"^⋯ \d+ (wk|mo) ⋯$", l));
        Assert.All(timeline.Gaps, g => Assert.Contains("with no data", g.Tooltip, StringComparison.Ordinal));
    }

    [Fact]
    public void Option_arrows_jump_from_cluster_to_cluster()
    {
        var (service, timeline, _, runs) = Rotterdam(T0.AddDays(240));

        Assert.True(timeline.HandleKey(Key.Right, KeyModifiers.Alt));
        Assert.Equal(runs[1].First, service.CurrentTime);
        timeline.NextDataCommand.Execute(null);
        Assert.Equal(runs[2].First, service.CurrentTime);
        Assert.True(timeline.HandleKey(Key.Left, KeyModifiers.Alt));
        Assert.Equal(runs[1].First, service.CurrentTime);
        Assert.Equal(TimeMode.Pinned, service.Mode);
    }

    [Fact]
    public void A_data_jump_lands_on_the_first_sample_not_the_tolerance_before_it()
    {
        var coverage = new[] { new CoverageSegment(T0.AddHours(-1), T0.AddHours(10)), new CoverageSegment(T0.AddDays(9), T0.AddDays(10)) };
        var samples = new[] { T0, T0.AddHours(9), T0.AddDays(9).AddHours(1) };

        Assert.Equal(T0.AddDays(9).AddHours(1), TimelineStepper.DataStep(coverage, T0.AddHours(3), +1, samples));
        Assert.Equal(T0, TimelineStepper.DataStep(coverage, T0.AddDays(9).AddHours(5), -1, samples));
    }

    [Fact]
    public void The_default_driver_is_the_coarsest_forecast()
    {
        var hourly = Dataset("111US00_CBOFS_20260112T00Z_A", "S-111", Hourly(T0, 49));
        var sixMinute = Dataset("104US00_station", "S-104", [.. Enumerable.Range(0, 480).Select(i => T0.AddMinutes(6 * i))]);
        var threeHourly = Dataset("111US00_RTOFS_EAST_20260112T00Z_B", "S-111", Hourly(T0, 17, every: 3));

        var driver = TimelineStepper.DefaultDriver([hourly, sixMinute, threeHourly], d => d.ProductSpec == "S-111");

        Assert.Same(threeHourly, driver);
    }

    [Theory]
    [InlineData("Hour", +1, "2026-01-12T03:00:00Z")]
    [InlineData("Hour", -1, "2026-01-12T02:00:00Z")]
    [InlineData("TenMinutes", +1, "2026-01-12T02:40:00Z")]
    [InlineData("SixHours", +1, "2026-01-12T06:00:00Z")]
    [InlineData("Day", +1, "2026-01-13T00:00:00Z")]
    [InlineData("Day", -1, "2026-01-12T00:00:00Z")]
    public void Fixed_steps_land_on_whole_units(string kind, int direction, string expected)
    {
        var at = T0.AddHours(2).AddMinutes(30);
        var target = TimelineStepper.Step(Enum.Parse<TimelineStepKind>(kind), at, direction, [], [], null, (T0.AddDays(-5), T0.AddDays(5)), TimeZoneInfo.Utc);

        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal), target);
    }

    [Fact]
    public void Shift_steps_coarser_and_steps_stay_within_the_window()
    {
        Assert.Equal(TimelineStepKind.SixHours, TimelineStepper.Coarse(TimelineStepKind.Hour));
        Assert.Equal(TimelineStepKind.Day, TimelineStepper.Coarse(TimelineStepKind.Day));
        Assert.Null(TimelineStepper.Step(TimelineStepKind.Hour, T0, +1, [], [], null, (T0, T0.AddMinutes(30)), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Day_labels_use_the_cultures_day_and_month_without_the_year()
    {
        Assert.Equal("dd.MM", TimelineAxisLabels.DayMonthPattern(CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("M/d", TimelineAxisLabels.DayMonthPattern(CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("MM-dd", TimelineAxisLabels.DayMonthPattern(CultureInfo.GetCultureInfo("sv-SE")));
    }

    [Theory]
    [InlineData(5, "5 h")]
    [InlineData(72, "3 d")]
    [InlineData(42 * 24, "6 wk")]
    [InlineData(90 * 24, "3 mo")]
    public void Gap_lengths_read_naturally(int hours, string expected) =>
        Assert.Equal(expected, TimelineAxisLabels.GapLength(TimeSpan.FromHours(hours), CultureInfo.InvariantCulture));

    [Fact]
    public void Labels_never_crowd_each_other()
    {
        var axis = new TimelineAxisMap(T0, T0.AddDays(10), [new CoverageSegment(T0, T0.AddDays(10))]);

        var labels = TimelineAxisLabels.Layout(axis, TimeZoneInfo.Utc, CultureInfo.InvariantCulture);

        Assert.NotEmpty(labels);
        Assert.All(labels.Zip(labels.Skip(1)), p => Assert.True(p.Second.Position - p.First.Position >= TimelineAxisLabels.HourSpacing - 1e-9));
    }

    [Fact]
    public void Presets_zoom_and_All_loaded_resets()
    {
        var now = T0.AddHours(20);
        var run = Dataset("111US00_CBOFS_20260112T00Z_A", "S-111", Hourly(T0, 49));
        var service = new GlobalTimeService();
        var timeline = new TimelineViewModel(service, null, new FakeTimeProvider(new DateTimeOffset(now)), action => action());
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = run.First,
            Maximum = run.Last,
            Current = run.First,
            Samples = run.Samples,
            CoverageSegments = run.Coverage,
            Datasets = [run],
        });

        timeline.ApplyPresetCommand.Execute(nameof(TimelinePreset.NowSixHours));
        Assert.True(timeline.IsZoomed);
        Assert.Equal("Now ± 6 h", timeline.PresetLabel);
        Assert.InRange(timeline.OverviewWindowWidth, 0.2, 0.3);

        timeline.ZoomInCommand.Execute(null);
        Assert.Equal("Custom", timeline.PresetLabel);

        Assert.True(timeline.HandleKey(Key.D0, KeyModifiers.None));
        Assert.False(timeline.IsZoomed);
        Assert.Equal("All loaded", timeline.PresetLabel);
        Assert.Equal(1, timeline.OverviewWindowWidth, 6);
    }

    [Fact]
    public void Keys_with_Ctrl_or_Cmd_are_left_alone_and_N_goes_live()
    {
        var (service, timeline, _, _) = Rotterdam(T0.AddDays(240));

        Assert.False(timeline.HandleKey(Key.Right, KeyModifiers.Control));
        Assert.False(timeline.HandleKey(Key.D0, KeyModifiers.Meta));
        Assert.True(timeline.HandleKey(Key.N, KeyModifiers.None));
        Assert.Equal(TimeMode.Live, service.Mode);
    }
}
