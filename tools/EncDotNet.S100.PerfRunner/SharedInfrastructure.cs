using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// Lazily initialises and caches the shared infrastructure (catalogue
/// manager, Lua engine, CRS factory, pipeline factory) so scenarios can
/// share it across iterations without paying repeated start-up costs.
/// </summary>
internal static class SharedInfrastructure
{
    private static readonly Lazy<PortrayalCatalogueManager> s_catalogueManager = new(CreateCatalogueManager);
    private static readonly Lazy<MoonSharpLuaEngine> s_luaEngine = new(() => new MoonSharpLuaEngine());
    private static readonly Lazy<ProjNetCrsTransformFactory> s_crsFactory = new(() => new ProjNetCrsTransformFactory());
    private static readonly Lazy<FeatureCatalogueManager> s_featureCatalogueManager =
        new(() => new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

    public static PortrayalCatalogueManager CatalogueManager => s_catalogueManager.Value;
    public static MoonSharpLuaEngine LuaEngine => s_luaEngine.Value;
    public static ProjNetCrsTransformFactory CrsFactory => s_crsFactory.Value;
    public static FeatureCatalogueManager FeatureCatalogueManager =>
        s_featureCatalogueManager.Value;

    public static Datasets.Pipelines.DatasetPipelineFactory CreatePipelineFactory()
    {
        var factoryType = typeof(Datasets.Pipelines.DatasetPipelineFactory);

        // Newest shape (PR-L1): adds IInteroperabilityAuthorityProvider.
        var providerType = typeof(Datasets.Pipelines.Interoperability.IInteroperabilityAuthorityProvider);
        var providerCtor = factoryType.GetConstructor(
            [
                typeof(PortrayalCatalogueManager),
                typeof(ILuaEngine),
                typeof(ICrsTransformFactory),
                typeof(FeatureCatalogueManager),
                providerType,
            ]);
        if (providerCtor is not null)
        {
            var provider = new Datasets.Pipelines.Interoperability.InteroperabilityAuthorityProvider(
                new Datasets.Pipelines.Interoperability.InteroperabilityAuthority());
            return (Datasets.Pipelines.DatasetPipelineFactory)providerCtor.Invoke(
                [CatalogueManager, LuaEngine, CrsFactory, FeatureCatalogueManager, provider]);
        }

        var managerCtor = factoryType.GetConstructor(
            [
                typeof(PortrayalCatalogueManager),
                typeof(ILuaEngine),
                typeof(ICrsTransformFactory),
                typeof(FeatureCatalogueManager)
            ]);
        if (managerCtor is not null)
        {
            return (Datasets.Pipelines.DatasetPipelineFactory)managerCtor.Invoke(
                [CatalogueManager, LuaEngine, CrsFactory, FeatureCatalogueManager]);
        }

        var resolverCtor = factoryType.GetConstructor(
            [
                typeof(PortrayalCatalogueManager),
                typeof(ILuaEngine),
                typeof(ICrsTransformFactory),
                typeof(Func<string, Stream?>)
            ]);
        if (resolverCtor is not null)
        {
            Func<string, Stream?> resolver = Specification.TryOpenFeatureCatalogue;
            return (Datasets.Pipelines.DatasetPipelineFactory)resolverCtor.Invoke(
                [CatalogueManager, LuaEngine, CrsFactory, resolver]);
        }

        throw new MissingMethodException(
            nameof(Datasets.Pipelines.DatasetPipelineFactory),
            ".ctor(PortrayalCatalogueManager, ILuaEngine, ICrsTransformFactory, ...)");
    }

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
            {
                var source = Specification.CreatePortrayalCatalogueSource(spec);
                manager.SetSource(spec, source);
            }
        }
        return manager;
    }
}
