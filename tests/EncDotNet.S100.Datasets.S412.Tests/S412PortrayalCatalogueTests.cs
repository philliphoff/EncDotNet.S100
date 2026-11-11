using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.S412.Tests;

/// <summary>
/// Runs S-412 features through the bundled draft portrayal catalogue's XSLT
/// rules, as the vector pipeline does.
/// </summary>
public class S412PortrayalCatalogueTests
{
    private static async Task<S412PortrayalCatalogue> OpenCatalogueAsync()
    {
        var provider = await PortrayalCatalogueProvider.OpenAsync(
            Specification.CreatePortrayalCatalogueSource("S-412"),
            cancellationToken: TestContext.Current.CancellationToken);
        return new S412PortrayalCatalogue(provider);
    }

    private static async Task<XDocument> PortrayAsync(string file)
    {
        var catalogue = await OpenCatalogueAsync();
        var rule = Assert.Single(catalogue.Rules);
        var transform = await catalogue.GetCompiledRuleAsync(rule.Name, TestContext.Current.CancellationToken);

        var source = new GmlFeatureXmlSource<S412Feature>(S412Dataset.Open(file).Features);
        var displayList = new XDocument();
        using (var input = source.GetFeatureXml(TestContext.Current.CancellationToken))
        using (var writer = displayList.CreateWriter())
        {
            transform.Transform(input, writer);
        }

        return displayList;
    }

    // The fill colour each feature's areaInstruction uses.
    private static Dictionary<string, string> FillColours(XDocument displayList) =>
        displayList.Root!.Elements("areaInstruction").ToDictionary(
            a => a.Element("featureReference")!.Value,
            a => a.Descendants("color").Single().Value);

    [Fact]
    public void Catalogue_IsBundled()
    {
        Assert.Contains("S-412", Specification.AvailableSpecs);
        Assert.True(Specification.HasPortrayalCatalogue("S-412"));
        using var fc = Specification.TryOpenFeatureCatalogue("S-412");
        Assert.NotNull(fc);
    }

    [Fact]
    public async Task Sample_StormForceWind_IsOrange()
    {
        var displayList = await PortrayAsync(S412DatasetReaderTests.SampleFile);

        Assert.Equal(new Dictionary<string, string> { ["ID1"] = "WXORG" }, FillColours(displayList));
    }

    [Fact]
    public async Task Synthetic_EveryWarningTypeHasItsOwnStyle()
    {
        var displayList = await PortrayAsync(S412DatasetReaderTests.SyntheticFile);

        // No feature falls back to the default rule (a QUESMRK1 symbol).
        Assert.Empty(displayList.Root!.Elements("pointInstruction"));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["WIND1"] = "WXYLW",
                ["WAVE1"] = "WXSTB",
                ["VIS1"] = "WXGRY",
                ["TS1"] = "WXBRN",
                ["ICE1"] = "WXHPK",
            },
            FillColours(displayList));
    }

    [Fact]
    public async Task WaveHeightRulePatch_IsStillNeeded()
    {
        // The draft catalogue (upstream 1e4fe55) has no
        // SignificantWaveHeightWarning.xsl, so S412PortrayalCatalogue serves a
        // corrected WaveHeightWarning.xsl in its place. When this fails, a
        // refreshed catalogue ships the rule: delete WaveHeightRulePatch and
        // its row in content/S412/README.md.
        var provider = await PortrayalCatalogueProvider.OpenAsync(
            Specification.CreatePortrayalCatalogueSource("S-412"),
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            provider.FetchRuleAsync("SignificantWaveHeightWarning.xsl", TestContext.Current.CancellationToken));
        await using var upstream = await provider.FetchRuleAsync("WaveHeightWarning.xsl", TestContext.Current.CancellationToken);
        using var reader = new StreamReader(upstream);
        Assert.Contains("match=\"WaveHeightWarning[", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }
}
