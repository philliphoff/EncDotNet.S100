using System.Xml.Linq;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Specifications;
using Mapsui.Layers;
using Mapsui.Nts;

namespace EncDotNet.S100.Datasets.S421.Tests;

/// <summary>
/// Integration tests that drive the full S-421 portrayal pipeline:
/// dataset → FeatureXML → XSLT (bundled PC) → MapsuiS421VectorRenderer.
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
        var transform = catalogue.GetCompiledRule(rule.Name);

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
    public void Renderer_Minimal_ProducesLayerWithFeatures()
    {
        var (displayList, dataset, catalogue) = RunPipeline("RTE-TEST-GMIN.s421.gml");

        var renderer = new MapsuiS421VectorRenderer
        {
            Palette = catalogue.ActivePalette,
            SymbolProvider = name =>
            {
                try { return catalogue.GetSymbol(name); }
                catch { return null; }
            },
        };

        var layer = renderer.Render(displayList, dataset);
        Assert.IsType<MemoryLayer>(layer);

        var memLayer = (MemoryLayer)layer;
        var features = memLayer.Features.OfType<GeometryFeature>().ToList();
        Assert.Equal(2, features.Count); // both waypoints

        // Each rendered feature must carry its source dataset reference.
        foreach (var f in features)
        {
            var refValue = f[MapsuiS101VectorRenderer.FeatureRefKey] as string;
            Assert.False(string.IsNullOrEmpty(refValue));
        }
    }

    [Fact]
    public void Renderer_Full_RoutLegsRenderAsLines()
    {
        var (displayList, dataset, catalogue) = RunPipeline("RTE-TEST-GFULL.s421.gml");

        var renderer = new MapsuiS421VectorRenderer { Palette = catalogue.ActivePalette };
        var layer = (MemoryLayer)renderer.Render(displayList, dataset);

        var features = layer.Features.OfType<GeometryFeature>().ToList();
        Assert.NotEmpty(features);

        // At least one feature whose geometry is a LineString (a route leg).
        Assert.Contains(features, f => f.Geometry is NetTopologySuite.Geometries.LineString);
    }
}
