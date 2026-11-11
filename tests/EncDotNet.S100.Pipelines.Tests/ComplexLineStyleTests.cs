using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The complex line style of the code-defined portrayal toolkit (#1293):
/// <see cref="ComplexLineStyle"/> becomes an S-100 Part 9 line style with a
/// symbol per place and side, <see cref="LineStyleSet"/> serves it by name,
/// <see cref="InstructionBuilder.ComplexLine"/> references it, and
/// <see cref="LegendBuilder"/> turns it and the portrayal's tokens and symbols
/// into legend rows.
/// </summary>
public sealed class ComplexLineStyleTests
{
    [Fact]
    public void One_symbol_on_one_side_repeats_every_spacing_from_the_start()
    {
        var style = FrontPipFixtures.Cold.ToLineStyle();

        Assert.Equal("TPIP_COLD", style.Name);
        Assert.Equal(FrontPipFixtures.Ink, style.Color);
        Assert.Equal(0.7f, style.Width, 0.0001f);
        Assert.True(style.HasPen);
        Assert.Null(style.DashPattern);
        Assert.Equal(9f, style.IntervalLength);
        Assert.Equal([new LineStyleSymbol(FrontPipFixtures.Triangle, 4f, 0f)], style.Symbols);
    }

    [Fact]
    public void The_right_side_turns_the_symbol_through_180_degrees()
    {
        var style = FrontPipFixtures.Warm.ToLineStyle();

        Assert.Equal([new LineStyleSymbol(FrontPipFixtures.Semicircle, 4f, 180f)], style.Symbols);
    }

    [Fact]
    public void Alternating_symbols_and_sides_repeat_after_both_have_been_placed()
    {
        var style = FrontPipFixtures.Stationary.ToLineStyle();

        Assert.Equal(18f, style.IntervalLength);
        Assert.Equal(
            [
                new LineStyleSymbol(FrontPipFixtures.Triangle, 4f, 0f),
                new LineStyleSymbol(FrontPipFixtures.WarmSemicircle, 13f, 180f),
            ],
            style.Symbols);
    }

    [Fact]
    public void One_symbol_alternating_sides_repeats_after_two_places()
    {
        var style = (FrontPipFixtures.Cold with { AlternateSides = true, Side = LineSymbolSide.Right }).ToLineStyle();

        Assert.Equal(18f, style.IntervalLength);
        Assert.Equal([180f, 0f], style.Symbols.Select(s => s.Rotation));
        Assert.Equal([4f, 13f], style.Symbols.Select(s => s.Position));
    }

    [Fact]
    public void Three_symbols_alternating_sides_repeat_after_six_places()
    {
        var style = (FrontPipFixtures.Cold with { Symbols = ["A", "B", "C"], AlternateSides = true, SymbolSpacing = 5, SymbolStart = 0 }).ToLineStyle();

        Assert.Equal(30f, style.IntervalLength);
        Assert.Equal(["A", "B", "C", "A", "B", "C"], style.Symbols.Select(s => s.Reference));
        Assert.Equal([0f, 180f, 0f, 180f, 0f, 180f], style.Symbols.Select(s => s.Rotation));
    }

    [Fact]
    public void A_dashed_pen_fills_the_symbol_interval_with_dashes()
    {
        var style = FrontPipFixtures.Dashed.ToLineStyle();

        // 9 mm interval, 1.5 mm dashes every 2.5 mm: the fourth is cut at the interval's end.
        Assert.Equal(9f, style.IntervalLength);
        Assert.Equal([0f, 1.5f, 2.5f, 1.5f, 5f, 1.5f, 7.5f, 1.5f], style.DashPattern!);
    }

    [Fact]
    public void A_dashed_pen_without_symbols_repeats_its_dash_and_gap()
    {
        var style = new ComplexLineStyle { Name = "TROUGH", ColorToken = "INK", Width = 0.5, DashLength = 1.5, GapLength = 1 }.ToLineStyle();

        Assert.Equal(2.5f, style.IntervalLength);
        Assert.Equal([0f, 1.5f], style.DashPattern!);
        Assert.Empty(style.Symbols);
    }

    [Fact]
    public void Symbols_alone_draw_no_pen()
    {
        var style = (FrontPipFixtures.Cold with { Width = 0 }).ToLineStyle();

        Assert.False(style.HasPen);
        Assert.Single(style.Symbols);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no colour")]
    [InlineData("negative width")]
    [InlineData("zero spacing")]
    [InlineData("draws nothing")]
    [InlineData("empty symbol")]
    public void An_invalid_style_is_refused(string problem)
    {
        var style = problem switch
        {
            "" => FrontPipFixtures.Cold with { Name = " " },
            "no colour" => FrontPipFixtures.Cold with { ColorToken = "" },
            "negative width" => FrontPipFixtures.Cold with { Width = -1 },
            "zero spacing" => FrontPipFixtures.Cold with { SymbolSpacing = 0 },
            "draws nothing" => FrontPipFixtures.Cold with { Width = 0, Symbols = [] },
            _ => FrontPipFixtures.Cold with { Symbols = [""] },
        };

        Assert.Throws<ArgumentException>(style.ToLineStyle);
        Assert.Throws<ArgumentException>(() => new LineStyleSet([style]));
    }

    [Fact]
    public void A_set_serves_its_styles_by_name_through_one_provider()
    {
        var set = FrontPipFixtures.LineStyles;

        Assert.Same(set.Provider, set.Provider);
        Assert.Equal(["TPIP_COLD", "TPIP_DASHED", "TPIP_STATIONARY", "TPIP_WARM"], set.Names.Order(StringComparer.Ordinal));
        Assert.True(set.Contains("TPIP_WARM"));
        Assert.Equal(18f, set.Provider("TPIP_STATIONARY")!.IntervalLength);
        Assert.Same(FrontPipFixtures.Stationary, set.GetDefinition("TPIP_STATIONARY"));
        Assert.Null(set.Provider("CTNARE51"));
        Assert.Null(set.GetDefinition("CTNARE51"));
    }

    [Fact]
    public void A_set_refuses_two_styles_with_one_name()
    {
        Assert.Throws<ArgumentException>(() => new LineStyleSet([FrontPipFixtures.Cold, FrontPipFixtures.Warm with { Name = "TPIP_COLD" }]));
    }

    [Fact]
    public void A_complex_line_references_the_style_and_carries_the_builders_settings()
    {
        var builder = new InstructionBuilder
        {
            ViewingGroup = 90020,
            DrawingPriority = 92,
            Plane = DisplayPlane.OverRadar,
            ScaleMinimum = 40_000_000,
        };

        var line = builder.ComplexLine("front-1", "TPIP_COLD");

        Assert.Equal("front-1", line.FeatureReference);
        Assert.Equal("TPIP_COLD", line.LineStyleReference);
        Assert.Null(line.LineColor);
        Assert.Equal(0, line.LineWidth);
        Assert.Equal(90020, line.ViewingGroup);
        Assert.Equal(92, line.DrawingPriority);
        Assert.Equal(DisplayPlane.OverRadar, line.Plane);
        Assert.Equal(40_000_000, line.ScaleMinimum);
    }

    [Fact]
    public void A_complex_line_is_not_the_simple_style()
    {
        Assert.Throws<ArgumentException>(() => new InstructionBuilder().ComplexLine("f", LineInstruction.SimpleLineStyleReference));
        Assert.Throws<ArgumentException>(() => new InstructionBuilder().ComplexLine("f", ""));
    }

    [Theory]
    [InlineData(PaletteType.Day, "#1A2030", "#B0342A")]
    [InlineData(PaletteType.Dusk, "#E3E6EE", "#E88A80")]
    [InlineData(PaletteType.Night, "#646A76", "#6E3A36")]
    public void A_line_row_resolves_the_style_for_the_palette(PaletteType palette, string ink, string warm)
    {
        var legend = new LegendBuilder(FrontPipFixtures.Palette, palette, FrontPipFixtures.Symbols);

        var row = legend.Line(FrontPipFixtures.Stationary, "Stationary front");

        Assert.Equal("Stationary front", row.Label);
        Assert.Equal(ink, row.Line.Color);
        Assert.Equal(0.7, row.Line.Width);
        Assert.Equal(18, row.Line.IntervalLength);
        Assert.Empty(row.Line.Dashes);
        Assert.Equal([4.0, 13.0], row.Line.Symbols.Select(s => s.Position));
        Assert.Equal([0.0, 180.0], row.Line.Symbols.Select(s => s.Rotation));
        // Palette classes resolved to the palette's colours.
        Assert.Contains(ink, row.Line.Symbols[0].Svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(warm, row.Line.Symbols[1].Svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fTPIP", row.Line.Symbols[1].Svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dashed_line_row_keeps_its_dashes()
    {
        var legend = new LegendBuilder(FrontPipFixtures.Palette, PaletteType.Day, FrontPipFixtures.Symbols);

        var simple = legend.Line(FrontPipFixtures.Ink, 0.5, "Trough", dashLength: 1.5, gapLength: 1);

        Assert.Equal([(0.0, 1.5)], simple.Line.Dashes);
        Assert.Equal(2.5, simple.Line.IntervalLength);
        Assert.Empty(simple.Line.Symbols);
    }

    [Fact]
    public void A_symbol_row_resolves_the_symbol_for_the_palette()
    {
        var legend = new LegendBuilder(FrontPipFixtures.Palette, PaletteType.Night, FrontPipFixtures.Symbols);

        var row = legend.Symbol(FrontPipFixtures.Triangle, "Triangle");

        Assert.Equal("Triangle", row.Label);
        Assert.Contains("#646A76", row.Svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Swatch_and_area_rows_resolve_their_colours()
    {
        var legend = new LegendBuilder(FrontPipFixtures.Palette, PaletteType.Day);

        var swatch = legend.Swatch(FrontPipFixtures.WarmInk, "Warm");
        var translucent = legend.Swatch(FrontPipFixtures.WarmInk, "Warm, half", transparency: 0.5);
        var area = legend.Area(FrontPipFixtures.WarmInk, "34 kt winds", legend.Sample(FrontPipFixtures.Ink, 0.3, 0.6, 0.6), transparency: 0.75);

        Assert.Equal(new SwatchLegendEntry("#B0342A", "Warm"), swatch);
        Assert.Null(swatch.Outline);
        Assert.Equal("#80B0342A", translucent.Color);
        Assert.Equal("#40B0342A", area.Color);
        Assert.Equal("#1A2030", area.Outline!.Color);
        Assert.Equal([(0.0, 0.6)], area.Outline.Dashes);
    }

    [Fact]
    public void Missing_tokens_and_symbols_are_reported()
    {
        var withoutSymbols = new LegendBuilder(FrontPipFixtures.Palette, PaletteType.Day);
        var withSymbols = new LegendBuilder(FrontPipFixtures.Palette, PaletteType.Day, FrontPipFixtures.Symbols);

        Assert.Throws<KeyNotFoundException>(() => withoutSymbols.Swatch("NOPE", "x"));
        Assert.Throws<InvalidOperationException>(() => withoutSymbols.Symbol(FrontPipFixtures.Triangle, "x"));
        Assert.Throws<KeyNotFoundException>(() => withSymbols.Symbol("NOPE", "x"));
        Assert.Throws<KeyNotFoundException>(() => withSymbols.Line(FrontPipFixtures.Cold with { Symbols = ["NOPE"] }, "x"));
    }

    [Fact]
    public void A_scheme_legend_is_one_swatch_per_band()
    {
        var scheme = new Coverage.CoverageColorScheme
        {
            FieldName = "h",
            Bands =
            [
                new Coverage.ColorBand { MinValue = 0, MaxValue = 1, Color = "#111111", Label = "low" },
                new Coverage.ColorBand { MinValue = 1, MaxValue = 2, Color = "#222222", Label = "high" },
            ],
        };

        Assert.Equal([new SwatchLegendEntry("#111111", "low"), new SwatchLegendEntry("#222222", "high")], LegendEntry.FromScheme(scheme));
    }
}
