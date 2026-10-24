using System.Globalization;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Clearance tab (#902): the profile, readout and Sources, on
/// <see cref="PassageCheckFixture"/>.
/// </summary>
public sealed class ClearanceTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public ClearanceTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static async Task<(PassageCheckFixture F, ClearanceViewModel Vm)> CheckedAsync(PassageCheckFixture? fixture = null)
    {
        var f = fixture ?? new PassageCheckFixture();
        var vm = new ClearanceViewModel(f.Check, timeFormat: new Utc());
        await f.CheckAsync();
        return (f, vm);
    }

    private static double Meters(double lon) => lon * PassageGeodesy.MetersPerDegreeLatitude;

    [Fact]
    public async Task The_profile_follows_check_mode()
    {
        var f = new PassageCheckFixture();
        var vm = new ClearanceViewModel(f.Check, timeFormat: new Utc());
        Assert.False(vm.HasProfile);
        Assert.Equal("Turn on Check route on a route with a draft to see its clearance profile.", vm.EmptyText);

        await f.CheckAsync();

        Assert.True(vm.HasProfile);
        Assert.Null(vm.EmptyText);
        Assert.Equal(9, vm.Series.Length); // band, two squeezes, two seabeds, water level, keel, min dot, waypoint numbers
        Assert.Equal(Enumerable.Range(1, 9), vm.WaypointNumbers.Select(n => n.Number));
        Assert.Equal(0, vm.WaypointNumbers[0].X, 6);
        Assert.True(vm.CanShowTime);

        f.Routes.IsCheckModeActive = false;
        Assert.False(vm.HasProfile);
    }

    [Fact]
    public async Task Elevations_are_shifted_so_the_floor_is_zero_and_labelled_from_chart_datum()
    {
        var (f, vm) = await CheckedAsync();
        var profile = ClearanceProfile.Build(f.Check.Current!, ClearanceAxis.Distance);

        // Deepest seabed 15 m + 0,3 m uncertainty: the floor is 20 m below CD.
        Assert.Equal(20, profile.Offset);
        Assert.Equal("CD", profile.ElevationLabel(20));
        Assert.Equal("-5", profile.ElevationLabel(15));
        Assert.Equal("-15", profile.ElevationLabel(5));

        var shoal = profile.SampleAtDistance(Meters(0.11))!;
        Assert.Equal(20 - 9, profile.Seabed(shoal)!.Value, 6);
        Assert.Equal(20 + 0.5, profile.WaterLevel(shoal)!.Value, 6);
        Assert.Equal(20 + 0.5 - 11, profile.Keel(shoal)!.Value, 6);
        Assert.Equal(20 - 9 + 1, profile.BandTop(shoal)!.Value, 6);

        // The below-the-setting squeeze fills the shoal; the min dot is there.
        var squeeze = (LineSeries<ObservablePoint>)vm.Series[2];
        Assert.Contains(squeeze.Values!, p => p.Y is not null);
        var dot = Assert.IsType<ScatterSeries<ObservablePoint>>(vm.Series[^2]);
        Assert.InRange(dot.Values!.Single().X!.Value, 0.105 * 60.04 - 0.1, 0.115 * 60.04 + 0.1);
    }

    [Theory]
    [InlineData(15f, 5, 20)] // The handoff fixture: 5 m ticks, as before.
    [InlineData(40f, 10, 50)]
    [InlineData(120f, 25, 125)]
    [InlineData(600f, 200, 800)]
    public async Task The_elevation_ticks_coarsen_with_depth_and_keep_cd_on_a_tick(float deep, double step, double offset)
    {
        // #1038: 5 m ticks in 120 m of water made 25 labels in a short dock.
        var (f, vm) = await CheckedAsync(new PassageCheckFixture(deepMeters: deep));
        var profile = ClearanceProfile.Build(f.Check.Current!, ClearanceAxis.Distance);

        Assert.Equal(step, profile.ElevationStep);
        Assert.Equal(offset, profile.Offset);
        Assert.Equal(0, profile.Offset % profile.ElevationStep); // CD is a tick.
        Assert.InRange(profile.Top / profile.ElevationStep, 1, ClearanceProfile.MaxElevationIntervals + 1);
        var y = Assert.IsType<Axis>(Assert.Single(vm.YAxes));
        Assert.Equal(step, y.MinStep);
        Assert.True(y.ForceStepToMin);
    }

    [Fact]
    public async Task The_time_axis_starts_at_midnight_where_times_are_shown()
    {
        var (f, _) = await CheckedAsync();
        var departure = f.Check.Current!.Result.Departure;

        var utc = ClearanceProfile.Build(f.Check.Current!, ClearanceAxis.Time, TimeFormat.Utc);
        Assert.Equal(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), utc.TimeAt(0));
        Assert.Equal(7 * 60 + 10, utc.XAtDistance(0), 6);

        var local = ClearanceProfile.Build(f.Check.Current!, ClearanceAxis.Time, TimeFormat.Local);
        Assert.Equal(TimeSpan.Zero, local.TimeAt(0).ToLocalTime().TimeOfDay);
        Assert.Equal(departure, local.TimeAt(local.XAtDistance(0)));
    }

    [Theory]
    [InlineData(6, 300, 30)] // The handoff fixture: about 1,6 h at 6 kn, half-hour ticks.
    [InlineData(0.2, 300, 1440)] // About two days at 0,2 kn.
    [InlineData(0.2, 600, 720)]
    [InlineData(0.2, 1200, 360)]
    public async Task The_time_ticks_follow_the_passage_length_and_plot_width(double knots, double plotWidth, double step)
    {
        // #1038: 30-minute ticks on a two-day passage made 96 labels.
        var fixture = new PassageCheckFixture(waterEnd: PassageCheckFixture.WaterStart.AddHours(60));
        fixture.Route.SetPassageValue(Viewer.Routing.RoutePassageField.Speed, knots);
        var (_, vm) = await CheckedAsync(fixture);
        vm.SetPlotWidth(plotWidth);
        vm.Axis = ClearanceAxis.Time;

        var x = Assert.IsType<Axis>(Assert.Single(vm.XAxes));
        Assert.Equal(step, x.MinStep);
        Assert.True(x.ForceStepToMin);

        // Every label gets its width and a gap.
        var (from, to) = (x.MinLimit!.Value, x.MaxLimit!.Value);
        var labelWidth = Math.Max(x.Labeler(from).Length, x.Labeler(to).Length) * 6;
        Assert.True(Math.Floor((to - from) / step) * (labelWidth + 16) <= plotWidth, $"{(to - from) / step:0.#} labels at {step} min");

        // Ticks fall on the clock, not on multiples of the step after the 07:10Z departure.
        for (var tick = Math.Ceiling(from / step) * step; tick <= to; tick += step)
        {
            var label = x.Labeler(tick);
            var clock = TimeOnly.ParseExact(label[^6..^1], "HH:mm", CultureInfo.InvariantCulture);
            Assert.True(clock.ToTimeSpan().TotalMinutes % Math.Min(step, 1440) == 0, $"Tick '{label}' is off the {step}-minute clock.");
        }

        // A wider plot after the fact refines the ticks without a new check.
        vm.SetPlotWidth(plotWidth * 4);
        Assert.True(Assert.IsType<Axis>(Assert.Single(vm.XAxes)).MinStep <= step);
    }

    [Fact]
    public async Task Every_readout_number_traces_to_check_route()
    {
        var (f, vm) = await CheckedAsync();
        f.Check.SetCursor(Meters(0.11));

        var tool = new CheckRouteTool(f.Catalog, new ViewerPassageRouteProvider(f.Routes, new InlineInvoker()));
        var result = await tool.InvokeAsync(new CheckRouteRequest(IncludeSamples: true, MaxSamples: 10_000), TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value));
        var sample = value!.Clearance!.Samples!.Single(s => s.Index == vm.ReadoutSample!.Index);

        Assert.Equal($"{sample.ChartedDepthMeters:0.0} m", vm.DepthText);
        Assert.Equal($"S-102 · {sample.DepthDataset} ({sample.DepthReference}) · ±{sample.UncertaintyMeters:0.0} m", vm.DepthSource);
        Assert.Equal($"{sample.WaterLevelMeters:0.0} m", vm.WaterLevelText);
        Assert.StartsWith("S-104 · ", vm.WaterLevelSource);
        Assert.Contains(sample.TimeStep!.Value.ToString("HH:mm", CultureInfo.InvariantCulture), vm.WaterLevelSource);
        Assert.Equal($"{sample.UkcMeters:0.0} m", vm.UkcText);
        Assert.Equal("Below your UKC setting", vm.StateText);
        Assert.Equal(CheckTone.Destructive, vm.Tone);
        Assert.Equal("11.0 m", vm.DraftText);
        Assert.StartsWith("Leg 6 → 7", vm.ReadoutLeg);
        Assert.Matches(@"^6\.\d NM · 08:1\dZ$", vm.ReadoutPosition);
        Assert.Matches(@"^6\.\d NM$", vm.ReadoutDistance);
        Assert.Matches(@"^08:1\dZ$", vm.ReadoutEta);
    }

    [Fact]
    public async Task The_distance_and_time_toggle_keeps_the_cursor_on_the_same_sample()
    {
        var (f, vm) = await CheckedAsync();
        f.Check.SetCursor(Meters(0.0705)); // Mid-sample: a boundary rounds either way through minutes.
        var sample = vm.ReadoutSample!.Index;
        var nm = vm.CursorX!.Value;

        vm.AxisIndex = 1;
        Assert.Equal(ClearanceAxis.Time, vm.Axis);
        Assert.Equal(sample, vm.ReadoutSample!.Index);
        Assert.InRange(vm.CursorX!.Value, 430 + 41, 430 + 43); // 0,07° ≈ 4,2 NM at 6 kn ≈ 42 min after 07:10Z

        // Hovering at that time lands on the same sample.
        vm.SetCursorAtX(vm.CursorX!.Value);
        Assert.Equal(sample, vm.ReadoutSample!.Index);

        vm.Axis = ClearanceAxis.Distance;
        Assert.Equal(sample, vm.ReadoutSample!.Index);
        Assert.Equal(nm, vm.CursorX!.Value, 6);
    }

    [Fact]
    public async Task An_s101_fallback_stretch_is_hatched_and_lower_confidence()
    {
        var (f, vm) = await CheckedAsync(new PassageCheckFixture(s101FallbackUntilLon: 0.03));
        f.Check.SetCursor(Meters(0.01));

        Assert.Equal("S-101 DepthArea · lower confidence", vm.DepthSource);
        Assert.Equal("13.0 m", vm.DepthText);
        // The band is kept, qualified by confidence (#939).
        Assert.Equal("Meets your settings · lower confidence", vm.StateText);
        Assert.True(vm.IsLowConfidence);
        Assert.Equal(CheckTone.Success, vm.Tone);

        var s102 = (LineSeries<ObservablePoint>)vm.Series[3];
        var s101 = (LineSeries<ObservablePoint>)vm.Series[4];
        Assert.IsType<LinearGradientPaint>(s101.Fill);
        var nm = 0.01 * 60.04;
        Assert.Contains(s101.Values!, p => p.X < nm && p.Y is not null);
        Assert.DoesNotContain(s102.Values!, p => p.X < nm && p.Y is not null);

        var fallback = vm.SourceGroups.Single(g => g.Title == "S-101 fallback");
        Assert.Equal(["101FIXTURE", "0.0–1.8 NM"], fallback.Items);
    }

    [Fact]
    public async Task Sources_list_cells_water_level_uncertainty_datums_and_gaps()
    {
        var (_, vm) = await CheckedAsync(new PassageCheckFixture(waterEnd: PassageCheckFixture.WaterStart.AddHours(2)));

        var groups = vm.SourceGroups.ToDictionary(g => g.Title, g => g.Items);
        Assert.Equal(["102FIXTURE"], groups["S-102 cells"]);
        Assert.Equal(["None"], groups["S-101 fallback"]);
        Assert.Equal(["104FIXTURE · grid"], groups["S-104 water level"]);
        Assert.Equal(["Up to ±0.3 m"], groups["S-102 uncertainty"]);
        Assert.Equal(["Charted depth: Lowest Astronomical Tide · water level: Lowest Astronomical Tide"], groups["Vertical datums"]);
        Assert.EndsWith("· After S-104 ends", Assert.Single(groups["Gaps"]));
    }

    [Fact]
    public async Task The_clearance_mcp_tools_read_and_set_the_view()
    {
        var (f, vm) = await CheckedAsync();
        var tools = new ClearanceTools(vm, new InlineInvoker());
        f.Check.SetCursor(Meters(0.11));

        var state = await tools.SetAsync(new SetClearanceViewRequest("time", true), TestContext.Current.CancellationToken);
        Assert.True(state.TryGetValue(out var dto));
        Assert.Equal("time", dto!.Axis);
        Assert.True(dto.SourcesOpen);
        Assert.True(vm.IsSourcesOpen);
        Assert.Equal(vm.ReadoutSample!.Index, dto.ReadoutSample);
        Assert.Equal(vm.DepthSource, dto.DepthSource);
        Assert.Contains(dto.Sources, g => g.Title == "S-102 cells");

        var bad = await tools.SetAsync(new SetClearanceViewRequest("sideways"), TestContext.Current.CancellationToken);
        Assert.False(bad.TryGetValue(out _));
    }

    [Fact]
    public async Task Get_clearance_state_reports_the_band_and_confidence_separately()
    {
        // #939: no S-102 datum, so the readout is lower confidence but keeps its band.
        var (f, vm) = await CheckedAsync(new PassageCheckFixture(depthDatum: null));
        var tools = new ClearanceTools(vm, new InlineInvoker());
        f.Check.SetCursor(Meters(0.01));

        var state = await tools.GetAsync(TestContext.Current.CancellationToken);
        Assert.True(state.TryGetValue(out var dto));
        Assert.Equal("meets_settings", dto!.Band);
        Assert.Equal(["datum_unconfirmed"], dto.Confidence);
        Assert.Equal("Meets your settings · lower confidence", dto.State);

        f.Check.SetCursor(Meters(0.11));
        Assert.True((await tools.GetAsync(TestContext.Current.CancellationToken)).TryGetValue(out var shoal));
        Assert.Equal("below_setting", shoal!.Band);
        Assert.Equal(["datum_unconfirmed"], shoal.Confidence);
    }
}
