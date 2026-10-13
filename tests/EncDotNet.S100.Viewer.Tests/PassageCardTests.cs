using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Passage value text: the app's locale, the mariner's depth unit, and lenient parsing.</summary>
public sealed class PassageTextTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public PassageTextTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    [Fact]
    public void Values_use_the_locale_and_units()
    {
        Assert.Equal("11,5 m · 0,4 m", PassageText.DraftSquat(11.5, 0.4, DepthUnit.Metres));
        Assert.Equal("11,5 m", PassageText.DraftSquat(11.5, null, DepthUnit.Metres));
        Assert.Equal("37,7 ft", PassageText.Depth(11.5, DepthUnit.Feet));
        Assert.Equal("12,0 kn", PassageText.Speed(12));
        Assert.Equal("150 m", PassageText.CrossTrack(150, 150));
        Assert.Equal("150 / 120 m", PassageText.CrossTrack(150, 120));
        Assert.Equal("150 / – m", PassageText.CrossTrack(150, null));
        Assert.Equal(string.Empty, PassageText.CrossTrack(null, null));
    }

    [Theory]
    [InlineData("11,5 · 0,4", 11.5, 0.4)]
    [InlineData("11.5 m / 0.4 m", 11.5, 0.4)]
    [InlineData("11,5 m", 11.5, null)]
    [InlineData("11,5 + 0,4", 11.5, 0.4)]
    public void Draft_and_squat_parse(string text, double draft, double? squat)
    {
        Assert.True(PassageText.TryParseDraftSquat(text, DepthUnit.Metres, out var d, out var s));
        Assert.Equal(draft, d!.Value, 6);
        Assert.Equal(squat, s);
    }

    [Fact]
    public void Draft_parses_in_the_mariners_depth_unit()
    {
        Assert.True(PassageText.TryParseDraftSquat("33", DepthUnit.Feet, out var draft, out _));
        Assert.Equal(33 / 3.28084, draft!.Value, 3);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1 · 2 · 3")]
    [InlineData("-3")]
    public void Bad_draft_text_is_rejected(string text)
        => Assert.False(PassageText.TryParseDraftSquat(text, DepthUnit.Metres, out _, out _));

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12,5 kn", 12.5)]
    [InlineData("8 kt", 8)]
    public void Speed_parses(string text, double expected)
    {
        Assert.True(PassageText.TryParseSpeed(text, out var knots));
        Assert.Equal(expected, knots, 6);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("fast")]
    public void Bad_speed_is_rejected(string text) => Assert.False(PassageText.TryParseSpeed(text, out _));

    [Fact]
    public void Cross_track_parses_one_or_both_sides()
    {
        Assert.True(PassageText.TryParseCrossTrack("150", out var p, out var s));
        Assert.Equal((150.0, 150.0), (p!.Value, s!.Value));
        Assert.True(PassageText.TryParseCrossTrack("150 / 120 m", out p, out s));
        Assert.Equal((150.0, 120.0), (p!.Value, s!.Value));
        Assert.True(PassageText.TryParseCrossTrack("", out p, out s));
        Assert.Null(p);
        Assert.False(PassageText.TryParseCrossTrack("wide", out _, out _));
    }

    [Fact]
    public void Departure_parses_a_time_on_today_and_a_utc_date_time()
    {
        var today = new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

        Assert.True(PassageText.TryParseDeparture("07:30 UTC", TimeFormat.Local, today, out var time));
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 7, 30, 0, TimeSpan.Zero), time);

        Assert.True(PassageText.TryParseDeparture("2026-10-13 06:15", TimeFormat.Utc, today, out var full));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 6, 15, 0, TimeSpan.Zero), full);
        Assert.Equal("2026-10-13 06:15 UTC", PassageText.Departure(full!.Value, TimeFormat.Utc));

        Assert.True(PassageText.TryParseDeparture("", TimeFormat.Utc, today, out var cleared));
        Assert.Null(cleared);
        Assert.False(PassageText.TryParseDeparture("soon", TimeFormat.Utc, today, out _));
    }
}

/// <summary>The Passage card and the inline leg detail, against the route model.</summary>
public sealed class PassageCardTests
{
    private static (RoutesService Service, RoutesPanelViewModel Vm, Route Route) Create()
    {
        var service = new RoutesService();
        var vm = new RoutesPanelViewModel(service);
        var route = service.Routes.CreateRoute("R");
        route.AppendWaypoint(new GeoPosition(50.0, -1.0));
        route.AppendWaypoint(new GeoPosition(50.1, -1.1));
        route.AppendWaypoint(new GeoPosition(50.2, -1.2));
        return (service, vm, route);
    }

