using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that picked S-411 sea-ice features carry the WMO egg code
/// (<see cref="IceEggCodeSection"/>) through <see cref="IceEggCodeContributor"/>,
/// with definitions from the <see cref="S411DatasetProcessor"/>'s catalogue.
/// </summary>
public class S411EggCodeProcessorTests
{
    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "S411", "display_modes.gml");

    private static string EmptyIceactFixturePath =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "S411", "empty_iceact_total_concentration.gml");

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

    // The egg code reaches a pick from the contributor step, which the hosts
    // run on the processor's feature info.
    private static IceEggCode? EggOf(S411DatasetProcessor processor, int ordinal)
    {
        var info = processor.GetFeatureInfoAt(ordinal);
        Assert.NotNull(info);
        return PickContributorRegistry.Default.Apply(info!, processor)
            .Sections.OfType<IceEggCodeSection>().SingleOrDefault()?.Egg;
    }

    [Fact]
    public void Pick_SeaIceFeature_CarriesEggCode()
    {
        var egg = EggOf(CreateProcessor(), 0);

        Assert.NotNull(egg);
        Assert.Equal("1", egg!.TotalConcentration!.Text);
        // iceapc = 1 (single type) folds the partial-concentration row away.
        Assert.True(egg.ConcentrationRowFolded);
        Assert.Equal(new[] { "87" }, egg.StagesOfDevelopment.Select(v => v.Text).ToArray());
        Assert.Equal(new[] { "7" }, egg.FormsOfIce.Select(v => v.Text).ToArray());

        // The stage-of-development code is enriched with its Feature Catalogue
        // prose meaning for the pick report's hover tooltip.
        var stage = egg.StagesOfDevelopment[0];
        Assert.Equal("Thin First Year Ice (30 to <70 cm)", stage.Definition);
    }

    [Fact]
    public void Pick_EmptyIceact_UsesCanonicalTotalConcentration()
    {
        var egg = EggOf(CreateProcessor(EmptyIceactFixturePath), 0);

        Assert.NotNull(egg?.TotalConcentration);
        Assert.Equal("70", egg!.TotalConcentration!.Text);
        Assert.Equal("totalConcentration", egg.TotalConcentration.SourceCode);
    }

    [Fact]
    public void GetFeatureInfo_LeavesSectionsToTheContributors()
    {
        var info = CreateProcessor().GetFeatureInfoAt(0);

        Assert.NotNull(info);
        Assert.Empty(info!.Sections);
    }

    [Fact]
    public void Processor_ResolvesListedValueDefinitionsThroughItsCatalogue()
    {
        IListedValueDefinitions processor = CreateProcessor();

        Assert.Equal(
            "Thin First Year Ice (30 to <70 cm)",
            processor.ResolveListedValueDefinition("iceStageofDevelopment", "87"));
    }
}
