using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Façade that drives the appropriate inner portrayal pipeline
/// (<see cref="VectorPipeline"/> for feature-based products,
/// <see cref="CoveragePipeline"/> for gridded coverage products) and
/// returns the result as the unified <see cref="IPortrayalLayer"/> type.
/// </summary>
/// <remarks>
/// Use the strongly-typed <c>ProcessAsync</c> overloads when you know which
/// kind of source you have. Callers that need to dispatch on the produced
/// layer type can pattern-match the returned <see cref="IPortrayalLayer"/>
/// against <see cref="IVectorLayer"/> or <see cref="StyledCoverageLayer"/>.
/// </remarks>
public sealed class PortrayalPipeline
{
    private readonly VectorPipeline _vectorPipeline;
    private readonly CoveragePipeline _coveragePipeline;

    public PortrayalPipeline(ILuaRuleExecutor? luaExecutor = null)
    {
        _vectorPipeline = new VectorPipeline(luaExecutor);
        _coveragePipeline = new CoveragePipeline();
    }

    /// <summary>
    /// Drives the vector portrayal pipeline and returns the result as an
    /// <see cref="IPortrayalLayer"/>. The runtime type is
    /// <see cref="IVectorLayer"/>.
    /// </summary>
    public async Task<IPortrayalLayer> ProcessAsync(
        IFeatureXmlSource source,
        IVectorPortrayalCatalogue catalogue,
        Viewport? viewport = null,
        MarinerSettings? mariner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        return await _vectorPipeline.ProcessAsync(source, catalogue, viewport, mariner)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Drives the coverage portrayal pipeline and returns the result as an
    /// <see cref="IPortrayalLayer"/>. The runtime type is
    /// <see cref="StyledCoverageLayer"/>.
    /// </summary>
    public async Task<IPortrayalLayer> ProcessAsync(
        ICoverageSource source,
        ICoveragePortrayalCatalogue catalogue,
        MarinerSettings? mariner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        return await _coveragePipeline.ProcessAsync(source, catalogue, mariner)
            .ConfigureAwait(false);
    }
}
