using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// ETAs (#899): leg durations, waypoint ETAs and the arrival, from the
/// departure or the view time, in the Routes panel, the map labels and
/// <c>get_route</c>.
/// </summary>
public sealed class RouteEtaTests : IDisposable
{
    private static readonly DateTimeOffset Departure = new(2026, 10, 12, 6, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public RouteEtaTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class ViewTime(DateTimeOffset start) : IRouteViewTime
    {
        private DateTimeOffset _current = start;

        public DateTimeOffset Current
        {
            get => _current;
            set
            {
                _current = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? Changed;
    }

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    /// <summary>Three legs due north along a meridian, 6, 3 and 6 NM long.</summary>
    private static Route MeridianRoute()
    {
        var route = new Route("r", "North");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.1, -1.0));
        route.AppendWaypoint(new GeoPosition(50.15, -1.0));
        route.AppendWaypoint(new GeoPosition(50.25, -1.0));
        return route;
    }

    private static (RoutesService Service, Route Route, RoutesPanelViewModel Vm, ViewTime Time) Panel()
    {
        var time = new ViewTime(Departure);
        var service = new RoutesService(time);
        var vm = new RoutesPanelViewModel(service, timeFormat: new Utc());
        var route = service.Routes.CreateRoute("North", "r");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.1, -1.0));
        route.AppendWaypoint(new GeoPosition(50.15, -1.0));
        route.AppendWaypoint(new GeoPosition(50.25, -1.0));
        return (service, route, vm, time);
    }

    private static List<string?> WaypointEtas(RoutesPanelViewModel vm)
        => vm.Timeline.OfType<RouteWaypointRowViewModel>().Select(r => r.Eta).ToList();

    [Fact]
    public void Speed_gives_leg_durations_waypoint_etas_and_the_arrival()
    {
        var route = MeridianRoute();
        route.SetPassageValue(RoutePassageField.Speed, 12);
        route.SetDeparture(Departure);

        var etas = RouteEtas.Compute(route, DateTimeOffset.UnixEpoch);

        Assert.True(etas.HasEtas);
        Assert.False(etas.IsFromViewTime);
        Assert.Equal(30, etas.LegDuration(0)!.Value.TotalMinutes, 0);
        Assert.Equal(15, etas.LegDuration(1)!.Value.TotalMinutes, 0);
        Assert.Equal(Departure, etas.WaypointEta(0));
        Assert.Equal(Departure.AddMinutes(45), etas.WaypointEta(2)!.Value, TimeSpan.FromSeconds(30));
        Assert.Equal(Departure.AddMinutes(75), etas.Arrival!.Value, TimeSpan.FromSeconds(30));
        Assert.Equal(75, etas.Duration!.Value.TotalMinutes, 0);
    }

    [Fact]
    public void A_leg_speed_override_changes_downstream_etas_only()
    {
        var route = MeridianRoute();
        route.SetPassageValue(RoutePassageField.Speed, 12);
        route.SetDeparture(Departure);
        var before = RouteEtas.Compute(route, DateTimeOffset.UnixEpoch);

        route.SetLegOverride(1, RoutePassageField.Speed, 6);
        var after = RouteEtas.Compute(route, DateTimeOffset.UnixEpoch);

        Assert.Equal(before.WaypointEta(1), after.WaypointEta(1));
        Assert.Equal(30, after.LegDuration(1)!.Value.TotalMinutes, 0);
        Assert.Equal(before.WaypointEta(2)!.Value.AddMinutes(15), after.WaypointEta(2)!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(before.Arrival!.Value.AddMinutes(15), after.Arrival!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Etas_stop_at_the_first_leg_with_no_speed()
    {
        var route = MeridianRoute();
        route.SetLegOverride(0, RoutePassageField.Speed, 12);
        route.SetLegOverride(2, RoutePassageField.Speed, 12);

        var etas = RouteEtas.Compute(route, Departure);

        Assert.Equal(1, etas.FirstLegWithoutSpeed);
        Assert.NotNull(etas.WaypointEta(1));
        Assert.Null(etas.WaypointEta(2));
        Assert.Null(etas.Arrival);
        Assert.NotNull(etas.LegDuration(2));
    }

    [Fact]
    public void No_speed_means_no_times()
    {
        var etas = RouteEtas.Compute(MeridianRoute(), Departure);

        Assert.False(etas.HasEtas);
        Assert.Null(etas.FirstLegWithoutSpeed);
        Assert.Null(etas.WaypointEta(0));
        Assert.Null(etas.LegDuration(0));
    }

    [Fact]
    public void Panel_speed_only_shows_etas_and_durations_and_keeps_check_passage_disabled()
    {
        var (_, route, vm, _) = Panel();
        Assert.All(WaypointEtas(vm), Assert.Null);

        route.History.Execute(RouteEditLabels.EditPassage, () => route.SetPassageValue(RoutePassageField.Speed, 12));

        Assert.Equal(["06:00Z", "06:30Z", "06:45Z", "07:15Z"], WaypointEtas(vm));
        var legs = vm.Timeline.OfType<RouteLegRowViewModel>().ToList();
        Assert.EndsWith(" · 30 min", legs[0].LegText);
        Assert.EndsWith(" · 15 min", legs[1].LegText);
        Assert.Equal("4 WP · 15.0 NM · 1 h 15 min · arrives 07:15Z from view time", vm.ActiveRouteDetailMeta);
        Assert.False(vm.CanCheckPassage);
        Assert.Equal("Add a draft to check clearance", vm.CheckPassageTooltip);
    }

    [Fact]
    public void Unset_departure_follows_the_view_time_and_a_set_one_does_not()
    {
        var (_, route, vm, time) = Panel();
        route.SetPassageValue(RoutePassageField.Speed, 12);

        time.Current = Departure.AddHours(2);
        Assert.Equal("08:00Z", WaypointEtas(vm)[0]);
        Assert.Equal("4 WP · 15.0 NM · 1 h 15 min · arrives 09:15Z from view time", vm.ActiveRouteDetailMeta);

        route.SetDeparture(Departure);
        time.Current = Departure.AddHours(5);

        Assert.Equal("06:00Z", WaypointEtas(vm)[0]);
        Assert.Equal("4 WP · 15.0 NM · 1 h 15 min · arrives 07:15Z", vm.ActiveRouteDetailMeta);
    }

    [Fact]
    public void Panel_header_says_where_etas_stop()
    {
        var (_, route, vm, _) = Panel();
        route.SetLegOverride(0, RoutePassageField.Speed, 12);

        Assert.Equal("4 WP · 15.0 NM · ETAs stop at leg 2 → 3: no speed", vm.ActiveRouteDetailMeta);
        Assert.Equal(["06:00Z", "06:30Z", null, null], WaypointEtas(vm));
    }

    [Fact]
    public void Etas_on_another_day_name_the_day()
    {
        var departure = new DateTimeOffset(2026, 10, 12, 23, 30, 0, TimeSpan.Zero);

        Assert.Equal("23:30Z", PassageFormatting.FormatEta(departure, departure, TimeFormat.Utc));
        Assert.Equal("Tue 00:15Z", PassageFormatting.FormatEta(departure.AddMinutes(45), departure, TimeFormat.Utc));
    }

    [Theory]
    [InlineData(0.2, "1 min")]
    [InlineData(12, "12 min")]
    [InlineData(120, "2 h")]
    [InlineData(130, "2 h 10 min")]
    public void Durations_are_whole_minutes(double minutes, string expected)
        => Assert.Equal(expected, PassageFormatting.FormatDuration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Map_leg_labels_gain_minutes_when_speed_is_known()
    {
        var routes = new RouteCollection();
        var route = routes.CreateRoute("North");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.1, -1.0));
        Assert.DoesNotContain("min", RouteOverlayScene.Build(routes, null, null, false, MeasureOverlayAppearance.Default).ActiveRoute!.Legs[0].Label);

        route.SetPassageValue(RoutePassageField.Speed, 12);
        var label = RouteOverlayScene.Build(routes, null, null, false, MeasureOverlayAppearance.Default).ActiveRoute!.Legs[0].Label;

        Assert.Equal("6.0 NM · 000° · 30 min", label);
    }

    [Fact]
    public void Get_route_reports_etas_durations_and_times()
    {
        var (service, route, _, _) = Panel();
        Assert.Null(RouteProjection.Detail(route, service).Times);

        route.SetPassageValue(RoutePassageField.Speed, 12);
        var detail = RouteProjection.Detail(route, service);

        Assert.Equal(Departure, detail.Waypoints[0].Eta);
        Assert.Equal(Departure.AddMinutes(30), detail.Waypoints[1].Eta!.Value, TimeSpan.FromSeconds(30));
        Assert.Equal(30, detail.Legs[0].DurationMinutes!.Value, 0);
        Assert.NotNull(detail.Times);
        Assert.True(detail.Times!.FromViewTime);
        Assert.Equal(Departure, detail.Times.Departure);
        Assert.Equal(75, detail.Times.DurationMinutes!.Value, 0);
        Assert.Null(detail.Times.EtasStopAtLeg);

        var call = GetRouteMcpAdapter.TranslateResult(EncDotNet.S100.Datasets.Pipelines.Query.ToolResult<RouteDetail>.Ok(detail));
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(call.Content.Single()).Text;
        using var doc = System.Text.Json.JsonDocument.Parse(text);
        Assert.True(doc.RootElement.GetProperty("times").GetProperty("fromViewTime").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("waypoints")[1].TryGetProperty("eta", out _));
        Assert.True(doc.RootElement.GetProperty("legs")[0].TryGetProperty("durationMinutes", out _));
    }
}
