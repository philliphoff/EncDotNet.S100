using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The real Routes panel, laid out headlessly with the route editor's keyboard
/// shortcuts attached to its window as <c>MainWindow</c> attaches them, driven
/// with pointer and keyboard input and found by its <c>Routes.*</c> automation ids.
/// </summary>
public sealed class RoutesViewTests
{
    private static (RoutesService Service, Route Route, ViewHost Host) Show()
    {
        var service = new RoutesService();
        var panel = new RoutesPanelViewModel(service);
        var route = service.Routes.CreateRoute("Solent");
        route.AppendWaypoint(new GeoPosition(50.70, -1.10));
        route.AppendWaypoint(new GeoPosition(50.75, -1.20));
        route.AppendWaypoint(new GeoPosition(50.80, -1.30));
        var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 800);
        RouteEditorKeys.Attach(host.Window, service);
        return (service, route, host);
    }

    private static TextBox Field(ViewHost host, string id, int index)
        => host.FindAll<TextBox>(id)[index];

    [AvaloniaFact]
    public void Ctrl_E_toggles_edit_mode_but_not_while_a_text_field_has_focus()
    {
        var (service, route, host) = Show();
        using var _ = host;

        host.Press(PhysicalKey.E, RawInputModifiers.Control);
        Assert.True(service.IsEditModeActive);
        host.Press(PhysicalKey.E, RawInputModifiers.Meta);
        Assert.False(service.IsEditModeActive);

        var name = Field(host, "Routes.Waypoint.Name", 0);
        host.Click(name);
        Assert.True(name.IsFocused);

        host.Press(PhysicalKey.E, RawInputModifiers.Control);
        Assert.False(service.IsEditModeActive);

        // A bare E is never a shortcut: it types.
        host.Press(PhysicalKey.E);
        host.Type("e");
        Assert.False(service.IsEditModeActive);
        Assert.Equal("e", name.Text);
    }

    [AvaloniaFact]
    public void Edit_toggle_turns_edit_mode_on_and_off_and_shows_the_editing_badge()
    {
        var (service, _, host) = Show();
        using var _host = host;
        var toggle = host.Find<ToggleButton>("Routes.EditToggle");
        Assert.False(host.IsShown("Routes.Active.EditingBadge"));

        host.Click(toggle);
        Assert.True(service.IsEditModeActive);
        Assert.True(toggle.IsChecked);
        Assert.True(host.IsShown("Routes.Active.EditingBadge"));

        host.Click(toggle);
        Assert.False(service.IsEditModeActive);
        Assert.False(host.IsShown("Routes.Active.EditingBadge"));

        // Edit mode set elsewhere (Ctrl+E, the map tool, MCP) shows on the toggle.
        service.IsEditModeActive = true;
        host.Settle();
        Assert.True(toggle.IsChecked);
    }

    [AvaloniaFact]
    public void Check_passage_is_present_but_disabled_with_its_reason()
    {
        var (_, _, host) = Show();
        using var _host = host;

        var check = host.Find<ToggleButton>("Routes.CheckPassageToggle");

        Assert.False(check.IsEnabled);
        Assert.Equal("Add a draft to check clearance", ToolTip.GetTip(check));
        Assert.False(host.IsShown("Routes.Done"));
    }

    [AvaloniaFact]
    public void Undo_and_redo_buttons_and_keys_step_through_panel_edits()
    {
        var (service, route, host) = Show();
        using var _host = host;
        var undo = host.Find<Button>("Routes.Undo");
        var redo = host.Find<Button>("Routes.Redo");
        Assert.False(undo.IsEffectivelyEnabled);

        host.Click(host.Find<Button>("Routes.Reverse"));
        Assert.Equal(-1.30, route.Waypoints[0].Position.Longitude, 6);
        Assert.True(undo.IsEffectivelyEnabled);

        host.Click(undo);
        Assert.Equal(-1.10, route.Waypoints[0].Position.Longitude, 6);
        Assert.True(redo.IsEffectivelyEnabled);

        host.Click(redo);
        Assert.Equal(-1.30, route.Waypoints[0].Position.Longitude, 6);

        // The keys act from the Routes panel or the map (here nothing has
        // focus: the Redo button lost it when it was disabled).
        host.Press(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(-1.10, route.Waypoints[0].Position.Longitude, 6);
        host.Press(PhysicalKey.Z, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(-1.30, route.Waypoints[0].Position.Longitude, 6);
    }

    [AvaloniaFact]
    public void Delete_removes_the_selected_waypoint_only_while_editing()
    {
        var (service, route, host) = Show();
        using var _host = host;
        service.SelectedWaypointIndex = 1;

        host.Press(PhysicalKey.Delete);
        Assert.Equal(3, route.Waypoints.Count);

        service.IsEditModeActive = true;
        host.Press(PhysicalKey.Delete);
        Assert.Equal(2, route.Waypoints.Count);
        Assert.Null(service.SelectedWaypointIndex);

        host.Press(PhysicalKey.Z, RawInputModifiers.Control);
        Assert.Equal(3, route.Waypoints.Count);
    }

    [AvaloniaFact]
    public void Typed_position_moves_the_waypoint_and_bad_input_shows_an_error()
    {
        var (_, route, host) = Show();
        using var _host = host;
        var position = Field(host, "Routes.Waypoint.Position", 1);
        Assert.Equal(LatLonFormatter.Format(50.75, -1.20), position.Text);

        host.Click(position);
        position.SelectAll();
        host.Type("not a position");
        host.Press(PhysicalKey.Enter);

        Assert.True(host.IsShown("Routes.Waypoint.PositionError"));
        Assert.Equal(50.75, route.Waypoints[1].Position.Latitude, 6);

        position = Field(host, "Routes.Waypoint.Position", 1);
        host.Click(position);
        position.SelectAll();
        host.Type("50 46.000 N 001 15.000 W");
        host.Press(PhysicalKey.Enter);

        Assert.Equal(50 + 46.0 / 60, route.Waypoints[1].Position.Latitude, 6);
        Assert.Equal(-(1 + 15.0 / 60), route.Waypoints[1].Position.Longitude, 6);
        Assert.False(host.IsShown("Routes.Waypoint.PositionError"));
    }

    [AvaloniaFact]
    public void Waypoint_name_commits_when_the_field_loses_focus()
    {
        var (_, route, host) = Show();
        using var _host = host;

        host.Click(Field(host, "Routes.Waypoint.Name", 2));
        host.Type("Nab Tower");
        host.Click(host.Find<Button>("Routes.ZoomToRoute"));

        Assert.Equal("Nab Tower", route.Waypoints[2].Name);
    }

    [AvaloniaFact]
    public void Passage_card_expands_and_a_typed_draft_sets_the_passage()
    {
        var (_, route, host) = Show();
        using var _host = host;
        Assert.True(host.IsShown("Routes.Passage.Summary"));
        Assert.False(host.IsShown("Routes.Passage.Draft"));

        host.Click(host.Find<Button>("Routes.Passage.Toggle"));
        Assert.True(host.IsShown("Routes.Passage.Draft"));
        Assert.False(host.IsShown("Routes.Passage.Summary"));

        var draft = host.Find<TextBox>("Routes.Passage.Draft");
        host.Click(draft);
        host.Type("4");
        host.Press(PhysicalKey.Enter);

        Assert.Equal(4, route.Passage.DraftMeters);
        Assert.True(host.IsShown("Routes.Passage.Squat"));
    }

    [AvaloniaFact]
    public void Passage_field_commits_on_focus_loss_and_keeps_the_next_field()
    {
        var (_, route, host) = Show();
        using var _host = host;
        host.Click(host.Find<Button>("Routes.Passage.Toggle"));

        host.Click(host.Find<TextBox>("Routes.Passage.Speed"));
        host.Type("9");
        var crossTrack = host.Find<TextBox>("Routes.Passage.CrossTrack");
        host.Click(crossTrack);

        Assert.Equal(9, route.Passage.SpeedKnots);
        Assert.True(crossTrack.IsFocused);
    }

    [AvaloniaFact]
    public void Leg_detail_override_shows_on_the_row_and_reset_clears_it()
    {
        var (_, route, host) = Show();
        using var _host = host;
        route.SetPassageValue(RoutePassageField.Speed, 12);
        Assert.False(host.IsShown("Routes.LegDetail"));

        host.Click(host.FindAll<Button>("Routes.Leg")[0]);
        Assert.True(host.IsShown("Routes.LegDetail"));

        var speed = host.Find<TextBox>("Routes.Leg.Speed");
        Assert.Equal("", speed.Text ?? "");
        host.Click(speed);
        host.Type("8");
        host.Press(PhysicalKey.Enter);

        Assert.Equal(8, route.Legs[0].PlannedSpeedKnots);
        Assert.True(host.IsShown("Routes.Leg.Overrides"));
        Assert.True(host.IsShown("Routes.Leg.Speed.Reset"));

        host.Click(host.Find<Button>("Routes.Leg.Speed.Reset"));

        Assert.Null(route.Legs[0].PlannedSpeedKnots);
        Assert.False(host.IsShown("Routes.Leg.Overrides"));

        // Clicking the open leg's row closes it.
        host.Click(host.FindAll<Button>("Routes.Leg")[0]);
        Assert.False(host.IsShown("Routes.LegDetail"));
    }

    [AvaloniaFact]
    public void Leg_detail_geometry_segments_switch_the_leg()
    {
        var (_, route, host) = Show();
        using var _host = host;
        host.Click(host.FindAll<Button>("Routes.Leg")[1]);

        host.Click(host.Find<ToggleButton>("Routes.LegDetail.GreatCircle"));
        Assert.Equal(RouteLegGeometryType.Geodesic, route.Legs[1].GeometryType);
        Assert.True(host.Find<ToggleButton>("Routes.LegDetail.GreatCircle").IsChecked);
        Assert.False(host.Find<ToggleButton>("Routes.LegDetail.Rhumb").IsChecked);

        host.Click(host.Find<ToggleButton>("Routes.LegDetail.Rhumb"));
        Assert.Equal(RouteLegGeometryType.Loxodrome, route.Legs[1].GeometryType);
    }
}

/// <summary>Unit tests for the route shortcut rules, independent of focus plumbing.</summary>
public sealed class RouteEditorKeysTests
{
    private static (RoutesService Service, Route Route) Make()
    {
        var service = new RoutesService();
        var route = service.Routes.CreateRoute("R");
        route.History.Execute(RouteEditLabels.AppendWaypoint, () => route.AppendWaypoint(new GeoPosition(0, 0)));
        route.History.Execute(RouteEditLabels.AppendWaypoint, () => route.AppendWaypoint(new GeoPosition(0, 1)));
        return (service, route);
    }

    [Theory]
    [InlineData((int)RouteEditorKeys.KeyScope.Map)]
    [InlineData((int)RouteEditorKeys.KeyScope.RoutesPanel)]
    [InlineData((int)RouteEditorKeys.KeyScope.Other)]
    public void Ctrl_E_toggles_edit_mode_outside_text_fields(int scopeValue)
    {
        var scope = (RouteEditorKeys.KeyScope)scopeValue;
        var (service, _) = Make();

        Assert.True(RouteEditorKeys.Handle(service, Key.E, KeyModifiers.Control, scope));
        Assert.True(service.IsEditModeActive);
        Assert.True(RouteEditorKeys.Handle(service, Key.E, KeyModifiers.Meta, scope));
        Assert.False(service.IsEditModeActive);
    }

    [Fact]
    public void No_shortcut_fires_in_a_text_field()
    {
        var (service, route) = Make();
        service.IsEditModeActive = true;
        service.SelectedWaypointIndex = 0;

        Assert.False(RouteEditorKeys.Handle(service, Key.E, KeyModifiers.Control, RouteEditorKeys.KeyScope.TextInput));
        Assert.False(RouteEditorKeys.Handle(service, Key.Z, KeyModifiers.Control, RouteEditorKeys.KeyScope.TextInput));
        Assert.False(RouteEditorKeys.Handle(service, Key.Delete, KeyModifiers.None, RouteEditorKeys.KeyScope.TextInput));
        Assert.True(service.IsEditModeActive);
        Assert.Equal(2, route.Waypoints.Count);
    }

    [Fact]
    public void Bare_E_is_not_a_shortcut()
    {
        var (service, _) = Make();

        Assert.False(RouteEditorKeys.Handle(service, Key.E, KeyModifiers.None, RouteEditorKeys.KeyScope.Map));
        Assert.False(service.IsEditModeActive);
    }

    [Fact]
    public void Undo_and_redo_work_from_the_map()
    {
        var (service, route) = Make();

        Assert.True(RouteEditorKeys.Handle(service, Key.Z, KeyModifiers.Control, RouteEditorKeys.KeyScope.Map));
        Assert.Single(route.Waypoints);
        Assert.True(RouteEditorKeys.Handle(service, Key.Z, KeyModifiers.Control | KeyModifiers.Shift, RouteEditorKeys.KeyScope.Map));
        Assert.Equal(2, route.Waypoints.Count);
    }

    [Fact]
    public void Undo_does_not_reach_into_other_panels()
    {
        var (service, route) = Make();
        service.IsEditModeActive = true;

        Assert.False(RouteEditorKeys.Handle(service, Key.Z, KeyModifiers.Control, RouteEditorKeys.KeyScope.Other));
        Assert.False(RouteEditorKeys.Handle(service, Key.Delete, KeyModifiers.None, RouteEditorKeys.KeyScope.Other));
        Assert.Equal(2, route.Waypoints.Count);
    }
}
