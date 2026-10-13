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
        Assert.Equal("Turn on Check passage on a route to see its clearance profile.", vm.EmptyText);

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

    [Fact]
    public async Task Every_readout_number_traces_to_get_passage_result()
    {
        var (f, vm) = await CheckedAsync();
        f.Check.SetCursor(Meters(0.11));

        var tool = new GetPassageResultTool(f.Catalog, new ViewerPassageRouteProvider(f.Routes, new InlineInvoker()));
        var result = await tool.InvokeAsync(new GetPassageResultRequest(IncludeSamples: true, MaxSamples: 10_000), TestContext.Current.CancellationToken);
        Assert.True(result.TryGetValue(out var value));
        var sample = value!.Samples!.Single(s => s.Index == vm.ReadoutSample!.Index);

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
    }

    [Fact]
    public async Task The_distance_and_time_toggle_keeps_the_cursor_on_the_same_sample()
    {
        var (f, vm) = await CheckedAsync();
        f.Check.SetCursor(Meters(0.07));
        var sample = vm.ReadoutSample!.Index;
        var nm = vm.CursorX!.Value;

        vm.Axis = ClearanceAxis.Time;
        Assert.Equal(sample, vm.ReadoutSample!.Index);
        Assert.InRange(vm.CursorX!.Value, 41, 43); // 0,07° ≈ 4,2 NM at 6 kn ≈ 42 min

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
        Assert.Equal("Low confidence", vm.StateText);

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
}
