using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Builds a fully-wired <see cref="DatasetPipelineFactory"/> seeded with the
/// portrayal and feature catalogues bundled in
/// <c>EncDotNet.S100.Specifications</c>. Mirrors the bootstrap used by the
/// visual-regression <c>RenderHarness</c> and the Avalonia viewer so the CLI
/// drives exactly the same pipelines.
/// </summary>
internal static class ProcessorFactoryBuilder
{
    /// <summary>
    /// Creates a <see cref="DatasetPipelineFactory"/> and the
    /// <see cref="PortrayalCatalogueManager"/> it owns. The caller is
    /// responsible for disposing the returned manager.
    /// </summary>
    public static (DatasetPipelineFactory Factory, PortrayalCatalogueManager CatalogueManager) Build()
    {
        var catalogueManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
            {
                catalogueManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
            }
        }

        var featureCatalogueManager =
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);

        var factory = new DatasetPipelineFactory(
            catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            featureCatalogueManager,
            new DisplayPlaneAuthorityProvider());

        return (factory, catalogueManager);
    }
}
