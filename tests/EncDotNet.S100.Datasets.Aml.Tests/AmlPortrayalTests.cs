using EncDotNet.S100.Datasets.Aml.Families;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.Aml.Tests;

/// <summary>Issue #1373 (AML S5): drawing a synthetic RAL cell with the built-in styles.</summary>
public sealed class AmlPortrayalTests
{
    private static readonly AmlDrawing Ral = new AmlPortrayal().Draw(AmlFixtures.Ral(), "UKR0TEST.000");

    [Fact]
    public void Every_RAL_class_with_a_style_is_drawn_and_the_rest_are_still_features()
    {
        Assert.Equal(13, Ral.Features.Count);
        Assert.Equal(12, Ral.DrawnFeatureCount);
        Assert.Equal(["RADSTA"], Ral.UndrawnClasses);
        var radar = Assert.Single(Ral.Features, f => f.FeatureType == "RADSTA");
        Assert.Equal(S100GeometryType.Point, radar.GeometryType);
        Assert.DoesNotContain(Ral.Instructions.Values.SelectMany(i => i), i => i.FeatureReference == radar.Id);
        Assert.Contains("not drawn: RADSTA", Ral.Info, StringComparison.Ordinal);
    }

    [Fact]
    public void Features_are_identified_by_their_RCID_and_classed_through_the_catalogue()
    {
        var area = Assert.Single(Ral.Features, f => f.FeatureType == "airres");
        Assert.Equal("4", area.Id);
        Assert.Equal("Airspace Restriction", area.ClassName);
        Assert.Equal("D 613", area.Attribute("OBJNAM"));
    }

    [Fact]
    public void Each_family_gets_a_sub_layer_on_the_additional_military_layers_plane()
    {
        var layers = Ral.BuildSubLayers(PaletteType.Day);

        Assert.Equal(["aml.operational", "aml.air"], layers.Select(l => l.LayerKey));
        Assert.All(layers, l => Assert.Equal(S98DisplayPlane.AdditionalMilitaryLayers, l.Plane));
        Assert.Equal(45, (int)S98DisplayPlane.AdditionalMilitaryLayers);
        Assert.True(layers[1].WithinPlanePriority > layers[0].WithinPlanePriority, "Air draws over operational.");
        Assert.All(layers, l => Assert.NotEmpty(l.Legend!));
    }

    [Fact]
    public void Areas_get_the_ticked_boundary_and_no_fill()
    {
        var area = Assert.Single(Ral.Features, f => f.FeatureType == "airres");
        var drawn = Instructions(area);

        Assert.DoesNotContain(drawn, i => i is AreaInstruction);
        var boundary = Assert.Single(drawn.OfType<LineInstruction>());
        Assert.Equal("AML_AIR_AREA", boundary.LineStyleReference);
        Assert.Equal(AmlFamily.Air.ViewingGroup, boundary.ViewingGroup);
    }

    [Fact]
    public void Airspace_limits_are_a_stacked_fraction_under_the_name()
    {
        var area = Assert.Single(Ral.Features, f => f.FeatureType == "airres");
        var drawn = Instructions(area);

        Assert.Contains(drawn, i => i is PointInstruction { SymbolReference: "AML_AIR_BAR" });
        var texts = drawn.OfType<TextInstruction>().ToList();
        Assert.Equal(["FL245", "SFC", "D 613"], texts.Select(t => t.Text));
        Assert.Equal(TextVerticalAlignment.Bottom, texts[0].VerticalAlignment);
        Assert.Equal(TextVerticalAlignment.Top, texts[1].VerticalAlignment);
        Assert.True(texts[2].OffsetY < texts[0].OffsetY, "The name sits above the upper limit.");
    }

