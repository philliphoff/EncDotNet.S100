using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Aml.Families;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.Aml.Tests;

/// <summary>
/// Issue #1377 (AML S9): CLB contours and soundings, the MFF classes and
/// the AML 3.0 chart and land background (NCD, LND) and airfields.
/// </summary>
public sealed class AmlClbMffStylesTests
{
    private static readonly AmlDrawing ClbDrawing = new AmlPortrayal().Draw(AmlFixtures.Clb(), "UKC6TEST.000");

    [Fact]
    public void CLB_contours_are_thin_brown_lines_at_55_percent_with_their_depth()
    {
        var contour = ClbDrawing.Features.First(f => f.FeatureType == "DEPCNT");
        var instructions = Instructions(ClbDrawing, contour);

        var line = Assert.Single(instructions.OfType<LineInstruction>());
        Assert.Equal(LineInstruction.SimpleLineStyleReference, line.LineStyleReference);
        Assert.Equal("AMLBATHY", line.LineColor);
        Assert.Equal(0.7 * 0.32, line.LineWidth, 6);
        Assert.Null(line.Dashes);

        var label = Assert.Single(instructions.OfType<TextInstruction>());
        Assert.Equal("22", label.Text);
        Assert.Equal(TextFontSlant.Italics, label.FontSlant);
        Assert.Equal(AmlFamily.Seabed.ColorToken, label.FontColor);
        Assert.Equal(0.5, label.LinePlacementPosition);

        // Seabed brown at 55 % in every palette.
        var palette = AmlStyleCatalog.Default.Palette;
        Assert.Equal(new RgbaColor(0x7A, 0x4B, 0x1F, 0x8C), palette.Resolve("AMLBATHY", PaletteType.Day));
        Assert.Equal(0x8C, palette.Resolve("AMLBATHY", PaletteType.Night).A);
    }

    [Fact]
    public void CLB_draws_on_its_own_sub_layer_at_the_bottom_of_the_AML_plane()
    {
        var bathymetry = Assert.Single(ClbDrawing.BuildSubLayers(PaletteType.Day), s => s.LayerKey == "aml.bathymetry");

        Assert.Equal(S98DisplayPlane.AdditionalMilitaryLayers, bathymetry.Plane);
        Assert.True(AmlStyleCatalog.Default.Families.All(f => f == ClbStyles.Bathymetry || f.Order > ClbStyles.Bathymetry.Order));
    }

    [Fact]
    public void CLB_depth_areas_have_no_fill_and_no_boundary_but_stay_pickable()
    {
        var area = ClbDrawing.Features.First(f => f.FeatureType == "DEPARE");

        Assert.Empty(Instructions(ClbDrawing, area));
        Assert.DoesNotContain(ClbDrawing.Instructions.Values.SelectMany(i => i), i => i is AreaInstruction);
        Assert.Contains("DEPARE", ClbDrawing.UndrawnClasses);
    }

    [Fact]
    public void CLB_soundings_are_small_italic_brown_figures_at_each_sounding()
    {
        var sounding = Assert.Single(ClbDrawing.Features, f => f.FeatureType == "SOUNDG");
        Assert.Equal(6, sounding.Soundings.Count);
        Assert.Equal(sounding.Soundings.Select(s => s.Position), sounding.Points);

        var figures = Instructions(ClbDrawing, sounding).Cast<TextInstruction>().ToList();
        Assert.Equal(["23.4", "25.1", "27", "29.9", "31", "34"], figures.Select(f => f.Text));
        Assert.All(figures, f => Assert.Equal(TextFontSlant.Italics, f.FontSlant));
        Assert.All(figures, f => Assert.Equal(AmlFamily.Seabed.ColorToken, f.FontColor));
        Assert.All(figures, f => Assert.True(f.FontSize < AmlDrawingContext.FontSize));
        Assert.Equal(sounding.Soundings.Select(s => (GeoPosition?)s.Position), figures.Select(f => f.CoordinateOverride));
    }

    [Theory]
    [InlineData(23.46, "23.4")]
    [InlineData(0.05, "0")]
    [InlineData(30.99, "30.9")]
    [InlineData(31.7, "31")]
    [InlineData(-1.25, "-1.2")]
    public void Sounding_figures_are_truncated(double depth, string figure) =>
        Assert.Equal(figure, ClbStyles.SoundingFigure(depth));

    [Fact]
    public void CLB_classes_are_chart_classes_drawn_in_their_own_style()
    {
        Assert.All([ClbStyles.DepthContour, ClbStyles.DepthArea, ClbStyles.Sounding], s => Assert.True(s.IsChartClass));
        Assert.Equal("AML · CLB not used for depth checks", ClbStyles.NotForDepthChecksChip);
    }

    [Fact]
    public void The_CLB_legend_shows_the_thin_contour_and_a_figure()
    {
        var legend = AmlStyleCatalog.Default.BuildLegend(ClbStyles.Bathymetry, PaletteType.Day);

        var contour = Assert.IsType<LineLegendEntry>(legend[0]);
        Assert.Equal(ClbStyles.ContourWidth, contour.Line.Width);
        Assert.Empty(contour.Line.Dashes);
        Assert.IsType<SymbolLegendEntry>(legend[2]);
    }

