using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S421.Tests;

/// <summary>
/// Integration tests that drive the full S-421 portrayal pipeline:
/// dataset → FeatureXML → XSLT (bundled PC) → MapsuiDisplayListRenderer.
/// </summary>
public class S421RendererIntegrationTests
{
    private const string TestDataDir = "TestData";

    private static (XDocument DisplayList, S421Dataset Dataset, S421PortrayalCatalogue Catalogue)
        RunPipeline(string fileName)
    {
        var dataset = S421Dataset.Open(Path.Combine(TestDataDir, fileName));

        var pcSource = Specification.CreatePortrayalCatalogueSource("S-421");
        var provider = PortrayalCatalogueProvider.OpenAsync(pcSource).GetAwaiter().GetResult();
        var catalogue = new S421PortrayalCatalogue(provider);

        var featureSource = new S421FeatureXmlSource(dataset);
        XDocument featureDoc;
        using (var reader = featureSource.GetFeatureXml())
            featureDoc = XDocument.Load(reader);

        var rule = catalogue.Rules.First();
        var transform = catalogue.GetCompiledRuleAsync(rule.Name).AsTask().GetAwaiter().GetResult();

        var displayList = new XDocument();
        using (var input = featureDoc.CreateReader())
        using (var writer = displayList.CreateWriter())
        {
            transform.Transform(input, writer);
        }

        return (displayList, dataset, catalogue);
    }

    [Fact]
    public void Pipeline_Minimal_ProducesDisplayListWithInstructions()
    {
        var (displayList, _, _) = RunPipeline("RTE-TEST-GMIN.s421.gml");

        Assert.NotNull(displayList.Root);
        var instructions = displayList.Root!.Elements().ToList();
        Assert.NotEmpty(instructions);

        // Two waypoints in the minimal sample → expect two pointInstructions.
        var pointCount = instructions.Count(e => e.Name.LocalName == "pointInstruction");
        Assert.Equal(2, pointCount);
    }

    [Fact]
    public void Pipeline_Full_EmitsLineInstructionsForLegs()
    {
        var (displayList, _, _) = RunPipeline("RTE-TEST-GFULL.s421.gml");

        var instructions = displayList.Root!.Elements().ToList();
        Assert.Contains(instructions, e => e.Name.LocalName == "lineInstruction");
    }

    [Fact]
    public void Renderer_Minimal_BindsBothWaypoints()
    {
        var (displayList, dataset, catalogue) = RunPipeline("RTE-TEST-GMIN.s421.gml");

        var renderer = new MapsuiDisplayListRenderer
        {
            Palette = catalogue.ActivePalette,
            SymbolProvider = name =>
            {
                try { return catalogue.GetSymbolAsync(name).AsTask().GetAwaiter().GetResult().SvgContent; }
                catch { return null; }
            },
        };

        var instructions = Part9DisplayListReader.Read(displayList);
        var geometryProvider = new FeatureGeometryProvider<S421Feature>(dataset.Features);
        var layer = renderer.Render(instructions, geometryProvider);

        // The layer is drawn and picked from its bound scene.
        Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(layer, out _, out var overlay));
        var points = overlay.Ops.OfType<PointPaintOp>().ToList();
        Assert.NotEmpty(points);

        // Each painted op must carry its source dataset reference, and both
        // waypoints are drawn.
        Assert.All(points, p => Assert.False(string.IsNullOrEmpty(p.FeatureReference)));
        Assert.Equal(2, points.Select(p => p.FeatureReference).Distinct().Count());
    }

    [Fact]
    public void Renderer_Full_RoutLegsRenderAsLines()
    {
        var (displayList, dataset, catalogue) = RunPipeline("RTE-TEST-GFULL.s421.gml");

        var renderer = new MapsuiDisplayListRenderer { Palette = catalogue.ActivePalette };
        var instructions = Part9DisplayListReader.Read(displayList);
        var geometryProvider = new FeatureGeometryProvider<S421Feature>(dataset.Features);
        var layer = renderer.Render(instructions, geometryProvider);

        // At least one line op (a route leg) in the bound scene.
        Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(layer, out var baseScene, out _));
        Assert.Contains(baseScene.Ops, op => op is LinePaintOp { World.Count: >= 2 });
    }
}
