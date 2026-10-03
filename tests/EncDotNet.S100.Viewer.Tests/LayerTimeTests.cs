using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Layer time, loading and "no data" opening the layer list (#709, handoff D2–D4, B3).</summary>
public sealed class LayerTimeTests
{
    private static readonly DateTime Run = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>An hourly cbofs run named with its run time (nearest sample within an hour).</summary>
    private static MapsuiMapTimedDataset Cbofs(int hours = 49) => new("111US00_CBOFS_20261002T00Z_US4MD1DD", Run, Run.AddHours(hours - 1))
    {
        DatasetId = "cbofs",
        ProductSpec = "S-111",
        Samples = [.. Enumerable.Range(0, hours).Select(h => Run.AddHours(h))],
        Selection = MapsuiTimeSelectionKind.Nearest,
        Tolerance = TimeSpan.FromHours(1),
        Coverage = [new MapsuiMapTimeSegment(Run.AddHours(-1), Run.AddHours(hours))],
    };

    /// <summary>A six-minute water-level station (sample at or before, held six minutes).</summary>
    private static MapsuiMapTimedDataset Baltimore(DateTime start, int count) => new("104US00_BALTIMORE", start, start.AddMinutes(6 * (count - 1)))
    {
        DatasetId = "baltimore",
        ProductSpec = "S-104",
        Samples = [.. Enumerable.Range(0, count).Select(i => start.AddMinutes(6 * i))],
        Selection = MapsuiTimeSelectionKind.AtOrBefore,
        Tolerance = TimeSpan.FromMinutes(6),
        Coverage = [new MapsuiMapTimeSegment(start, start.AddMinutes(6 * count))],
    };

    // ── forecast runs (#720) ───────────────────────────────────────────

    /// <summary>A Library tile: its name carries the model and tile but not the run; the data says when it was issued.</summary>
    private static MapsuiMapTimedDataset LibraryTile() => Cbofs() with
    {
        Name = "111US00_CBOFS_US4MD1DD",
        IssueTime = Run.AddMinutes(105),
    };

    [Fact]
    public void A_run_in_the_name_wins_over_the_issue_time()
    {
        var run = ForecastRunNames.RunOf(Cbofs() with { IssueTime = Run.AddMinutes(105) });

        Assert.Equal(new ForecastRun("cbofs", Run, FromIssueTime: false), run);
    }

    [Fact]
    public void A_Library_tile_takes_its_run_from_the_Library_else_its_issue_time()
    {
        var tile = LibraryTile();
        var loaded = new LibraryTimedEntry("feed:cbofs/111US00_CBOFS_US4MD1DD", "111US00_CBOFS_US4MD1DD", "S-111", Run, Run.AddHours(48), LibraryTimedState.Loaded) { Run = Run };

        Assert.Equal("cbofs 00:00Z", ForecastRunNames.Describe(tile, [loaded]));
        Assert.Equal(new ForecastRun("cbofs", Run.AddMinutes(105), FromIssueTime: true), ForecastRunNames.RunOf(tile));
        Assert.Equal("cbofs 01:45Z", ForecastRunNames.Describe(tile));

        // Forecast hours count from the run, never from the later issue time.
        Assert.Equal("08:00Z · T+8 h", LayerTimes.Describe(tile, Run.AddHours(8), false, TimeFormat.Utc, TimeZoneInfo.Utc, ForecastRunNames.RunOf(tile, [loaded])).Text);
        Assert.Equal("08:00Z", LayerTimes.Describe(tile, Run.AddHours(8), false, TimeFormat.Utc, TimeZoneInfo.Utc, ForecastRunNames.RunOf(tile)).Text);
    }

    [Fact]
    public void An_observation_station_is_no_run_whatever_its_issue_time()
    {
        var station = Baltimore(Run, 10) with { IssueTime = Run.AddHours(1) };

        Assert.Null(ForecastRunNames.RunOf(station));
    }

    [Fact]
    public void The_timeline_lists_a_Library_tiles_run()
    {
        var service = new GlobalTimeService();
        var library = new StaticLibrary(new LibraryTimedEntry("feed:cbofs/111US00_CBOFS_US4MD1DD", "111US00_CBOFS_US4MD1DD", "S-111", Run, Run.AddHours(48), LibraryTimedState.Loaded) { Run = Run });
        var timeline = new TimelineViewModel(service, null, new FakeTimeProvider(new DateTimeOffset(Run.AddHours(2))), action => action(), library: library);
        var tile = LibraryTile();
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = tile.First,
            Maximum = tile.Last,
            Current = tile.First,
            Samples = tile.Samples,
            CoverageSegments = [.. tile.Coverage],
            Datasets = [tile],
        });

        Assert.Equal(["cbofs 00:00Z"], timeline.Runs);
        Assert.Contains("cbofs 00:00Z", timeline.RangeLabel, StringComparison.Ordinal);
    }

    private sealed class StaticLibrary(params LibraryTimedEntry[] entries) : ILibraryTimeSource
    {
        public IReadOnlyList<LibraryTimedEntry> Entries => entries;

        public event Action? Changed { add { } remove { } }

        public event Action? ProgressChanged { add { } remove { } }

        public double? ProgressOf(LibraryTimedEntry entry) => null;

        public Task GetAsync(IReadOnlyList<LibraryTimedEntry> entries) => Task.CompletedTask;

        public Task LoadAsync(IReadOnlyList<LibraryTimedEntry> entries) => Task.CompletedTask;

        public void Reveal(LibraryTimedEntry entry)
        {
        }
    }

    private static LayerTime Describe(MapsuiMapTimedDataset dataset, DateTime at, bool drawing = false) =>
        LayerTimes.Describe(dataset, at, drawing, TimeFormat.Utc, TimeZoneInfo.Utc);

    [Fact]
    public void A_forecast_drawing_the_view_time_shows_its_forecast_hour()
    {
        var time = Describe(Cbofs(), Run.AddHours(8));

        Assert.Equal("08:00Z · T+8 h", time.Text);
        Assert.Equal(LayerTimeState.Exact, time.State);
        Assert.False(time.IsHidden);
    }

    [Fact]
    public void A_sample_within_tolerance_shows_its_offset()
    {
        var station = Baltimore(Run.AddHours(20), 10);

        var time = Describe(station, Run.AddHours(20).AddMinutes(30).AddMinutes(-6).AddMinutes(4));

        Assert.Equal("20:24Z (−4 min)", time.Text);
        Assert.Equal(LayerTimeState.Offset, time.State);
    }

    [Fact]
    public void No_data_says_where_the_nearest_data_is_and_how_far()
    {
        var earlier = Describe(Cbofs(19), Run.AddHours(24));
        Assert.Equal("no data · last 18:00Z, 6 h earlier", earlier.Text);
        Assert.True(earlier.IsHidden);
        Assert.Equal(Run.AddHours(18), earlier.Nearest);

        // More than 20 h away: the date as well.
        var later = Describe(Cbofs(), Run.AddDays(-42));
        Assert.StartsWith("no data · next ", later.Text, StringComparison.Ordinal);
        Assert.EndsWith(" 00:00Z, 6 wk later", later.Text, StringComparison.Ordinal);
        Assert.Equal(Run, later.Nearest);
    }

    [Fact]
    public void Drawing_shows_while_the_layer_draws()
    {
        var time = Describe(Cbofs(), Run.AddHours(8), drawing: true);

        Assert.Equal("drawing…", time.Text);
        Assert.Equal(LayerTimeState.Drawing, time.State);
    }

    [Fact]
    public void Progress_counts_layers_and_ignores_a_superseded_refresh()
    {
        var progress = new TimeRefreshProgress();

        var first = progress.Begin(Run.AddHours(1));
        progress.Started("cbofs");
        var second = progress.Begin(Run.AddHours(2));
        progress.Started("cbofs");
        progress.Started("baltimore");
        progress.Finished("cbofs");

        Assert.True(progress.IsDrawing);
        Assert.Equal((1, 2), (progress.Ready, progress.Total));
        Assert.True(progress.IsDatasetDrawing("baltimore"));

        progress.End(first);
        Assert.True(progress.IsDrawing);
        progress.End(second);
        Assert.False(progress.IsDrawing);
        Assert.Equal(Run.AddHours(2), progress.Drawn);
    }

    private static (GlobalTimeService Service, TimelineViewModel Timeline, TimeRefreshProgress Progress) Scenario4()
    {
        // Pinned 3.5 h ahead: cbofs draws, the Baltimore station (observations up to now) has nothing.
        var service = new GlobalTimeService();
        var progress = new TimeRefreshProgress();
        var now = Run.AddHours(10);
        var timeline = new TimelineViewModel(service, null, new FakeTimeProvider(new DateTimeOffset(now)), action => action(), null, progress);
        var cbofs = Cbofs();
        var baltimore = Baltimore(Run, 100);
        var samples = cbofs.Samples.Concat(baltimore.Samples).Distinct().Order().ToArray();
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(Run.AddHours(-1), Run.AddHours(49))],
            Datasets = [cbofs, baltimore],
        });
        service.SetCurrentTime(now.AddHours(3.5));
        return (service, timeline, progress);
    }

    [Fact]
    public void Scenario_4_counts_the_hidden_layer_and_opens_the_list_on_it()
    {
        var (_, timeline, _) = Scenario4();
        string? shown = null;
        timeline.ShowLayerRequested += id => shown = id;

        Assert.Equal("No data at this time for 1 of 2 layers", timeline.StatusMessage);
        Assert.True(timeline.ShowLayersCommand.CanExecute(null));
        timeline.ShowLayersCommand.Execute(null);
        Assert.Equal("baltimore", shown);
    }

    [Fact]
    public void Loading_comes_first_and_Cancel_returns_to_what_is_drawn()
    {
        var (service, timeline, progress) = Scenario4();
        var drawn = service.CurrentTime!.Value;
        progress.End(progress.Begin(drawn));

        service.SetCurrentTime(drawn.AddHours(2));
        progress.Begin(drawn.AddHours(2));
        progress.Started("cbofs");
        progress.Started("baltimore");
        progress.Finished("cbofs");

        Assert.True(timeline.IsDrawing);
        Assert.StartsWith("Drawing ", timeline.StatusMessage, StringComparison.Ordinal);
        Assert.EndsWith(" · 1 of 2 layers ready", timeline.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("Cancel", timeline.StatusActionText);
        Assert.Contains(" · drawing ", timeline.StampText, StringComparison.Ordinal);

        timeline.StatusActionCommand!.Execute(null);
        Assert.Equal(drawn, service.CurrentTime);
    }

    [Fact]
    public void The_coordinator_puts_layer_times_on_rows_and_a_click_jumps_to_the_data()
    {
        var (service, _, progress) = Scenario4();
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        var cbofsRow = new DatasetEntry("/data/cbofs.h5", "S-111");
        var baltimoreRow = new DatasetEntry("/data/baltimore.h5", "S-104");
        datasets.Entries.Add(cbofsRow);
        datasets.Entries.Add(baltimoreRow);
        // Match the rows to the timed datasets by their session ids.
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = service.MinTime,
            Maximum = service.MaxTime,
            Current = service.CurrentTime,
            Samples = service.AllSamples,
            CoverageSegments = [.. service.CoverageSegments.Select(c => new MapsuiMapTimeSegment(c.Start, c.End))],
            Datasets = [.. service.TimedDatasets.Select(d => d with { DatasetId = d.DatasetId == "cbofs" ? cbofsRow.Id.Value : baltimoreRow.Id.Value })],
        });

        _ = new LayerTimeCoordinator(service, datasets, progress, new FakeTimeProvider(), new FixedFormat(), action => action());

        Assert.Equal("13:00Z (−30 min)", cbofsRow.LayerTimeText);
        Assert.False(cbofsRow.IsLayerHidden);
        Assert.True(baltimoreRow.IsLayerHidden);
        Assert.StartsWith("no data · last 09:54Z", baltimoreRow.LayerTimeText, StringComparison.Ordinal);

        baltimoreRow.JumpToLayerDataCommand.Execute(null);
        Assert.Equal(Run.AddHours(9).AddMinutes(54), service.CurrentTime);
        Assert.False(baltimoreRow.IsLayerHidden);
    }

    private sealed class FixedFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