    [Fact]
    public void Labels_are_italic_in_the_family_ink_with_a_halo()
    {
        var all = Ral.Instructions.Values.SelectMany(i => i).OfType<TextInstruction>().ToList();

        Assert.NotEmpty(all);
        Assert.All(all, t =>
        {
            Assert.Equal(TextFontSlant.Italics, t.FontSlant);
            Assert.Equal(AmlPalette.HaloToken, t.HaloColor);
            Assert.Contains(t.FontColor, new[] { "AMLAIR", "AMLOPS" });
        });
    }

    [Fact]
    public void Points_sit_in_the_frame_with_their_glyph()
    {
        Assert.Equal("AML_AIR_NAVAID", Symbol("navaid"));
        Assert.Equal("AML_OPERATIONAL_TURN", Symbol("turnpt"));
    }

    [Fact]
    public void Q_routes_draw_as_routes_and_ATS_routes_as_lines()
    {
        var qroute = Assert.Single(Ral.Features, f => f.FeatureType == "qroute");
        var ats = Ral.Features.First(f => f.FeatureType == "atsctl");

        Assert.Equal("AML_OPERATIONAL_ROUTE", Instructions(qroute).OfType<LineInstruction>().Single().LineStyleReference);
        Assert.Equal("AML_AIR_LINE", Instructions(ats).OfType<LineInstruction>().Single().LineStyleReference);
        var label = Instructions(ats).OfType<TextInstruction>().Single();
        Assert.Equal(0.5, label.LinePlacementPosition);
    }

    [Fact]
    public void A_practice_area_is_a_chart_class_that_gets_only_its_AML_boundary()
    {
        Assert.True(RalOperationalStyles.PracticeArea.IsChartClass);
        var area = Assert.Single(Ral.Features, f => f.FeatureType == "MIPARE");
        var drawn = Instructions(area);

        Assert.Equal("AML_OPERATIONAL_AREA", drawn.OfType<LineInstruction>().Single().LineStyleReference);
        Assert.DoesNotContain(drawn, i => i is AreaInstruction);
        Assert.Equal(["Firing practice area X5"], drawn.OfType<TextInstruction>().Select(t => t.Text));
    }

    [Fact]
    public void Arc_edges_are_densified_and_rings_turn_clockwise()
    {
        var circle = Assert.Single(Ral.Features, f => f.FeatureType == "ctlasp");

        Assert.Equal(S100GeometryType.Surface, circle.GeometryType);
        Assert.True(circle.ExteriorRing.Count > 40, $"The arc circle has only {circle.ExteriorRing.Count} points.");
        Assert.Equal(circle.ExteriorRing[0], circle.ExteriorRing[^1]);
        // The arcs bulge east and west of the centre, between its north and south points.
        Assert.InRange(circle.ExteriorRing.Max(p => p.Longitude), -1.285, -1.25);
        Assert.InRange(circle.ExteriorRing.Min(p => p.Longitude), -1.35, -1.315);
        Assert.All(circle.ExteriorRing, p => Assert.InRange(p.Latitude, 50.4099, 50.4701));

        Assert.All(Ral.Features.Where(f => f.GeometryType == S100GeometryType.Surface), f =>
            Assert.True(AmlGeometry.SignedArea(f.ExteriorRing) < 0, $"{f.FeatureType} isn't clockwise."));
    }

    [Fact]
    public void An_anticlockwise_ring_is_turned_clockwise()
    {
        // The intwtr ring of the fixture runs clockwise; this one the other way.
        var cell = AmlCellBuilder.Ral()
            .Area("patare", [(0, 0), (1, 0), (1, 1), (0, 1), (0, 0)], ("OBJNAM", "A"))
            .Build();
        var feature = Assert.Single(new AmlPortrayal().Draw(cell).Features);

        Assert.True(AmlGeometry.SignedArea(feature.ExteriorRing) < 0);
    }

