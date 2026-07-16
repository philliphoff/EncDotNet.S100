using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// End-to-end coverage of S-411 selectable portrayal display modes (issue
/// #416). A single synthetic sea-ice polygon carries the full WMO egg code;
/// running the portrayal pipeline with each of the three declared display
/// modes must yield three <em>distinct</em> area fills from the same dataset,
/// and the concentration / stage-of-development fills must match the colours
/// held inline in the adapter — which are mirrored from the bundled upstream
/// WMO colour tables and guarded against drift by
/// <see cref="S411WmoColourParityTests"/>.
/// </summary>
public class S411DisplayModePortrayalTests
{
    private const string ConcentrationModeId = "IceScientificIceactDisplayMode";
    private const string StageOfDevelopmentModeId = "IceScientificIcesodDisplayMode";
    private const string NavigationalModeId = "IceNavigationalDisplayMode";

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "S411", "display_modes.gml");

    private static string ListFixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "S411", "display_modes_list.gml");

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }
        return manager;
    }

    private static S411DatasetProcessor CreateProcessor(string? fixturePath = null) =>
        new(
            fixturePath ?? FixturePath,
            CreateCatalogueManager(),
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

    private static async Task<IReadOnlyList<string>> RenderAreaFillsAsync(string? displayModeId, string? fixturePath = null)
    {
        var processor = CreateProcessor(fixturePath);
        var context = new S411RenderContext(null) { DisplayModeId = displayModeId };
        var result = await processor.BuildVectorPortrayalAsync(context);
        return result.SubLayers
            .SelectMany(sl => sl.Instructions)
            .OfType<AreaInstruction>()
            .Select(a => a.FillColor)
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .ToList();
    }

    [Fact]
    public void CatalogueDeclaresTheThreeDisplayModes()
    {
        var processor = CreateProcessor();
        Assert.Contains(ConcentrationModeId, processor.DeclaredDisplayModeIds);
        Assert.Contains(StageOfDevelopmentModeId, processor.DeclaredDisplayModeIds);
        Assert.Contains(NavigationalModeId, processor.DeclaredDisplayModeIds);
    }

    [Fact]
    public async Task ConcentrationMode_UsesUpstreamIceactColour()
    {
        // iceact = 1 -> upstream colorToken '000 100 255' -> inline #0064FF.
        var fills = await RenderAreaFillsAsync(ConcentrationModeId);
        Assert.Contains("#0064FF", fills);
    }

    [Fact]
    public async Task StageOfDevelopmentMode_UsesUpstreamIcesodColour()
    {
        // icesod = 87 -> upstream colorToken '155 210 000' -> inline #9BD200.
        var fills = await RenderAreaFillsAsync(StageOfDevelopmentModeId);
        Assert.Contains("#9BD200", fills);
    }

    [Fact]
    public async Task NavigationalMode_UsesAdapterAuthoredRiskFill()
    {
        // iceact = 1 -> concentration lead 1 -> navigational "safe" green #00C800.
        var fills = await RenderAreaFillsAsync(NavigationalModeId);
        Assert.Contains("#00C800", fills);
    }

    [Fact]
    public async Task DefaultMode_MatchesConcentrationMode()
    {
        // No explicit mode selected: portrayal defaults to concentration.
        var fills = await RenderAreaFillsAsync(null);
        Assert.Contains("#0064FF", fills);
    }

    [Fact]
    public async Task EachModeProducesADistinctFill()
    {
        var concentration = await RenderAreaFillsAsync(ConcentrationModeId);
        var stageOfDevelopment = await RenderAreaFillsAsync(StageOfDevelopmentModeId);
        var navigational = await RenderAreaFillsAsync(NavigationalModeId);

        // The same dataset drives three different looks.
        Assert.NotEqual(concentration, stageOfDevelopment);
        Assert.NotEqual(concentration, navigational);
        Assert.NotEqual(stageOfDevelopment, navigational);
    }

    [Fact]
    public async Task ListStyleConcentration_UsesFirstCodeColour()
    {
        // iceact = [80, 60] -> first code 80 -> upstream '255-125-007' -> #FF7D07.
        // Proves the adapter reduces DMI/CIS list-style egg codes to the
        // leading (thickest / dominant) element before the colour lookup.
        var fills = await RenderAreaFillsAsync(ConcentrationModeId, ListFixturePath);
        Assert.Contains("#FF7D07", fills);
    }

    [Fact]
    public async Task ListStyleStageOfDevelopment_UsesFirstCodeColour()
    {
        // icesod = [95, 93, 91, 98] -> first code 95 -> upstream '180 100 050'
        // -> #B46432 (the brown fill matching the BSIS SOD quicklook on real
        // DMI CentralWest data).
        var fills = await RenderAreaFillsAsync(StageOfDevelopmentModeId, ListFixturePath);
        Assert.Contains("#B46432", fills);
    }

    [Fact]
    public async Task ListStyleNavigational_DerivesFromFirstConcentrationCode()
    {
        // iceact = [80, 60] -> first 80 -> concentration lead 8 -> navigational
        // "danger" red #E00000.
        var fills = await RenderAreaFillsAsync(NavigationalModeId, ListFixturePath);
        Assert.Contains("#E00000", fills);
    }
}
