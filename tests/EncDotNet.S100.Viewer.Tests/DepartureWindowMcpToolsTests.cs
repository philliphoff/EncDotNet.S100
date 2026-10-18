using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The departure-sweep MCP surface (#903): <c>set_departure</c>, the window
/// in <c>get_check_state</c> and the band in <c>get_clearance_state</c>.
/// </summary>
public sealed class DepartureWindowMcpToolsTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public DepartureWindowMcpToolsTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    private static DateTimeOffset At(int hour, int minute) => new(2024, 6, 1, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_check_state_reports_the_window_and_set_departure_jumps_to_the_nearest_meeting_departure()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = new PassageCheckFixture(tide: PassageCheckFixture.HighWaterUntil0750);
        var invoker = new InlineInvoker();
        Value(await new SetCheckModeTool(f.Routes, invoker, f.Check).InvokeAsync(new SetCheckModeRequest(true), ct));

        var state = Value(await new GetCheckStateTool(f.Routes, invoker, f.Check).InvokeAsync(true, ct));
        Assert.Equal(PassageCheckFixture.Departure, state.Departure);
        Assert.False(state.DepartureIsViewTime);
        Assert.False(state.WindowComputing);
        var window = Assert.IsType<DepartureWindowDto>(state.Window);
        Assert.Equal(At(6, 0), window.From);
        Assert.Equal(At(12, 0), window.To);
        Assert.Equal(5, window.StepMinutes);
        Assert.Equal([new DepartureRangeDto(At(6, 0), At(6, 40))], window.Windows);
        Assert.Equal(new DepartureRangeDto(At(10, 25), At(12, 0)), window.ArrivalAfterDataEnds);
        Assert.False(window.InsideWindow);
        Assert.Equal(At(6, 40), window.NearestMeetingDeparture);
        Assert.Equal("Departures 06:00Z–06:40Z meet your settings.", window.Text);
        Assert.Equal("Depart 06:40Z", window.DepartButton);

        var setDeparture = new SetDepartureTool(f.Routes, invoker, f.Check);
        var moved = Value(await setDeparture.InvokeAsync(new SetDepartureRequest(NearestMeeting: true), ct));
        Assert.Equal(At(6, 40), moved.Departure);
        Assert.Equal("meets_settings", moved.Verdict);
        Assert.True(moved.Window!.InsideWindow);
        Assert.Null(moved.Window.DepartButton);
        Assert.StartsWith("This departure is inside the window.", moved.Window.Text);

        var explicitly = Value(await setDeparture.InvokeAsync(new SetDepartureRequest(At(9, 0)), ct));
        Assert.Equal(At(9, 0), explicitly.Departure);
        Assert.Equal("below_setting", explicitly.Verdict);
        Assert.Equal(At(6, 40), explicitly.Window!.NearestMeetingDeparture);

        // Each set is one undo step.
        Assert.True(f.Route.History.Undo());
        Assert.Equal(At(6, 40), f.Route.Passage.Departure);
    }

    [Fact]
    public async Task Set_departure_validates_its_arguments()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = new PassageCheckFixture(tide: _ => 0.5f);
        var tool = new SetDepartureTool(f.Routes, new InlineInvoker(), f.Check);

        Assert.True((await tool.InvokeAsync(new SetDepartureRequest(), ct)).TryGetError(out var neither));
        Assert.IsType<InvalidArgument>(neither);
        Assert.True((await tool.InvokeAsync(new SetDepartureRequest(At(6, 0), NearestMeeting: true), ct)).TryGetError(out _));

        // Check route is off: there is no window to jump into.
        Assert.True((await tool.InvokeAsync(new SetDepartureRequest(NearestMeeting: true), ct)).TryGetError(out var off));
        Assert.Contains("set_check_mode", off.Message, StringComparison.Ordinal);

        // Nothing meets with 0,5 m of tide over the 9 m shoal.
        await f.CheckAsync();
        Assert.True((await tool.InvokeAsync(new SetDepartureRequest(NearestMeeting: true), ct)).TryGetError(out var none));
        Assert.Contains("no departure", none.Message, StringComparison.Ordinal);

        // An explicit departure works with check mode off too.
        f.Routes.IsCheckModeActive = false;
        Value(await tool.InvokeAsync(new SetDepartureRequest(At(8, 0)), ct));
        Assert.Equal(At(8, 0), f.Route.Passage.Departure);
    }

    [Fact]
    public async Task Get_clearance_state_reports_the_departure_band()
    {
        var f = new PassageCheckFixture(tide: PassageCheckFixture.HighWaterUntil0750);
        var vm = new ClearanceViewModel(f.Check, timeFormat: new UtcTime());
        var tools = new ClearanceTools(vm, new InlineInvoker());
        Assert.Null(Value(await tools.GetAsync(TestContext.Current.CancellationToken)).DepartureBand);

        await f.CheckAsync();
        var band = Value(await tools.GetAsync(TestContext.Current.CancellationToken)).DepartureBand;

        Assert.NotNull(band);
        Assert.Equal("07:10Z", band.Departure);
        Assert.Equal("06:00Z", band.StartLabel);
        Assert.Equal("12:00Z · S-104 ends", band.EndLabel);
        Assert.False(band.Stale);
        Assert.Equal(["meets_settings", "below_setting", "arrival_after_data_ends"], band.Segments.Select(s => s.Kind));
        Assert.Equal(At(6, 40), band.Segments[0].Last);
        Assert.Equal(At(10, 25), band.Segments[2].First);
        Assert.InRange(band.KnobFraction!.Value, band.Segments[1].Start, band.Segments[1].End);
    }

    [Fact]
    public void Set_departure_adapter_exposes_its_parameters()
    {
        var f = new PassageCheckFixture();
        var tool = PassageCheckMcpAdapters.Create(new SetDepartureTool(f.Routes, new InlineInvoker(), f.Check));
        Assert.Equal("set_departure", tool.ProtocolTool.Name);
        var props = tool.ProtocolTool.InputSchema.GetProperty("properties");
        foreach (var name in new[] { "departure", "nearestMeeting", "routeId", "wait" })
            Assert.True(props.TryGetProperty(name, out _), $"set_departure is missing {name}");
    }

    [Fact]
    public void Every_check_and_clearance_wire_property_has_a_description()
    {
        Type[] types =
        [
            typeof(CheckStateDto), typeof(DepartureWindowDto), typeof(DepartureRangeDto), typeof(SetDepartureRequest),
            typeof(SetCheckModeRequest), typeof(SetPassageCursorRequest),
            typeof(ClearanceStateDto), typeof(ClearanceSourcesGroupDto), typeof(DepartureBandDto), typeof(DepartureBandSegmentDto),
            typeof(SetClearanceViewRequest),
        ];
        var missing = types
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.Name != "EqualityContract")
                .Where(p => string.IsNullOrWhiteSpace(p.GetCustomAttribute<DescriptionAttribute>()?.Description))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        Assert.Empty(missing);
    }

    private sealed class UtcTime : Services.ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
