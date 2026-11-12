using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The S-131 portrayal catalogue declares a <c>NationalLanguage</c> context
/// parameter that its <c>GetFeatureName</c> uses to choose among a feature's
/// <c>featureName</c> entries. These tests run the real S-131 Lua rules to
/// check that <see cref="MarinerSettings.NationalLanguage"/> reaches them.
/// </summary>
public class S131NationalLanguageTests
{
    private const string EnglishName = "Inner Anchorage";
    private const string FrenchName = "Mouillage Intérieur";

    // An anchorage point with a default English name (nameUsage 1) and a
    // French name for chart display (nameUsage 2).
    private const string Gml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <S131:Dataset xmlns:S131="http://www.iho.int/S131/1.0"
                      xmlns:S100="http://www.iho.int/s100gml/5.0"
                      xmlns:gml="http://www.opengis.net/gml/3.2"
                      gml:id="DS_S131_Language">
          <S100:DatasetIdentificationInformation>
            <S100:productIdentifier>S-131</S100:productIdentifier>
          </S100:DatasetIdentificationInformation>
          <S131:members>
            <S131:AnchorageArea gml:id="f1">
              <S131:geometry>
                <S100:pointProperty>
                  <gml:Point gml:id="p1" srsName="EPSG:4326">
                    <gml:pos>44.65 -63.57</gml:pos>
                  </gml:Point>
                </S100:pointProperty>
              </S131:geometry>
              <S131:featureName>
                <S131:name>{EnglishName}</S131:name>
                <S131:language>eng</S131:language>
                <S131:nameUsage>1</S131:nameUsage>
              </S131:featureName>
              <S131:featureName>
                <S131:name>{FrenchName}</S131:name>
                <S131:language>fra</S131:language>
                <S131:nameUsage>2</S131:nameUsage>
              </S131:featureName>
            </S131:AnchorageArea>
          </S131:members>
        </S131:Dataset>
        """;

    [Theory]
    [InlineData("", EnglishName)]
    [InlineData("eng", EnglishName)]
    [InlineData("fra", FrenchName)]
    [InlineData("deu", EnglishName)] // no German name: falls back to the default
    public async Task FeatureName_FollowsNationalLanguage(string language, string expected)
    {
        var texts = await RunAsync(MarinerSettings.Default with { NationalLanguage = language });

        Assert.Contains(texts, t => t.Contains(expected, StringComparison.Ordinal));
        var other = expected == EnglishName ? FrenchName : EnglishName;
        Assert.DoesNotContain(texts, t => t.Contains(other, StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> RunAsync(MarinerSettings mariner)
    {
        using var gml = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Gml));
        var dataset = S131Dataset.Open(gml);

        using var fcStream = Specification.TryOpenFeatureCatalogue("S-131")
            ?? throw new InvalidOperationException("S-131 feature catalogue not bundled.");
        var fc = FeatureCatalogueReader.Read(fcStream);

        using var pcSource = Specification.CreatePortrayalCatalogueSource("S-131");
        var provider = await PortrayalCatalogueProvider.OpenAsync(pcSource);
        var luaEngine = TestLuaEngine.Create();
        var catalogue = new S131PortrayalCatalogue(provider, luaEngine);
        await catalogue.SwitchPaletteAsync(PaletteType.Day);

        var executor = new S131LuaRuleExecutor(luaEngine, dataset, catalogue, fc);
        var emitted = await executor.ExecuteRawAsync(mariner);

        return [.. emitted
            .Select(e => e.InstructionString)
            .Where(s => s.Contains("TextInstruction:", StringComparison.Ordinal))];
    }
}
