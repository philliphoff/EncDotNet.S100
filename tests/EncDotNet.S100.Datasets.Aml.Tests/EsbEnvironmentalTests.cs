using EncDotNet.S100.Datasets.Aml.Families;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.Aml.Tests;

/// <summary>
/// Issue #1376 (AML S8): the environmental family, drawn from a synthetic ESB
/// cell laid out as the design's frame B2d and a synthetic AML 3.0 SED cell.
/// </summary>
public sealed class EsbEnvironmentalTests
{
    private static readonly AmlDrawing Esb = new AmlPortrayal().Draw(AmlFixtures.Esb(), "UKE6TEST.000");
    private static readonly AmlDrawing Sed = new AmlPortrayal().Draw(AmlFixtures.Sed(), "AMLTEST6U_SED.000");

    [Fact]
    public void Every_ESB_class_is_drawn_in_the_environmental_family_but_chart_classes_are_left_to_the_ENC()
    {
        Assert.Equal(["LIGHTS"], Esb.UndrawnClasses);
        Assert.Equal(Esb.Features.Count - 1, Esb.DrawnFeatureCount);
        var family = Assert.Single(Esb.Instructions.Keys);
        Assert.Equal(AmlFamily.Environmental, family);
        Assert.Equal(["aml.environmental"], Esb.BuildSubLayers(PaletteType.Day).Select(l => l.LayerKey));
        Assert.All(Esb.Instructions[family], i => Assert.Equal(AmlFamily.Environmental.ViewingGroup, i.ViewingGroup));
    }

    [Fact]
    public void Every_ESB_class_in_the_local_samples_has_a_style()
    {
        // The AML classes of the UKE6UT00 and UKE4U1xx samples and the 3.0 SED trial.
        string[] classes =
        [
            "bchare", "bchext", "bchprf", "botmft", "bprare", "brgare", "btdare", "divloc", "drpzne", "envare", "fshare",
            "icebrg", "icelea", "icelin", "icemov", "icepol", "imgare", "lndice", "lndplc", "lndpnt", "lndste", "lndstp",
            "lndzne", "lngare", "lsrare", "mcmare", "pfdare", "resloc", "rkdare", "seaice", "sedlay", "shlloc", "trfare",
            "twlscr", "viewpt",
        ];

        Assert.All(classes, c => Assert.Equal(AmlFamily.Environmental, AmlStyleCatalog.Default.Find(c)?.Family));
        Assert.Equal(classes.Order(), EsbEnvironmentalStyles.Module.Classes.Select(c => c.Acronym).Order());
        Assert.All(EsbEnvironmentalStyles.Module.Classes, c => Assert.False(c.IsChartClass));
    }

    [Theory]
    [InlineData(523, false, "AML_ENVIRONMENTAL_SED_S__")] // sand: dots
    [InlineData(521, false, "AML_ENVIRONMENTAL_SED_M__")] // mud: dashes
    [InlineData(522, false, "AML_ENVIRONMENTAL_SED_M__")] // silt
    [InlineData(514, false, "AML_ENVIRONMENTAL_SED_M__")] // clays
    [InlineData(525, false, "AML_ENVIRONMENTAL_SED_G__")] // gravel: rings
    [InlineData(526, false, "AML_ENVIRONMENTAL_SED_G__")] // pebbles
    [InlineData(531, false, "AML_ENVIRONMENTAL_SED_B__")] // boulder
    [InlineData(519, false, "AML_ENVIRONMENTAL_SED_R__")] // chalk: rock
    [InlineData(528, false, "AML_ENVIRONMENTAL_SED_R__")] // rock
    [InlineData(530, false, "AML_ENVIRONMENTAL_SED_H__")] // shells
    [InlineData(513, false, "AML_ENVIRONMENTAL_SED_H__")] // coral
    [InlineData(546, true, "AML_ENVIRONMENTAL_SED_T__")] // till
    [InlineData(543, false, "AML_ENVIRONMENTAL_SED_G_S_")] // sandy gravel
    [InlineData(540, false, "AML_ENVIRONMENTAL_SED_S_G_")] // gravelly sand
    [InlineData(537, false, "AML_ENVIRONMENTAL_SED_S__G")] // slightly gravelly sand
    [InlineData(534, false, "AML_ENVIRONMENTAL_SED_M_S_G")] // slightly gravelly sandy mud
    [InlineData(542, false, "AML_ENVIRONMENTAL_SED_G_SM_")] // muddy sandy gravel
    [InlineData(544, false, "AML_ENVIRONMENTAL_SED_S_MG_")] // AML 1.0: gravelly muddy sand
    [InlineData(544, true, "AML_ENVIRONMENTAL_SED_R__")] // AML 3.0: crystalline rock
    public void Sediment_is_a_pattern_per_kind_and_a_mixture_draws_its_parts_in_one_pattern(int natsed, bool aml30, string fill)
    {
        Assert.Equal(fill, EsbSediment.FillName(EsbSediment.Classify(natsed, aml30)));
    }

