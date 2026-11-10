using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals.Application;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.Aml.Tests;

/// <summary>Issue #1373 (AML S5): the style catalogue, palette, signature, symbols and legend.</summary>
public sealed class AmlStyleCatalogTests
{
    [Theory]
    [InlineData("AMLAIR", "#2350B5", "#3E66C2", "#33508F")]
    [InlineData("AMLOPS", "#B2381B", "#B44A2C", "#8A3B27")]
    [InlineData("AMLSEA", "#7A4B1F", "#8A5A2E", "#6B4A2B")]
    [InlineData("AMLENV", "#2B7547", "#3A7E52", "#2E5C40")]
    [InlineData("AMLHALO", "#FFFFFF", "#000000", "#000000")]
    public void The_palette_has_the_four_families_and_the_halo_in_each_palette(string token, string day, string dusk, string night)
    {
        var palette = AmlStyleCatalog.Default.Palette;

        Assert.Equal(day, palette.Resolve(token, PaletteType.Day).ToHex().ToUpperInvariant());
        Assert.Equal(dusk, palette.Resolve(token, PaletteType.Dusk).ToHex().ToUpperInvariant());
        Assert.Equal(night, palette.Resolve(token, PaletteType.Night).ToHex().ToUpperInvariant());
    }

    [Fact]
    public void Every_line_style_is_dash_dot_14_3_2_3_on_a_5_px_halo()
    {
        foreach (var family in AmlFamily.All)
        {
            var style = AmlStyleCatalog.Default.LineStyleProvider(AmlSignature.LineStyleName(family))!;
            var halo = style.Components[0];
            var ink = style.Components[1];

            Assert.Equal(AmlPalette.HaloToken, halo.Color);
            Assert.Equal(5 * 0.32, halo.Width, 3);
            Assert.Equal(family.ColorToken, ink.Color);
            Assert.Equal([0, 14 * 0.32f, 17 * 0.32f, 2 * 0.32f], ink.DashPattern!, (a, b) => Math.Abs(a - b) < 1e-4);
            Assert.Equal(22 * 0.32, ink.IntervalLength, 3);
        }
    }

    [Fact]
    public void Area_boundaries_put_their_ticks_on_the_right_inside_a_clockwise_ring()
    {
        var style = AmlStyleCatalog.Default.LineStyleProvider("AML_AIR_AREA")!;
        var ticks = style.Components[2];

        Assert.False(ticks.HasPen);
        var tick = Assert.Single(ticks.Symbols);
        Assert.Equal("AML_AIR_TICK", tick.Reference);
        Assert.Equal(180, tick.Rotation);
    }

    [Fact]
    public void Each_glyph_gets_a_framed_symbol_in_its_family_with_a_notch()
    {
        var symbols = AmlStyleCatalog.Default.Symbols;

        foreach (var name in new[] { "AML_AIR_WPT", "AML_AIR_NAVAID", "AML_OPERATIONAL_TURN" })
        {
            var svg = symbols.GetSymbol(name)!;
            Assert.Contains("id=\"frame\"", svg, StringComparison.Ordinal);
            Assert.Contains("L 2.0 -3.6 L 3.6 -2.0", svg, StringComparison.Ordinal);
            Assert.Contains($"<title>{name}</title>", svg, StringComparison.Ordinal);
        }

        Assert.Contains("sAMLAIR", symbols.GetSymbol("AML_AIR_WPT"), StringComparison.Ordinal);
        Assert.Contains("sAMLOPS", symbols.GetSymbol("AML_OPERATIONAL_TURN"), StringComparison.Ordinal);
        Assert.All(AmlFamily.All, f =>
        {
            Assert.NotNull(symbols.GetSymbol(AmlSignature.TickSymbol(f)));
            Assert.NotNull(symbols.GetSymbol(AmlSignature.ChevronSymbol(f)));
            Assert.NotNull(symbols.GetSymbol(AmlSignature.BarSymbol(f)));
        });
    }

    [Fact]
    public void Two_styles_for_one_class_are_refused()
    {
        var module = new AmlStyleModule("Again", [new AmlClassStyle("airres", AmlFamily.Air, AmlClassKind.Area, "Again")]);

        Assert.Throws<ArgumentException>(() => new AmlStyleCatalog([.. AmlStyleCatalog.BuiltInModules, module]));
    }

    [Fact]
    public void A_point_class_needs_a_glyph_and_a_glyph_may_not_take_a_reserved_name()
    {
        Assert.Throws<ArgumentException>(() => new AmlStyleCatalog(
            [new AmlStyleModule("Bad", [new AmlClassStyle("awaypt", AmlFamily.Air, AmlClassKind.Point, "No glyph")])]));
        Assert.Throws<ArgumentException>(() => new AmlStyleCatalog(
            [new AmlStyleModule("Bad", [new AmlClassStyle("awaypt", AmlFamily.Air, AmlClassKind.Point, "Tick") { Glyph = new AmlGlyph("TICK", "x", "<g/>") }])]));
    }