    [Fact]
    public void SCAMIN_limits_the_scales_a_feature_draws_at()
    {
        var fai = new AmlPortrayal().Draw(AmlFixtures.Fai());
        var restricted = Assert.Single(fai.Features, f => f.FeatureType == "airres");

        Assert.All(Instructions(fai, restricted), i => Assert.Equal(1_500_000, i.ScaleMinimum));
        Assert.All(Instructions(fai, fai.Features.First(f => f.FeatureType == "navaid")), i => Assert.Null(i.ScaleMinimum));
    }

    [Fact]
    public void AML_3_air_classes_use_the_same_styles()
    {
        var fai = new AmlPortrayal().Draw(AmlFixtures.Fai());

        Assert.Equal(["AIRARE"], fai.UndrawnClasses);
        Assert.Equal(["aml.air"], fai.Instructions.Keys.Select(k => k.LayerKey));
        var waypoint = fai.Features.First(f => f.FeatureType == "awaypt");
        Assert.Equal(
            ["AML_AIR_WPT"],
            Instructions(fai, waypoint).OfType<PointInstruction>().Select(p => p.SymbolReference));
        Assert.Equal(["OSTOR"], Instructions(fai, waypoint).OfType<TextInstruction>().Select(t => t.Text));
        var restricted = fai.Features.First(f => f.FeatureType == "airres");
        Assert.Equal(
            ["FL100", "1000 ft AGL", "TRA-WESER 2"],
            Instructions(fai, restricted).OfType<TextInstruction>().Select(t => t.Text));
    }

    [Fact]
    public void The_result_carries_the_palette_symbols_and_line_styles()
    {
        var result = Ral.ToVectorPortrayalResult(new Core.SpecRef("S-57", default), "UKR0TEST.000", new Pipelines.RenderContext { Palette = PaletteType.Night });

        Assert.Equal("#33508F", result.Palette.Resolve("AMLAIR").ToUpperInvariant());
        Assert.Equal("#000000", result.Palette.Resolve(AmlPalette.HaloToken).ToUpperInvariant());
        Assert.NotNull(result.SymbolProvider!("AML_AIR_WPT"));
        Assert.NotNull(result.LineStyleProvider!("AML_OPERATIONAL_ROUTE"));
        Assert.NotNull(result.GeometryProvider.GetGeometry(Ral.Features[0].Id));
        Assert.Equal(["aml.operational", "aml.air"], result.LayerNames);
        Assert.NotNull(result.GeographicExtent);
    }

    [Fact]
    public void The_interoperability_authority_stacks_AML_over_the_chart_overlays_and_under_the_cautions()
    {
        var result = Ral.ToVectorPortrayalResult(new Core.SpecRef("S-57", default), "UKR0TEST.000");
        VectorSubLayer Other(string key, S98DisplayPlane plane) =>
            new() { LayerKey = key, LayerName = key, Instructions = [], Plane = plane, WithinPlanePriority = 100 };
        var items = result.SubLayers
            .Append(Other("cautions", S98DisplayPlane.CautionsAndWarnings))
            .Append(Other("overlays", S98DisplayPlane.OtherChartOverlays))
            .Select(sub => new SubLayerStackItem(new VectorStackPayload(result, sub), sub.Plane, sub.WithinPlanePriority, "x", sub.LayerKey));

        var sorted = new InteroperabilityAuthority().Sort(items);

        Assert.Equal(["overlays", "aml.operational", "aml.air", "cautions"], sorted.Select(i => i.SourceFeatureType));
    }

    private static string Symbol(string acronym)
    {
        var feature = Ral.Features.First(f => f.FeatureType == acronym);
        return Instructions(feature).OfType<PointInstruction>().Single().SymbolReference!;
    }

    private static List<DrawingInstruction> Instructions(AmlFeature feature) => Instructions(Ral, feature);

    private static List<DrawingInstruction> Instructions(AmlDrawing drawing, AmlFeature feature) =>
        [.. drawing.Instructions.Values.SelectMany(i => i).Where(i => i.FeatureReference == feature.Id)];
}
