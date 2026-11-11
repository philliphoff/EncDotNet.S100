using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies <see cref="CataloguePreWarm"/> behaviour around the S-100 Part 9A
/// inline "simple" line-style sentinel
/// (<see cref="LineInstruction.SimpleLineStyleReference"/>). Regression cover
/// for issue #286: each S-101 dataset load drove three
/// <see cref="KeyNotFoundException"/> throws because the synthetic
/// <c>_simple_</c> line style was looked up against (and missing from) the
/// portrayal catalogue on every pre-warm pass.
/// </summary>
public sealed class CataloguePreWarmTests
{
    [Fact]
    public async Task ForInstructions_DoesNotResolveSimpleLineStyleSentinel()
    {
        var catalogue = new CountingPortrayalAssetSource();
        var instructions = new DrawingInstruction[]
        {
            new LineInstruction
            {
                FeatureReference = "f1",
                LineStyleReference = LineInstruction.SimpleLineStyleReference,
                LineColor = "CHGRD",
                LineWidth = 0.32,
            },
            new AreaInstruction
            {
                FeatureReference = "f2",
                OutlineStyleReference = LineInstruction.SimpleLineStyleReference,
            },
        };

        var result = await CataloguePreWarm.ForInstructionsAsync(
            catalogue, instructions, CancellationToken.None);

        Assert.Equal(0, catalogue.LineStyleLookups);
        Assert.False(result.LineStyles.ContainsKey(LineInstruction.SimpleLineStyleReference));
    }

    [Fact]
    public async Task ForInstructions_StillResolvesNamedLineStyles()
    {
        var catalogue = new CountingPortrayalAssetSource();
        var instructions = new DrawingInstruction[]
        {
            new LineInstruction { FeatureReference = "f1", LineStyleReference = "ACHARE51" },
        };

        var result = await CataloguePreWarm.ForInstructionsAsync(
            catalogue, instructions, CancellationToken.None);

        Assert.Equal(1, catalogue.LineStyleLookups);
        Assert.NotNull(result.ResolveLineStyle("ACHARE51"));
    }

    private sealed class CountingPortrayalAssetSource : IPortrayalAssetSource
    {
        public int LineStyleLookups { get; private set; }

        public ValueTask<SvgSymbol> GetSymbolAsync(string symbolName, CancellationToken cancellationToken = default) =>
            new(new SvgSymbol { Name = symbolName, SvgContent = $"<svg id=\"{symbolName}\"/>" });

        public ValueTask<LineStyle> GetLineStyleAsync(string name, CancellationToken cancellationToken = default)
        {
            LineStyleLookups++;
            return new(new LineStyle { Name = name, Width = 1.0f, Color = "#000000" });
        }

        public ValueTask<AreaFill> GetAreaFillAsync(string name, CancellationToken cancellationToken = default) =>
            new(new AreaFill { Name = name, Color = "#C8C8C8" });
    }
}
