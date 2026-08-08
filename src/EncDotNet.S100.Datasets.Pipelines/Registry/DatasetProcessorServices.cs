using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// The shared, product-agnostic dependencies a per-product registration closes
/// over when it constructs an <see cref="IDatasetProcessor"/>. A single instance
/// is built once by <see cref="DatasetPipelineFactory"/> from the managers it was
/// given and passed to every registration, so — exactly as before the registry
/// existed — the portrayal / feature-catalogue parse caches survive for the
/// lifetime of the managers rather than per processor.
/// </summary>
public sealed class DatasetProcessorServices
{
    /// <summary>Portrayal catalogue manager shared by every processor.</summary>
    public required PortrayalCatalogueManager CatalogueManager { get; init; }

    /// <summary>Lua engine for Part 9A portrayal.</summary>
    public required ILuaEngine LuaEngine { get; init; }

    /// <summary>CRS transform factory for coverage products.</summary>
    public required ICrsTransformFactory CrsTransformFactory { get; init; }

    /// <summary>Feature catalogue manager (shared FC parse cache).</summary>
    public required FeatureCatalogueManager FeatureCatalogueManager { get; init; }

    /// <summary>Default S-98 display-plane authority provider.</summary>
    public required IDisplayPlaneAuthorityProvider AuthorityProvider { get; init; }

    /// <summary>
    /// Optional process-wide portrayal-instruction cache shared by every S-101
    /// processor; <see langword="null"/> falls back to a bounded per-processor
    /// in-memory cache.
    /// </summary>
    public IPortrayalInstructionCache? SharedInstructionCache { get; init; }

    /// <summary>
    /// Optional process-wide line-LOD cache shared by every S-101 processor;
    /// <see langword="null"/> falls back to the renderer's inline simplification.
    /// </summary>
    public ILineLodCache? SharedLineLodCache { get; init; }
}
