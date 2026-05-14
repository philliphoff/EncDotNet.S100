using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

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
    private static readonly Lazy<EncDotNet.S100.Features.FeatureCatalogueManager> s_featureCatalogueManager =
        new(() => new EncDotNet.S100.Features.FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));

    public static PortrayalCatalogueManager CatalogueManager => s_catalogueManager.Value;
    public static MoonSharpLuaEngine LuaEngine => s_luaEngine.Value;
    public static ProjNetCrsTransformFactory CrsFactory => s_crsFactory.Value;
    public static EncDotNet.S100.Features.FeatureCatalogueManager FeatureCatalogueManager =>
        s_featureCatalogueManager.Value;

    public static Datasets.Pipelines.DatasetPipelineFactory CreatePipelineFactory() =>
        new(CatalogueManager, LuaEngine, CrsFactory, FeatureCatalogueManager);

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
