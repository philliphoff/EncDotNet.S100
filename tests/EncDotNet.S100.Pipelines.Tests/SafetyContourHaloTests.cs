using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #1377 (AML S9, decision 22): while AML CLB draws contours over the
/// chart, the chart's safety contour keeps its weight and gains a halo, added
/// to the display list after the portrayal rules.
/// </summary>
public sealed class SafetyContourHaloTests
{
    [Fact]
    public void Every_safety_contour_line_gains_a_halo_under_it_in_its_plane_and_priority()
    {
        var other = Line("1", "DEPCN", priority: 13);
        var first = Line("2", SafetyContourHalo.SafetyContourColorToken, priority: 13);
        var second = Line("3", SafetyContourHalo.SafetyContourColorToken, priority: 13);
        var later = Line("4", "CHBLK", priority: 20);

        var result = SafetyContourHalo.Apply([other, first, second, later]);

        // Both halos come before the first safety-contour line, so one edge's
        // halo never cuts across the next edge's line.
        Assert.Equal(6, result.Count);
        Assert.Same(other, result[0]);
        var halos = result.Skip(1).Take(2).Cast<LineInstruction>().ToList();
        Assert.Equal(["2", "3"], halos.Select(h => h.FeatureReference));
        Assert.All(halos, h => Assert.Equal(SafetyContourHalo.HaloColorToken, h.LineColor));
        Assert.All(halos, h => Assert.Equal(SafetyContourHalo.HaloWidth, h.LineWidth));
        Assert.All(halos, h => Assert.Equal(13, h.DrawingPriority));
        Assert.All(halos, h => Assert.False(h.Suppression));
        Assert.All(halos, h => Assert.Equal(first.ViewingGroup, h.ViewingGroup));
        Assert.Same(first, result[3]);
        Assert.Same(second, result[4]);
        Assert.Same(later, result[5]);

        // The contour keeps its own weight.
        Assert.Equal(0.64, first.LineWidth);
    }

    [Fact]
    public void A_list_without_a_safety_contour_is_returned_as_it_is()
    {
        IReadOnlyList<DrawingInstruction> instructions = [Line("1", "DEPCN", priority: 13)];

        Assert.Same(instructions, SafetyContourHalo.Apply(instructions));
    }

    [Fact]
    public void A_dashed_safety_contour_also_gains_a_solid_halo()
    {
        var dashed = new LineInstruction
        {
            FeatureReference = "1",
            LineStyleReference = LineInstruction.SimpleLineStyleReference,
            LineColor = SafetyContourHalo.SafetyContourColorToken,
            LineWidth = 0.64,
            Dashes = [(0, 3.6)],
            DashIntervalLength = 5.4,
            DrawingPriority = 13,
        };

        var halo = Assert.IsType<LineInstruction>(SafetyContourHalo.Apply([dashed])[0]);

        Assert.Null(halo.Dashes);
        Assert.Equal(SafetyContourHalo.HaloColorToken, halo.LineColor);
    }

    [Fact]
    public void The_presentation_carries_the_halo_to_each_render_context()
    {
        var presentation = MapPresentationState.Default.WithSafetyContourHalo(true);

        Assert.True(presentation.ApplyTo(new RenderContext(), new SpecRef("S-101", new SpecVersion(2, 0, 0))).SafetyContourHalo);
        Assert.True(presentation.WithPalette(PaletteType.Night).SafetyContourHalo);
        Assert.Same(presentation, presentation.WithSafetyContourHalo(true));
        Assert.False(MapPresentationState.Default.ApplyTo(new RenderContext(), new SpecRef("S-101", new SpecVersion(2, 0, 0))).SafetyContourHalo);
    }

    [Fact]
    public async Task The_S57_processor_adds_the_halo_only_when_asked_without_re_running_the_rules()
    {
        var dir = Directory.CreateTempSubdirectory("s57-halo-").FullName;
        try
        {
            var path = SyntheticS57Cell.Write(dir, "U5HALO.000", S57ProductSpecificationEnc);
            var catalogueManager = new PortrayalCatalogueManager();
            catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
            var processor = new S57DatasetProcessor(
                path, catalogueManager, TestLuaEngine.Create(), new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));
            var ct = TestContext.Current.CancellationToken;

            // A safety contour the cell's depth areas chart.
            var mariner = MarinerSettings.Default with { SafetyContour = Quantities.Depth.FromMetres(10) };
            var plain = await processor.BuildVectorPortrayalAsync(new RenderContext { Mariner = mariner }, ct);
            var runs = processor.RuleRunCount;
            var haloed = await processor.BuildVectorPortrayalAsync(new RenderContext { Mariner = mariner, SafetyContourHalo = true }, ct);

            var contours = plain.SubLayers.SelectMany(s => s.Instructions).Count(SafetyContourHalo.IsSafetyContour);
            Assert.True(contours > 0, "The fixture charts no safety contour at 10 m.");
            var halos = haloed.SubLayers.SelectMany(s => s.Instructions)
                .OfType<LineInstruction>()
                .Count(l => l.LineColor == SafetyContourHalo.HaloColorToken && l.LineWidth == SafetyContourHalo.HaloWidth);
            Assert.Equal(contours, halos);
            Assert.Equal(runs, processor.RuleRunCount);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private const byte S57ProductSpecificationEnc = 1;

    private static LineInstruction Line(string feature, string color, int priority) => new()
    {
        FeatureReference = feature,
        LineStyleReference = LineInstruction.SimpleLineStyleReference,
        LineColor = color,
        LineWidth = 0.64,
        ViewingGroup = 13030,
        DrawingPriority = priority,
    };
}