    [Fact]
    public void MFF_classes_draw_in_the_family_each_belongs_to()
    {
        var mff = new AmlPortrayal().Draw(AmlFixtures.Mff(), "UKM6TEST.000");

        Assert.Empty(mff.UndrawnClasses);
        Assert.Equal(
            ["aml.background", "aml.seabed", "aml.environmental", "aml.operational"],
            mff.Instructions.Keys.Select(k => k.LayerKey));
        Assert.Equal("AML_BACKGROUND_LIGHT", Symbol(mff, "LIGHTS"));
        Assert.Equal("AML_BACKGROUND_BUOY", Symbol(mff, "BOYLAT"));
        Assert.Equal("AML_BACKGROUND_BEACON", Symbol(mff, "BCNCAR"));
        Assert.Equal("AML_OPERATIONAL_PLATFORM", Symbol(mff, "OFSPLF"));
        Assert.Equal("AML_ENVIRONMENTAL_TIDE", Symbol(mff, "TS_PAD"));
        Assert.Equal("AML_SEABED_LINE", Line(mff, "CBLSUB"));
        Assert.Equal("AML_BACKGROUND_LINE", Line(mff, "COALNE"));
        Assert.Equal("AML_SEABED_AREA", Line(mff, "PIPARE"));
    }

    [Fact]
    public void Navigation_aids_draw_without_a_name_since_the_chart_names_them()
    {
        var mff = new AmlPortrayal().Draw(AmlFixtures.Mff(), "UKM6TEST.000");
        var light = mff.Features.First(f => f.FeatureType == "LIGHTS");

        Assert.DoesNotContain(Instructions(mff, light), i => i is TextInstruction);
        Assert.Contains(Instructions(mff, mff.Features.First(f => f.FeatureType == "OFSPLF")), i => i is TextInstruction { Text: "Alpha" });
    }

    [Fact]
    public void Land_areas_draw_only_their_name_and_islets_a_dot()
    {
        var drawing = new AmlPortrayal().Draw(
            AmlCellBuilder.Mff()
                .Area("LNDARE", [(-1.4, 50.6), (-1.3, 50.6), (-1.3, 50.5), (-1.4, 50.5), (-1.4, 50.6)], ("OBJNAM", "Isle"))
                .Point("LNDARE", (-1.2, 50.5))
                .Area("SEAARE", [(-1.2, 50.6), (-1.1, 50.6), (-1.1, 50.5), (-1.2, 50.5), (-1.2, 50.6)], ("OBJNAM", "The Solent"))
                .Build());

        var island = Instructions(drawing, drawing.Features[0]);
        Assert.Equal("Isle", Assert.IsType<TextInstruction>(Assert.Single(island)).Text);
        Assert.Equal("AML_BACKGROUND_ISLET", Assert.IsType<PointInstruction>(Assert.Single(Instructions(drawing, drawing.Features[1]))).SymbolReference);
        Assert.Equal("The Solent", Assert.IsType<TextInstruction>(Assert.Single(Instructions(drawing, drawing.Features[2]))).Text);
    }

    [Fact]
    public void AML_3_land_and_chart_background_and_airfields_are_drawn()
    {
        var drawing = new AmlPortrayal().Draw(
            AmlCellBuilder.Aml30("AMLTEST6U_NCD.000")
                .Point("LIGHTS", (9.0, 54.0))
                .Point("BCNSPP", (9.1, 54.0))
                .Point("LNDMRK", (9.2, 54.0), ("OBJNAM", "Tower"))
                .Point("HRBFAC", (9.3, 54.0))
                .Point("shedht", (9.4, 54.0))
                .Area("wstdsp", [(9.0, 53.9), (9.1, 53.9), (9.1, 53.8), (9.0, 53.8), (9.0, 53.9)])
                .Line("inlsho", [(9.2, 53.9), (9.3, 53.8)])
                .Point("AIRARE", (9.4, 53.9), ("OBJNAM", "HAMBURG"))
                .Area("RUNWAY", [(9.5, 53.9), (9.51, 53.9), (9.51, 53.8), (9.5, 53.8), (9.5, 53.9)])
                .Build());

        Assert.Empty(drawing.UndrawnClasses);
        Assert.Equal("AML_AIR_AIRFIELD", Symbol(drawing, "AIRARE"));
        Assert.Equal("AML_BACKGROUND_SITE", Symbol(drawing, "shedht"));
        Assert.Equal("AML_BACKGROUND_HARBOUR", Symbol(drawing, "HRBFAC"));
    }

    [Fact]
    public void Every_new_style_has_a_legend_entry_and_symbol()
    {
        var catalog = AmlStyleCatalog.Default;
        IAmlStyleModule[] modules = [ClbStyles.Module, MffStyles.Module, ChartBackgroundStyles.Module, AirfieldStyles.Module];

        foreach (var style in modules.SelectMany(m => m.Classes))
        {
            Assert.Same(style, catalog.Find(style.Acronym));
            Assert.NotNull(catalog.BuildLegendEntry(style, PaletteType.Day));
            if (style.Glyph is { } glyph)
                Assert.NotNull(catalog.Symbols.GetSymbol(AmlSignature.FramedSymbol(style.Family, glyph)));
        }

        Assert.Contains(catalog.BuildLegend(PaletteType.Day), g => g.Family == ChartBackgroundStyles.Background);
    }

    private static IReadOnlyList<DrawingInstruction> Instructions(AmlDrawing drawing, AmlFeature feature) =>
        [.. drawing.Instructions.Values.SelectMany(i => i).Where(i => i.FeatureReference == feature.Id)];

    private static string? Symbol(AmlDrawing drawing, string acronym) =>
        Instructions(drawing, drawing.Features.First(f => f.FeatureType == acronym)).OfType<PointInstruction>().First().SymbolReference;

    private static string? Line(AmlDrawing drawing, string acronym) =>
        Instructions(drawing, drawing.Features.First(f => f.FeatureType == acronym)).OfType<LineInstruction>().First().LineStyleReference;
}
