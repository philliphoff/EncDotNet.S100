using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Weather.Tests;

/// <summary>
/// Issue #1300 (vector weather S7): the fronts and pressure-centre style —
/// line styles and pip sides (V17, V20), the front ink (V18), centres (V19),
/// smoothing (V21), scale rules (V22) and the legend (V25).
/// </summary>
public sealed class SurfaceAnalysisStyleTests
{
    [Fact]
    public void Each_kind_of_front_has_its_line_and_pips()
    {
        var cold = SurfaceAnalysisStyle.LineStyleOf(SurfaceFrontType.Cold);
        Assert.Equal([SurfaceAnalysisStyle.TriangleSymbol], cold.Symbols);
        Assert.Equal(LineSymbolSide.Left, cold.Side);
        Assert.False(cold.AlternateSides);

        var warm = SurfaceAnalysisStyle.LineStyleOf(SurfaceFrontType.Warm);
        Assert.Equal([SurfaceAnalysisStyle.SemicircleSymbol], warm.Symbols);
        Assert.Equal(LineSymbolSide.Left, warm.Side);

        var occluded = SurfaceAnalysisStyle.LineStyleOf(SurfaceFrontType.Occluded);
        Assert.Equal([SurfaceAnalysisStyle.TriangleSymbol, SurfaceAnalysisStyle.SemicircleSymbol], occluded.Symbols);
        Assert.False(occluded.AlternateSides);

        var stationary = SurfaceAnalysisStyle.LineStyleOf(SurfaceFrontType.Stationary);
        Assert.Equal([SurfaceAnalysisStyle.TriangleSymbol, SurfaceAnalysisStyle.SemicircleSymbol], stationary.Symbols);
        Assert.True(stationary.AlternateSides);
        Assert.Equal(LineSymbolSide.Left, stationary.Side);

        var trough = SurfaceAnalysisStyle.LineStyleOf(SurfaceFrontType.Trough);
        Assert.Empty(trough.Symbols);
        Assert.True(trough.IsDashed);
        Assert.Equal((1.5, 1.0, 0.5), (trough.DashLength, trough.GapLength, trough.Width));

        // Fronts are 0.7 mm, 9 mm between pips from 4 mm in (V17, V20), all in the one front ink (V18).
        foreach (var style in new[] { cold, warm, occluded, stationary })
            Assert.Equal((0.7, 9.0, 4.0), (style.Width, style.SymbolSpacing, style.SymbolStart));
        foreach (var type in Enum.GetValues<SurfaceFrontType>())
            Assert.Equal(WeatherPalette.FrontInkToken, SurfaceAnalysisStyle.LineStyleOf(type).ColorToken);
    }

    [Fact]
    public void Stationary_fronts_put_the_triangle_left_and_the_semicircle_right()
    {
        var symbols = SurfaceAnalysisStyle.LineStyles.GetLineStyle(SurfaceAnalysisStyle.Stationary.Name)!.Symbols;

        Assert.Equal(
            [(SurfaceAnalysisStyle.TriangleSymbol, 0f), (SurfaceAnalysisStyle.SemicircleSymbol, 180f)],
            symbols.Select(s => (s.Reference, s.Rotation)));
    }

