using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.TropicalCyclones;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Weather.Tests;

/// <summary>
/// Issue #1296 (vector weather S2): the storm ink tokens, the class symbols
/// and their southern mirror, the line patterns, labels and legends of
/// <see cref="TropicalCycloneStyle"/>.
/// </summary>
public sealed class TropicalCycloneStyleTests
{
    private static readonly string[] StormTokens =
    [
        WeatherPalette.TropicalCycloneInkToken,
        WeatherPalette.TropicalCycloneHaloToken,
        WeatherPalette.TropicalCycloneConeToken,
        WeatherPalette.TropicalCyclone34Token,
        WeatherPalette.TropicalCyclone50Token,
        WeatherPalette.TropicalCyclone64Token,
    ];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void The_storm_ink_is_a_tinted_grey_in_every_palette_so_no_colour_is_red_amber_magenta_blue_or_green()
    {
        // §9.3: a low-chroma plum grey (OKLCH hue about 330) can't be taken for
        // S-101 danger, magenta (chroma about 0.22) or depth colours. The ink,
        // halo and cone are under chroma 0.05; the darkest day radii reach
        // 0.063 but are drawn at 16 to 38 % opacity, under 0.03 on the chart.
        foreach (var token in StormTokens)
        {
            var colors = WeatherPalette.Palette.Tokens[token];
            foreach (var (palette, color) in new[] { (PaletteType.Day, colors.Day), (PaletteType.Dusk, colors.Dusk), (PaletteType.Night, colors.Night) })
            {
                var (chroma, hue) = Oklch(color);
                bool fill = token is WeatherPalette.TropicalCyclone34Token or WeatherPalette.TropicalCyclone50Token or WeatherPalette.TropicalCyclone64Token;
                Assert.True(chroma < (fill ? 0.07 : 0.05), $"{token} {color.ToHex()} has chroma {chroma:0.000}.");
                if (chroma > 0.01)
                    Assert.InRange(hue, 300, 340);
                if (fill)
                {
                    var paper = palette == PaletteType.Day ? new RgbaColor(255, 255, 255, 255) : new RgbaColor(0x0B, 0x0E, 0x14, 255);
                    var drawn = Over(color, WeatherPalette.TropicalCycloneFillOpacity(token, palette), paper);
                    Assert.True(Oklch(drawn).Chroma < 0.03, $"{token} drawn as {drawn.ToHex()} in {palette}.");
                }
            }
        }
    }

