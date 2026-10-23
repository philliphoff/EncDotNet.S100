using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The findings list (#905): the clearance findings, grouping, tags, the
/// Findings segment count, click-to-zoom and the profile cursor, the map
/// outline, and the <c>get_findings</c> / <c>select_finding</c> tools, on
/// <see cref="PassageCheckFixture"/>.
/// </summary>
public sealed class RouteFindingsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public RouteFindingsTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class Utc : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    internal sealed class RecordingViewport : IMapViewportController
    {
        public List<MRect> Zooms { get; } = [];

        public void ZoomToExtent(MRect extent) => Zooms.Add(extent);

        public void SetViewportToExtent(MRect mercatorExtent) { }

        public void SetViewportToCenterAndResolution(MPoint mercatorCenter, double resolution) { }

        public void SetRotation(double degrees) { }

        public void CenterOn(double latitudeWgs84, double longitudeWgs84, long durationMs = 300) { }

        public GeoPosition? TryGetViewportCenterWgs84() => null;
    }

    internal static RouteFindingsService Findings(PassageCheckFixture f, RecordingViewport? viewport = null, Action<Action>? post = null) =>
        new(f.Check, f.Routes, f.Catalog,
            viewport is null ? null : new StaticCapabilityAccessor<IMapViewportController>(viewport),
            timeFormat: new Utc(),
            clock: new FixedClock(Now),
            post: post);

    private static T Value<T>(ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var e) ? e.Message : "");
        return value!;
    }

    [Fact]
    public async Task The_fixture_route_has_one_must_review_clearance_finding_on_leg_6_to_7()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f);
        await f.CheckAsync();

        var finding = Assert.Single(findings.Findings);
        Assert.Equal("clearance.below_setting.0", finding.Id);
        Assert.Equal(RouteFindingCodes.BelowSetting, finding.Code);
        Assert.Equal(RouteFindingGroup.MustReview, finding.Group);
        Assert.Equal(RouteFindingCategory.Clearance, finding.Category);
        Assert.Matches(@"^Below your UKC setting for \d+ min$", finding.Title);
        Assert.StartsWith("Leg 6 → 7 · min -1.5 m at ", finding.Meta);
        Assert.Equal(5, finding.LegIndex);

        // The span covers the shoal (0,105–0,115° E); the cursor goes to its minimum.
        var location = Assert.IsType<RouteFindingLocation>(finding.Location);
        Assert.False(location.IsPoint);
        Assert.InRange(location.Path[0].Longitude, 0.100, 0.106);
        Assert.InRange(location.Path[^1].Longitude, 0.114, 0.120);
        Assert.Equal(f.Check.Current!.Result.Minimum!.DistanceMeters, finding.Distance?.TotalMetres);
    }

    [Fact]
    public async Task Each_kind_gets_its_group_tag_icon_and_tone()
    {
        var f = new PassageCheckFixture(
            waterEnd: PassageCheckFixture.WaterStart.AddHours(2.5),
            s101FallbackUntilLon: 0.03,
            extraDatasets:
            [
                PassageCheckFixture.CurrentForecast("111EXPIRED", Now.AddDays(-2), Now.AddDays(-1)),
                PassageCheckFixture.CurrentForecast("111CURRENT", Now.AddDays(-1), Now.AddDays(1)),
                PassageCheckFixture.CurrentForecast("111ELSEWHERE", Now.AddDays(-2), Now.AddDays(-1), new BoundingBox(10, 10, 11, 11)),
            ]);
        using var findings = Findings(f);
        await f.CheckAsync();

        var rows = findings.Findings.Select(x => new RouteFindingRowViewModel(x)).ToList();
        var below = rows.Single(r => r.Finding.Code == RouteFindingCodes.BelowSetting);
        var s101 = rows.Single(r => r.Finding.Code == RouteFindingCodes.ChartedDepthFromS101);
        var ends = rows.Single(r => r.Finding.Code == RouteFindingCodes.WaterLevelEnds);

        Assert.Equal(("Clearance", RouteFindingIcon.Octagon, CheckTone.Destructive), (below.Tag, below.Icon, below.Tone));

        Assert.Equal("Charted depth from S-101 only", s101.Title);
        Assert.StartsWith("Legs 1 → 3 · ", s101.Meta); // the S-101 stretch runs into leg 2 → 3
        Assert.EndsWith("NM outside S-102 coverage", s101.Meta);
        Assert.Equal(RouteFindingGroup.Check, s101.Finding.Group);
        Assert.Equal(("Confidence", RouteFindingIcon.Waves, CheckTone.Neutral), (s101.Tag, s101.Icon, s101.Tone));

        Assert.Equal("Water-level data ends before arrival", ends.Title);
        Assert.Contains("tide not applied for", ends.Meta);
        Assert.Equal(RouteFindingGroup.Check, ends.Finding.Group);
        Assert.Equal(("Data", RouteFindingIcon.Triangle, CheckTone.Warning), (ends.Tag, ends.Icon, ends.Tone));

        // An expired S-111 forecast isn't reported: currents aren't applied to ETAs (#1033).
        Assert.DoesNotContain(rows, r => r.Finding.Code.Value == "data.s111_forecast_expired");

        // F5: red only for must-review findings.
        Assert.All(rows.Where(r => r.Finding.Group != RouteFindingGroup.MustReview), r => Assert.NotEqual(CheckTone.Destructive, r.Tone));

        // F1: grouped MUST REVIEW, CHECK, INFO, then by distance along the route.
        Assert.Equal(findings.Findings.OrderBy(x => x.Group).ThenBy(x => x.Distance?.TotalMetres ?? double.MaxValue), findings.Findings);
        Assert.Equal(["Route check", "Route check", "Route check", "S-124", "Clearance", "Confidence", "Data"],
            Enum.GetValues<RouteFindingCategory>().Select(RouteFindingRowViewModel.TagOf));
    }

    [Fact]
    public async Task Groups_render_in_order_and_the_findings_segment_shows_the_count()
    {
        // The panel listens to both services, so their results are serialised as the UI thread would.
        var f = new PassageCheckFixture(waterEnd: PassageCheckFixture.WaterStart.AddHours(2.5),
            post: PassageCheckFixture.SerialPost);
        using var findings = Findings(f, post: PassageCheckFixture.SerialPost);
        var panel = new RoutesPanelViewModel(f.Routes, timeFormat: new Utc(), check: f.Check, findings: findings);
        Assert.Equal("Findings 0", panel.Check!.FindingsLabel);

        await f.CheckAsync();

        var list = Assert.IsType<RouteFindingsViewModel>(panel.Check.Findings);
        // The clearance check has no Info findings, so only the groups with findings show.
        Assert.Equal(["MUST REVIEW", "CHECK"], list.Groups.Select(g => g.Label));
        Assert.Equal(findings.Findings.Count, list.Count);
        Assert.Equal(list.Groups.Sum(g => g.Items.Count), list.Count);
        Assert.Equal($"Findings {list.Count}", panel.Check.FindingsLabel);
        Assert.False(list.IsEmpty);

        f.Routes.IsCheckModeActive = false;
        await f.Check.WhenIdle();
        Assert.Equal("Findings 0", panel.Check.FindingsLabel);
        Assert.Empty(list.Groups);
        Assert.True(list.IsEmpty);
    }

    [Fact]
    public async Task Clicking_a_finding_zooms_the_map_to_it_and_moves_the_profile_cursor_there()
    {
        var f = new PassageCheckFixture();
        var viewport = new RecordingViewport();
        using var findings = Findings(f, viewport);
        var list = new RouteFindingsViewModel(findings);
        await f.CheckAsync();

        var row = list.Groups[0].Items[0];
        list.SelectCommand.Execute(row);

        Assert.Same(row.Finding, findings.Selected);
        Assert.True(row.IsSelected);
        var cursor = Assert.IsType<PassageCursor>(f.Check.Cursor);
        Assert.Equal(row.Finding.Distance!.Value.TotalMetres, cursor.DistanceMeters, 3);
        Assert.Equal(5, cursor.LegIndex);

        var zoom = Assert.Single(viewport.Zooms);
        foreach (var p in row.Finding.Location!.Path)
        {
            var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
            Assert.True(zoom.Grow(1).Contains(new MPoint(x, y)));
        }

        // The profile readout follows the cursor to the finding.
        var clearance = new ClearanceViewModel(f.Check, timeFormat: new Utc());
        Assert.Equal(5, clearance.ReadoutSample!.LegIndex);
    }

    [Fact]
    public async Task A_point_finding_zooms_to_at_least_a_kilometre()
    {
        var finding = TestFinding.Make("x", "x", RouteFindingGroup.Check, RouteFindingCategory.Chart, "t", "m", 0, 0,
            RouteFindingLocation.Point(new GeoPosition(50, -1), 0));
        var extent = Assert.IsType<MRect>(RouteFindingsService.ResolveZoomExtent(finding));
        Assert.Equal(2 * RouteFindingsService.PointZoomHalfMetres, extent.Width, 6);
        Assert.Null(RouteFindingsService.ResolveZoomExtent(TestFinding.With(finding, clearLocation: true)));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_map_outlines_the_selected_finding_in_its_tone_with_the_chip()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f);
        await f.CheckAsync();
        findings.Select(findings.Findings[0]);

        var overlay = Assert.IsType<RouteCheckOverlay>(RouteCheckOverlay.Build(
            true, f.Check.Current, false, f.Check.Cursor, MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc,
            findings.Selected));
        var focus = Assert.IsType<RouteCheckOverlay.FindingFocus>(overlay.Finding);
        Assert.Equal(overlay.Colors.Below, focus.Tone);
        Assert.Equal("Zoomed to finding · leg 6 → 7", focus.ChipText);
        Assert.Equal(findings.Selected!.Location!.Path, focus.Path);

        findings.Select(null);
        Assert.Null(RouteCheckOverlay.Build(
            true, f.Check.Current, false, f.Check.Cursor, MeasureOverlayAppearance.Default, DepthUnit.Metres, TimeFormat.Utc,
            findings.Selected)!.Finding);
    }

    [Fact]
    public async Task The_renderer_outlines_the_selected_finding_dashed_in_its_tone()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f);
        await f.CheckAsync();
        findings.Select(findings.Findings[0], zoom: false);

        var appearance = MeasureOverlayAppearance.Default with { Palette = PaletteType.Night };
        var check = RouteCheckOverlay.Build(true, f.Check.Current, false, f.Check.Cursor, appearance, DepthUnit.Metres, TimeFormat.Utc, findings.Selected)!;
        var style = new RouteOverlayStyle { Scene = RouteOverlayScene.Build(f.Routes.Routes, null, null, false, appearance, check) };

        // Centred on the shoal at about 5 m per pixel.
        var (cx, cy) = Mapsui.Projections.SphericalMercator.FromLonLat(0.11, 0);
        var viewport = new Mapsui.Viewport(cx, cy, 5, 0, 800, 500);
        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(800, 500));
        surface.Canvas.Clear(SkiaSharp.SKColors.Black);
        Assert.True(RouteOverlayRenderer.Instance.Draw(surface.Canvas, viewport, new Mapsui.Layers.MemoryLayer(),
            new Mapsui.Layers.PointFeature(cx, cy), style, new Mapsui.Rendering.RenderService(), 0));

        using var image = surface.Snapshot();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);

        // The corridor outline runs FindingOutlineHalfWidth above the route
        // (y = 250 on screen). Right of the min-UKC pill, look for its dashes
        // in the Below colour, column by column.
        var tone = check.Colors.Below;
        var top = 250 - (int)RouteOverlayRenderer.FindingOutlineHalfWidth;
        var hits = 0;
        for (var x = 450; x < 510; x++)
        {
            for (var y = top - 3; y <= top + 3; y++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red > 90 && c.Red > c.Green + 40 && c.Red > c.Blue + 30
                    && Math.Abs((c.Green / (double)c.Red) - (tone.Green / (double)tone.Red)) < 0.15)
                {
                    hits++;
                    break;
                }
            }
        }

        Assert.InRange(hits, 15, 55); // dashed: present in most columns, but not all
    }

    [Fact]
    public async Task The_selection_survives_a_recompute_that_finds_the_same_thing_and_clears_when_check_is_off()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f);
        await f.CheckAsync();
        findings.Select(findings.Findings[0], zoom: false);

        f.Route.SetPassageValue(RoutePassageField.RequiredUkc, 1.2);
        await f.Check.WhenIdle();
        Assert.Equal("clearance.below_setting.0", findings.Selected?.Id);
        Assert.Contains(findings.Selected!, findings.Findings);

        f.Routes.IsCheckModeActive = false;
        await f.Check.WhenIdle();
        Assert.Empty(findings.Findings);
        Assert.Null(findings.Selected);
    }

    [Fact]
    public async Task A_route_that_meets_the_settings_has_no_findings()
    {
        var f = new PassageCheckFixture();
        f.Route.SetPassageValue(RoutePassageField.Draft, 5);
        using var findings = Findings(f);
        await f.CheckAsync();
        Assert.Empty(findings.Findings);
    }

    // ----- MCP --------------------------------------------------------------

    [Fact]
    public async Task Get_findings_returns_the_live_list_and_select_finding_selects_one()
    {
        var f = new PassageCheckFixture();
        var viewport = new RecordingViewport();
        using var findings = Findings(f, viewport);
        await f.CheckAsync();
        var get = new GetFindingsTool(f.Routes, new InlineInvoker(), f.Check, findings);
        var select = new SelectFindingTool(f.Routes, new InlineInvoker(), f.Check, findings);
        var ct = TestContext.Current.CancellationToken;

        var list = Value(await get.InvokeAsync(null, cancellationToken: ct));
        Assert.True(list.Live);
        Assert.Equal(f.Route.Id, list.RouteId);
        Assert.Equal((1, 1, 0, 0), (list.Count, list.MustReview, list.Check, list.Info));
        Assert.Null(list.SelectedId);
        var dto = Assert.Single(list.Findings);
        Assert.Equal(("must_review", "clearance", "Clearance", 5), (dto.Group, dto.Category, dto.Tag, dto.LegIndex));
        Assert.Equal("span", dto.Location!.Kind);
        Assert.True(dto.Location.StartNm < dto.DistanceNm && dto.DistanceNm < dto.Location.EndNm);

        var selected = Value(await select.InvokeAsync(new SelectFindingRequest(dto.Id), ct));
        Assert.Equal(dto.Id, selected.SelectedId);
        Assert.True(selected.Findings[0].Selected);
        Assert.Single(viewport.Zooms);
        Assert.Equal(dto.DistanceNm!.Value, f.Check.Cursor!.Value.DistanceMeters / PassageGeodesy.MetersPerNauticalMile, 2);

        var missing = await select.InvokeAsync(new SelectFindingRequest("nope"), ct);
        Assert.True(missing.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);

        var cleared = Value(await select.InvokeAsync(new SelectFindingRequest(null), ct));
        Assert.Null(cleared.SelectedId);
    }

    [Fact]
    public async Task Get_findings_checks_another_route_once_without_changing_the_view()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f);
        var other = f.Routes.Routes.CreateRoute("Other");
        for (var i = 0; i < 9; i++)
            other.AppendWaypoint(new GeoPosition(0, i * 0.02));
        other.SetPassageValue(RoutePassageField.Draft, 11);
        other.SetPassageValue(RoutePassageField.Speed, 6);
        other.SetPassageValue(RoutePassageField.RequiredUkc, 1);
        other.SetDeparture(PassageCheckFixture.Departure);
        f.Routes.Routes.SetActiveRoute(f.Route);
        var get = new GetFindingsTool(f.Routes, new InlineInvoker(), f.Check, findings);
        var ct = TestContext.Current.CancellationToken;

        var list = Value(await get.InvokeAsync(other.Id, cancellationToken: ct));

        Assert.False(list.Live);
        Assert.Equal(other.Id, list.RouteId);
        Assert.Equal("clearance.below_setting", Assert.Single(list.Findings).Code);
        Assert.False(f.Routes.IsCheckModeActive);
        Assert.Same(f.Route, f.Routes.Routes.ActiveRoute);

        // #953: without a draft the route checks still run; there are no clearance findings.
        other.SetPassageValue(RoutePassageField.Draft, null);
        var noDraft = Value(await get.InvokeAsync(other.Id, cancellationToken: ct));
        Assert.Empty(noDraft.Findings);
        Assert.Equal("No findings for your settings", noDraft.Summary);
    }

    [Fact]
    public async Task Select_finding_needs_check_passage()
    {
        var f = new PassageCheckFixture();
        using var findings = Findings(f);
        var select = new SelectFindingTool(f.Routes, new InlineInvoker(), f.Check, findings);
        var result = await select.InvokeAsync(new SelectFindingRequest("clearance.below_setting.0"), TestContext.Current.CancellationToken);
        Assert.True(result.TryGetError(out var error));
        Assert.IsType<InvalidArgument>(error);
    }
}