    [Fact]
    public void A_later_slice_adds_a_family_and_classes_in_its_own_module()
    {
        // How S7–S9 extend the portrayal: a new module (here with a new family)
        // added to the built-in ones. Nothing else changes.
        var family = new AmlFamily("test", "Test", "AMLTEST", PaletteColors.FromHex("#112233", "#223344", "#334455"), order: 35, viewingGroup: 45035);
        var glyph = new AmlGlyph("DOT", "A dot.", """<circle class="f{ink}" cx="0" cy="0" r="1"/>""");
        var module = new AmlStyleModule(
            "Test",
            [
                new AmlClassStyle("RADSTA", family, AmlClassKind.Point, "Radar station") { Glyph = glyph },
                new AmlClassStyle("RESARE", family, AmlClassKind.Area, "Restricted area")
                {
                    Build = (feature, context) => [context.Boundary(), context.At(5).Area(feature.Id, "AMLTEST", 0.9)],
                },
            ]);
        var catalog = new AmlStyleCatalog([.. AmlStyleCatalog.BuiltInModules, module]);

        Assert.Contains(family, catalog.Families);
        Assert.Equal("#112233", catalog.Palette.Resolve("AMLTEST", PaletteType.Day).ToHex().ToUpperInvariant());
        Assert.NotNull(catalog.Symbols.GetSymbol("AML_TEST_DOT"));
        Assert.NotNull(catalog.LineStyleProvider("AML_TEST_AREA"));

        var drawing = new AmlPortrayal(catalog).Draw(AmlFixtures.Ral());
        Assert.Empty(drawing.UndrawnClasses);
        Assert.Equal(["aml.operational", "aml.test", "aml.air"], drawing.Instructions.Keys.Select(k => k.LayerKey));
        var legend = Assert.Single(catalog.BuildLegend(PaletteType.Day), g => g.Family == family);
        Assert.Equal(["Radar station", "Restricted area"], legend.Entries.Select(e => e.Label));
    }

    [Fact]
    public void The_legend_has_an_entry_per_class_drawn_from_its_style()
    {
        var legend = AmlStyleCatalog.Default.BuildLegend(PaletteType.Night);

        // Top of the plane first; later families follow.
        Assert.Equal([AmlFamily.Air, AmlFamily.Operational], legend.Select(g => g.Family).Take(2));
        var air = legend[0].Entries;
        Assert.Equal(
            ["ATS route", "Air waypoint", "NAVAID", "Airspace restriction", "Controlled airspace", "Military exercise airspace"],
            air.Select(e => e.Label).Take(6));
        var route = Assert.IsType<LineLegendEntry>(air[0]);
        Assert.Equal("#33508F", route.Line.Color.ToUpperInvariant());
        Assert.Equal(AmlSignature.DashDot, route.Line.Dashes);
        Assert.IsType<SymbolLegendEntry>(air[1]);
        var area = Assert.IsType<SwatchLegendEntry>(air[3]);
        Assert.Equal("#00000000", area.Color);
        Assert.Equal(2, area.Outline!.Symbols.Count);

        var qroute = Assert.IsType<LineLegendEntry>(legend[1].Entries.Single(e => e.Label == "Q-route leg"));
        Assert.Equal(AmlSignature.RouteInkWidth, qroute.Line.Width);
        Assert.Single(qroute.Line.Symbols);
    }

    [Theory]
    [InlineData("limitu", "FL660", "limitl", "FL245", "FL660", "FL245")]
    [InlineData("limitu", "FL195", "limitl", "07800", "FL195", "7800 ft")]
    [InlineData("efaltu", "003500AMSL", "efaltl", "01000AGL", "3500 ft AMSL", "1000 ft AGL")]
    [InlineData("efaltu", "FL75", "efaltl", "GND", "FL75", "GND")]
    [InlineData("maxftl", "245", "minalt", "0", "FL245", "SFC")]
    [InlineData("maxalt", "5000", "minftl", "100", "5000 ft", "FL100")]
    [InlineData("maxftl", "3000", "minalt", "300", "–", "300 ft")]
    public void Limits_read_coded_and_numeric_attributes(string upperAttribute, string upper, string lowerAttribute, string lower, string expectedUpper, string expectedLower)
    {
        var feature = Feature(("OBJNAM", "A"), (upperAttribute, upper), (lowerAttribute, lower));

        Assert.Equal((expectedUpper, expectedLower), AmlLabels.Limits(feature));
    }

    [Fact]
    public void Limits_in_metres_follow_HUNITS_and_a_feature_without_limits_has_none()
    {
        Assert.Equal(("120 m", "SFC"), AmlLabels.Limits(Feature(("maxalt", "120"), ("minalt", "0"), ("HUNITS", "1"))));
        Assert.Null(AmlLabels.Limits(Feature(("OBJNAM", "A"))));
        Assert.Equal("FL245 / SFC", AmlLabels.Fraction(("FL245", "SFC")));
    }

    [Fact]
    public void A_name_is_OBJNAM_or_a_short_identifier_never_prose()
    {
        Assert.Equal("D 613", AmlLabels.Name(Feature(("OBJNAM", "D 613"), ("identy", "EGD613"))));
        Assert.Equal("EGD613", AmlLabels.Name(Feature(("identy", "EGD613"))));
        Assert.Null(AmlLabels.Name(Feature(("identy", "Marked by buoys and lights"))));
    }

    [Fact]
    public void AML_is_never_a_depth_cell_or_route_check_source()
    {
        // Decision 23: nothing in the AML portrayal may answer depths or feed
        // route checks. The interfaces live in packages this one doesn't
        // reference, so look them up by name.
        var forbidden = new[] { "IChartDepthCell", "IChartedDepthSource", "IRouteChartSource", "IRouteAreaSource", "IRouteCheck" };
        var assembly = typeof(AmlPortrayal).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            Assert.DoesNotContain(type.GetInterfaces(), i => forbidden.Contains(i.Name.Split('`')[0]));
        }

        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name is "EncDotNet.S100.Navigation" or "EncDotNet.S100.Datasets.Pipelines");
        Assert.Equal("AML depths aren't used for depth answers or route checks.", AmlStyleCatalog.NoChecksNote);
    }

    // A point feature of an AML 3.0 airspace class with the given attributes.
    private static AmlFeature Feature(params (string Acronym, string Value)[] attributes) =>
        new AmlPortrayal().Draw(AmlCellBuilder.Aml30().Point("airres", (0, 0), attributes).Build()).Features.Single();
}