    [Theory]
    [InlineData(701)]
    [InlineData(704)]
    [InlineData(0)]
    public void Unknown_sediment_has_no_pattern(int natsed)
    {
        Assert.Empty(EsbSediment.Classify(natsed, aml30: false));
        Assert.Null(EsbSediment.FillName(EsbSediment.Classify(natsed, aml30: false)));
    }

    [Fact]
    public void A_list_of_sediments_is_drawn_with_the_first_as_the_main_one()
    {
        // 525,523,522 in the UKE4U105 sample: gravel, with sand and silt.
        var cell = AmlCellBuilder.Esb().Area("sedlay", [(0, 1), (1, 1), (1, 0), (0, 0), (0, 1)], ("natsed", "525,523,522")).Build();
        var feature = Assert.Single(new AmlPortrayal().Draw(cell).Features);

        Assert.Equal("AML_ENVIRONMENTAL_SED_G_SM_", EsbSediment.FillName(EsbSediment.Classify(feature)));
    }

    [Fact]
    public void A_sediment_area_is_the_pattern_over_a_7_percent_wash_with_a_thin_boundary_and_no_label()
    {
        var sand = Esb.Features.First(f => f.FeatureType == "sedlay" && f.Attribute("natsed") == "523");
        var drawn = Instructions(Esb, sand);

        Assert.Equal(3, drawn.Count);
        var wash = Assert.Single(drawn.OfType<AreaInstruction>(), a => a.FillColor is not null);
        Assert.Equal("AMLENV", wash.FillColor);
        Assert.Equal(0.93, wash.Transparency);
        var pattern = Assert.Single(drawn.OfType<AreaInstruction>(), a => a.AreaFillReference is not null);
        Assert.Equal("AML_ENVIRONMENTAL_SED_S__", pattern.AreaFillReference);
        Assert.True(wash.DrawingPriority < pattern.DrawingPriority, "The wash is under the pattern.");
        var boundary = Assert.Single(drawn.OfType<LineInstruction>());
        Assert.Equal(LineInstruction.SimpleLineStyleReference, boundary.LineStyleReference);
        Assert.Equal("AMLENV", boundary.LineColor);
        Assert.Equal(AmlSignature.PixelMm, boundary.LineWidth);
        Assert.Equal(AmlSignature.DashDot, boundary.Dashes);
        Assert.DoesNotContain(drawn, i => i is TextInstruction);
    }

