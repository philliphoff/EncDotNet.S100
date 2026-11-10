using EncDotNet.S100.Datasets.Aml.Families;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Datasets.Aml.Tests;

/// <summary>Issue #1375 (AML S7): the seabed family (SBO, LBO) and its clustering.</summary>
public sealed class AmlSeabedStylesTests
{
    private static readonly AmlDrawing Sbo = new AmlPortrayal().Draw(AmlFixtures.Sbo(), "UKS0TEST.000");
    private static readonly AmlDrawing Lbo = new AmlPortrayal().Draw(AmlFixtures.Lbo(), "UKL0TEST.000");

    // The scales the ladder is checked at: in every rung, clear of its ends,
    // where the renderer's inclusive bounds draw both neighbouring rungs.
    private static readonly double[] Scales =
        [500, 900, 1_100, 1_500, 2_200, 3_000, 4_500, 6_000, 9_000, 12_000, 17_000, 25_000, 35_000, 48_000, 70_000, 90_000, 140_000, 200_000, 300_000, 400_000, 700_000, 1_100_000, 4_500_000, 10_000_000];

    [Fact]
    public void Every_SBO_and_LBO_class_with_a_style_draws_in_the_seabed_family()
    {
        Assert.Equal(["watloc"], Sbo.UndrawnClasses);
        Assert.Equal(43, Sbo.DrawnFeatureCount);
        Assert.Empty(Lbo.UndrawnClasses);
        Assert.Equal(6, Lbo.DrawnFeatureCount);

        var layer = Assert.Single(Sbo.BuildSubLayers(PaletteType.Day));
        Assert.Equal("aml.seabed", layer.LayerKey);
        Assert.Equal(S98DisplayPlane.AdditionalMilitaryLayers, layer.Plane);
        Assert.All(Sbo.Instructions[AmlFamily.Seabed], i => Assert.Equal(AmlFamily.Seabed.ViewingGroup, i.ViewingGroup));
    }

    [Theory]
    [InlineData("504", "AML_SEABED_CONTACT")]
    [InlineData("501", "AML_SEABED_CONTACT")]
    [InlineData("503", "AML_SEABED_CONTACT")]
    [InlineData("502", "AML_SEABED_MINE")]
    [InlineData("505,507", "AML_SEABED_MINE")]
    public void A_contact_is_an_open_circle_unless_it_is_mine_like(string status, string symbol)
    {
        var feature = Draw(AmlCellBuilder.Sbo().Point("contct", (0, 50), ("stacon", status))).Features.Single();

        var point = Assert.Single(AmlPortrayal.Build(feature, SeabedStyles.Contact).OfType<PointInstruction>());
        Assert.Equal(symbol, point.SymbolReference);
    }

    [Fact]
    public void Mines_and_small_bottom_objects_draw_from_their_class_and_status()
    {
        var mine = Draw(AmlCellBuilder.Sbo().Point("mindev", (0, 50))).Features.Single();
        Assert.Equal("AML_SEABED_MINE", Assert.Single(AmlPortrayal.Build(mine, SeabedStyles.Mine).OfType<PointInstruction>()).SymbolReference);

        var objects = new AmlPortrayal().Draw(AmlFixtures.SmallBottomObjects());
        Assert.Equal(
            ["AML_SEABED_CONTACT", "AML_SEABED_MINE", "AML_SEABED_MINE"],
            objects.Features.Select(f => AmlPortrayal.Build(f, SeabedStyles.SmallBottomObject).OfType<PointInstruction>().Single().SymbolReference));
    }

    [Fact]
    public void Seabed_points_carry_no_labels()
    {
        // Details are on pick (decision 13); only cluster bubbles are labelled.
        // Bubbles alone have a scale maximum: they never draw at the largest scales.
        foreach (var drawing in new[] { Sbo, Lbo })
        {
            var lone = drawing.Instructions[AmlFamily.Seabed].Where(i => i.ScaleMaximum is null).ToList();
            Assert.NotEmpty(lone);
            Assert.DoesNotContain(lone, i => i is TextInstruction);
        }
    }

