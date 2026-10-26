using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Weather.Tests;

/// <summary>
/// Tests for the weather coverage styles: bands, rotation conventions,
/// palettes, layer order and opacity, and the sub-layers
/// <see cref="WeatherPortrayal"/> builds (#1068).
/// </summary>
public class WeatherCoverageStyleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0.0f, 1)]
    [InlineData(0.49f, 1)]
    [InlineData(0.5f, 2)]
    [InlineData(1.2f, 3)]
    [InlineData(2.9f, 6)]
    [InlineData(3.0f, 7)]
    [InlineData(9.5f, 13)]
    [InlineData(13.9f, 14)]
    [InlineData(14.0f, 15)]
    [InlineData(30.0f, 15)]
    public void Wave_height_resolves_to_its_band(float metres, int band)
    {
        var style = new WaveHeightStyle();
        var scheme = style.ResolveColorScheme(MarinerSettings.Default);

        Assert.Equal(WeatherParameters.WaveHeight, scheme.FieldName);
        Assert.Equal(15, scheme.Bands.Count);
        Assert.Equal(WeatherPalette.Palette.Resolve(WeatherPalette.WaveHeightToken(band), PaletteType.Day).ToHex(), scheme.Resolve(metres));
    }

    [Fact]
    public void Wave_height_bands_break_at_the_design_boundaries()
    {
        Assert.Equal([0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 7, 8, 9, 12, 14], WaveHeightStyle.Boundaries);
        Assert.Equal("0–0.5 m", WaveHeightStyle.Bands[0].Label);
        Assert.Equal("14 m and higher", WaveHeightStyle.Bands[^1].Label);
        Assert.Null(new WaveHeightStyle().ResolveColorScheme(MarinerSettings.Default).Resolve(float.NaN));
    }

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(1.0, 1)]
    [InlineData(3.9, 1)]
    [InlineData(4.0, 2)]
    [InlineData(10.0, 3)]
    [InlineData(16.0, 4)]
    [InlineData(21.0, 5)]
    [InlineData(27.0, 6)]
    [InlineData(33.0, 7)]
    [InlineData(40.0, 8)]
    [InlineData(47.0, 9)]
    [InlineData(55.0, 10)]
    [InlineData(63.0, 11)]
    [InlineData(64.0, 12)]
    [InlineData(100.0, 12)]
    public void Wind_speed_in_metres_per_second_resolves_to_its_Beaufort_band(double knots, int force)
    {
        var speed = Speed.FromKnots(knots);
        var scheme = new WindSpeedStyle().ResolveColorScheme(MarinerSettings.Default);

        Assert.Equal(WeatherParameters.WindSpeed, scheme.FieldName);
        Assert.Equal(force, WindSpeedStyle.BeaufortForce(speed));
        // A hair above the knot value, so float rounding of the m/s value
        // can't drop it below a band edge that sits exactly on it.
        float metresPerSecond = (float)(speed.TotalMetresPerSecond + 1e-4);
        Assert.Equal(WeatherPalette.Palette.Resolve(WeatherPalette.WindSpeedToken(force), PaletteType.Day).ToHex(), scheme.Resolve(metresPerSecond));
    }

    [Fact]
    public void Wind_speed_labels_are_in_knots()
    {
        Assert.Equal("Force 0, under 1 kn", WindSpeedStyle.Bands[0].Label);
        Assert.Equal("Force 3, 7–10 kn", WindSpeedStyle.Bands[3].Label);
        Assert.Equal("Force 12, 64 kn and higher", WindSpeedStyle.Bands[12].Label);
    }

    [Theory]
    [InlineData(270f, 90f)]
    [InlineData(0f, 180f)]
    [InlineData(200f, 20f)]
    public void Wave_arrows_point_the_way_the_waves_travel(float fromDirection, float drawn)
    {
        var scheme = new WaveDirectionStyle().ResolveSymbolScheme(MarinerSettings.Default);

        Assert.Equal(WeatherParameters.WaveHeight, scheme.ValueFieldName);
        Assert.Equal(WeatherParameters.WaveDirection, scheme.RotationFieldName);
        Assert.Equal(180, scheme.RotationOffsetDegrees);
        Assert.Equal(drawn, scheme.RotationFor(fromDirection), 3);
    }

    [Theory]
    [InlineData(0.05f, null)]
    [InlineData(1.0f, 0.5)]
    [InlineData(1.5f, 0.5)]
    [InlineData(3.0f, 1.0)]
    [InlineData(6.0f, 2.0)]
    [InlineData(12.0f, 2.0)]
    public void Wave_arrows_grow_with_height(float metres, double? scale)
    {
        var band = new WaveDirectionStyle().ResolveSymbolScheme(MarinerSettings.Default).Resolve(metres, 0);

        if (scale is null)
        {
            Assert.Null(band);
            return;
        }

        Assert.NotNull(band);
        Assert.Equal(WaveDirectionStyle.ArrowSymbol, band.SymbolRef);
        Assert.Equal(scale.Value, band.ScaleFor(metres), 3);
    }

    [Fact]
    public void Fills_sit_under_S104_on_the_on_demand_surface_at_half_opacity()
    {
        foreach (WeatherCoverageStyle style in new WeatherCoverageStyle[] { new WaveHeightStyle(), new WindSpeedStyle() })
        {
            Assert.Equal(WeatherLayerKind.Fill, style.Kind);
            Assert.Equal(S98DisplayPlane.OnDemandSurface, style.Plane);
            Assert.Equal(0, style.WithinPlanePriority);
            Assert.Equal(0.5, style.Opacity);
            Assert.Null(style.ResolveSymbolScheme(MarinerSettings.Default));
        }
    }

    [Fact]
    public void Symbols_sit_above_S111_on_the_dynamic_arrows_plane()
    {
        foreach (WeatherCoverageStyle style in new WeatherCoverageStyle[] { new WindBarbStyle(), new WaveDirectionStyle() })
        {
            Assert.Equal(WeatherLayerKind.Symbols, style.Kind);
            Assert.Equal(S98DisplayPlane.DynamicArrows, style.Plane);
            // S-111 arrows are priority 10 on the same plane.
            Assert.Equal(20, style.WithinPlanePriority);
            Assert.Equal(1.0, style.Opacity);
            Assert.Null(style.ResolveColorScheme(MarinerSettings.Default));
        }
    }

    [Fact]
    public void Wave_height_and_barbs_are_on_by_default_and_the_others_off()
    {
        Assert.True(new WaveHeightStyle().IsVisibleByDefault);
        Assert.True(new WindBarbStyle().IsVisibleByDefault);
        Assert.False(new WindSpeedStyle().IsVisibleByDefault);
        Assert.False(new WaveDirectionStyle().IsVisibleByDefault);
    }

    [Theory]
    [InlineData(PaletteType.Day, "#222630", "#F3EEFF")]
    [InlineData(PaletteType.Dusk, "#D4D7DE", "#494260")]
    [InlineData(PaletteType.Night, "#5F636C", "#171322")]
    public async Task Each_palette_has_its_own_colours(PaletteType palette, string barbInk, string lowestWave)
    {
        var barbs = new WindBarbStyle();
        var waves = new WaveHeightStyle();
        await barbs.SwitchPaletteAsync(palette, Ct);
        await waves.SwitchPaletteAsync(palette, Ct);

        Assert.Equal(palette, barbs.ActivePaletteType);
        Assert.Equal(barbInk, barbs.ActivePalette.Resolve(WeatherPalette.BarbInkToken));
        Assert.Equal(lowestWave, waves.ResolveColorScheme(MarinerSettings.Default).Bands[0].Color);
    }

    [Fact]
    public void Night_colours_stay_under_luminance_0_2()
    {
        Assert.All(
            WeatherPalette.Palette.Tokens,
            pair => Assert.True(RelativeLuminance(pair.Value.Night) < 0.2, $"{pair.Key} {pair.Value.Night} is too bright for Night."));
    }

    [Fact]
    public void Night_is_dimmer_than_dusk_for_every_symbol_ink()
    {
        foreach (var token in new[] { WeatherPalette.BarbInkToken, WeatherPalette.WaveArrowToken })
        {
            var colors = WeatherPalette.Palette.Tokens[token];
            Assert.True(RelativeLuminance(colors.Night) < RelativeLuminance(colors.Dusk), token);
        }
    }

    [Fact]
    public void Palette_tokens_are_prefixed_so_they_never_resolve_against_S101_colours()
    {
        Assert.All(WeatherPalette.Palette.Tokens.Keys, token => Assert.StartsWith("WX", token, StringComparison.Ordinal));
        Assert.Equal(2 + 15 + 13, WeatherPalette.Palette.Tokens.Count);
    }

    [Fact]
    public void Content_scope_carries_the_style_version()
    {
        Assert.Equal(ApplicationContentScope.For("Weather", WeatherCoverageStyle.StyleVersion), WeatherCoverageStyle.ContentScope);
        Assert.StartsWith("app:Weather@1|toolkit:", WeatherCoverageStyle.ContentScope, StringComparison.Ordinal);
    }

    [Fact]
    public void Styles_apply_only_to_sources_with_their_fields()
    {
        var wind = WindSource();
        var waves = WaveSource(withDirection: false);

        Assert.Equal([WindSpeedStyle.Key, WindBarbStyle.Key], WeatherCoverageStyle.CreateFor(wind).Select(s => s.LayerKey));
        Assert.Equal([WaveHeightStyle.Key], WeatherCoverageStyle.CreateFor(waves).Select(s => s.LayerKey));
        Assert.Equal(
            [WaveHeightStyle.Key, WaveDirectionStyle.Key],
            WeatherCoverageStyle.CreateFor(WaveSource(withDirection: true)).Select(s => s.LayerKey));
    }

    [Fact]
    public async Task Portrayal_builds_one_sub_layer_per_style_with_its_plane_priority_and_opacity()
    {
        var source = WindSource();

        var result = await WeatherPortrayal.BuildAsync(
            source,
            WeatherCoverageStyle.CreateAll(),
            "wind.grb2",
            new RenderContext { Palette = PaletteType.Night, SymbolScale = 1.5 },
            datasetContentHash: "abc",
            cancellationToken: Ct);

        Assert.Equal([WindSpeedStyle.Key, WindBarbStyle.Key], result.LayerNames);
        var fill = Assert.IsType<GridCoverageSubLayer>(result.SubLayers[0]);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, fill.Plane);
        Assert.Equal(0, fill.WithinPlanePriority);
        Assert.Equal(0.5, fill.Opacity);
        Assert.Equal(WeatherPalette.Palette.Resolve(WeatherPalette.WindSpeedToken(0), PaletteType.Night).ToHex(), fill.Coverage.ColorScheme!.Bands[0].Color);

        var barbs = Assert.IsType<ArrowCoverageSubLayer>(result.SubLayers[1]);
        Assert.Equal(S98DisplayPlane.DynamicArrows, barbs.Plane);
        Assert.Equal(20, barbs.WithinPlanePriority);
        Assert.Equal(1.5, barbs.BaseSymbolScale);
        Assert.Equal("#5F636C", barbs.Palette!.Resolve(WeatherPalette.BarbInkToken));
        Assert.NotNull(barbs.SymbolProvider("WXBARBN025"));
        Assert.Equal(WeatherParameters.WindSpeed, barbs.Coverage.SymbolScheme!.ValueFieldName);

        // The derived speed field reaches the styled layer.
        var speed = barbs.Coverage.Coverage.GetField(WeatherParameters.WindSpeed);
        Assert.Equal(10.0, speed[0, 0], 3);

        Assert.Equal($"ds:abc|{WeatherCoverageStyle.ContentScope}", result.ContentScope);
        Assert.Equal(new GeographicBounds(0, 0, 2, 1), result.CoverageExtent);
        Assert.Contains("Not for navigation", result.Info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Portrayal_selects_the_context_time_step()
    {
        var u = TestGrids.Parameter(WeatherParameters.WindU, (0, TestGrids.Filled(6, 0)), (3, TestGrids.Filled(6, 0)));
        var v = TestGrids.Parameter(WeatherParameters.WindV, (0, TestGrids.Filled(6, -5)), (3, TestGrids.Filled(6, -20)));
        var source = Source(u, v);

        var result = await WeatherPortrayal.BuildAsync(
            source, [new WindBarbStyle()], "wind", new RenderContext { TimeStep = TestGrids.Run.AddHours(3) }, cancellationToken: Ct);

        var barbs = Assert.IsType<ArrowCoverageSubLayer>(Assert.Single(result.SubLayers));
        Assert.Equal(TestGrids.Run.AddHours(3), source.SelectedTime);
        Assert.Equal(20.0, barbs.Coverage.Coverage.GetField(WeatherParameters.WindSpeed)[0, 0], 3);
    }

    [Fact]
    public async Task Building_a_sub_layer_for_a_style_the_source_lacks_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            WeatherPortrayal.BuildSubLayerAsync(WindSource(), new WaveHeightStyle(), "wind", cancellationToken: Ct));
    }

    // A 2 × 3 grid of a 10 m/s southerly (u = 0, v = 10).
    private static WeatherCoverageSource WindSource() => Source(
        TestGrids.Parameter(WeatherParameters.WindU, (0, TestGrids.Filled(6, 0))),
        TestGrids.Parameter(WeatherParameters.WindV, (0, TestGrids.Filled(6, 10))));

    private static WeatherCoverageSource WaveSource(bool withDirection) => withDirection
        ? Source(
            TestGrids.Parameter(WeatherParameters.WaveHeight, (0, TestGrids.Filled(6, 2))),
            TestGrids.Parameter(WeatherParameters.WaveDirection, (0, TestGrids.Filled(6, 270))))
        : Source(TestGrids.Parameter(WeatherParameters.WaveHeight, (0, TestGrids.Filled(6, 2))));

    private static WeatherCoverageSource Source(params WeatherParameter[] parameters)
    {
        var set = new WeatherGridSet(TestGrids.Origin, TestGrids.Regular(2, 3), parameters);
        return new WeatherCoverageSource(set, "test", parameters);
    }

    private static double RelativeLuminance(RgbaColor color)
    {
        static double Linear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