    [Fact]
    public void Sediment_patterns_and_their_tiles_come_with_the_result()
    {
        var result = Esb.ToVectorPortrayalResult(new Core.SpecRef("AML", default), "UKE6TEST.000");
        var fills = Esb.Instructions.Values.SelectMany(i => i).OfType<AreaInstruction>()
            .Select(a => a.AreaFillReference).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            ["AML_ENVIRONMENTAL_SED_G_S_", "AML_ENVIRONMENTAL_SED_G__", "AML_ENVIRONMENTAL_SED_M__", "AML_ENVIRONMENTAL_SED_R__", "AML_ENVIRONMENTAL_SED_S__"],
            fills);
        Assert.All(fills, name =>
        {
            var fill = result.AreaFillProvider!(name);
            Assert.NotNull(fill);
            Assert.Equal(name, fill.PatternSymbol);
            Assert.Equal(EsbSediment.TileSize, fill.V1X);
            Assert.Equal(EsbSediment.TileSize, fill.V2Y);
            var tile = result.SymbolProvider!(name);
            Assert.NotNull(tile);
            Assert.Contains("AMLENV", tile, StringComparison.Ordinal);
        });
        Assert.Null(result.AreaFillProvider!("AML_ENVIRONMENTAL_SED_X__"));
        Assert.Null(result.SymbolProvider!("AML_ENVIRONMENTAL_SED_"));
    }

    [Fact]
    public void A_cell_with_a_content_scope_has_its_sediment_patterns_clipped_once_per_cell()
    {
        var cached = Esb.ToVectorPortrayalResult(new Core.SpecRef("AML", default), "UKE6TEST.000", contentScope: "sha:1");
        var uncached = Esb.ToVectorPortrayalResult(new Core.SpecRef("AML", default), "UKE6TEST.000");
        var ral = new AmlPortrayal().Draw(AmlFixtures.Ral()).ToVectorPortrayalResult(new Core.SpecRef("AML", default), "R", contentScope: "sha:2");

        Assert.Equal($"aml|sha:1|{AmlStyleCatalog.Default.ContentScope}|environmental", Assert.Single(cached.SubLayers).PatternClipCacheKey);
        Assert.Null(Assert.Single(uncached.SubLayers).PatternClipCacheKey);
        Assert.All(ral.SubLayers, s => Assert.Null(s.PatternClipCacheKey));
    }

    [Fact]
    public void Sand_is_dots_mud_dashes_and_gravel_rings()
    {
        var sand = EsbSediment.ResolveTile(EsbSediment.FillName(AmlSedimentKind.Sand))!;
        var mud = EsbSediment.ResolveTile(EsbSediment.FillName(AmlSedimentKind.Mud))!;
        var gravel = EsbSediment.ResolveTile(EsbSediment.FillName(AmlSedimentKind.Gravel))!;

        Assert.Equal(24, Count(sand, """<circle class="fAMLENV" """));
        Assert.Equal(12, Count(mud, "<path "));
        Assert.Equal(9, Count(gravel, """<circle class="sAMLENV f0" """));

        // Sandy gravel: half the shared slots rings, a quarter dots.
        var sandyGravel = EsbSediment.ResolveTile("AML_ENVIRONMENTAL_SED_G_S_")!;
        Assert.Equal(18, Count(sandyGravel, """<circle class="sAMLENV f0" """));
        Assert.Equal(9, Count(sandyGravel, """<circle class="fAMLENV" """));
    }

    [Fact]
    public void A_sediment_sample_point_is_framed_and_named_from_the_catalogue()
    {
        var chalk = Assert.Single(Esb.Features, f => f.FeatureType == "sedlay" && f.GeometryType == S100GeometryType.Point);
        var drawn = Instructions(Esb, chalk);

        Assert.Equal("AML_ENVIRONMENTAL_SED", drawn.OfType<PointInstruction>().Single().SymbolReference);
        Assert.Equal("Chalk", drawn.OfType<TextInstruction>().Single().Text);
    }

    [Theory]
    [InlineData("botmft", "AML_ENVIRONMENTAL_CHEVRON")]
    [InlineData("lndpnt", "AML_ENVIRONMENTAL_LZ")]
    [InlineData("drpzne", "AML_ENVIRONMENTAL_DZ")]
    [InlineData("divloc", "AML_ENVIRONMENTAL_DIVE")]
    [InlineData("viewpt", "AML_ENVIRONMENTAL_VIEW")]
    [InlineData("resloc", "AML_ENVIRONMENTAL_RES")]
    [InlineData("shlloc", "AML_ENVIRONMENTAL_SHELTER")]
    [InlineData("icebrg", "AML_ENVIRONMENTAL_ICEBERG")]
    public void Points_are_framed_glyphs(string acronym, string symbol)
    {
        var point = Esb.Features.First(f => f.FeatureType == acronym && f.GeometryType == S100GeometryType.Point);

        Assert.Equal(symbol, Instructions(Esb, point).OfType<PointInstruction>().Single().SymbolReference);
        var svg = AmlStyleCatalog.Default.Symbols.GetSymbol(symbol);
        Assert.NotNull(svg);
        Assert.Contains("id=\"frame\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bottom_feature_is_a_point_line_or_area_in_the_signature()
    {
        var line = Esb.Features.First(f => f.FeatureType == "botmft" && f.GeometryType == S100GeometryType.Curve);
        var area = Esb.Features.First(f => f.FeatureType == "botmft" && f.GeometryType == S100GeometryType.Surface);

        Assert.Equal("AML_ENVIRONMENTAL_LINE", Instructions(Esb, line).OfType<LineInstruction>().Single().LineStyleReference);
        Assert.Equal("AML_ENVIRONMENTAL_AREA", Instructions(Esb, area).OfType<LineInstruction>().Single().LineStyleReference);
        Assert.Equal(["Reela"], Instructions(Esb, area).OfType<TextInstruction>().Select(t => t.Text));
    }

    [Fact]
    public void Trawl_scours_are_short_parallel_strokes_along_the_scour()
    {
        var scours = Esb.Features.Where(f => f.FeatureType == "twlscr").ToList();
        var style = AmlStyleCatalog.Default.LineStyleProvider("AML_ENVIRONMENTAL_SCOURS");

        Assert.Equal(3, scours.Count);
        Assert.All(scours, s => Assert.Equal("AML_ENVIRONMENTAL_SCOURS", Assert.IsType<LineInstruction>(Instructions(Esb, s).Single()).LineStyleReference));
        Assert.NotNull(style);
        Assert.False(style.HasPen);
        Assert.Equal("AML_ENVIRONMENTAL_SCOUR", Assert.Single(style.Symbols).Reference);
        Assert.NotNull(AmlStyleCatalog.Default.Symbols.GetSymbol("AML_ENVIRONMENTAL_SCOUR"));
    }

    [Fact]
    public void A_beach_exit_is_a_solid_line_on_a_halo_with_chevrons_off_the_beach()
    {
        var exit = Assert.Single(Esb.Features, f => f.FeatureType == "bchext");
        var style = AmlStyleCatalog.Default.LineStyleProvider("AML_ENVIRONMENTAL_EXIT");

        Assert.Equal("AML_ENVIRONMENTAL_EXIT", Assert.IsType<LineInstruction>(Instructions(Esb, exit).Single()).LineStyleReference);
        Assert.NotNull(style);
        Assert.Equal(3, style.Components.Count);
        Assert.Equal(AmlPalette.HaloToken, style.Components[0].Color);
        Assert.Empty(style.Components[1].DashPattern ?? []);
        Assert.Equal("AML_ENVIRONMENTAL_CHEV", Assert.Single(style.Components[2].Symbols).Reference);
    }

    [Fact]
    public void Ice_movement_is_an_arrow_turned_to_the_drift_and_labelled_with_it()
    {
        var drifting = Esb.Features.First(f => f.FeatureType == "icemov" && f.Attribute("icebdr") == "504");
        var still = Esb.Features.First(f => f.FeatureType == "icemov" && f.Attribute("icebdr") == "501");

        var arrow = Instructions(Esb, drifting).OfType<PointInstruction>().Single();
        Assert.Equal("AML_ENVIRONMENTAL_DRIFT", arrow.SymbolReference);
        Assert.Equal(135, arrow.Rotation);
        Assert.Equal(SymbolRotationCrs.Geographic, arrow.RotationCrs);
        Assert.Equal("Little Ice Island · drift SE", Instructions(Esb, drifting).OfType<TextInstruction>().Single().Text);

        Assert.Equal("AML_ENVIRONMENTAL_ICE", Instructions(Esb, still).OfType<PointInstruction>().Single().SymbolReference);
        Assert.Empty(Instructions(Esb, still).OfType<TextInstruction>());
    }

    [Theory]
    [InlineData("502", 45.0)]
    [InlineData("505", 180.0)]
    [InlineData("509", 0.0)]
    [InlineData("508", 315.0)]
    [InlineData("510", null)]
    [InlineData("511", null)]
    public void The_drift_direction_comes_from_icebdr(string icebdr, double? degrees)
    {
        var cell = AmlCellBuilder.Esb().Point("icemov", (0, 0), ("icebdr", icebdr)).Build();

        Assert.Equal(degrees, EsbEnvironmentalStyles.DriftDirection(Assert.Single(new AmlPortrayal().Draw(cell).Features)));
    }

    [Fact]
    public void Ice_and_zone_areas_and_lines_keep_the_AML_signature()
    {
        var seaIce = Assert.Single(Esb.Features, f => f.FeatureType == "seaice");
        var iceEdge = Assert.Single(Esb.Features, f => f.FeatureType == "icelin");

        Assert.Equal("AML_ENVIRONMENTAL_AREA", Instructions(Esb, seaIce).OfType<LineInstruction>().Single().LineStyleReference);
        Assert.DoesNotContain(Instructions(Esb, seaIce), i => i is AreaInstruction);
        Assert.Equal("AML_ENVIRONMENTAL_LINE", Instructions(Esb, iceEdge).OfType<LineInstruction>().Single().LineStyleReference);
    }

    [Fact]
    public void AML_3_SED_cells_use_the_same_styles_and_read_544_as_crystalline_rock()
    {
        Assert.Empty(Sed.UndrawnClasses);
        var fills = Sed.Instructions.Values.SelectMany(i => i).OfType<AreaInstruction>()
            .Select(a => a.AreaFillReference).OfType<string>().ToList();
        Assert.Equal(
            ["AML_ENVIRONMENTAL_SED_R__", "AML_ENVIRONMENTAL_SED_T__", "AML_ENVIRONMENTAL_SED_S_G_", "AML_ENVIRONMENTAL_SED_H__"],
            fills);
        var sample = Assert.Single(Sed.Features, f => f.GeometryType == S100GeometryType.Point);
        Assert.Equal("Sand", Instructions(Sed, sample).OfType<TextInstruction>().Single().Text);
    }

    [Fact]
    public void The_sediment_legend_has_a_swatch_per_kind_and_a_mixture()
    {
        var legend = AmlStyleCatalog.Default.BuildLegend(AmlFamily.Environmental, PaletteType.Day);
        var sediment = legend.Take(8).ToList();

        Assert.All(sediment, e => Assert.IsType<SymbolLegendEntry>(e));
        Assert.Equal(
            [
                "Sediment: sand", "Sediment: mud, silt, clay", "Sediment: gravel, pebbles", "Sediment: cobbles, boulders",
                "Sediment: rock", "Sediment: shells, coral", "Sediment: till, glacial drift",
                "Sediment mixture, such as sandy gravel: sparser marks for the lesser parts",
            ],
            sediment.Select(e => e.Label));
        var sand = (SymbolLegendEntry)sediment[0];
        Assert.Contains("#2B7547", sand.Svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fill-opacity=\"0.07\"", sand.Svg, StringComparison.Ordinal);
        Assert.DoesNotContain("AMLENV", sand.Svg, StringComparison.Ordinal);

        // Then one entry per other class, in module order.
        Assert.Equal(EsbEnvironmentalStyles.Module.Classes.Count - 1 + 8, legend.Count);
        Assert.IsType<LineLegendEntry>(legend.Single(e => e.Label == "Trawl scours"));
        Assert.IsType<LineLegendEntry>(legend.Single(e => e.Label == "Beach exit"));
        Assert.IsType<SymbolLegendEntry>(legend.Single(e => e.Label == "Viewpoint"));
        Assert.IsType<SwatchLegendEntry>(legend.Single(e => e.Label == "Sea ice"));
        Assert.Equal("Sediment: sand", AmlStyleCatalog.Default.BuildLegendEntry(EsbEnvironmentalStyles.GeologicalLayer, PaletteType.Day).Label);
    }

    [Fact]
    public void A_module_brings_its_own_symbols_line_styles_and_fills_and_names_may_not_collide()
    {
        var clash = new AmlStyleModule("Clash", [])
        {
            Resources = new AmlStyleResources { LineStyles = [new LineStyle { Name = "AML_ENVIRONMENTAL_SCOURS", Width = 1, Color = "AMLENV" }] },
        };

        Assert.Throws<ArgumentException>(() => new AmlStyleCatalog([.. AmlStyleCatalog.BuiltInModules, clash]));
    }

    private static int Count(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static List<DrawingInstruction> Instructions(AmlDrawing drawing, AmlFeature feature) =>
        [.. drawing.Instructions.Values.SelectMany(i => i).Where(i => i.FeatureReference == feature.Id)];
}