    [Fact]
    public void LBO_anomalies_scours_and_chart_classes_have_their_own_symbols()
    {
        string Symbol(string acronym, int index = 0) =>
            Points(Lbo).Where(p => Lbo.Features.Single(f => f.Id == p.FeatureReference).FeatureType == acronym).ElementAt(index).SymbolReference!;

        Assert.Equal("AML_SEABED_ANOMALY", Symbol("senanm"));
        Assert.Equal("AML_SEABED_SCOUR", Symbol("iscour"));
        Assert.Equal("AML_SEABED_RING", Symbol("WRECKS"));
        Assert.Equal("AML_SEABED_RING", Symbol("OBSTRN"));
        Assert.Equal("AML_SEABED_RING", Symbol("UWTROC"));
        Assert.True(SeabedStyles.Wreck.IsChartClass && SeabedStyles.Obstruction.IsChartClass && SeabedStyles.Rock.IsChartClass);
    }

    [Fact]
    public void Scours_turn_to_their_cardinal_orientation()
    {
        var scours = Points(Lbo).Where(p => p.SymbolReference == "AML_SEABED_SCOUR").ToList();

        Assert.Equal([90.0, 45.0], scours.Select(s => s.Rotation!.Value));
        Assert.Equal(135, SeabedStyles.ScourRotation(Draw(AmlCellBuilder.Lbo().Point("iscour", (0, 50), ("orcard", "504"))).Features.Single()));
        Assert.Equal(0, SeabedStyles.ScourRotation(Draw(AmlCellBuilder.Lbo().Point("iscour", (0, 50), ("orcard", "505"))).Features.Single()));
    }

