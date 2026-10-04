using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// S-128 product coverages draw as outlines only. The bundled catalogue is
/// byte-identical to the IHO upstream, which fills each coverage 70 % opaque on
/// <c>OVERRADAR</c>; nested products (a harbour cell inside an approach cell)
/// stacked to near-opaque over the ENC. <see cref="S128PortrayalCatalogue"/>
/// wraps the upstream rule in an adapter that drops the fills, following
/// upstream issue #51 and the upstream Lua port (PR #56).
/// </summary>
public sealed class S128CoverageOverlayTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("s128-overlay-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Nested_product_coverages_draw_as_outlines_only()
    {
        var instructions = await PortrayAsync(new S128PortrayalCatalogue(CreateProvider()));

        Assert.DoesNotContain(instructions, i => i is AreaInstruction);
        var outlines = instructions.OfType<LineInstruction>().ToList();
        Assert.Equal(["APPROACH", "HARBOUR"], outlines.Select(o => o.FeatureReference).Order());
        Assert.All(outlines, o =>
        {
            Assert.Equal(Vector.DisplayPlane.OverRadar, o.Plane);
            Assert.Equal(31000, o.ViewingGroup);
        });
    }

    [Fact]
    public async Task Adapter_keeps_every_upstream_instruction_except_the_coverage_fills()
    {
        var upstream = await PortrayAsync(new UpstreamCatalogue(CreateProvider()));
        var adapted = await PortrayAsync(new S128PortrayalCatalogue(CreateProvider()));

        // The bundled upstream rule still fills the coverages 70 % opaque. When a
        // catalogue refresh drops the fills, this fails: remove the adapter.
        var fills = upstream.OfType<AreaInstruction>().ToList();
        Assert.Equal(["APPROACH", "HARBOUR"], fills.Select(f => f.FeatureReference).Order());
        Assert.All(fills, f => Assert.Equal(0.30, f.Transparency));

        Assert.Equal(
            upstream.Where(i => i is not AreaInstruction).Select(Describe),
            adapted.Select(Describe));
    }

    private static string Describe(DrawingInstruction i) =>
        $"{i.GetType().Name} {i.FeatureReference} {i.Plane} vg{i.ViewingGroup} p{i.DrawingPriority}";

    private async Task<IReadOnlyList<DrawingInstruction>> PortrayAsync(GmlPortrayalCatalogueBase catalogue)
    {
        var source = new S128FeatureXmlSource(S128Dataset.Open(SyntheticS128Catalogue.Write(_directory)));
        var layer = (IVectorLayer)await new PortrayalPipeline().ProcessAsync(source, catalogue);
        return layer.Instructions;
    }

    private static PortrayalCatalogueProvider CreateProvider()
    {
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-128", Specification.CreatePortrayalCatalogueSource("S-128"));
        return catalogues.GetProvider("S-128");
    }

    /// <summary>The bundled catalogue's rules as published, with no adapter.</summary>
    private sealed class UpstreamCatalogue(PortrayalCatalogueProvider provider) : GmlPortrayalCatalogueBase(provider)
    {
        public override SpecRef Spec => new("S-128", default);
    }
}
