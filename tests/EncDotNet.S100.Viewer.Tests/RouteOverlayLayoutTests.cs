using Avalonia;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;
using Mapsui.UI.Avalonia;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The route overlay's screen layout: label placement, insert handles and colours.</summary>
public sealed class RouteOverlayLayoutTests
{
    // A flat test projection: 1° of longitude is 1000 px to the right, 1° of
    // latitude 1000 px up. Linear, so midpoints and lengths are easy to check.
    private static Point? Flat(GeoPosition p) => new Point(p.Longitude * 1000, -p.Latitude * 1000);

    private static double FixedWidth(string _) => 80.0;

    private static RouteOverlayScene.SceneRoute Route(params (double Lat, double Lon)[] points)
    {
        var route = new Route();
        foreach (var (lat, lon) in points)
            route.AppendWaypoint(new GeoPosition(lat, lon));
        return RouteOverlayScene.SceneRoute.From(route, isActive: true);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(45, 45)]
    [InlineData(90, 90)]
    [InlineData(91, -89)]
    [InlineData(180, 0)]
    [InlineData(-90, 90)]
    [InlineData(-135, 45)]
    [InlineData(270, 90)]
    [InlineData(-200, -20)]
    public void Labels_are_never_upside_down(double lineAngle, double expected)
    {
        var upright = RouteOverlayLayout.Upright(lineAngle);

        Assert.InRange(upright, -90.0 + 1e-9, 90.0);
        Assert.Equal(expected, upright, 9);
    }

    [Fact]
    public void Every_direction_gives_an_upright_label_above_its_line()
    {
        for (var angle = -360; angle <= 360; angle += 15)
        {
            var placement = RouteOverlayLayout.PlaceLabel(
                new Point(0, 0), angle, legLengthPx: 500, labelWidthPx: 80, isSelected: false, besideHandle: false);

            Assert.NotNull(placement);
            Assert.InRange(placement!.Value.AngleDegrees, -90.0 + 1e-9, 90.0);
            // "Above" in the label's own frame: the offset points to screen-up
            // relative to the upright text, i.e. its y component in the label
            // frame is negative.
            var radians = placement.Value.AngleDegrees * Math.PI / 180;
            var localY = -placement.Value.Center.X * Math.Sin(radians) + placement.Value.Center.Y * Math.Cos(radians);
            Assert.Equal(-RouteOverlayLayout.LabelOffsetPx, localY, 6);
        }
    }

    [Fact]
    public void Short_legs_drop_their_label_unless_selected()
    {
        var shortLeg = RouteOverlayLayout.PlaceLabel(new Point(0, 0), 0, legLengthPx: 60, labelWidthPx: 80, isSelected: false, besideHandle: false);
        var selected = RouteOverlayLayout.PlaceLabel(new Point(0, 0), 0, legLengthPx: 60, labelWidthPx: 80, isSelected: true, besideHandle: false);
        var longLeg = RouteOverlayLayout.PlaceLabel(new Point(0, 0), 0, legLengthPx: 200, labelWidthPx: 80, isSelected: false, besideHandle: false);

        Assert.Null(shortLeg);
        Assert.NotNull(selected);
        Assert.NotNull(longLeg);
    }

    [Fact]
    public void A_label_beside_a_handle_moves_along_the_line_and_needs_more_room()
    {
        var placement = RouteOverlayLayout.PlaceLabel(new Point(0, 0), 0, legLengthPx: 200, labelWidthPx: 80, isSelected: false, besideHandle: true);
        var tooShort = RouteOverlayLayout.PlaceLabel(new Point(0, 0), 0, legLengthPx: 120, labelWidthPx: 80, isSelected: false, besideHandle: true);

        Assert.Equal(RouteOverlayLayout.LabelHandleClearancePx, placement!.Value.Center.X, 6);
        Assert.Null(tooShort);
    }

    [Fact]
    public void Insert_handles_show_only_on_legs_longer_than_40_px_while_editing()
    {
        // Leg 0 is 30 px long, leg 1 is 50 px.
        var route = Route((0, 0), (0, 0.030), (0, 0.080));

        var editing = RouteOverlayLayout.Layout(route, Flat, FixedWidth, selectedLeg: null, isEditing: true);
        var notEditing = RouteOverlayLayout.Layout(route, Flat, FixedWidth, selectedLeg: null, isEditing: false);

        Assert.Equal(30, editing[0].LengthPx, 6);
        Assert.False(editing[0].ShowInsertHandle);
        Assert.True(editing[1].ShowInsertHandle);
        Assert.All(notEditing, leg => Assert.False(leg.ShowInsertHandle));
    }

