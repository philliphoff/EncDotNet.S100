using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Mapsui.Layers;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Check route's map layer (#959 slice 3, handoff C1–C8): corridor edges,
/// the corridor fill on the selected finding's leg, sleeves for spans,
/// shaped markers for Must review and Check (Info only when selected),
/// leaders, 24 px clusters, one pill, and the folding legend with
/// <c>get_map_legend</c> / <c>set_map_legend</c>. The scenario is the
/// generated "Warnings on route" (no draft): 001/26 crosses in force (Must
/// review), 003/26 and 006/26 are near (Check), and 002/26, 005/26 and
/// 007/26 are Info.
/// </summary>
public sealed class RouteCheckMapTests(WarningsScenarioFixture fixture) : IClassFixture<WarningsScenarioFixture>, IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    public static TheoryData<PaletteType> Palettes => [PaletteType.Day, PaletteType.Dusk, PaletteType.Night];

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private static T Value<T>(ToolResult<T> result) where T : class =>
        result.TryGetValue(out var value) ? value! : throw new Xunit.Sdk.XunitException(result.TryGetError(out var e) ? e.Message : "error");

    private static MeasureOverlayAppearance Appearance(PaletteType palette) => MeasureOverlayAppearance.Default with
    {
        Palette = palette,
        ChartBackground = MeasureOverlayAppearance.ChartBackgroundFor(palette),
    };

    private static RouteCheckOverlay Overlay(WarningsRouteCheck s, RouteFinding? selected = null, PaletteType palette = PaletteType.Day) =>
        RouteCheckOverlay.Build(true, s.Check.Current, false, null, Appearance(palette), DepthUnit.Metres, TimeFormat.Utc,
            selected, s.Findings.Findings, hasDraft: false, route: s.Route, viewTime: s.Routes.ViewTime.Current)!;

    private async Task<WarningsRouteCheck> CheckedAsync()
    {
        var s = WarningsRouteCheck.Create(fixture);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        Assert.NotEmpty(s.Findings.Findings);
        return s;
    }

    [Fact]
    public async Task Markers_show_for_must_review_and_check_and_info_only_when_selected()
    {
        using var s = await CheckedAsync();
        var findings = s.Findings.Findings;
        var map = Overlay(s).Map;

        // C4: a marker per Must review and Check finding, none for Info.
        var expected = findings.Where(f => f.Group != RouteFindingGroup.Info && f.Location is not null).Select(f => f.Id);
        Assert.Equal(expected, map.Marks.Select(m => m.FindingId));
        Assert.DoesNotContain(map.Marks, m => m.Group == RouteFindingGroup.Info);
        Assert.All(map.Sleeves, sl => Assert.NotEqual(RouteFindingGroup.Info, sl.Group));
        Assert.All(map.Marks, m => Assert.Null(m.Pill));
        Assert.Empty(map.FilledLegs);

        // Selected, an Info finding shows, ringed with the one pill.
        var info = findings.First(f => f.Group == RouteFindingGroup.Info && f.Location is not null);
        var selected = Overlay(s, info).Map;
        var mark = Assert.Single(selected.Marks, m => m.IsSelected);
        Assert.Equal((info.Id, RouteFindingGroup.Info), (mark.FindingId, mark.Group));
        Assert.StartsWith(info.Title, mark.Pill, StringComparison.Ordinal);
        Assert.Single(selected.Marks, m => m.Pill is not null);
    }

    [Fact]
    public async Task The_corridor_has_both_edges_on_every_leg_and_fills_only_the_selected_findings_leg()
    {
        using var s = await CheckedAsync();
        var map = Overlay(s).Map;

        // C1: one corridor leg per route leg, its edges at the leg's cross-track limits.
        Assert.Equal(s.Route.Legs.Count, map.Corridor.Count);
        var start = s.Route.Waypoints[0].Position;
        var port = s.Route.GetEffectiveLegValue(0, RoutePassageField.PortCrossTrack) ?? 50;
        var starboard = s.Route.GetEffectiveLegValue(0, RoutePassageField.StarboardCrossTrack) ?? 50;
        Assert.Equal(port, PassageGeodesy.DistanceMeters(start, map.Corridor[0].Port[0], LegGeometry.GreatCircle), 1);
        Assert.Equal(starboard, PassageGeodesy.DistanceMeters(start, map.Corridor[0].Starboard[0], LegGeometry.GreatCircle), 1);
        Assert.Equal(port + starboard, PassageGeodesy.DistanceMeters(map.Corridor[0].Port[0], map.Corridor[0].Starboard[0], LegGeometry.GreatCircle), 1);

        // C3: the fill follows the selected finding's legs.
        var review = s.Findings.Findings.First(f => f.Group == RouteFindingGroup.MustReview);
        var filled = Overlay(s, review).Map.FilledLegs;
        Assert.NotEmpty(filled);
        Assert.Equal(review.LegIndex, filled[0]);
        Assert.Equal(filled, filled.Order().Distinct());

        // A cross-track limit moves that side's edge; without one it's the 50 m default.
        s.Route.SetLegOverride(0, RoutePassageField.PortCrossTrack, 200);
        s.Route.SetLegOverride(0, RoutePassageField.StarboardCrossTrack, null);
        s.Route.SetPassageValue(RoutePassageField.StarboardCrossTrack, null);
        var wide = Overlay(s).Map;
        Assert.Equal(200, PassageGeodesy.DistanceMeters(start, wide.Corridor[0].Port[0], LegGeometry.GreatCircle), 1);
        Assert.Equal(50, PassageGeodesy.DistanceMeters(start, wide.Corridor[0].Starboard[0], LegGeometry.GreatCircle), 1);
    }

    [Fact]
    public void A_point_finding_off_the_centreline_gets_a_leader()
    {
        var route = new Route("r", "Test");
        route.AppendWaypoint(new GeoPosition(50, -1));
        route.AppendWaypoint(new GeoPosition(50, -0.9));
        RouteFinding Point(string id, double northMeters) => TestFinding.Make(
            id, RouteFindingCodes.DangerNearRoute, RouteFindingGroup.Check, RouteFindingCategory.Chart, "Wreck", "meta", 0, 1000,
            RouteFindingLocation.Point(new GeoPosition(50 + (northMeters / 111_195), -1 + (1000 / (111_195 * Math.Cos(50 * Math.PI / 180)))), 1000));

        var map = RouteCheckMap.Build(route, [Point("near", 20), Point("far", 200)], null, RouteCheckColors.For(PaletteType.Day),
            SKColors.Blue, PaletteType.Day, DateTimeOffset.UnixEpoch, TimeFormat.Utc, 50);

        // C5: beyond 40 m (or the corridor), a leader to the feature; the marker stands on the route.
        Assert.Null(map.Marks.Single(m => m.FindingId == "near").Feature);
        var far = map.Marks.Single(m => m.FindingId == "far");
        Assert.NotNull(far.Feature);
        Assert.Equal(50, far.Anchor.Latitude, 6);
        Assert.Equal(1000, PassageGeodesy.DistanceMeters(route.Waypoints[0].Position, far.Anchor, LegGeometry.Rhumb), 0);
    }

    // Marks at screen x = 100 + longitude (degrees as pixels), on one row.
    private static RouteCheckMapLayout.Placement Place(params (string Id, double X, RouteFindingGroup Group, bool Selected)[] marks)
    {
        var map = RouteCheckMap.Empty with
        {
            Marks = marks.Select(m => new RouteCheckMap.Mark(m.Id, new GeoPosition(0, m.X), null, m.Group, SKColors.Red, m.Selected, null)).ToList(),
        };
        return RouteCheckMapLayout.Place(map, p => new Point(100 + p.Longitude, 200));
    }

    [Fact]
    public void Markers_within_24_px_cluster_in_the_worst_groups_shape()
    {
        // C6: a and b are 20 px apart, c is 40 px from a.
        var placement = Place(("a", 0, RouteFindingGroup.Check, false), ("b", 20, RouteFindingGroup.MustReview, false), ("c", 60, RouteFindingGroup.Check, false));
        var cluster = Assert.Single(placement.Clusters);
        Assert.Equal(RouteFindingGroup.MustReview, cluster.Group);
        Assert.Equal(["b", "a"], cluster.Members.Select(m => m.Mark.FindingId));
        Assert.Equal(["c"], placement.Markers.Select(m => m.Mark.FindingId));
        Assert.Equal(110, cluster.Center.X, 6);
        Assert.Equal(200 - RouteCheckMapLayout.StemLength, cluster.Center.Y, 6);

        // 25 px apart: no cluster.
        Assert.Empty(Place(("a", 0, RouteFindingGroup.Check, false), ("b", 25, RouteFindingGroup.Check, false)).Clusters);

        // The selected marker is never clustered, and draws last.
        var selected = Place(("a", 0, RouteFindingGroup.Check, false), ("b", 10, RouteFindingGroup.Check, true));
        Assert.Empty(selected.Clusters);
        Assert.Equal("b", selected.Markers[^1].Mark.FindingId);

        // Hit-testing finds the marker, then the cluster.
        Assert.Equal("c", RouteCheckMapLayout.HitTest(placement, new Point(160, 200 - RouteCheckMapLayout.StemLength))?.Marker?.Mark.FindingId);
        Assert.Same(cluster, RouteCheckMapLayout.HitTest(placement, cluster.Center)?.Cluster);
        Assert.Null(RouteCheckMapLayout.HitTest(placement, new Point(160, 200 + 40)));
    }

    [Fact]
    public async Task A_route_finding_is_ringed_not_outlined_and_a_clearance_finding_keeps_its_outline()
    {
        using var s = await CheckedAsync();
        var review = s.Findings.Findings.First(f => f.Group == RouteFindingGroup.MustReview);
        var focus = Overlay(s, review).Finding!;
        Assert.False(focus.IsOutlined);
        Assert.StartsWith("Zoomed to finding", focus.ChipText, StringComparison.Ordinal);

        var clearance = TestFinding.Make("clearance.below_setting.0", "clearance.below_setting", RouteFindingGroup.MustReview, RouteFindingCategory.Clearance,
            "Below your UKC setting", "meta", 0, 100, RouteFindingLocation.Point(s.Route.Waypoints[0].Position, 100));
        Assert.True(Overlay(s, clearance).Finding!.IsOutlined);
        Assert.Null(Overlay(s, clearance).Map.SelectedMark);
    }

    [Fact]
    public async Task Select_finding_reports_the_map_highlight()
    {
        using var s = await CheckedAsync();
        var select = new SelectFindingTool(s.Routes, new InlineInvoker(), s.Check, s.Findings);
        var review = s.Findings.Findings.First(f => f.Group == RouteFindingGroup.MustReview);

        var result = Value(await select.InvokeAsync(new SelectFindingRequest(review.Id, Zoom: false), TestContext.Current.CancellationToken));
        var highlight = Assert.IsType<MapHighlightDto>(result.Highlight);
        Assert.Equal(review.Location!.IsPoint ? "marker" : "marker_and_sleeve", highlight.Kind);
        Assert.Equal("octagon", highlight.Shape);
        Assert.Contains(review.LegIndex!.Value, highlight.CorridorFilledLegs);

        var check = s.Findings.Findings.First(f => f.Group == RouteFindingGroup.Check);
        Assert.Equal("triangle", Value(await select.InvokeAsync(new SelectFindingRequest(check.Id, Zoom: false), TestContext.Current.CancellationToken)).Highlight!.Shape);

        var cleared = Value(await select.InvokeAsync(new SelectFindingRequest(null), TestContext.Current.CancellationToken));
        Assert.Equal(("none", 0), (cleared.Highlight!.Kind, cleared.Highlight.CorridorFilledLegs.Count));
    }

    [Fact]
    public async Task Map_legend_tools_read_and_fold_the_legend_and_the_fold_persists()
    {
        using var s = await CheckedAsync();
        var settings = new ViewerSettings { IsReadOnly = true };
        var state = new RouteCheckLegendState(settings);
        var get = new GetMapLegendTool(state, s.Routes, s.Check, new InlineInvoker());
        var set = new SetMapLegendTool(state, s.Routes, s.Check, new InlineInvoker());

        var legend = Value(await get.InvokeAsync(TestContext.Current.CancellationToken));
        Assert.Equal((true, true), (legend.Shown, legend.Open));
        // No draft: no "UKC AT ETA".
        var findings = Assert.Single(legend.Sections);
        Assert.Equal("FINDINGS", findings.Title);
        Assert.Equal(["Must review", "Check", "Info (on select)", "Stretch of the route", "Cross-track corridor"], findings.Rows);

        var folded = Value(await set.InvokeAsync(false, TestContext.Current.CancellationToken));
        Assert.False(folded.Open);
        Assert.False(settings.IsRouteCheckLegendOpen);
        Assert.False(new RouteCheckLegendState(settings).IsOpen);

        // With a draft the legend starts with "UKC AT ETA".
        s.Route.SetPassageValue(RoutePassageField.Draft, 5);
        await s.Check.WhenIdle();
        Assert.Equal(["UKC AT ETA", "FINDINGS"], Value(await get.InvokeAsync(TestContext.Current.CancellationToken)).Sections.Select(x => x.Title));
    }

    [AvaloniaFact]
    public void The_legend_folds_to_a_chip_from_its_header()
    {
        var routes = new RoutesService();
        var route = new Route("r", "Test");
        route.AppendWaypoint(new GeoPosition(50, -1));
        route.AppendWaypoint(new GeoPosition(50, -0.9));
        routes.Routes.Add(route);
        routes.Routes.SetActiveRoute(route);
        using var check = new PassageCheckService(routes, new PassageCheckFixture.FixedCatalog([]), debounce: TimeSpan.Zero);
        var state = new RouteCheckLegendState();
        using var legend = new RouteCheckLegendViewModel(routes, check, null, state);
        using var host = ViewHost.Show(new RouteCheckLegendView { DataContext = legend }, width: 400, height: 400);

        // Shown only while Check route is on.
        Assert.False(host.IsShown("Map.RouteCheckLegend.Toggle"));
        routes.IsCheckModeActive = true;
        host.Settle();
        Assert.True(host.IsShown("Map.RouteCheckLegend.Findings"));
        Assert.False(host.IsShown("Map.RouteCheckLegend.Ukc"));
        var toggle = host.Find<Button>("Map.RouteCheckLegend.Toggle");
        Assert.Equal("FINDINGS", Avalonia.Automation.AutomationProperties.GetName(toggle));

        // C8: the header folds it to a 28 px "Legend" chip, and back.
        host.Click(toggle);
        host.Settle();
        Assert.False(state.IsOpen);
        Assert.False(host.IsShown("Map.RouteCheckLegend.Findings"));
        Assert.Equal("Legend", Avalonia.Automation.AutomationProperties.GetName(toggle));
        var box = (Control)toggle.Parent!.Parent!;
        Assert.Equal(28, box.Bounds.Height, 0);

        host.Click(toggle);
        host.Settle();
        Assert.True(state.IsOpen);
        Assert.True(host.IsShown("Map.RouteCheckLegend.Findings"));
    }

    [AvaloniaFact]
    public void The_legend_glyphs_sit_level_with_their_text()
    {
        var routes = new RoutesService();
        var route = new Route("r", "Test");
        route.AppendWaypoint(new GeoPosition(50, -1));
        route.AppendWaypoint(new GeoPosition(50, -0.9));
        route.SetPassageValue(RoutePassageField.Draft, 5);
        routes.Routes.Add(route);
        routes.Routes.SetActiveRoute(route);
        using var check = new PassageCheckService(routes, new PassageCheckFixture.FixedCatalog([]), debounce: TimeSpan.Zero);
        using var legend = new RouteCheckLegendViewModel(routes, check, null, new RouteCheckLegendState());
        using var host = ViewHost.Show(new RouteCheckLegendView { DataContext = legend }, width: 400, height: 400);
        routes.IsCheckModeActive = true;
        host.Settle();
        Assert.True(host.IsShown("Map.RouteCheckLegend.Ukc"));

        // Every row: the glyph's centre line and the text's centre within a pixel.
        var rows = host.All<StackPanel>()
            .Where(r => r.Classes.Contains("row") && r.IsEffectivelyVisible).ToList();
        Assert.Equal(10, rows.Count);
        foreach (var row in rows)
        {
            var glyph = (Control)row.Children[0];
            var text = Assert.IsType<TextBlock>(row.Children[1]);
            var glyphMiddle = glyph is Line line
                ? glyph.Bounds.Top + line.StartPoint.Y
                : glyph.Bounds.Top + glyph.Bounds.Height / 2;
            Assert.True(Math.Abs(glyphMiddle - (text.Bounds.Top + text.Bounds.Height / 2)) <= 1, text.Text);
        }
    }

    /// <summary>
    /// The scenario route checked without a draft (2a): the accent line, the
    /// corridor's dotted edges, the Must review sleeve and markers above the
    /// route, on the palette's chart background. Nothing selected.
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public async Task Check_mode_route_findings(PaletteType palette)
    {
        using var s = await CheckedAsync();
        await Verify(Render(s, palette, null), "png").UseParameters(palette);
    }

    /// <summary>
    /// The same with the Must review finding selected: its sleeve wider, its
    /// leg's corridor filled, its marker ringed with the pill, and the
    /// "Zoomed to finding" chip.
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public async Task Check_mode_selected_route_finding(PaletteType palette)
    {
        using var s = await CheckedAsync();
        var review = s.Findings.Findings.First(f => f.Group == RouteFindingGroup.MustReview);
        await Verify(Render(s, palette, review), "png").UseParameters(palette);
    }

    private static byte[] Render(WarningsRouteCheck s, PaletteType palette, RouteFinding? selected)
    {
        const int width = 900;
        const int height = 420;
        var appearance = Appearance(palette);
        var scene = RouteOverlayScene.Build(s.Routes.Routes, null, null, false, appearance, Overlay(s, selected, palette));

        var points = s.Route.Waypoints.Select(w => Mapsui.Projections.SphericalMercator.FromLonLat(w.Position.Longitude, w.Position.Latitude)).ToList();
        var (minX, maxX) = (points.Min(p => p.x), points.Max(p => p.x));
        var (minY, maxY) = (points.Min(p => p.y), points.Max(p => p.y));
        var resolution = Math.Max((maxX - minX) / (width - 160), (maxY - minY) / (height - 160));
        var viewport = new Mapsui.Viewport((minX + maxX) / 2, (minY + maxY) / 2, resolution, 0, width, height);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var (r, g, b) = appearance.ChartBackground;
        surface.Canvas.Clear(new SKColor(r, g, b));
        RouteOverlayRenderer.Instance.Draw(surface.Canvas, viewport, new MemoryLayer(), new Mapsui.Layers.PointFeature(0, 0), new RouteOverlayStyle { Scene = scene }, null!, 0);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
