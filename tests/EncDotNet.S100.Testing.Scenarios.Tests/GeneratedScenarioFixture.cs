using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Testing.Scenarios.Tests;

/// <summary>
/// Writes a scenario to a temporary folder for the life of a test class and
/// deletes it afterwards.
/// </summary>
public abstract class GeneratedScenarioFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "s100-scenario-" + Guid.NewGuid().ToString("N"));

    protected GeneratedScenarioFixture(ScenarioDefinition definition)
    {
        Scenario = ScenarioGenerator.Generate(definition, _root);
    }

    public GeneratedScenario Scenario { get; }

    public ScenarioDefinition Definition => Scenario.Definition;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>"Spit across scales" with the default seed.</summary>
public sealed class SpitAcrossScalesFixture() : GeneratedScenarioFixture(SyntheticScenarios.SpitAcrossScales());

/// <summary>"Spit across scales" with an S-102 surface that declares no vertical datum (#939).</summary>
public sealed class SpitWithoutS102DatumFixture() : GeneratedScenarioFixture(SyntheticScenarios.SpitAcrossScales(declareS102VerticalDatum: false));

/// <summary>"Approach with tide window" with the default seed.</summary>
public sealed class ApproachWithTideWindowFixture() : GeneratedScenarioFixture(SyntheticScenarios.ApproachWithTideWindow());

/// <summary>"Warnings on route" in S-124 Edition 2.0.0.</summary>
public sealed class WarningsOnRouteFixture() : GeneratedScenarioFixture(SyntheticScenarios.WarningsOnRoute());

/// <summary>"Warnings on route" in S-124 Edition 1.0.0.</summary>
public sealed class WarningsOnRouteEdition1Fixture() : GeneratedScenarioFixture(SyntheticScenarios.WarningsOnRoute(s124Edition: 1));

/// <summary>"Port plan vs own route" with the default seed.</summary>
public sealed class PortPlanVsOwnRouteFixture() : GeneratedScenarioFixture(SyntheticScenarios.PortPlanVsOwnRoute());

/// <summary>Loads a generated exchange set the way the hosts do.</summary>
public static class ScenarioLoading
{
    /// <summary>
    /// Reads <c>CATALOG.XML</c> with the real catalogue reader and loads
    /// every listed dataset into a <see cref="FileDatasetCatalog"/>, using the
    /// catalogue's product identifier as the spec.
    /// </summary>
    public static FileDatasetCatalog Catalog(GeneratedScenario scenario)
    {
        var catalogue = ExchangeCatalogueReader.Read(scenario.CataloguePath);
        var inputs = catalogue.DatasetDiscoveryMetadata
            .Select(d => new FileDatasetInput(
                new DatasetId(Path.GetFileNameWithoutExtension(d.RelativePath)),
                d.ProductSpecification!.ProductIdentifier!,
                Path.Combine(scenario.Root, d.RelativePath)))
            .ToList();
        var catalog = FileDatasetCatalog.Build(inputs, new ProjNetCrsTransformFactory());
        Assert.Empty(catalog.Warnings);
        Assert.Equal(inputs.Count, catalog.Datasets.Count);
        return catalog;
    }

    /// <summary>The loaded dataset whose catalog id is <paramref name="id"/>.</summary>
    public static LoadedDataset Dataset(FileDatasetCatalog catalog, string id) =>
        catalog.Datasets.Single(d => d.Id.Value == id);

    /// <summary>The passage-check sources over the scenario's datasets.</summary>
    public static PassageDataSources Sources(GeneratedScenario scenario) =>
        PassageDataSources.FromDatasets(Catalog(scenario).Datasets, new ProjNetCrsTransformFactory());
}