    private static RouteLegRowViewModel Leg(RoutesPanelViewModel vm, int index)
        => vm.Timeline.OfType<RouteLegRowViewModel>().Single(l => l.LegIndex == index);

    [Fact]
    public void A_new_route_collapses_the_card_with_the_prompt()
    {
        var (service, vm, _) = Create();

        Assert.False(service.IsPassageCardExpanded);
        Assert.False(vm.PassageCard.IsExpanded);
        Assert.Equal(Strings.Passage_CollapsedPrompt, vm.PassageCard.CollapsedText);
    }

    [Fact]
    public void Collapsed_text_switches_from_the_prompt_to_a_summary_once_values_are_set()
    {
        var (_, vm, route) = Create();

        route.History.Execute(RouteEditLabels.EditPassage, () =>
        {
            route.SetPassageValue(RoutePassageField.Draft, 11.5);
            route.SetPassageValue(RoutePassageField.Speed, 12);
        });

        Assert.NotEqual(Strings.Passage_CollapsedPrompt, vm.PassageCard.CollapsedText);
        Assert.Contains(PassageText.Speed(12), vm.PassageCard.CollapsedText);
        Assert.Contains(PassageText.Depth(11.5, DepthUnit.Metres), vm.PassageCard.CollapsedText);
    }

    [Fact]
    public void Without_a_draft_check_passage_is_disabled_with_its_tooltip()
    {
        var (_, vm, _) = Create();

        Assert.False(vm.CanCheckPassage);
        Assert.False(vm.PassageCard.HasDraft);
        Assert.Equal("Add a draft to check clearance", vm.CheckPassageTooltip);
    }

    [Fact]
    public void Typed_values_set_the_passage_as_undoable_steps()
    {
        var (_, vm, route) = Create();
        var card = vm.PassageCard;

        card.DraftSquat.Text = "11.5 · 0.4";
        card.DraftSquat.CommitCommand.Execute(null);
        card.Speed.Text = "12";
        card.Speed.CommitCommand.Execute(null);
        card.CrossTrack.Text = "150 / 120";
        card.CrossTrack.CommitCommand.Execute(null);
        card.RequiredUkc.Text = "1";
        card.RequiredUkc.CommitCommand.Execute(null);

        Assert.Equal(11.5, route.Passage.DraftMeters);
        Assert.Equal(0.4, route.Passage.SquatMeters);
        Assert.Equal(12, route.Passage.SpeedKnots);
        Assert.Equal((150.0, 120.0), (route.Passage.PortCrossTrackMeters!.Value, route.Passage.StarboardCrossTrackMeters!.Value));
        Assert.Equal(1, route.Passage.RequiredUkcMeters);
        Assert.True(card.Speed.IsSet);
        Assert.True(card.HasDraft);
        Assert.True(card.RequiredUkc.ShowsSuffix);
        Assert.Equal(4, route.History.UndoCount);

        route.History.Undo();
        Assert.Null(route.Passage.RequiredUkcMeters);
        Assert.False(vm.PassageCard.RequiredUkc.IsSet);
    }

    [Fact]
    public void Bad_text_shows_an_error_and_changes_nothing()
    {
        var (_, vm, route) = Create();
        var speed = vm.PassageCard.Speed;

        speed.Text = "fast";
        speed.CommitCommand.Execute(null);

        Assert.True(speed.HasError);
        Assert.Equal(speed.ErrorText, speed.ErrorTip);
        Assert.Null(route.Passage.SpeedKnots);
        Assert.False(route.History.CanUndo);

        speed.RevertCommand.Execute(null);
        Assert.False(speed.HasError);
        Assert.Equal(string.Empty, speed.Text);
    }

    [Fact]
    public void Clearing_a_field_unsets_the_value()
    {
        var (_, vm, route) = Create();
        route.SetPassageValue(RoutePassageField.Speed, 12);
        vm.PassageCard.Refresh(route);

        vm.PassageCard.Speed.Text = "";
        vm.PassageCard.Speed.CommitCommand.Execute(null);

        Assert.Null(route.Passage.SpeedKnots);
    }

    [Fact]
    public void Unset_departure_reads_view_time_with_a_clock()
    {
        var (_, vm, _) = Create();

        Assert.Equal(Strings.Passage_ViewTime, vm.PassageCard.Departure.Placeholder);
        Assert.True(vm.PassageCard.Departure.ShowsClock);
        Assert.False(vm.PassageCard.Departure.IsSet);
    }

