using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Passage card and inline leg detail (#898): field states, the collapsed
/// summary, leg overrides on the rows, units, and the Check passage gate.
/// </summary>
public sealed class RoutePassageCardTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public RoutePassageCardTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Mariner(DepthUnit unit) : IMarinerSettingsProvider
    {
        public MarinerSettings Current { get; } = new() { DepthUnit = unit };

        public event Action<MarinerSettings>? Changed { add { } remove { } }
    }

    private sealed class Clock(TimeFormat format) : ITimeFormatProvider
    {
        public TimeFormat Current => format;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private static (RoutesService Service, Route Route, RoutesPanelViewModel Vm) Create(
        DepthUnit unit = DepthUnit.Metres, TimeFormat time = TimeFormat.Utc)
    {
        var service = new RoutesService();
        var vm = new RoutesPanelViewModel(service, marinerSettings: new Mariner(unit), timeFormat: new Clock(time));
        var route = service.Routes.CreateRoute("Solent");
        route.AppendWaypoint(new GeoPosition(50.70, -1.10));
        route.AppendWaypoint(new GeoPosition(50.75, -1.20));
        route.AppendWaypoint(new GeoPosition(50.80, -1.30));
        return (service, route, vm);
    }

    private static RouteLegRowViewModel LegRow(RoutesPanelViewModel vm, int index)
        => vm.Timeline.OfType<RouteLegRowViewModel>().ElementAt(index);

    private static void Type(RouteFieldViewModel field, string text)
    {
        field.Text = text;
        field.CommitCommand.Execute(null);
    }

    [Fact]
    public void No_passage_fields_disables_check_passage_with_its_tooltip()
    {
        var (_, route, vm) = Create();

        Assert.False(vm.HasDraft);
        Assert.False(vm.CanCheckPassage);
        Assert.Equal("Add a draft to check clearance", vm.CheckPassageTooltip);

        Type(vm.Passage.Draft, "4.5");

        Assert.Equal(4.5, route.Passage.DraftMeters);
        Assert.True(vm.HasDraft);
        Assert.NotEqual("Add a draft to check clearance", vm.CheckPassageTooltip);
    }

    [Fact]
    public void A_draft_on_every_leg_also_counts()
    {
        var (_, route, vm) = Create();
        route.SetLegOverride(0, RoutePassageField.Draft, 4);
        route.SetLegOverride(1, RoutePassageField.Draft, 5);

        Assert.True(vm.HasDraft);
    }

    [Fact]
    public void Collapsed_line_switches_from_the_prompt_to_a_summary()
    {
        var (_, route, vm) = Create();

        Assert.False(vm.Passage.IsExpanded);
        Assert.Equal("Add draft and speed to check clearance", vm.Passage.Summary);

        route.History.Execute(RouteEditLabels.EditPassage, () =>
        {
            route.SetPassageValue(RoutePassageField.Draft, 11.5);
            route.SetPassageValue(RoutePassageField.Squat, 0.4);
            route.SetPassageValue(RoutePassageField.Speed, 12);
            route.SetDeparture(new DateTimeOffset(2026, 10, 12, 6, 40, 0, TimeSpan.Zero));
        });

        Assert.Equal("11.5 m · squat 0.4 m · 12.0 kn · dep 2026-10-12 06:40 UTC", vm.Passage.Summary);
    }

    [Fact]
    public void Card_opens_for_a_route_that_already_has_passage_values()
    {
        var service = new RoutesService();
        var vm = new RoutesPanelViewModel(service);
        var casual = service.Routes.CreateRoute("Casual");
        Assert.False(vm.Passage.IsExpanded);

        var planned = service.Routes.CreateRoute("Planned");
        planned.SetPassageValue(RoutePassageField.Speed, 10);
        service.Routes.SetActiveRoute(casual);
        service.Routes.SetActiveRoute(planned);

        Assert.True(vm.Passage.IsExpanded);
    }

    [Fact]
    public void The_card_state_is_mirrored_on_the_routes_service_both_ways()
    {
        var (service, route, vm) = Create();
        Assert.False(service.IsPassageCardExpanded);

        vm.Passage.ToggleExpandedCommand.Execute(null);
        Assert.True(service.IsPassageCardExpanded);

        service.IsPassageCardExpanded = false;
        Assert.False(vm.Passage.IsExpanded);

        // Switching routes still follows the card's rule, and the service sees it.
        route.SetPassageValue(RoutePassageField.Speed, 10);
        service.Routes.CreateRoute("Other");
        Assert.False(service.IsPassageCardExpanded);
        service.Routes.SetActiveRoute(route);
        Assert.True(service.IsPassageCardExpanded);
    }

    [Fact]
    public void Fields_are_dashed_until_set_and_show_their_unit()
    {
        var (_, route, vm) = Create();
        Assert.False(vm.Passage.Speed.IsSet);
        Assert.Equal("+ Add", vm.Passage.Speed.Placeholder);
        Assert.Equal("View time", vm.Passage.Departure.Placeholder);
        Assert.False(vm.Passage.ShowSquat);

        Type(vm.Passage.Speed, "12");
        Type(vm.Passage.Draft, "11.5 m");

        Assert.True(vm.Passage.Speed.IsSet);
        Assert.Equal("12.0 kn", vm.Passage.Speed.Text);
        Assert.Equal("11.5 m", vm.Passage.Draft.Text);
        Assert.True(vm.Passage.ShowSquat);
        Assert.Equal(12, route.Passage.SpeedKnots);
    }

    [Fact]
    public void Card_edits_are_undoable_and_clearing_a_field_unsets_it()
    {
        var (_, route, vm) = Create();
        Type(vm.Passage.Speed, "12");
        Assert.Equal("edit passage", route.History.UndoLabel);

        Type(vm.Passage.Speed, "");
        Assert.Null(route.Passage.SpeedKnots);
        Assert.False(vm.Passage.Speed.IsSet);

        route.History.Undo();
        Assert.Equal(12, route.Passage.SpeedKnots);
        Assert.Equal("12.0 kn", vm.Passage.Speed.Text);
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("-3")]
    [InlineData("0")]
    public void Invalid_entries_revert(string text)
    {
        var (_, route, vm) = Create();
        Type(vm.Passage.Speed, "10");

        Type(vm.Passage.Speed, text);

        Assert.Equal(10, route.Passage.SpeedKnots);
        Assert.Equal("10.0 kn", vm.Passage.Speed.Text);
    }

    [Fact]
    public void Cross_track_takes_one_value_or_port_and_starboard()
    {
        var (_, route, vm) = Create();

        Type(vm.Passage.CrossTrack, "50");
        Assert.Equal(50, route.Passage.PortCrossTrackMeters);
        Assert.Equal(50, route.Passage.StarboardCrossTrackMeters);
        Assert.Equal("50 m", vm.Passage.CrossTrack.Text);

        Type(vm.Passage.CrossTrack, "40 / 80");
        Assert.Equal(40, route.Passage.PortCrossTrackMeters);
        Assert.Equal(80, route.Passage.StarboardCrossTrackMeters);
        Assert.Equal("40 / 80 m", vm.Passage.CrossTrack.Text);
    }

    [Fact]
    public void Depths_follow_the_mariner_depth_unit()
    {
        var (_, route, vm) = Create(DepthUnit.Feet);

        Type(vm.Passage.Draft, "10");

        Assert.Equal(3.048, route.Passage.DraftMeters!.Value, 6);
        Assert.Equal("10.0 ft", vm.Passage.Draft.Text);
        Type(vm.Passage.Speed, "8");
        Assert.Equal("8.0 kn", vm.Passage.Speed.Text);
    }

    [Fact]
    public void Departure_is_typed_and_shown_in_the_time_preference()
    {
        var (_, route, vm) = Create(time: TimeFormat.Utc);

        Type(vm.Passage.Departure, "2026-10-12 06:40");

        Assert.Equal(new DateTimeOffset(2026, 10, 12, 6, 40, 0, TimeSpan.Zero), route.Passage.Departure);
        Assert.Equal("2026-10-12 06:40 UTC", vm.Passage.Departure.Text);

        Type(vm.Passage.Departure, "");
        Assert.Null(route.Passage.Departure);
    }

    [Fact]
    public void Vessel_fields_create_and_remove_the_vessel()
    {
        var (_, route, vm) = Create();
        Assert.False(vm.Passage.HasVessel);

        Type(vm.Passage.VesselName, "MV Test");
        Type(vm.Passage.VesselLength, "120");

        Assert.Equal("MV Test", route.Info.Vessel?.Name);
        Assert.Equal(120, route.Info.Vessel?.LengthMeters);
        Assert.Equal("MV Test", vm.Passage.VesselText);

        Type(vm.Passage.VesselName, "");
        Type(vm.Passage.VesselLength, "");
        Assert.Null(route.Info.Vessel);
    }

    [Fact]
    public void Leg_override_shows_the_dot_and_value_on_the_row_and_reset_clears_it()
    {
        var (_, route, vm) = Create();
        Type(vm.Passage.Speed, "12");

        vm.ToggleLegDetailCommand.Execute(LegRow(vm, 1));
        Assert.True(LegRow(vm, 1).IsOpen);
        Assert.Equal("Leg 2 → 3", vm.LegDetail.Title);
        Assert.False(vm.LegDetail.Speed.IsOverridden);
        Assert.Equal("12.0 kn", vm.LegDetail.Speed.Placeholder);

        Type(vm.LegDetail.Speed, "8");

        Assert.Equal(8, route.Legs[1].PlannedSpeedKnots);
        Assert.True(LegRow(vm, 1).HasOverride);
        Assert.Equal("8.0 kn", LegRow(vm, 1).OverrideText);
        Assert.False(LegRow(vm, 0).HasOverride);
        Assert.True(vm.LegDetail.Speed.IsOverridden);
        Assert.Equal("Speed · override", vm.LegDetail.Speed.Label);
        Assert.True(LegRow(vm, 1).IsOpen);

        vm.LegDetail.Speed.ResetCommand.Execute(null);

        Assert.Null(route.Legs[1].PlannedSpeedKnots);
        Assert.False(LegRow(vm, 1).HasOverride);
        Assert.False(vm.LegDetail.Speed.IsOverridden);
        Assert.Equal("edit leg", route.History.UndoLabel);
    }

    [Fact]
    public void A_leg_value_equal_to_the_passage_stores_no_override()
    {
        var (_, route, vm) = Create();
        Type(vm.Passage.Draft, "4.5");
        vm.ToggleLegDetailCommand.Execute(LegRow(vm, 0));

        Type(vm.LegDetail.Draft, "4.5");

        Assert.Null(route.Legs[0].DraftMeters);
        Assert.False(LegRow(vm, 0).HasOverride);
    }

    [Fact]
    public void One_leg_is_open_at_a_time_and_the_row_survives_edits()
    {
        var (_, route, vm) = Create();
        var first = LegRow(vm, 0);

        vm.ToggleLegDetailCommand.Execute(first);
        vm.ToggleLegDetailCommand.Execute(LegRow(vm, 1));
        Assert.False(LegRow(vm, 0).IsOpen);
        Assert.True(LegRow(vm, 1).IsOpen);

        Type(vm.LegDetail.Draft, "6");
        Assert.Same(first, LegRow(vm, 0));

        vm.ToggleLegDetailCommand.Execute(LegRow(vm, 1));
        Assert.False(LegRow(vm, 1).IsOpen);
    }

    [Fact]
    public void Opening_a_leg_selects_it_for_the_map_halo()
    {
        var (service, _, vm) = Create();

        vm.ToggleLegDetailCommand.Execute(LegRow(vm, 1));

        Assert.Equal(1, service.SelectedLegIndex);
        Assert.True(LegRow(vm, 1).IsSelected);
        Assert.False(LegRow(vm, 0).IsSelected);
    }

    [Fact]
    public void Leg_detail_sets_geometry_cross_track_and_note()
    {
        var (_, route, vm) = Create();
        vm.ToggleLegDetailCommand.Execute(LegRow(vm, 0));
        Assert.True(vm.LegDetail.IsRhumb);
        Assert.False(vm.LegDetail.IsNoteVisible);

        vm.LegDetail.SetGreatCircleCommand.Execute(null);
        Type(vm.LegDetail.CrossTrack, "100 / 150");
        vm.LegDetail.AddNoteCommand.Execute(null);
        Type(vm.LegDetail.Note, "Mind the shoal");

        Assert.Equal(RouteLegGeometryType.Geodesic, route.Legs[0].GeometryType);
        Assert.True(vm.LegDetail.IsGreatCircle);
        Assert.Equal(100, route.Legs[0].PortCrossTrackDistanceLimitMeters);
        Assert.Equal(150, route.Legs[0].StarboardCrossTrackDistanceLimitMeters);
        Assert.Equal("Mind the shoal", route.Legs[0].Note);
        Assert.Equal("100 / 150 m", LegRow(vm, 0).OverrideText);

        vm.LegDetail.CrossTrack.ResetCommand.Execute(null);
        Assert.Null(route.Legs[0].PortCrossTrackDistanceLimitMeters);
        Assert.Null(route.Legs[0].StarboardCrossTrackDistanceLimitMeters);
    }

    [Fact]
    public void Formatting_parses_both_decimal_separators()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        Assert.True(PassageFormatting.TryParse("11,5", PassageQuantity.Depth, DepthUnit.Metres, out var comma));
        Assert.True(PassageFormatting.TryParse("11.5", PassageQuantity.Depth, DepthUnit.Metres, out var dot));
        Assert.Equal(11.5, comma);
        Assert.Equal(11.5, dot);
        Assert.Equal("11,5 m", PassageFormatting.Format(11.5, PassageQuantity.Depth, DepthUnit.Metres));
    }
}
