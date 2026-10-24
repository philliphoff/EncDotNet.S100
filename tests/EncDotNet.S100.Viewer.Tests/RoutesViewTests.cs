using Avalonia;
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

    [AvaloniaFact]
    public void A_long_route_name_trims_and_keeps_the_rename_pencil_in_view()
    {
        var (_, route, host) = Show();
        using var _host = host;
        route.Name = "A long coastal passage with twelve waypoints and a deliberately long name to test truncation";
        host.Settle();

        var name = host.Find<TextBlock>("Routes.Active.Name");
        var pencil = host.Find<Button>("Routes.Active.Rename");
        var right = pencil.TranslatePoint(new Point(pencil.Bounds.Width, 0), host.Window)!.Value.X;
        Assert.True(right <= host.Window.Bounds.Width, $"pencil ends at {right}");
        Assert.Contains(name.TextLayout.TextLines, l => l.HasCollapsed);
    }

    [AvaloniaFact]
    public void The_route_name_box_commits_when_it_loses_focus()
    {
        var (_, route, host) = Show();
        using var _host = host;
        host.Click(host.Find<Button>("Routes.Active.Rename"));
        var box = host.Find<TextBox>("Routes.Active.NameBox");
        box.Focus();
        box.Text = "Approach";
        host.Click(host.FindAll<Button>("Routes.Leg")[0]);

        Assert.Equal("Approach", route.Name);
        Assert.False(host.IsShown("Routes.Active.NameBox"));
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

        var check = host.Find<ToggleButton>("Routes.CheckRouteToggle");

        Assert.False(check.IsEnabled); // no check service in this panel
        Assert.Equal("Check the route against your settings; add a draft to check clearance too", ToolTip.GetTip(check));
        Assert.False(host.IsShown("Routes.Done"));
    }

    [AvaloniaFact]
    public void Check_passage_shows_the_verdict_card_and_leg_chips_and_turns_off()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            var fixture = new PassageCheckFixture(post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
            var panel = new RoutesPanelViewModel(fixture.Routes, check: fixture.Check);
            using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 800);

            var check = host.Find<ToggleButton>("Routes.CheckRouteToggle");
            Assert.True(check.IsEnabled);
            Assert.True(host.IsShown("Routes.Waypoint"));

            host.Click(check);
            Assert.True(fixture.Routes.IsCheckModeActive);
            WaitForResult(host, fixture);

            Assert.True(host.IsShown("Routes.Check.Verdict"));
            Assert.Equal("Below your UKC setting in 1 place", host.Find<TextBlock>("Routes.Check.VerdictTitle").Text);
            Assert.Equal("min -1.5 m · required 1.0 m", host.FindAll<TextBlock>("Routes.Check.Summary.Text")[0].Text);
            Assert.True(host.IsShown("Routes.Check.Summary.Clearance"));
            Assert.False(host.IsShown("Routes.Check.Summary.AddDraft"));
            Assert.StartsWith("Not for navigation", host.Find<TextBlock>("Routes.Check.Footer").Text);
            Assert.True(host.IsShown("Routes.Check.HeaderMeta"));
            Assert.Equal(8, host.FindAll<Button>("Routes.Check.Leg").Count);
            // A leg row fills the list's width, so its hover tint and chips reach the edge.
            var leg = host.FindAll<Button>("Routes.Check.Leg")[0];
            Assert.Equal(leg.Bounds.Width - leg.Padding.Left - leg.Padding.Right, ((Control)leg.Content!).Bounds.Width, 1);
            var chips = host.FindAll<Panel>("Routes.Check.Leg.Chip");
            Assert.Contains("destructive", chips[5].Classes);
            Assert.Contains("success", chips[0].Classes);
            Assert.False(host.IsShown("Routes.Waypoint"));

            host.Click(host.Find<TabItem>("Routes.Check.FindingsTab"));
            Assert.True(host.IsShown("Routes.Check.Findings"));
            Assert.False(host.IsShown("Routes.Check.Leg"));
            host.Click(host.Find<TabItem>("Routes.Check.LegsTab"));
            Assert.True(host.IsShown("Routes.Check.Leg"));

            host.Click(check);
            host.Settle();
            Assert.False(fixture.Routes.IsCheckModeActive);
            Assert.False(host.IsShown("Routes.Check.Verdict"));
            Assert.True(host.IsShown("Routes.Waypoint"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [AvaloniaFact]
    public void The_findings_tab_lists_grouped_findings_and_clicking_one_zooms_and_moves_the_cursor()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        try
        {
            var fixture = new PassageCheckFixture(
                waterEnd: PassageCheckFixture.WaterStart.AddHours(2.5),
                post: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
            var viewport = new RouteFindingsTests.RecordingViewport();
            using var findings = RouteFindingsTests.Findings(fixture, viewport);
            var panel = new RoutesPanelViewModel(fixture.Routes, check: fixture.Check, findings: findings);
            using var host = ViewHost.Show(new RoutesView { DataContext = panel }, width: 420, height: 800);

            host.Click(host.Find<ToggleButton>("Routes.CheckRouteToggle"));
            WaitForResult(host, fixture);

            var tab = host.Find<TabItem>("Routes.Check.FindingsTab");
            Assert.Equal($"Findings {findings.Findings.Count}", Avalonia.Automation.AutomationProperties.GetName(tab));
            var count = host.Find<TextBlock>("Routes.Check.FindingsTab.Count");
            Assert.Equal($"{findings.Findings.Count}", count.Text);
            var label = (TextBlock)((StackPanel)count.Parent!).Children[0];
            Assert.NotEqual(label.Foreground?.ToString(), count.Foreground?.ToString());  // the secondary colour
            Assert.Equal(2, findings.Findings.Count);
            Assert.False(host.IsShown("Routes.Check.Findings"));

            host.Click(tab);
            Assert.True(host.IsShown("Routes.Check.Findings"));
            Assert.False(host.IsShown("Routes.Check.Findings.Empty"));
            Assert.True(host.IsShown("Routes.Check.Findings.Group.MustReview"));
            Assert.True(host.IsShown("Routes.Check.Findings.Group.Check"));
            Assert.False(host.IsShown("Routes.Check.Findings.Group.Info"));
            var rows = host.FindAll<Button>("Routes.Check.Finding");
            Assert.Equal(2, rows.Count);
            Assert.Equal(["Clearance", "Data"], host.FindAll<Border>("Routes.Check.Finding.Tag")
                .Select(t => Avalonia.Automation.AutomationProperties.GetName(t)));

            // The summary rows (#959) push the list down: scroll the row into view first.
            rows[0].BringIntoView();
            host.Settle();
            host.Click(rows[0]);
            host.Settle();
            Assert.Equal("clearance.below_setting.0", findings.Selected?.Id);
            Assert.Contains("selected", rows[0].Classes);
            Assert.DoesNotContain("selected", rows[1].Classes);
            Assert.Single(viewport.Zooms);
            Assert.Equal(5, fixture.Check.Cursor?.LegIndex);

            host.Click(host.Find<TabItem>("Routes.Check.LegsTab"));
            Assert.False(host.IsShown("Routes.Check.Findings"));
            Assert.True(host.IsShown("Routes.Check.Leg"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    private static void WaitForResult(ViewHost host, PassageCheckFixture fixture)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        do
        {
            host.Settle();
            // The departure window is swept after the result lands and adds a
            // line to the verdict card; wait for it so the layout is final.
            if (!fixture.Check.IsComputing && !fixture.Check.IsWindowComputing && fixture.Check.Current is not null)
            {
                host.Settle();
                return;
            }
            Thread.Sleep(10);
        }
        while (DateTime.UtcNow < deadline);
        throw new TimeoutException("Check passage didn't produce a result.");
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
    public void Clicking_a_leg_row_selects_the_leg()
    {
        var (service, _, host) = Show();
        using var _host = host;

        host.Click(host.FindAll<Button>("Routes.Leg")[1]);

        Assert.Equal(1, service.SelectedLegIndex);
        Assert.Null(service.SelectedWaypointIndex);
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
    public void Waypoint_etas_show_once_the_route_has_a_speed()
    {
        var (_, route, host) = Show();
        using var _host = host;
        Assert.False(host.IsShown("Routes.Waypoint.Eta"));

        route.SetPassageValue(RoutePassageField.Speed, 10);
        host.Settle();

        var etas = host.FindAll<TextBlock>("Routes.Waypoint.Eta");
        Assert.Equal(3, etas.Count);
        Assert.All(etas, eta => Assert.True(eta.IsEffectivelyVisible));
        Assert.Contains("from the view time", ToolTip.GetTip(etas[1]) as string);
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