    [Fact]
    public void The_insert_handle_sits_at_the_legs_midpoint()
    {
        var route = Route((50.0, -1.0), (50.0, -1.2));

        var leg = RouteOverlayLayout.Layout(route, Flat, FixedWidth, null, isEditing: true)[0];

        Assert.Equal(new Point(-1100, -50000), leg.Midpoint);
        Assert.Equal(50.0, leg.MidpointPosition!.Value.Latitude, 9);
        Assert.Equal(-1.1, leg.MidpointPosition!.Value.Longitude, 9);
    }

    [Fact]
    public void The_selected_leg_keeps_its_label_however_short()
    {
        var route = Route((0, 0), (0, 0.010), (0, 0.300));

        var layout = RouteOverlayLayout.Layout(route, Flat, FixedWidth, selectedLeg: 0, isEditing: true);

        Assert.NotNull(layout[0].Label);
        Assert.NotNull(layout[1].Label);

        layout = RouteOverlayLayout.Layout(route, Flat, FixedWidth, selectedLeg: null, isEditing: true);
        Assert.Null(layout[0].Label);
    }

    [Fact]
    public void A_leg_across_the_antimeridian_gets_no_handle_or_label()
    {
        var route = Route((10, 179.5), (10, -179.5));

        var leg = RouteOverlayLayout.Layout(route, Flat, FixedWidth, selectedLeg: 0, isEditing: true)[0];

        Assert.Null(leg.Midpoint);
        Assert.False(leg.ShowInsertHandle);
        Assert.Null(leg.Label);
    }

    [Fact]
    public void Leg_labels_read_distance_and_bearing()
    {
        var label = RouteOverlayScene.LegLabel(2.5, 320);

        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:0.0} NM · 320°", 2.5), label);
    }

    [Theory]
    [InlineData(PaletteType.Day, 255)]
    [InlineData(PaletteType.Dusk, 0)]
    [InlineData(PaletteType.Night, 0)]
    public void The_casing_is_white_by_day_and_black_at_night(PaletteType palette, byte expected)
    {
        var colors = RouteOverlayColors.For(MeasureOverlayAppearance.Default with { Palette = palette });

        Assert.Equal(new SKColor(expected, expected, expected), colors.Casing);
    }

    [Fact]
    public void The_edit_colour_is_the_accent()
    {
        var colors = RouteOverlayColors.For(new MeasureOverlayAppearance((10, 20, 30), false));

        Assert.Equal(new SKColor(10, 20, 30), colors.Edit);
    }

    [Fact]
    public void The_scene_snapshots_the_selection_and_edit_mode()
    {
        var routes = new RouteCollection();
        routes.CreateRoute("Other").AppendWaypoint(new GeoPosition(1, 1));
        var active = routes.CreateRoute("Active");
        active.AppendWaypoint(new GeoPosition(0, 0));
        active.AppendWaypoint(new GeoPosition(0, 1));

        var scene = RouteOverlayScene.Build(routes, null, 0, isEditing: true, MeasureOverlayAppearance.Default);

        Assert.Equal(2, scene.Routes.Count);
        Assert.Same(scene.Routes[1], scene.ActiveRoute);
        Assert.Equal(0, scene.SelectedLegIndex);
        Assert.True(scene.IsEditing);

        // A snapshot: later edits don't reach it.
        active.MoveWaypoint(0, new GeoPosition(5, 5));
        Assert.Equal(0, scene.ActiveRoute!.Waypoints[0].Latitude);
    }

    [AvaloniaFact]
    public void Clicking_an_insert_handle_inserts_at_the_legs_midpoint()
    {
        var service = new RoutesService();
        var route = service.Routes.CreateRoute("R");
        route.AppendWaypoint(new GeoPosition(0, 0));
        route.AppendWaypoint(new GeoPosition(0, 0.100)); // 100 px: has a handle
        route.AppendWaypoint(new GeoPosition(0, 0.130)); // 30 px: no handle
        var tool = new RouteEditTool(service);
        tool.OnActivated(new MapToolContext(
            new MapControl(), _ => { }, _ => { }, _ => { }, () => { }, _ => null, Flat));

        Assert.Equal(0, tool.HitTestHandle(new Point(52, -3)));
        Assert.Null(tool.HitTestHandle(new Point(115, 0)));
        Assert.Null(tool.HitTestHandle(new Point(20, 0)));

        route.History.Execute(RouteEditLabels.InsertWaypoint, () => tool.InsertAtHandle(route, 0));

        Assert.Equal(4, route.Waypoints.Count);
        Assert.Equal(0.050, route.Waypoints[1].Position.Longitude, 9);
        Assert.Equal(1, service.SelectedWaypointIndex);
        route.History.Undo();
        Assert.Equal(3, route.Waypoints.Count);
    }
}