    [Fact]
    public void Every_storm_token_has_distinct_day_and_dusk_values_and_night_stays_under_luminance_0_2()
    {
        foreach (var token in StormTokens)
        {
            var colors = WeatherPalette.Palette.Tokens[token];
            Assert.NotEqual(colors.Day, colors.Dusk);
            Assert.True(RelativeLuminance(colors.Night) < 0.2, token);
        }

        Assert.Equal(0.12, RelativeLuminance(WeatherPalette.Palette.Tokens[WeatherPalette.TropicalCycloneInkToken].Night), 2);
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public void The_radii_fills_nest_with_the_64_kt_core_strongest(PaletteType palette)
    {
        double Opacity(string token) => WeatherPalette.TropicalCycloneFillOpacity(token, palette);

        Assert.True(Opacity(WeatherPalette.TropicalCyclone64Token) > Opacity(WeatherPalette.TropicalCyclone50Token));
        Assert.True(Opacity(WeatherPalette.TropicalCyclone50Token) > Opacity(WeatherPalette.TropicalCyclone34Token));
        Assert.Throws<ArgumentException>(() => Opacity(WeatherPalette.TropicalCycloneInkToken));
    }

    [Fact]
    public void Fills_carry_the_palettes_opacity_and_the_cone_beyond_72_hours_is_stippled()
    {
        var dayCone = Assert.IsType<AreaInstruction>(Assert.Single(TropicalCycloneStyle.BuildConeFill("c", daysFourToFive: false, PaletteType.Day)));
        Assert.Equal(1 - 0.14, dayCone.Transparency!.Value, 6);

        var later = TropicalCycloneStyle.BuildConeFill("c", daysFourToFive: true, PaletteType.Night);
        Assert.Equal(2, later.Count);
        Assert.Equal(1 - 0.18, Assert.IsType<AreaInstruction>(later[0]).Transparency!.Value, 6);
        Assert.Equal(TropicalCycloneStyle.StippleFill, Assert.IsType<AreaInstruction>(later[1]).AreaFillReference);
        Assert.NotNull(TropicalCycloneStyle.ResolveAreaFill(TropicalCycloneStyle.StippleFill));

        var core = Assert.IsType<AreaInstruction>(TropicalCycloneStyle.BuildRadiusFill("r", 64, PaletteType.Dusk));
        Assert.Equal(WeatherPalette.TropicalCyclone64Token, core.FillColor);
        Assert.Equal(1 - 0.32, core.Transparency!.Value, 6);
    }

    [Fact]
    public void Line_patterns_separate_the_radii_and_tracks()
    {
        // V4: 34 kt dashed, 50 kt solid, 64 kt heavy; cone dashed; past track dotted.
        var r34 = (LineInstruction)TropicalCycloneStyle.BuildRadiusOutline("r", 34);
        var r50 = (LineInstruction)TropicalCycloneStyle.BuildRadiusOutline("r", 50);
        var r64 = (LineInstruction)TropicalCycloneStyle.BuildRadiusOutline("r", 64);
        var cone = (LineInstruction)TropicalCycloneStyle.BuildConeOutline("c");
        var forecast = (LineInstruction)TropicalCycloneStyle.BuildForecastTrack("t");
        var past = (LineInstruction)TropicalCycloneStyle.BuildPastTrack("p");

        Assert.Equal(1.2, r34.DashIntervalLength, 6);
        Assert.Null(r50.Dashes);
        Assert.Null(r64.Dashes);
        Assert.Equal(2 * r50.LineWidth, r64.LineWidth, 6);
        Assert.Equal(2.0, cone.DashIntervalLength, 6);
        Assert.Null(forecast.Dashes);
        Assert.Equal(0.6, forecast.LineWidth, 6);
        Assert.Equal(0.8, past.DashIntervalLength, 6);
        Assert.All(new[] { r34, r50, r64, cone, forecast, past }, line => Assert.Equal(WeatherPalette.TropicalCycloneInkToken, line.LineColor));
    }

    [Theory]
    [InlineData(TropicalCycloneClass.TropicalDepression, null, 25.0, "WXTCDN")]
    [InlineData(TropicalCycloneClass.TropicalStorm, null, 25.0, "WXTCSN")]
    [InlineData(TropicalCycloneClass.Hurricane, 2, 25.0, "WXTCHN")]
    [InlineData(TropicalCycloneClass.Hurricane, 3, 25.0, "WXTCMN")]
    [InlineData(TropicalCycloneClass.MajorHurricane, null, 25.0, "WXTCMN")]
    [InlineData(TropicalCycloneClass.Typhoon, null, 15.0, "WXTCHN")]
    [InlineData(TropicalCycloneClass.SuperTyphoon, null, 15.0, "WXTCMN")]
    [InlineData(TropicalCycloneClass.SubtropicalDepression, null, 30.0, "WXTCSDN")]
    [InlineData(TropicalCycloneClass.SubtropicalStorm, null, 30.0, "WXTCSSN")]
    [InlineData(TropicalCycloneClass.PostTropical, null, 40.0, "WXTCPTN")]
    [InlineData(TropicalCycloneClass.Low, null, 40.0, "WXTCPTN")]
    [InlineData(TropicalCycloneClass.Disturbance, null, 12.0, "WXTCPTN")]
    [InlineData(TropicalCycloneClass.Hurricane, 1, -18.0, "WXTCHS")]
    [InlineData(TropicalCycloneClass.TropicalStorm, null, -18.0, "WXTCSS")]
    public void Each_class_has_its_symbol_in_each_hemisphere(TropicalCycloneClass classification, int? category, double latitude, string expected)
    {
        var symbol = TropicalCycloneStyle.SymbolFor(classification, category, latitude);

        Assert.Equal(expected, symbol);
        Assert.Contains(symbol, WeatherCoverageStyle.Symbols.Names);
    }

    [Theory]
    [InlineData("WXTCS")]
    [InlineData("WXTCH")]
    [InlineData("WXTCM")]
    [InlineData("WXTCSS")]
    public void Northern_arms_leave_clockwise_and_southern_ones_are_their_mirror_image(string baseName)
    {
        // V6: the top arm ends east of the centre in the northern hemisphere
        // (the storm turns anticlockwise) and west in the southern.
        var north = Arm(baseName + "N", "arm1");
        var south = Arm(baseName + "S", "arm1");

        Assert.True(north[^2] > 0, $"{baseName}N's top arm ends at x={north[^2]}.");
        Assert.True(north[^1] < 0, "The top arm is above the centre.");
        Assert.Equal(north.Length, south.Length);
        for (int i = 0; i < north.Length; i += 2)
        {
            Assert.Equal(north[i], -south[i], 6);
            Assert.Equal(north[i + 1], south[i + 1], 6);
        }
    }

    [Fact]
    public void A_depression_has_no_arms_and_a_major_hurricane_has_its_outer_ring()
    {
        Assert.DoesNotContain(WindBarbTests.Svg("WXTCDN").Descendants(), e => (string?)e.Attribute("id") == "arm1");
        Assert.Contains(WindBarbTests.Svg("WXTCMN").Descendants(), e => (string?)e.Attribute("id") == "ring");
        Assert.DoesNotContain(WindBarbTests.Svg("WXTCHN").Descendants(), e => (string?)e.Attribute("id") == "ring");
        Assert.Contains(WindBarbTests.Svg("WXTCPTN").Descendants(), e => (string?)e.Attribute("id") == "cross");
        Assert.Contains("stroke-dasharray", WindBarbTests.Svg(TropicalCycloneStyle.EstimatedRingSymbol).ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TropicalCycloneClass.Hurricane, 4, "Category 4")]
    [InlineData(TropicalCycloneClass.Typhoon, 2, "Category 2")]
    [InlineData(TropicalCycloneClass.TropicalStorm, null, "Tropical storm")]
    [InlineData(TropicalCycloneClass.PostTropical, null, "Post-tropical")]
    [InlineData(TropicalCycloneClass.TropicalDepression, 1, "Tropical depression")]
    public void The_pill_names_the_category_or_the_class(TropicalCycloneClass classification, int? category, string expected) =>
        Assert.Equal(expected, TropicalCycloneStyle.ClassName(classification, category));

    [Fact]
    public void The_storm_label_spells_out_the_class_and_gives_wind_pressure_advisory_and_motion()
    {
        var point = Point(tau: 0, wind: 115, pressure: 948) with { MotionDirectionDegrees = 315, MotionSpeedKnots = 9 };
        var advisory = Advisory(point);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test-5", TimeSpan.FromHours(-5), "Test-5", "Test-5");

        Assert.Equal("HURRICANE ZELDA · 115 kt · 948 hPa", TropicalCycloneStyle.StormLabel(advisory.Storm, point));
        Assert.Equal("Adv 17 · issued Wed 15:00 · NW 9 kt", TropicalCycloneStyle.AdvisoryLabel(advisory, point, TimeZoneInfo.Utc, Invariant));
        Assert.Equal("Adv 17 · issued Wed 10:00 · NW 9 kt", TropicalCycloneStyle.AdvisoryLabel(advisory, point, zone, Invariant));
        // The culture's own short time (en-US writes a narrow space before PM).
        Assert.Matches(@"^Adv 17 · issued Wed 3:00\s?PM · NW 9 kt$", TropicalCycloneStyle.AdvisoryLabel(advisory, point, TimeZoneInfo.Utc, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("HURRICANE ZELDA", TropicalCycloneStyle.StormLabel(advisory.Storm, point with { MaxWindKnots = null, PressureHectopascals = null }));
    }

    [Fact]
    public void Forecast_points_carry_their_time_and_wind_at_12_hours_every_24_hours_and_the_last()
    {
        var point = Point(tau: 36, wind: 110);

        Assert.Equal("Fri 03:00 · 110 kt", TropicalCycloneStyle.ForecastLabel(point, TimeZoneInfo.Utc, Invariant));
        Assert.False(TropicalCycloneStyle.IsLabelled(point, isLast: false));
        Assert.True(TropicalCycloneStyle.IsLabelled(point, isLast: true));
        Assert.True(TropicalCycloneStyle.IsLabelled(Point(tau: 12, wind: 0), isLast: false));
        Assert.True(TropicalCycloneStyle.IsLabelled(Point(tau: 48, wind: 0), isLast: false));
        Assert.False(TropicalCycloneStyle.IsLabelled(Point(tau: 0, wind: 0), isLast: false));

        var instructions = TropicalCycloneStyle.BuildForecastPoint("p", "WXTCHN", "label");
        var text = Assert.IsType<TextInstruction>(instructions[1]);
        Assert.Equal(TropicalCycloneStyle.ForecastTimeScaleMinimum, text.ScaleMinimum);
    }

    [Fact]
    public void The_storm_is_drawn_larger_in_a_ring_and_its_second_line_goes_first_at_small_scales()
    {
        var storm = TropicalCycloneStyle.BuildStorm("s", "WXTCMN", estimated: false, "HURRICANE ZELDA · 115 kt", "Adv 17");
        var points = storm.OfType<PointInstruction>().ToArray();
        var texts = storm.OfType<TextInstruction>().ToArray();

        Assert.Equal([TropicalCycloneStyle.RingSymbol, "WXTCMN"], points.Select(p => p.SymbolReference));
        Assert.All(points, p => Assert.Equal(TropicalCycloneStyle.StormSymbolScale, p.SymbolScale, 6));
        Assert.Null(texts[0].ScaleMinimum);
        Assert.Equal(TropicalCycloneStyle.StormDetailScaleMinimum, texts[1].ScaleMinimum);
        Assert.Equal(TropicalCycloneStyle.EstimatedRingSymbol, TropicalCycloneStyle.BuildStorm("s", "WXTCMN", estimated: true, "a", "b").OfType<PointInstruction>().First().SymbolReference);
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(11, "N")]
    [InlineData(12, "NNE")]
    [InlineData(315, "NW")]
    [InlineData(350, "N")]
    [InlineData(-90, "W")]
    public void Motion_is_on_the_16_point_compass(double degrees, string expected) =>
        Assert.Equal(expected, TropicalCycloneStyle.CompassPoint(degrees));

    [Fact]
    public void A_storm_under_a_knot_is_stationary_and_one_without_motion_has_none()
    {
        Assert.Equal("stationary", TropicalCycloneStyle.Motion(Point(0, 50) with { MotionSpeedKnots = 0.5, MotionDirectionDegrees = 90 }));
        Assert.Null(TropicalCycloneStyle.Motion(Point(0, 50)));
    }

    [Theory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Night)]
    public void Both_legends_have_their_rows(PaletteType palette)
    {
        var track = TropicalCycloneStyle.BuildTrackLegend(palette);
        var areas = TropicalCycloneStyle.BuildAreasLegend(palette);

        Assert.Equal(
            ["Tropical depression", "Tropical storm", "Hurricane, typhoon", "Major (cat. 3+)", "Post-tropical, low", "Southern hemisphere", "Forecast track", "Past track"],
            track.Select(r => r.Label));
        Assert.Equal(6, track.OfType<SymbolLegendEntry>().Count());
        Assert.Equal(2, track.OfType<LineLegendEntry>().Count());
        Assert.Equal(["Cone, days 1–3", "Cone, days 4–5 (stippled)", "34 kt winds", "50 kt winds", "64 kt winds"], areas.Select(r => r.Label));
        Assert.All(areas, row => Assert.NotNull(Assert.IsType<SwatchLegendEntry>(row).Outline));
    }

    private static TropicalCycloneForecastPoint Point(int tau, int wind, int? pressure = null) => new()
    {
        Tau = tau,
        ValidTime = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc).AddHours(3 + tau),
        Position = new GeoPosition(27.4, -86.1),
        MaxWindKnots = wind,
        PressureHectopascals = pressure,
        Classification = TropicalCycloneClass.Hurricane,
        ClassCode = "HU",
    };

    private static TropicalCycloneAdvisory Advisory(TropicalCycloneForecastPoint point) => new()
    {
        Storm = new TropicalCycloneStorm("AL142026", "Zelda"),
        Advisory = "17",
        IssueTime = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero),
        Forecast = [point],
        ForecastTrack = [],
    };

    // The coordinates of an arm's path, x and y in turn.
    private static double[] Arm(string symbol, string id)
    {
        var path = WindBarbTests.Svg(symbol).Descendants().Single(e => (string?)e.Attribute("id") == id);
        return ((string)path.Attribute("d")!)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !char.IsLetter(t[0]))
            .Select(t => double.Parse(t, Invariant))
            .ToArray();
    }

    private static RgbaColor Over(RgbaColor color, double opacity, RgbaColor paper)
    {
        static byte Mix(byte top, byte under, double alpha) => (byte)Math.Round(top * alpha + under * (1 - alpha));
        return new RgbaColor(Mix(color.R, paper.R, opacity), Mix(color.G, paper.G, opacity), Mix(color.B, paper.B, opacity), 255);
    }

    private static (double Chroma, double Hue) Oklch(RgbaColor color)
    {
        static double Linear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        double r = Linear(color.R), g = Linear(color.G), b = Linear(color.B);
        double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        double a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        double bb = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        double hue = Math.Atan2(bb, a) * 180 / Math.PI;
        return (Math.Sqrt(a * a + bb * bb), hue < 0 ? hue + 360 : hue);
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