    [Fact]
    public void Contacts_mines_and_anomalies_are_framed_scours_rings_and_bubbles_are_not()
    {
        var symbols = AmlStyleCatalog.Default.Symbols;

        foreach (var name in new[] { "AML_SEABED_CONTACT", "AML_SEABED_MINE", "AML_SEABED_ANOMALY" })
            Assert.Contains("id=\"frame\"", symbols.GetSymbol(name), StringComparison.Ordinal);
        foreach (var name in new[] { "AML_SEABED_SCOUR", "AML_SEABED_RING", "AML_SEABED_CLUSTER" })
        {
            var svg = symbols.GetSymbol(name)!;
            Assert.DoesNotContain("id=\"frame\"", svg, StringComparison.Ordinal);
            Assert.Contains("sAMLSEA", svg, StringComparison.Ordinal);
        }

        // The anomaly's diamond and the ring are dashed.
        Assert.Contains("stroke-dasharray", symbols.GetSymbol("AML_SEABED_ANOMALY"), StringComparison.Ordinal);
        Assert.Contains("stroke-dasharray", symbols.GetSymbol("AML_SEABED_RING"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_dense_field_is_one_bubble_at_the_design_scale_counting_its_mine_like_contacts()
    {
        var bubbles = Visible(Sbo, 90_000).Where(i => i is PointInstruction { SymbolReference: "AML_SEABED_CLUSTER" }).ToList();
        var bubble = Assert.Single(bubbles);

        var texts = Visible(Sbo, 90_000).OfType<TextInstruction>().Where(t => t.FeatureReference == bubble.FeatureReference).Select(t => t.Text).ToList();
        Assert.Equal(["38", "38 contacts · 2 mine-like"], texts);

        // The five lone points draw on their own; none of the field's does.
        Assert.Equal(5, Visible(Sbo, 90_000).OfType<PointInstruction>().Count(p => p.SymbolReference != "AML_SEABED_CLUSTER"));
    }

    [Fact]
    public void The_bubble_expands_into_its_points_on_zoom()
    {
        Assert.DoesNotContain(Visible(Sbo, 900), i => i is PointInstruction { SymbolReference: "AML_SEABED_CLUSTER" });
        Assert.Equal(43, Visible(Sbo, 900).OfType<PointInstruction>().Count());

        // Between, the field splits into smaller bubbles before its points.
        var counts = Scales.Select(s => Visible(Sbo, s).Count(i => i is PointInstruction { SymbolReference: "AML_SEABED_CLUSTER" })).ToList();
        Assert.Contains(counts, c => c > 1);
    }

    [Fact]
    public void At_every_scale_each_point_draws_once_alone_or_in_one_bubble()
    {
        var members = BubbleMembers(Sbo);
        var drawn = Sbo.Features.Where(f => f.FeatureType != "watloc").Select(f => f.Id).ToHashSet();

        foreach (var scale in Scales)
        {
            var visible = Visible(Sbo, scale).OfType<PointInstruction>().ToList();
            var counted = new List<string>();
            foreach (var point in visible)
            {
                if (point.SymbolReference == "AML_SEABED_CLUSTER")
                    counted.AddRange(members[point]);
                else
                    counted.Add(point.FeatureReference);
            }

            Assert.True(drawn.SetEquals(counted) && counted.Count == drawn.Count, $"At 1:{scale}, {counted.Count} drawn of {drawn.Count}.");
        }
    }

    [Fact]
    public void At_every_scale_what_draws_is_at_least_about_24_px_apart()
    {
        var positions = Sbo.Features.ToDictionary(f => f.Id, f => f.Points.FirstOrDefault());
        foreach (var scale in Scales.Where(s => s >= 2_000))
        {
            var visible = Visible(Sbo, scale).OfType<PointInstruction>().Select(p => positions[p.FeatureReference]).ToList();
            double metresPerPixel = AmlSignature.PixelMm / 1000 * scale;
            for (int i = 0; i < visible.Count; i++)
            {
                for (int j = i + 1; j < visible.Count; j++)
                {
                    double dx = (visible[i].Longitude - visible[j].Longitude) * 111_320 * Math.Cos(visible[i].Latitude * Math.PI / 180);
                    double dy = (visible[i].Latitude - visible[j].Latitude) * 111_320;
                    // Bubbles sit on a member near their centre, not the centre itself.
                    Assert.True(Math.Sqrt((dx * dx) + (dy * dy)) / metresPerPixel > 16, $"Two symbols closer than 16 px at 1:{scale}.");
                }
            }
        }
    }

    [Fact]
    public void A_point_hidden_by_its_SCAMIN_leaves_its_cluster()
    {
        var cell = AmlCellBuilder.Sbo()
            .Point("contct", (0.0, 50.0), ("stacon", "504"))
            .Point("contct", (0.0001, 50.0), ("stacon", "504"))
            .Point("contct", (0.0002, 50.0), ("stacon", "504"), ("SCAMIN", "20000"))
            .Build();
        var drawing = new AmlPortrayal().Draw(cell);

        var near = Visible(drawing, 10_000).Where(i => i is PointInstruction { SymbolReference: "AML_SEABED_CLUSTER" }).ToList();
        Assert.Equal("3", Assert.Single(Visible(drawing, 10_000).OfType<TextInstruction>(), t => t.Text.Length == 1).Text);
        Assert.Single(near);

        var far = Visible(drawing, 100_000).ToList();
        Assert.Equal("2", Assert.Single(far.OfType<TextInstruction>(), t => t.Text.Length == 1).Text);
        Assert.Equal("2 contacts", Assert.Single(far.OfType<TextInstruction>(), t => t.Text.Length > 1).Text);

        // The last rung's bubble draws at every smaller scale.
        Assert.Single(Visible(drawing, 50_000_000), i => i is PointInstruction);
    }

    [Fact]
    public void LBO_points_cluster_as_bottom_objects_apart_from_SBO_contacts()
    {
        var cell = AmlCellBuilder.Lbo()
            .Point("WRECKS", (0.0, 50.0), ("CATWRK", "1"))
            .Point("OBSTRN", (0.0005, 50.0), ("CATOBS", "7"))
            .Point("senanm", (0.0, 50.0005))
            .Build();
        var drawing = new AmlPortrayal().Draw(cell);

        Assert.Contains(Visible(drawing, 200_000).OfType<TextInstruction>(), t => t.Text == "3 bottom objects");
        Assert.Equal("1 contact", SeabedStyles.SboClustering.Label([Sbo.Features[0]]));
    }

    [Fact]
    public void A_clustering_must_be_in_its_classes_family_with_an_unframed_bubble()
    {
        Assert.Throws<ArgumentException>(() => new AmlClustering("Framed", AmlFamily.Seabed, SeabedGlyphs.Contact, _ => "x"));
        var style = new AmlClassStyle("contct", AmlFamily.Air, AmlClassKind.Point, "Wrong family") { Glyph = SeabedGlyphs.Contact, Clustering = SeabedStyles.SboClustering };
        Assert.Throws<ArgumentException>(() => new AmlStyleCatalog([new AmlStyleModule("Bad", [style])]));
    }

    [Fact]
    public void The_seabed_legend_lists_each_class_with_its_symbol()
    {
        var legend = Assert.Single(AmlStyleCatalog.Default.BuildLegend(PaletteType.Day), g => g.Family == AmlFamily.Seabed);

        // The SBO and LBO classes, after the seabed classes of earlier modules (MFF's cables).
        string[] labels = ["Contact", "Mine or mine-like contact", "Small bottom object", "Sensor anomaly", "Impact scour", "Wreck: AML details on pick", "Obstruction: AML details on pick", "Rock: AML details on pick"];
        var seabed = legend.Entries.SkipWhile(e => e.Label != labels[0]).ToList();
        Assert.Equal(labels, seabed.Select(e => e.Label));
        Assert.All(seabed, e => Assert.IsType<SymbolLegendEntry>(e));
    }

    private static AmlDrawing Draw(AmlCellBuilder builder) => new AmlPortrayal().Draw(builder.Build());

    private static IEnumerable<PointInstruction> Points(AmlDrawing drawing) => drawing.Instructions.Values.SelectMany(i => i).OfType<PointInstruction>();

    // The instructions drawn at a scale, by the renderer's rule (bounds inclusive).
    private static IEnumerable<DrawingInstruction> Visible(AmlDrawing drawing, double scale) =>
        drawing.Instructions.Values.SelectMany(i => i)
            .Where(i => (i.ScaleMaximum is not { } max || scale >= max) && (i.ScaleMinimum is not { } min || scale <= min));

    // Each bubble's members, from its count and the points that stop drawing alone where it starts.
    private static Dictionary<PointInstruction, IReadOnlyList<string>> BubbleMembers(AmlDrawing drawing)
    {
        var ladder = AmlClusterLadderFor(drawing);
        var bubbles = Points(drawing).Where(p => p.SymbolReference == "AML_SEABED_CLUSTER").ToList();
        var result = new Dictionary<PointInstruction, IReadOnlyList<string>>(ReferenceEqualityComparer.Instance);
        foreach (var bubble in bubbles)
        {
            var cluster = ladder.Bubbles.Single(c => c.Anchor.Id == bubble.FeatureReference && c.ScaleMaximum == bubble.ScaleMaximum);
            result[bubble] = [.. cluster.Members.Select(m => m.Id)];
        }

        return result;
    }

    private static AmlClusterLadder AmlClusterLadderFor(AmlDrawing drawing) =>
        AmlClusterLadder.Build(
            SeabedStyles.SboClustering,
            [.. drawing.Features.Where(f => AmlStyleCatalog.Default.Find(f.FeatureType)?.Clustering == SeabedStyles.SboClustering)
                .Select(f => (f, AmlDrawingContext.FeatureScaleMinimum(f)))]);
}