    [Fact]
    public void Vessel_values_commit_to_route_info()
    {
        var (_, vm, route) = Create();

        vm.PassageCard.Vessel.Name = "Solent Star";
        vm.PassageCard.Vessel.Length = "182,5";

        Assert.Equal("Solent Star", route.Info.Vessel!.Name);
        Assert.Equal(182.5, route.Info.Vessel.LengthMeters);
        Assert.Equal("Solent Star", vm.PassageCard.Vessel.Summary);
        route.History.Undo();
        Assert.Null(route.Info.Vessel!.LengthMeters);
    }

    [Fact]
    public void Setting_a_leg_override_shows_the_dot_and_value_and_reset_clears_it()
    {
        var (_, vm, route) = Create();
        route.SetPassageValue(RoutePassageField.Speed, 12);

        var speed = Leg(vm, 1).Speed;
        Assert.False(speed.IsOverridden);
        Assert.Equal(PassageText.Speed(12), speed.Placeholder);

        speed.Text = "8";
        speed.CommitCommand.Execute(null);

        var leg = Leg(vm, 1);
        Assert.Equal(8, route.Legs[1].GetOverride(RoutePassageField.Speed));
        Assert.True(leg.HasOverrides);
        Assert.Equal(PassageText.Speed(8), leg.OverrideSummary);
        Assert.True(leg.Speed.IsOverridden);
        Assert.Contains("override", leg.Speed.Label);
        Assert.False(Leg(vm, 0).HasOverrides);

        leg.Speed.ResetCommand.Execute(null);

        Assert.False(route.IsLegOverridden(1, RoutePassageField.Speed));
        Assert.False(Leg(vm, 1).HasOverrides);
        Assert.Equal(Strings.Passage_Speed, Leg(vm, 1).Speed.Label);
    }

    [Fact]
    public void Leg_cross_track_overrides_both_sides_and_resets_both()
    {
        var (_, vm, route) = Create();
        route.SetPassageValue(RoutePassageField.PortCrossTrack, 150);
        route.SetPassageValue(RoutePassageField.StarboardCrossTrack, 150);

        var crossTrack = Leg(vm, 0).CrossTrack;
        crossTrack.Text = "100 / 80";
        crossTrack.CommitCommand.Execute(null);

        Assert.Equal(100, route.Legs[0].GetOverride(RoutePassageField.PortCrossTrack));
        Assert.Equal(80, route.Legs[0].GetOverride(RoutePassageField.StarboardCrossTrack));
        Assert.Equal(PassageText.CrossTrack(100, 80), Leg(vm, 0).OverrideSummary);

        Leg(vm, 0).CrossTrack.ResetCommand.Execute(null);
        Assert.False(route.IsLegOverridden(0, RoutePassageField.PortCrossTrack));
        Assert.False(route.IsLegOverridden(0, RoutePassageField.StarboardCrossTrack));
    }

    [Fact]
    public void Selecting_a_leg_opens_its_detail_one_at_a_time()
    {
        var (service, vm, _) = Create();

        service.SelectedLegIndex = 0;
        Assert.True(Leg(vm, 0).IsSelected);
        Assert.False(Leg(vm, 1).IsSelected);

        service.SelectedLegIndex = 1;
        Assert.False(Leg(vm, 0).IsSelected);
        Assert.True(Leg(vm, 1).IsSelected);

        Leg(vm, 1).CloseCommand.Execute(null);
        Assert.Null(service.SelectedLegIndex);
    }

    [Fact]
    public void Leg_geometry_and_note_are_undoable()
    {
        var (_, vm, route) = Create();

        Leg(vm, 0).SetGreatCircleCommand.Execute(null);
        Assert.Equal(RouteLegGeometryType.Geodesic, route.Legs[0].GeometryType);

        var leg = Leg(vm, 0);
        Assert.False(leg.IsNoteVisible);
        leg.AddNoteCommand.Execute(null);
        Assert.True(leg.IsNoteVisible);
        leg.Note = "Keep to the channel";
        leg.CommitNoteCommand.Execute(null);
        Assert.Equal("Keep to the channel", route.Legs[0].Note);

        route.History.Undo();
        Assert.Null(route.Legs[0].Note);
        route.History.Undo();
        Assert.Equal(RouteLegGeometryType.Loxodrome, route.Legs[0].GeometryType);
    }

    [Fact]
    public void The_card_resets_when_the_active_route_changes()
    {
        var (service, _, first) = Create();
        first.SetPassageValue(RoutePassageField.Draft, 9);
        var second = service.Routes.CreateRoute("Empty");

        Assert.False(service.IsPassageCardExpanded);

        service.Routes.SetActiveRoute(first);
        Assert.True(service.IsPassageCardExpanded);

        service.IsPassageCardExpanded = false;
        service.Routes.SetActiveRoute(second);
        service.Routes.SetActiveRoute(first);
        Assert.True(service.IsPassageCardExpanded);
    }
}
