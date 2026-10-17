using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that <see cref="S124DatasetProcessor"/> surfaces the warning
/// message on <see cref="FeatureInfo.NavigationalWarning"/> for a picked
/// <c>NavwarnPart</c> (#932).
/// </summary>
public class S124NavigationalWarningProcessorTests
{
    private static S124DatasetProcessor CreateProcessor(string fileName)
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new S124DatasetProcessor(
            Path.Combine(AppContext.BaseDirectory, "TestData", "S124", fileName),
            manager,
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
    }

    [Fact]
    public void GetFeatureInfo_NavwarnPart_CarriesTheWarningMessage()
    {
        var info = CreateProcessor("navwarn_ccg_ed2.gml").GetFeatureInfoAt(0);

        var warning = info?.NavigationalWarning;
        Assert.NotNull(warning);
        Assert.Equal("P", warning!.Series);
        Assert.Equal("477/20", warning.Number);
        Assert.Equal("P 477/20", warning.Label);
        Assert.Equal("Presence of Scientific Equipment", warning.WarningType);
        Assert.Equal("Scientific Instruments Change", warning.GeneralType);
        Assert.Equal("Vancouver Harbour", warning.GeneralArea);
        Assert.Equal("Canadian Coast Guard", warning.Issuer);
        Assert.True(warning.HasLocation);
        var range = Assert.Single(warning.Validity);
        Assert.Equal(new DateTimeOffset(2020, 4, 3, 0, 0, 0, TimeSpan.Zero), range.Start);
        Assert.Null(range.End);
        var text = Assert.Single(warning.Texts);
        Assert.Equal("eng", text.Language);
        Assert.StartsWith("Wave monitoring buoys", text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void GetFeatureInfo_BilingualPart_CarriesEveryLanguage()
    {
        var warning = CreateProcessor("navwarn_ccg_ed2_bilingual.gml").GetFeatureInfoAt(0)?.NavigationalWarning;

        Assert.Equal(["eng", "fra"], warning!.Texts.Select(t => t.Language));
    }

    [Theory]
    [InlineData(106, 2026, "106/26")]
    [InlineData(98, 2026, "098/26")]
    [InlineData(1383, 2019, "1383/19")]
    [InlineData(7, null, "007")]
    public void FormatNumber_WritesTheMarinerForm(int number, int? year, string expected) =>
        Assert.Equal(expected, S124WarningInfo.FormatNumber(number, year));
}
