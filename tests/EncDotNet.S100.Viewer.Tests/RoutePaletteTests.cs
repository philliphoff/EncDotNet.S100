using System.Globalization;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tools;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The route palette pass (#906): the check-mode state colours, the editing
/// style and the profile on Day, Dusk and Night, checked against the S-101
/// colour profile, and the route overlay in check mode rendered over S-101
/// depth shades on each palette (verified against committed snapshots).
/// </summary>
public sealed class RoutePaletteTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public RoutePaletteTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    public static TheoryData<PaletteType> Palettes => [PaletteType.Day, PaletteType.Dusk, PaletteType.Night];

    private static readonly UkcState[] States =
        [UkcState.MeetsSettings, UkcState.Tight, UkcState.BelowSetting, UkcState.NoData];

    private static readonly string[] DepthTokens = ["DEPDW", "DEPMD", "DEPMS", "DEPVS", "DEPIT", "LANDA"];

    /// <summary>The S-101 colour profile shipped in the specifications content.</summary>
    private static ColorPalette S101(PaletteType palette)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EncDotNet.S100.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir.FullName, "src", "EncDotNet.S100.Specifications", "content", "S101", "pc", "ColorProfiles", "colorProfile.xml");
        using var stream = File.OpenRead(path);
        return ColorProfileReader.Read(stream, palette.ToString());
    }

    private static SKColor Token(ColorPalette palette, string token) => SKColor.Parse(palette.Colors[token]);

    private static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

    /// <summary>
    /// Route findings along a route on the equator, <paramref name="leg"/>
    /// degrees of longitude per leg: a Must review danger on the route, a
    /// Check restricted area along most of leg 3 (a sleeve), a Check S-124
    /// warning 450 m north of leg 4 (a leader), and an Info warning on leg 1.
    /// </summary>
    private static List<RouteFinding> SampleFindings(double leg)
    {
        const double metersPerDegree = 111_195;
        DataModel.GeoPosition At(double legs, double northMeters = 0) => new(northMeters / metersPerDegree, legs * leg);
        double Distance(double legs) => legs * leg * metersPerDegree;
        RouteFinding Point(string id, RouteFindingCode code, RouteFindingGroup group, RouteFindingCategory category, string title, double legs, double north = 0) =>
            TestFinding.Make(id, code, group, category, title, "meta", (int)legs, Distance(legs), RouteFindingLocation.Point(At(legs, north), Distance(legs)));

        return
        [
            Point("danger", RouteFindingCodes.DangerInCorridor, RouteFindingGroup.MustReview, RouteFindingCategory.Depth, "Wreck 9,4 m", 1.6, 20),
            TestFinding.Make("area", RouteFindingCodes.RestrictedArea, RouteFindingGroup.Check, RouteFindingCategory.Area, "Anchoring prohibited", "meta", 2,
                Distance(2.2), new RouteFindingLocation([At(2.2), At(2.5), At(2.8)], Distance(2.2), Distance(2.8))),
            Point("near", RouteFindingCodes.WarningNearRoute, RouteFindingGroup.Check, RouteFindingCategory.Warning, "006/26 · 0,3 NM N", 3.5, 450),
            Point("info", RouteFindingCodes.WarningNotInForce, RouteFindingGroup.Info, RouteFindingCategory.Warning, "002/26 · not in force", 0.5),
        ];
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Every_state_and_finding_tone_keeps_3_to_1_against_its_casing(PaletteType palette)
    {
        // Day's Tight was 2.3:1 on white until #934 darkened it to #B07D00.
        var colors = RouteCheckColors.For(palette);
        foreach (var (name, color) in States.Select(s => (s.ToString(), colors.Of(s))).Append(("Info", colors.Info)))
        {
            var ratio = ColorContrast.Ratio(color, colors.Casing);
            Assert.True(ratio >= ColorContrast.MinimumGraphicContrast, $"{palette} {name}: {ratio:0.00}:1 against the casing");
        }
    }

    /// <summary>
    /// The tone table (handoff Part G, #934), asserted value by value: the
    /// perceptual goldens can hide a small drift in a colour (#939).
    /// </summary>
    [Theory]
    [InlineData(PaletteType.Day, "#13895B", "#B07D00", "#D42A3A", "#5E6B7E", "#8C95A3", "#FFFFFF")]
    [InlineData(PaletteType.Dusk, "#20895C", "#C18D12", "#CA3240", "#6A7584", "#58616E", "#000000")]
    [InlineData(PaletteType.Night, "#226746", "#735614", "#A4323C", "#57606F", "#515A65", "#000000")]
    public void State_tones_are_the_handoffs(PaletteType palette, string meets, string tight, string below, string info, string noData, string casing)
    {
        var colors = RouteCheckColors.For(palette);
        Assert.Equal(
            [meets, tight, below, info, noData, noData, casing],
            new[] { colors.Meets, colors.Tight, colors.Below, colors.Info, colors.NoData, colors.Stale, colors.Casing }.Select(Hex));
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Findings_reuse_the_state_tones(PaletteType palette)
    {
        // Must review = Below, Check = Tight, Info = the Low confidence grey (decision 14).
        var colors = RouteCheckColors.For(palette);
        Assert.Equal(colors.Below, RouteCheckOverlay.ToneOf(RouteFindingGroup.MustReview, colors));
        Assert.Equal(colors.Tight, RouteCheckOverlay.ToneOf(RouteFindingGroup.Check, colors));
        Assert.Equal(colors.Info, RouteCheckOverlay.ToneOf(RouteFindingGroup.Info, colors));
    }

    [Fact]
    public void Night_states_stay_at_about_a_tenth_of_white()
    {
        // #934 question 1: S-101 Night's brightest colour is about 0,05; the
        // route states stay at or under 0,12 (and so just over 3:1 on black).
        var night = RouteCheckColors.For(PaletteType.Night);
        foreach (var (name, color) in States.Select(s => (s.ToString(), night.Of(s))).Append(("Info", night.Info)))
        {
            var luminance = ColorContrast.Luminance(color);
            Assert.True(luminance <= 0.12, $"Night {name}: {luminance:0.000} relative luminance");
        }
    }

    [Fact]
    public void Day_tight_keeps_3_to_1_on_white() =>
        Assert.True(ColorContrast.Ratio(RouteCheckColors.For(PaletteType.Day).Tight, SKColors.White) >= 3.0);

    [Fact]
    public void No_data_is_dotted_and_lower_confidence_dashed()
    {
        // #934 question 4: No data and lower confidence differ by pattern, not only by brightness.
        Assert.Equal([1.5f, 3.5f], RouteOverlayRenderer.CheckDash(UkcState.NoData)!);
        Assert.Equal([1.5f, 3.5f], RouteOverlayRenderer.CheckDash(UkcState.NoData, isLowConfidence: true)!);
        Assert.All(States.Where(s => s != UkcState.NoData), s =>
        {
            Assert.Null(RouteOverlayRenderer.CheckDash(s));
            Assert.Equal([6f, 4f], RouteOverlayRenderer.CheckDash(s, isLowConfidence: true)!);
        });
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Markers_and_sleeves_use_the_state_tones(PaletteType palette)
    {
        var colors = RouteCheckColors.For(palette);
        var route = new Route("r", "Test");
        route.AppendWaypoint(new DataModel.GeoPosition(0, 0));
        route.AppendWaypoint(new DataModel.GeoPosition(0, 0.1));
        var findings = SampleFindings(0.1 / 4);
        var info = findings.Single(f => f.Group == RouteFindingGroup.Info);
        var map = RouteCheckMap.Build(route, findings, info, colors, SKColors.Blue, palette, DateTimeOffset.UnixEpoch, TimeFormat.Utc, 50);

        Assert.All(map.Marks, m => Assert.Equal(RouteCheckOverlay.ToneOf(m.Group, colors), m.Tone));
        Assert.All(map.Sleeves, sl => Assert.Equal(RouteCheckOverlay.ToneOf(sl.Group, colors), sl.Tone));
        Assert.Equal(
            [RouteFindingGroup.MustReview, RouteFindingGroup.Check, RouteFindingGroup.Check, RouteFindingGroup.Info],
            map.Marks.Select(m => m.Group));
        Assert.Contains(map.Sleeves, sl => sl.Group == RouteFindingGroup.Check);

        // Red only for Must review, on the map and in the findings list.
        Assert.All(map.Marks.Where(m => m.Group != RouteFindingGroup.MustReview), m => Assert.NotEqual(colors.Below, m.Tone));
        Assert.All(findings, f => Assert.Equal(f.Group == RouteFindingGroup.MustReview, RouteFindingRowViewModel.ToneOf(f) == CheckTone.Destructive));
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void No_data_is_the_only_grey_so_a_dashed_band_never_reads_as_no_data(PaletteType palette)
    {
        // Lower confidence is a dashed line in its band's colour (#939);
        // only no data is grey, so the two dashed lines differ by hue.
        var colors = RouteCheckColors.For(palette);
        static float Saturation(SKColor c)
        {
            c.ToHsv(out _, out var s, out _);
            return s;
        }

        Assert.True(Saturation(colors.NoData) < 25, $"{palette}: No data should be grey");
        Assert.All(
            States.Where(s => s != UkcState.NoData),
            s => Assert.True(Saturation(colors.Of(s)) > 45, $"{palette} {s} should carry its hue"));
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Red_is_reserved_for_below_your_setting(PaletteType palette)
    {
        var colors = RouteCheckColors.For(palette);
        static bool IsRed(SKColor c)
        {
            c.ToHsv(out var h, out var s, out _);
            return s > 30 && (h < 20 || h > 340);
        }

        Assert.True(IsRed(colors.Below));
        Assert.All(States.Where(s => s != UkcState.BelowSetting), s => Assert.False(IsRed(colors.Of(s)), $"{palette} {s} reads as red"));
        Assert.True(RouteOverlayRenderer.CheckStrokeWidth(UkcState.BelowSetting) > RouteOverlayRenderer.CheckStrokeWidth(UkcState.MeetsSettings));
    }

    [Fact]
    public void Dusk_and_night_stay_within_the_s101_palettes_brightness()
    {
        // Dusk: no route colour brighter than S-101 Dusk's brightest (CHWHT).
        var duskWhite = ColorContrast.Luminance(Token(S101(PaletteType.Dusk), "CHWHT"));
        var dusk = RouteCheckColors.For(PaletteType.Dusk);
        Assert.All(States.Select(dusk.Of).Append(dusk.Ink), c => Assert.True(ColorContrast.Luminance(c) <= duskWhite + 0.01, $"{c} is brighter than S-101 Dusk"));

        // Night: dimmer than Day, and the ink no brighter than about 0.3.
        var night = RouteCheckColors.For(PaletteType.Night);
        var day = RouteCheckColors.For(PaletteType.Day);
        Assert.True(ColorContrast.Luminance(night.Ink) <= 0.3);
        Assert.True(ColorContrast.Luminance(night.Tight) < ColorContrast.Luminance(day.Tight));
        Assert.True(ColorContrast.Luminance(night.NoData) < ColorContrast.Luminance(day.NoData));
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void State_colours_read_against_the_washed_s101_depth_shades_on_dusk_and_night(PaletteType palette)
    {
        if (palette == PaletteType.Day)
            return; // on Day the white casing carries the line over the light chart
        var colors = RouteCheckColors.For(palette);
        var s101 = S101(palette);
        foreach (var token in DepthTokens)
        {
            var washed = ColorContrast.Over(Token(s101, token), colors.Casing, colors.Wash.Alpha / 255.0);
            foreach (var state in States)
            {
                var ratio = ColorContrast.Ratio(colors.Of(state), washed);
                Assert.True(ratio >= 2.0, $"{palette} {state} over washed {token}: {ratio:0.00}:1");
            }
        }
    }

    [Theory]
    [InlineData(0x00, 0x7A, 0xCC)] // the default accent
    [InlineData(0x25, 0x63, 0xEB)]
    [InlineData(0x0B, 0x1F, 0x5C)] // a dark navy accent
    [InlineData(0xE1, 0x1D, 0x48)]
    [InlineData(0xF5, 0xC5, 0x18)]
    public void The_editing_accent_tones_down_on_dusk_and_night_edits_in_a_fixed_blue(byte r, byte g, byte b)
    {
        var accent = new SKColor(r, g, b);
        Assert.Equal(accent, ColorContrast.AccentFor(accent, PaletteType.Day));
        accent.ToHsv(out _, out var daySaturation, out _);

        // Dusk: the accent with less chroma, still 3:1 on its black casing.
        var dusk = ColorContrast.AccentFor(accent, PaletteType.Dusk);
        dusk.ToHsv(out _, out var duskSaturation, out _);
        Assert.True(duskSaturation < daySaturation, $"Dusk: {dusk} keeps {duskSaturation:0} of {daySaturation:0} saturation");
        Assert.True(ColorContrast.Ratio(dusk, SKColors.Black) >= ColorContrast.MinimumGraphicContrast, $"Dusk: {dusk} on black");

        // Night: #3F64A6 whatever the accent (#934 question 5), 3:1 on black.
        var appearance = MeasureOverlayAppearance.Default with { Accent = (r, g, b) };
        var night = RouteOverlayColors.For(appearance with { Palette = PaletteType.Night });
        Assert.Equal("#3F64A6", Hex(night.Edit));
        Assert.Equal(ColorContrast.NightEdit, ColorContrast.AccentFor(accent, PaletteType.Night));
        Assert.True(ColorContrast.Ratio(night.Edit, SKColors.Black) >= ColorContrast.MinimumGraphicContrast);

        Assert.Equal(dusk, RouteOverlayColors.For(appearance with { Palette = PaletteType.Dusk }).Edit);
        foreach (var palette in new[] { PaletteType.Dusk, PaletteType.Night })
            Assert.Equal(SKColors.Black, RouteOverlayColors.For(appearance with { Palette = palette }).Casing);
    }

    [Theory]
    [InlineData(ChromeTheme.Light, PaletteType.Day)]
    [InlineData(ChromeTheme.S100Dusk, PaletteType.Dusk)]
    [InlineData(ChromeTheme.Dark, PaletteType.Night)]
    [InlineData(ChromeTheme.S100Night, PaletteType.Night)]
    public void The_profile_uses_the_route_palette_of_the_chrome_theme(ChromeTheme theme, PaletteType palette) =>
        Assert.Equal(palette, ClearanceViewModel.StatePalette(theme));

    /// <summary>
    /// Every state × confidence combination (#939) as it draws on the map,
    /// in seven runs along the fixture route: Meets, Tight and Below at full
    /// confidence, the same three at lower confidence (band colour, dashed),
    /// then No data (grey, dashed), over bands of the palette's S-101 depth
    /// shades, with the legend.
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public async Task Check_mode_states_and_confidence(PaletteType palette)
    {
        var f = new PassageCheckFixture();
        await f.CheckAsync();
        var appearance = MeasureOverlayAppearance.Default with
        {
            Palette = palette,
            ChartBackground = MeasureOverlayAppearance.ChartBackgroundFor(palette),
        };
        var built = RouteCheckOverlay.Build(true, f.Check.Current, false, null, appearance, DepthUnit.Metres, TimeFormat.Utc)!;
        (UkcState State, bool Low)[] rows =
        [
            (UkcState.MeetsSettings, false), (UkcState.Tight, false), (UkcState.BelowSetting, false),
            (UkcState.MeetsSettings, true), (UkcState.Tight, true), (UkcState.BelowSetting, true),
            (UkcState.NoData, false),
        ];
        const double length = 0.16;
        var segments = rows
            .Select((r, i) => new RouteCheckOverlay.Segment(
                [new DataModel.GeoPosition(0, i * length / rows.Length), new DataModel.GeoPosition(0, (i + 1) * length / rows.Length)], r.State, r.Low))
            .ToList();
        var check = built with { Segments = segments, Minimum = null, Ghost = null };
        var scene = RouteOverlayScene.Build(f.Routes.Routes, null, null, false, appearance, check);

        const int width = 900;
        const int height = 420;
        await Verify(Render(palette, scene, width, height), "png").UseParameters(palette);
    }

    private static byte[] Render(PaletteType palette, RouteOverlayScene scene, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        var s101 = S101(palette);
        for (var i = 0; i < DepthTokens.Length; i++)
        {
            using var paint = new SKPaint { Color = Token(s101, DepthTokens[i]) };
            canvas.DrawRect(i * width / (float)DepthTokens.Length, 0, width / (float)DepthTokens.Length + 1, height, paint);
        }

        var (cx, _) = Mapsui.Projections.SphericalMercator.FromLonLat(0.08, 0);
        var viewport = new Mapsui.Viewport(cx, 0, 22, 0, width, height);
        RouteOverlayRenderer.Instance.Draw(canvas, viewport, new MemoryLayer(), new PointFeature(0, 0), new RouteOverlayStyle { Scene = scene }, null!, 0);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// The route overlay in check mode on the fixture route (below the setting
    /// at 07:10, with the cursor and ghost at 3 NM, and the corridor's dotted
    /// edges), drawn over bands of the palette's S-101 depth shades and land.
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public async Task Check_mode_overlay(PaletteType palette)
    {
        var f = new PassageCheckFixture(s101FallbackUntilLon: 0.02);
        await f.CheckAsync();
        f.Check.SetCursor(3 * PassageGeodesy.MetersPerNauticalMile);
        var appearance = MeasureOverlayAppearance.Default with
        {
            Palette = palette,
            ChartBackground = MeasureOverlayAppearance.ChartBackgroundFor(palette),
        };
        var check = RouteCheckOverlay.Build(true, f.Check.Current, false, f.Check.Cursor, appearance, DepthUnit.Metres, TimeFormat.Utc, route: f.Route);
        var scene = RouteOverlayScene.Build(f.Routes.Routes, null, null, false, appearance, check);

        const int width = 900;
        const int height = 400;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        // Bands of S-101 depth shades and land, so every state is seen on each.
        var s101 = S101(palette);
        for (var i = 0; i < DepthTokens.Length; i++)
        {
            using var paint = new SKPaint { Color = Token(s101, DepthTokens[i]) };
            canvas.DrawRect(i * width / (float)DepthTokens.Length, 0, width / (float)DepthTokens.Length + 1, height, paint);
        }

        var (cx, _) = Mapsui.Projections.SphericalMercator.FromLonLat(0.08, 0);
        var viewport = new Mapsui.Viewport(cx, 0, 22, 0, width, height);
        RouteOverlayRenderer.Instance.Draw(canvas, viewport, new MemoryLayer(), new PointFeature(0, 0), new RouteOverlayStyle { Scene = scene }, null!, 0);

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        await Verify(png.ToArray(), "png").UseParameters(palette);
    }

    /// <summary>
    /// Handoff 2b's state: a draft set, so the line carries the UKC colours
    /// (every state and the dotted No data from the fixture tide ending), with
    /// route findings drawn over it: a Must review danger, a Check
    /// restricted area (sleeve) and a Check warning off the route (leader),
    /// over bands of the palette's S-101 depth shades. Nothing selected.
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public async Task Check_state_2b_draft_with_route_findings(PaletteType palette)
    {
        var f = StateFixture();
        await f.CheckAsync();
        await Verify(Render(palette, StateScene(f, palette, selected: null), 900, 420), "png").UseParameters(palette);
    }

    /// <summary>
    /// Handoff 2c's state: the same route with the Check warning near it
    /// selected (ringed, with its pill and leader, its leg's corridor filled,
    /// and the "Zoomed to finding" chip).
    /// </summary>
    [Theory]
    [MemberData(nameof(Palettes))]
    public async Task Check_state_2c_warning_near_the_route_selected(PaletteType palette)
    {
        var f = StateFixture();
        await f.CheckAsync();
        await Verify(Render(palette, StateScene(f, palette, selected: "near"), 900, 420), "png").UseParameters(palette);
    }

    // Meets, a low water that makes legs 2–3 tight, below over the 9 m shoal
    // on leg 6, and no data after the water level ends on leg 8.
    private static PassageCheckFixture StateFixture() => new(
        waterEnd: PassageCheckFixture.WaterStart.AddHours(2.6),
        tide: t => t >= PassageCheckFixture.WaterStart.AddMinutes(85) && t < PassageCheckFixture.WaterStart.AddMinutes(105) ? -2.6f : 0.5f);

    private static RouteOverlayScene StateScene(PassageCheckFixture f, PaletteType palette, string? selected)
    {
        var appearance = MeasureOverlayAppearance.Default with
        {
            Palette = palette,
            ChartBackground = MeasureOverlayAppearance.ChartBackgroundFor(palette),
        };
        var findings = SampleFindings(0.02);
        var check = RouteCheckOverlay.Build(true, f.Check.Current, false, null, appearance, DepthUnit.Metres, TimeFormat.Utc,
            findings.FirstOrDefault(x => x.Id == selected), findings, route: f.Route, viewTime: PassageCheckFixture.Departure);
        return RouteOverlayScene.Build(f.Routes.Routes, null, null, false, appearance, check);
    }
}