    [Fact]
    public void Every_symbol_the_style_draws_is_embedded()
    {
        string[] names =
        [
            SurfaceAnalysisStyle.TriangleSymbol,
            SurfaceAnalysisStyle.SemicircleSymbol,
            SurfaceAnalysisStyle.CentreSymbol,
            SurfaceAnalysisStyle.CentreWithRuleSymbol,
            SurfaceAnalysisStyle.CentreLegendSymbol,
        ];

        // Each is drawn in the front ink alone.
        Assert.All(names, name =>
        {
            var svg = WeatherCoverageStyle.Symbols.Provider(name);
            Assert.NotNull(svg);
            Assert.Contains(WeatherPalette.FrontInkToken, svg, StringComparison.Ordinal);
            Assert.DoesNotContain(WeatherPalette.IsobarInkToken, svg, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_front_ink_is_darker_than_the_isobars_by_day_and_lighter_at_dusk()
    {
        var front = WeatherPalette.Palette.Tokens[WeatherPalette.FrontInkToken];
        var isobar = WeatherPalette.Palette.Tokens[WeatherPalette.IsobarInkToken];

        Assert.Equal(RgbaColor.FromHex("#1A2030"), front.Day);
        Assert.Equal(RgbaColor.FromHex("#E3E6EE"), front.Dusk);
        Assert.Equal(RgbaColor.FromHex("#646A76"), front.Night);
        Assert.True(Luminance(front.Day) < Luminance(isobar.Day));
        Assert.True(Luminance(front.Dusk) > Luminance(isobar.Dusk));
        Assert.InRange(Luminance(front.Night), 0, 0.2);
    }

    [Fact]
    public void A_front_is_a_halo_then_its_line_and_a_trough_has_a_scale_minimum()
    {
        var front = SurfaceAnalysisStyle.BuildFront("COLD 1", SurfaceFrontType.Cold);
        var halo = Assert.IsType<LineInstruction>(front[0]);
        var line = Assert.IsType<LineInstruction>(front[1]);
        Assert.Equal(WeatherPalette.IsobarHaloToken, halo.LineColor);
        Assert.Equal(0.7 + 2 * SurfaceAnalysisStyle.HaloMargin, halo.LineWidth, 6);
        Assert.True(halo.DrawingPriority < line.DrawingPriority);
        Assert.Equal(SurfaceAnalysisStyle.Cold.Name, line.LineStyleReference);
        Assert.All(front, i => Assert.Null(i.ScaleMinimum));
        Assert.All(front, i => Assert.Equal(SurfaceAnalysisStyle.ViewingGroup, i.ViewingGroup));

        var trough = SurfaceAnalysisStyle.BuildFront("TROF 1", SurfaceFrontType.Trough);
        Assert.All(trough, i => Assert.Equal(SurfaceAnalysisStyle.TroughScaleMinimum, i.ScaleMinimum));
        Assert.NotNull(Assert.IsType<LineInstruction>(trough[0]).Dashes);
        Assert.True(trough.Max(i => i.DrawingPriority) < front.Min(i => i.DrawingPriority), "Troughs draw under fronts.");
    }

    [Fact]
    public void A_centre_is_its_letter_over_a_cross_and_its_pressure_at_larger_scales()
    {
        var instructions = SurfaceAnalysisStyle.BuildCentre("LOW 1", new PressureCentre(PressureCentreKind.Low, 996, new GeoPosition(45, -50)));

        var texts = instructions.OfType<TextInstruction>().ToArray();
        var letter = Assert.Single(texts, t => t.Text == "L");
        Assert.Equal(TextFontWeight.Bold, letter.FontWeight);
        Assert.Null(letter.ScaleMinimum);
        Assert.True(letter.OffsetY < 0, "The letter sits above the centre.");
        var value = Assert.Single(texts, t => t.Text == "996");
        Assert.Equal(SurfaceAnalysisStyle.PressureValueScaleMinimum, value.ScaleMinimum);
        Assert.True(value.OffsetY > 0, "The pressure sits below the centre.");
        Assert.All(texts, t => Assert.Equal(WeatherPalette.IsobarHaloToken, t.HaloColor));

        // One cross at any scale: alone at small scales, with the rule wherever the pressure is.
        var points = instructions.OfType<PointInstruction>().ToArray();
        var alone = Assert.Single(points, p => p.SymbolReference == SurfaceAnalysisStyle.CentreSymbol);
        var withRule = Assert.Single(points, p => p.SymbolReference == SurfaceAnalysisStyle.CentreWithRuleSymbol);
        Assert.Equal(SurfaceAnalysisStyle.PressureValueScaleMinimum, alone.ScaleMaximum);
        Assert.Null(alone.ScaleMinimum);
        Assert.Equal(SurfaceAnalysisStyle.PressureValueScaleMinimum, withRule.ScaleMinimum);
        Assert.Null(withRule.ScaleMaximum);

        Assert.Equal("H", SurfaceAnalysisStyle.LetterOf(PressureCentreKind.High));
    }

    [Fact]
    public void The_legend_has_the_five_lines_and_the_pressure_centre()
    {
        var legend = SurfaceAnalysisStyle.BuildLegend(PaletteType.Night);

        Assert.Equal(
            ["Cold front", "Warm front", "Occluded front", "Stationary front", "Trough", "Pressure centre, hPa"],
            legend.Select(e => e.Label));
        Assert.All(legend.Take(5), e => Assert.IsType<LineLegendEntry>(e));
        Assert.IsType<SymbolLegendEntry>(legend[5]);
    }

    [Fact]
    public void Smoothing_keeps_the_ends_and_the_direction_and_adds_points()
    {
        GeoPosition[] coded = [new(40, -60), new(42, -55), new(40, -50), new(42, -45)];

        var smooth = SurfaceAnalysisStyle.Smooth(coded, SurfaceAnalysisStyle.SmoothingPasses);

        Assert.Equal(coded[0], smooth[0]);
        Assert.Equal(coded[^1], smooth[^1]);
        // Each pass keeps the ends and cuts every edge twice: 4 points become 8, 16, then 32.
        Assert.Equal(32, smooth.Count);
        Assert.True(smooth.Zip(smooth.Skip(1)).All(p => p.Second.Longitude > p.First.Longitude), "The line still runs west to east.");
        Assert.DoesNotContain(coded[1], smooth);
        Assert.True(smooth.Take(smooth.Count - 1).Max(p => p.Latitude) < 42, "The corners are cut.");
    }

    [Fact]
    public void Smoothing_drops_repeated_points_and_leaves_short_lines_alone()
    {
        GeoPosition[] repeated = [new(29.8, -35.6), new(29.8, -35.6), new(27.7, -38.3)];

        Assert.Equal([repeated[0], repeated[2]], SurfaceAnalysisStyle.Smooth(repeated, 3));
        Assert.Equal([repeated[0]], SurfaceAnalysisStyle.Smooth([repeated[0], repeated[1]], 3));
        Assert.Empty(SurfaceAnalysisStyle.Smooth([], 3));
    }

    [Fact]
    public void Smoothing_a_line_across_180_takes_the_short_way_round()
    {
        GeoPosition[] coded = [new(50, 179), new(48, -178), new(46, -174)];

        var smooth = SurfaceAnalysisStyle.Smooth(coded, 3);

        Assert.All(smooth, p => Assert.InRange(p.Longitude, -180, 180));
        Assert.All(smooth, p => Assert.True(p.Longitude >= 179 || p.Longitude <= -174, $"{p} left the short way round."));
        Assert.Equal(coded[0], smooth[0]);
        Assert.Equal(coded[^1], smooth[^1]);
    }

    private static double Luminance(RgbaColor c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
