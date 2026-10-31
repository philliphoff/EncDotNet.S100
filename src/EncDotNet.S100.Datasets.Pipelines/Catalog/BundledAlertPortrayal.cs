using System.Collections.Concurrent;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// Alert portrayals (#1127) over the portrayal and feature catalogues the
/// library bundles, for cells read without a processor: the CLI and other
/// file-backed catalogs (<see cref="FileDatasetCatalog"/>).
/// </summary>
internal static class BundledAlertPortrayal
{
    // One catalogue pair per engine and product, opened on first use.
    private static readonly ConcurrentDictionary<(ILuaEngine Engine, string Spec), Lazy<Task<(S101PortrayalCatalogue, FeatureCatalogue)>>> Catalogues = new();

    /// <summary>
    /// The alert portrayal for a cell of <paramref name="spec"/> (<c>S-101</c>
    /// or <c>S-401</c>), or <see langword="null"/> when the library bundles no
    /// catalogues for it.
    /// </summary>
    public static S101AlertPortrayal? For(ILuaEngine luaEngine, S101Dataset dataset, string spec)
    {
        if (!Specification.HasPortrayalCatalogue(spec))
        {
            return null;
        }

        var catalogues = Catalogues.GetOrAdd(
            (luaEngine, spec),
            key => new Lazy<Task<(S101PortrayalCatalogue, FeatureCatalogue)>>(() => OpenAsync(key.Engine, key.Spec)));
        return new S101AlertPortrayal(luaEngine, dataset, _ => catalogues.Value);
    }

    private static async Task<(S101PortrayalCatalogue, FeatureCatalogue)> OpenAsync(ILuaEngine luaEngine, string spec)
    {
        using var stream = Specification.TryOpenFeatureCatalogue(spec)
            ?? throw new InvalidOperationException($"The {spec} feature catalogue isn't bundled.");
        var features = FeatureCatalogueReader.Read(stream);
        var provider = await PortrayalCatalogueProvider.OpenAsync(Specification.CreatePortrayalCatalogueSource(spec)).ConfigureAwait(false);
        return (new S101PortrayalCatalogue(provider, luaEngine), features);
    }
}
