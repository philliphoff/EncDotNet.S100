using System.Globalization;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
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

    [Theory]
    [MemberData(nameof(Palettes))]
    public void Every_state_keeps_3_to_1_against_its_casing_except_day_amber(PaletteType palette)
    {
        var colors = RouteCheckColors.For(palette);
        foreach (var state in States)
        {
            var ratio = ColorContrast.Ratio(colors.Of(state), colors.Casing);
            if (palette == PaletteType.Day && state == UkcState.Tight)
                Assert.InRange(ratio, 2.2, 3.0); // the handoff's amber on white; identified by hue
            else
                Assert.True(ratio >= ColorContrast.MinimumGraphicContrast, $"{palette} {state}: {ratio:0.00}:1 against the casing");
        }
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
    public void The_editing_accent_loses_chroma_on_night_and_keeps_contrast_on_its_casing(byte r, byte g, byte b)
    {
        var accent = new SKColor(r, g, b);
        Assert.Equal(accent, ColorContrast.AccentFor(accent, PaletteType.Day));
        accent.ToHsv(out _, out var daySaturation, out _);
        foreach (var palette in new[] { PaletteType.Dusk, PaletteType.Night })
        {
            var edit = RouteOverlayColors.For(MeasureOverlayAppearance.Default with { Palette = palette }).Edit;
            var adjusted = ColorContrast.AccentFor(accent, palette);
            adjusted.ToHsv(out _, out var saturation, out _);
            Assert.True(saturation < daySaturation, $"{palette}: {adjusted} keeps {saturation:0} of {daySaturation:0} saturation");
            Assert.True(ColorContrast.Ratio(adjusted, SKColors.Black) >= ColorContrast.MinimumGraphicContrast, $"{palette}: {adjusted} on black");
            Assert.Equal(SKColors.Black, RouteOverlayColors.For(MeasureOverlayAppearance.Default with { Palette = palette }).Casing);
            Assert.NotEqual(new SKColor(0x00, 0x7A, 0xCC), edit);
        }

        var night = ColorContrast.AccentFor(accent, PaletteType.Night);
        var dusk = ColorContrast.AccentFor(accent, PaletteType.Dusk);
        night.ToHsv(out _, out var ns, out _);
        dusk.ToHsv(out _, out var ds, out _);
        Assert.True(ns < ds, "Night lowers chroma further than Dusk");
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
    /// at 07:10, with the cursor and ghost at 3 NM), drawn over bands of the
    /// palette's S-101 depth shades and land.
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
        var check = RouteCheckOverlay.Build(true, f.Check.Current, false, f.Check.Cursor, appearance, DepthUnit.Metres, TimeFormat.Utc);
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
}
