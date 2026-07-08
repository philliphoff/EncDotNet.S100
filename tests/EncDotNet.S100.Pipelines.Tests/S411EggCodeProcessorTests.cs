using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that <see cref="S411DatasetProcessor"/> surfaces the WMO egg code
/// on <see cref="FeatureInfo.EggCode"/> for picked sea-ice features.
/// </summary>
public class S411EggCodeProcessorTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "S411", "display_modes.gml");

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

    private static S411DatasetProcessor CreateProcessor() =>
        new(
            FixturePath,
            CreateCatalogueManager(),
            new DisplayPlaneAuthorityProvider(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

    [Fact]
    public void GetFeatureInfo_SeaIceFeature_CarriesEggCode()
    {
        var processor = CreateProcessor();

        var info = processor.GetFeatureInfoAt(0);

        Assert.NotNull(info);
        Assert.NotNull(info!.EggCode);
        Assert.Equal("1", info.EggCode!.TotalConcentration!.Text);
        // iceapc = 1 (single type) folds the partial-concentration row away.
        Assert.True(info.EggCode.ConcentrationRowFolded);
        Assert.Equal(new[] { "87" }, info.EggCode.StagesOfDevelopment.Select(v => v.Text).ToArray());
        Assert.Equal(new[] { "7" }, info.EggCode.FormsOfIce.Select(v => v.Text).ToArray());

        // The stage-of-development code is enriched with its Feature Catalogue
        // prose meaning for the pick report's hover tooltip.
        var stage = info.EggCode.StagesOfDevelopment[0];
        Assert.Equal("Thin First Year Ice (30 to <70 cm)", stage.Definition);
    }
}
