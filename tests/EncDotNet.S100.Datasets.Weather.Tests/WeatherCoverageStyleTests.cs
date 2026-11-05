using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
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

    [Theory]
    // S-111 Ed 2.0.0 select_arrow.xsl: three bands at 0.4, five at 0.2 per
    // knot, the fastest at 2.6.
    [InlineData(0.2, 1, 0.4)]
    [InlineData(0.5, 2, 0.4)]
    [InlineData(1.5, 3, 0.4)]
    [InlineData(2.0, 4, 0.4)]
    [InlineData(4.0, 5, 0.8)]
    [InlineData(6.0, 6, 1.2)]
    [InlineData(9.0, 7, 1.8)]
    [InlineData(12.0, 8, 2.4)]
    [InlineData(13.0, 9, 2.6)]
    [InlineData(20.0, 9, 2.6)]
    public void Current_arrows_follow_the_S111_speed_bands(double knots, int band, double scale)
    {
        float metresPerSecond = (float)Speed.FromKnots(knots).TotalMetresPerSecond;

        var resolved = new CurrentArrowStyle().ResolveSymbolScheme(MarinerSettings.Default).Resolve(metresPerSecond, 0);

        Assert.NotNull(resolved);
        Assert.Equal(CurrentArrowStyle.SymbolName(band), resolved.SymbolRef);
        Assert.Equal(scale, resolved.ScaleFor(metresPerSecond), 3);
    }

    [Fact]
    public void Current_arrows_point_the_way_the_water_flows_and_are_on_by_default()
    {
        var style = new CurrentArrowStyle();
        var scheme = style.ResolveSymbolScheme(MarinerSettings.Default);

        Assert.Equal(WeatherParameters.CurrentSpeed, scheme.ValueFieldName);
        Assert.Equal(WeatherParameters.CurrentToDirection, scheme.RotationFieldName);
        Assert.Equal(0, scheme.RotationOffsetDegrees);
        Assert.True(style.IsVisibleByDefault);
        Assert.False(style.ClipToWater);
        Assert.Equal(["Under 0.5 kn", "0.5–1 kn", "1–2 kn", "2–3 kn", "3–5 kn", "5–7 kn", "7–10 kn", "10–13 kn", "13 kn and faster"],
            CurrentArrowStyle.Bands.Select(b => b.Label));
    }

    [Fact]
    public void Current_band_colours_are_the_S111_colour_profile_s()
    {
        // SCBN1 and SCBN9 of the S-111 Ed 2.0.0 colour profile, by day.
        Assert.Equal("#7652E2", WeatherPalette.Palette.Tokens[WeatherPalette.CurrentToken(1)].Day.ToHex());
        Assert.Equal("#FF1E1E", WeatherPalette.Palette.Tokens[WeatherPalette.CurrentToken(9)].Day.ToHex());
        Assert.Contains("fWXCUR5", WeatherCoverageStyle.Symbols.Provider(CurrentArrowStyle.SymbolName(5)));
    }

    [Theory]
    [InlineData(-1.9, 1)]
    [InlineData(-0.01, 1)]
    [InlineData(0.0, 2)]
    [InlineData(15.5, 17)]
    [InlineData(26.4, 28)]
    [InlineData(29.99, 31)]
    [InlineData(30.0, 32)]
    [InlineData(34.0, 32)]
    public void Sea_temperature_resolves_to_its_1_degree_band(double celsius, int band)
    {
        var style = new SeaTemperatureStyle();
        var scheme = style.ResolveColorScheme(MarinerSettings.Default);

        Assert.Equal(WeatherParameters.SeaSurfaceTemperature, scheme.FieldName);
        Assert.Equal(32, scheme.Bands.Count);
        Assert.Equal(
            WeatherPalette.Palette.Resolve(WeatherPalette.SeaTemperatureToken(band), PaletteType.Day).ToHex(),
            scheme.Resolve((float)(celsius + SeaTemperatureStyle.CelsiusZero)));
    }

    [Theory]
    // Marine forecast terms: very poor under 1000 m, poor to 2 nm, moderate to 5 nm.
    [InlineData(0, 1)]
    [InlineData(999, 1)]
    [InlineData(1000, 2)]
    [InlineData(3703, 2)]
    [InlineData(3704, 3)]
    [InlineData(9259, 3)]
    public void Restricted_visibility_resolves_to_its_forecast_band(double metres, int band)
    {
        var scheme = new VisibilityStyle().ResolveColorScheme(MarinerSettings.Default);

        Assert.Equal(WeatherParameters.Visibility, scheme.FieldName);
        Assert.Equal(
            WeatherPalette.Palette.Resolve(WeatherPalette.VisibilityToken(band), PaletteType.Day).ToHex(),
            scheme.Resolve((float)metres));
    }

    [Theory]
    [InlineData(9260)] // 5 nm: good.
    [InlineData(24100)] // GFS's ceiling.
    [InlineData(float.NaN)]
    public void Good_visibility_is_not_drawn(float metres)
    {
        Assert.Null(new VisibilityStyle().ResolveColorScheme(MarinerSettings.Default).Resolve(metres));
    }

    [Fact]
    public void Visibility_bands_are_labelled_with_the_forecast_terms_and_drawn_by_default()
    {
        var style = new VisibilityStyle();

        Assert.Equal(["Very poor (fog), under 1000 m", "Poor, 1000 m to 2 nm", "Moderate, 2 to 5 nm"], VisibilityStyle.Bands.Select(b => b.Label));
        Assert.True(style.IsVisibleByDefault);
        Assert.Equal(VisibilityStyle.Key, style.LayerKey);
        Assert.Contains(WeatherCoverageStyle.CreateAll(), s => s is VisibilityStyle);
    }

    [Fact]
    public void Visibility_greys_darken_by_day_and_lighten_at_dusk_and_night_as_it_worsens()
    {
        double Luminance(int band, Func<PaletteColors, RgbaColor> pick) =>
            RelativeLuminance(pick(WeatherPalette.Palette.Tokens[WeatherPalette.VisibilityToken(band)]));

        Assert.True(Luminance(1, c => c.Day) < Luminance(2, c => c.Day));
        Assert.True(Luminance(2, c => c.Day) < Luminance(3, c => c.Day));
        Assert.True(Luminance(1, c => c.Dusk) > Luminance(2, c => c.Dusk));
        Assert.True(Luminance(2, c => c.Night) > Luminance(3, c => c.Night));
    }

    [Fact]
    public void Sea_temperature_bands_are_labelled_in_celsius()
    {
        Assert.Equal("below 0 °C", SeaTemperatureStyle.Bands[0].Label);
        Assert.Equal("15–16 °C", SeaTemperatureStyle.Bands[16].Label);
        Assert.Equal("30 °C and higher", SeaTemperatureStyle.Bands[^1].Label);
        Assert.Null(new SeaTemperatureStyle().ResolveColorScheme(MarinerSettings.Default).Resolve(float.NaN));
    }

    [Fact]
    public void Sea_temperature_ramp_is_lightest_in_the_middle_by_day_and_darkest_at_night()
    {
        var tokens = Enumerable.Range(1, WeatherPalette.SeaTemperatureBandCount).Select(WeatherPalette.SeaTemperatureToken).ToArray();
        double Luminance(string token, Func<PaletteColors, RgbaColor> pick) => RelativeLuminance(pick(WeatherPalette.Palette.Tokens[token]));

        var day = tokens.Select(t => Luminance(t, c => c.Day)).ToArray();
        var dusk = tokens.Select(t => Luminance(t, c => c.Dusk)).ToArray();
        Assert.True(day[15] > day[0] && day[15] > day[^1], "Day is lightest near 15 °C.");
        Assert.True(dusk[15] < dusk[0] && dusk[15] < dusk[^1], "Dusk is darkest near 15 °C.");
        Assert.Equal(day.Length, day.Distinct().Count());
    }

    [Fact]
    public void Isotherms_are_every_2_degrees_in_kelvin_labelled_in_celsius_and_off_by_default()
    {
        var style = new SeaTemperatureIsothermStyle();
        var contours = style.Contours;

        Assert.False(style.IsVisibleByDefault);
        Assert.Equal(WeatherLayerKind.Isolines, style.Kind);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, style.Plane);
        Assert.Equal(WeatherParameters.SeaSurfaceTemperature, style.ValueFieldName);
        Assert.Equal(20, contours.Count);
        Assert.Equal(271.15, contours[0].Value, 6);
        Assert.Equal(309.15, contours[^1].Value, 6);
        Assert.All(contours, c => Assert.Equal("K", c.Unit));
        Assert.Equal("18°", style.FormatContourLabel(contours.Single(c => Math.Abs(c.Value - 291.15) < 1e-6)));
        Assert.Equal("-2°", style.FormatContourLabel(contours[0]));
        Assert.Equal(WeatherPalette.Palette.Resolve(WeatherPalette.IsothermInkToken, PaletteType.Day).ToHex(), contours[0].Color);
    }

    [Fact]
    public async Task Portrayal_draws_sea_temperature_as_a_clipped_fill_with_a_celsius_legend()
    {
        const int rows = 4, columns = 6;
        var values = new float[rows * columns];
        for (int i = 0; i < values.Length; i++)
            values[i] = 288.15f + i * 0.5f; // 15 °C rising half a degree a node
        var sst = TestGrids.Parameter(WeatherParameters.SeaSurfaceTemperature, (0, values));
        var set = new WeatherGridSet(TestGrids.Origin, TestGrids.Regular(rows, columns), [sst]);
        var source = new WeatherCoverageSource(set, WeatherParameters.SeaTemperatureGroup, [sst]);

        Assert.Equal(
            [SeaTemperatureStyle.Key, SeaTemperatureIsothermStyle.Key],
            WeatherCoverageStyle.CreateFor(source).Select(s => s.LayerKey));

        var result = await WeatherPortrayal.BuildAsync(source, WeatherCoverageStyle.CreateFor(source), "sst.nc", cancellationToken: Ct);

        var fill = Assert.IsType<GridCoverageSubLayer>(result.SubLayers[0]);
        Assert.True(fill.ClipToWater);
        Assert.Equal(S98DisplayPlane.OnDemandSurface, fill.Plane);
        Assert.Equal(SeaTemperatureStyle.Bands.Select(b => b.Label), fill.Legend!.Select(e => e.Label));
        var isotherms = Assert.IsType<IsolineCoverageSubLayer>(result.SubLayers[1]);
        Assert.Equal(["16°", "18°", "20°", "22°", "24°", "26°"], isotherms.Isolines.Select(l => l.Label).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Fills_sit_under_S104_on_the_on_demand_surface_at_half_opacity()
    {
        foreach (WeatherCoverageStyle style in new WeatherCoverageStyle[] { new WaveHeightStyle(), new WindSpeedStyle(), new SeaTemperatureStyle(), new VisibilityStyle() })
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
        foreach (WeatherCoverageStyle style in new WeatherCoverageStyle[] { new WindBarbStyle(), new WaveDirectionStyle(), new CurrentArrowStyle() })
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
    public void Wave_height_barbs_and_isobars_are_on_by_default_and_the_others_off()
    {
        Assert.True(new WaveHeightStyle().IsVisibleByDefault);
        Assert.True(new WindBarbStyle().IsVisibleByDefault);
        Assert.False(new WindSpeedStyle().IsVisibleByDefault);
        Assert.False(new WaveDirectionStyle().IsVisibleByDefault);
        Assert.True(new PressureIsobarStyle().IsVisibleByDefault);
    }

    [Fact]
    public void The_display_switches_override_the_defaults_and_unset_keys_follow_them()
    {
        var display = new EcdisDisplaySettings
        {
            LayerVisibility = new Dictionary<string, bool>
            {
                [PressureIsobarStyle.Key] = false,
                [WindSpeedStyle.Key] = true,
            },
        };

        Assert.False(new PressureIsobarStyle().IsDrawnWith(display));
        Assert.True(new WindSpeedStyle().IsDrawnWith(display));
        Assert.False(new WaveDirectionStyle().IsDrawnWith(display));
        Assert.True(new WaveHeightStyle().IsDrawnWith(display));

        // No display settings (headless rendering): the defaults.
        Assert.True(new PressureIsobarStyle().IsDrawnWith(null));
        Assert.False(new WindSpeedStyle().IsDrawnWith(null));
        Assert.True(new WindBarbStyle().IsDrawnWith(new EcdisDisplaySettings()));
    }

    [Fact]
    public void Only_the_sea_fills_are_clipped_to_water()
    {
        Assert.True(new WaveHeightStyle().ClipToWater);
        Assert.True(new SeaTemperatureStyle().ClipToWater);
        Assert.True(new VisibilityStyle().ClipToWater);
        Assert.All(
            WeatherCoverageStyle.CreateAll().Where(s => s is not (WaveHeightStyle or SeaTemperatureStyle or VisibilityStyle)),
            style => Assert.False(style.ClipToWater, style.LayerKey));
    }

    [Fact]
    public void Isobars_sit_on_other_chart_overlays_under_the_observations()
    {
        var style = new PressureIsobarStyle();

        Assert.Equal(WeatherLayerKind.Isolines, style.Kind);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, style.Plane);
        Assert.Equal(90, style.WithinPlanePriority);
        Assert.Equal(1.0, style.Opacity);
        Assert.Equal(WeatherParameters.PressureMsl, style.ValueFieldName);
        Assert.Null(style.ResolveColorScheme(MarinerSettings.Default));
        Assert.Null(style.ResolveSymbolScheme(MarinerSettings.Default));
    }

    [Fact]
    public void Isobars_are_every_4_hPa_in_pascals_and_labelled_in_hectopascals()
    {
        var style = new PressureIsobarStyle();
        var contours = style.Contours;

        Assert.Equal(61, contours.Count);
        Assert.Equal(86_000, contours[0].Value);
        Assert.Equal(110_000, contours[^1].Value);
        Assert.All(contours.Zip(contours.Skip(1)), pair => Assert.Equal(400, pair.Second.Value - pair.First.Value));
        Assert.All(contours, c =>
        {
            Assert.Equal("Pa", c.Unit);
            Assert.True(c.ShowLabel);
            Assert.Null(c.DashPattern);
        });
        Assert.Equal("1012", style.FormatContourLabel(contours.Single(c => c.Value == 101_200)));
        Assert.Equal("1012", style.FormatContourLabel(new ContourStyle { Value = 101_199.99, Unit = "Pa", LineWidth = 1, Color = "#000000" }));
    }

    [Theory]
    [InlineData(PaletteType.Day, "#4A5160")]
    [InlineData(PaletteType.Night, "#555A63")]
    public async Task Isobar_colour_follows_the_palette(PaletteType palette, string ink)
    {
        var style = new PressureIsobarStyle();
        await style.SwitchPaletteAsync(palette, Ct);

        Assert.All(style.Contours, c => Assert.Equal(ink, c.Color));
    }

    [Fact]
    public void Fills_and_symbols_have_no_contours()
    {
        Assert.All(
            WeatherCoverageStyle.CreateAll().Where(s => s.Kind != WeatherLayerKind.Isolines),
            style => Assert.Empty(style.Contours));
    }

    [Fact]
    public async Task Portrayal_traces_labelled_isobars_from_a_pressure_grid()
    {
        // Pressure rising 1 hPa per column eastward from 999.5 hPa over 13
        // columns: isobars at 1000, 1004 and 1008 hPa, north–south lines half a
        // column east of the 1000, 1004 and 1008 nodes.
        const int rows = 5, columns = 13;
        var values = new float[rows * columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
                values[r * columns + c] = 99_950f + 100f * c;
        }

        var pressure = TestGrids.Parameter(WeatherParameters.PressureMsl, (0, values));
        var set = new WeatherGridSet(TestGrids.Origin, new WeatherNativeGrid(rows, columns, -40, 10, 1, 1), [pressure]);
        var source = new WeatherCoverageSource(set, WeatherParameters.PressureGroup, [pressure]);
        Assert.Equal([PressureIsobarStyle.Key], WeatherCoverageStyle.CreateFor(source).Select(s => s.LayerKey));

        var result = await WeatherPortrayal.BuildAsync(
            source, [new PressureIsobarStyle()], "msl.grib2", new RenderContext { Palette = PaletteType.Dusk, TextScale = 2 }, cancellationToken: Ct);

        var isobars = Assert.IsType<IsolineCoverageSubLayer>(Assert.Single(result.SubLayers));
        Assert.Equal(PressureIsobarStyle.Key, isobars.LayerKey);
        Assert.Equal(S98DisplayPlane.OtherChartOverlays, isobars.Plane);
        Assert.Equal(90, isobars.WithinPlanePriority);
        Assert.Empty(isobars.DashPattern);
        Assert.Equal(20, isobars.LabelFontSize);
        Assert.Equal(WeatherPalette.Palette.Resolve(WeatherPalette.IsobarInkToken, PaletteType.Dusk), isobars.Ink);
        Assert.Equal(WeatherPalette.Palette.Resolve(WeatherPalette.IsobarHaloToken, PaletteType.Dusk), isobars.LabelHalo);
        Assert.Equal(["1000", "1004", "1008"], isobars.Isolines.Select(l => l.Label).Distinct());
        Assert.NotNull(isobars.Extent);

        // The 1004 hPa isobar runs north–south along 14.5°E (column 4.5).
        var line = isobars.Isolines.Single(l => l.Label == "1004");
        double x = line.MercatorCoordinates[0];
        Assert.All(Enumerable.Range(0, line.MercatorCoordinates.Length / 2), i => Assert.Equal(x, line.MercatorCoordinates[2 * i], 3));
        Assert.Equal(6378137.0 * 14.5 * Math.PI / 180, x, 3);
    }

    [Fact]
    public async Task Portrayal_flags_the_wave_fill_to_be_clipped_to_water_but_not_the_wind_fill()
    {
        var waves = await WeatherPortrayal.BuildAsync(WaveSource(withDirection: false), [new WaveHeightStyle()], "waves", cancellationToken: Ct);
        var wind = await WeatherPortrayal.BuildAsync(WindSource(), [new WindSpeedStyle()], "wind", cancellationToken: Ct);

        Assert.True(Assert.IsType<GridCoverageSubLayer>(Assert.Single(waves.SubLayers)).ClipToWater);
        Assert.False(Assert.IsType<GridCoverageSubLayer>(Assert.Single(wind.SubLayers)).ClipToWater);
    }

    [Fact]
    public async Task Fills_carry_their_band_legend_in_the_drawn_palette()
    {
        var waves = await WeatherPortrayal.BuildAsync(
            WaveSource(withDirection: false), [new WaveHeightStyle()], "waves", new RenderContext { Palette = PaletteType.Night }, cancellationToken: Ct);

        var legend = Assert.IsType<GridCoverageSubLayer>(Assert.Single(waves.SubLayers)).Legend;

        Assert.NotNull(legend);
        Assert.Equal(WaveHeightStyle.Bands.Select(b => b.Label), legend.Select(e => e.Label));
        Assert.Equal(WeatherPalette.Palette.Resolve(WeatherPalette.WaveHeightToken(1), PaletteType.Night).ToHex(), legend[0].Color);
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
        foreach (var token in new[] { WeatherPalette.BarbInkToken, WeatherPalette.WaveArrowToken, WeatherPalette.IsobarInkToken, WeatherPalette.IsothermInkToken })
        {
            var colors = WeatherPalette.Palette.Tokens[token];
            Assert.True(RelativeLuminance(colors.Night) < RelativeLuminance(colors.Dusk), token);
        }
    }

    [Fact]
    public void Palette_tokens_are_prefixed_so_they_never_resolve_against_S101_colours()
    {
        Assert.All(WeatherPalette.Palette.Tokens.Keys, token => Assert.StartsWith("WX", token, StringComparison.Ordinal));
        Assert.Equal(6 + 15 + 13 + 32 + 9 + 3, WeatherPalette.Palette.Tokens.Count);
    }

    [Fact]
    public void Content_scope_carries_the_style_version()
    {
        Assert.Equal(ApplicationContentScope.For("Weather", WeatherCoverageStyle.StyleVersion), WeatherCoverageStyle.ContentScope);
        Assert.StartsWith("app:Weather@5|toolkit:", WeatherCoverageStyle.ContentScope, StringComparison.Ordinal);
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
