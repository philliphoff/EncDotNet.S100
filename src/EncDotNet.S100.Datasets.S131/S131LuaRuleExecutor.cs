using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S131;

/// <summary>
/// Executes the S-131 Lua portrayal stage (S-100 Part 9A) by composing the
/// product-agnostic <see cref="LuaRuleExecutor"/> with the S-131-specific
/// seams: an <see cref="S131LuaDataProvider"/> host bridge over GML-encoded
/// features, and the S-131 context parameter bindings.
/// </summary>
/// <remarks>
/// <para>
/// This is the GML+Lua bridge: S-131 features are parsed from GML by
/// <see cref="S131DatasetReader"/>, then the Lua portrayal engine processes
/// them identically to how it processes S-101 ISO 8211 features. The
/// <see cref="S131LuaDataProvider"/> translates between the GML feature model
/// and the Lua host API contract, including the GML-specific spatial shim.
/// </para>
/// <para>
/// Unlike S-101, S-131 applies no anchor provider (it has no augmented line
/// geometry) and no post-parse transform (no SAFCON merger). Its portrayal
/// catalogue declares <c>TwoShades</c> rather than S-101's <c>FourShades</c>,
/// so the context-parameter binding inverts the mariner's four-shades flag.
/// </para>
/// </remarks>
public sealed class S131LuaRuleExecutor : ILuaVectorRuleExecutor
{
    private readonly LuaRuleExecutor _inner;

    /// <summary>Initialises the S-131 Lua rule executor.</summary>
    /// <param name="luaEngine">The sandboxed Lua engine (S-100 Part 9A).</param>
    /// <param name="dataset">The S-131 dataset (GML-encoded) to portray.</param>
    /// <param name="catalogue">The S-131 portrayal catalogue (Lua rule source).</param>
    /// <param name="featureCatalogue">The S-131 feature catalogue (ISO 19110).</param>
    public S131LuaRuleExecutor(
        ILuaEngine luaEngine,
        S131Dataset dataset,
        S131PortrayalCatalogue catalogue,
        FeatureCatalogue featureCatalogue)
    {
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(featureCatalogue);

        _inner = new LuaRuleExecutor(
            luaEngine,
            catalogue,
            new S131LuaDataProviderFactory(dataset, featureCatalogue),
            "S-131",
            S131ContextParameterBindings.Build());
    }

    /// <inheritdoc/>
    public IReadOnlyList<DrawingInstruction> Execute(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
        => _inner.Execute(mariner, cancellationToken);

    /// <inheritdoc/>
    public IReadOnlyList<EmittedInstruction> ExecuteRaw(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
        => _inner.ExecuteRaw(mariner, cancellationToken);
}

/// <summary>
/// Creates render-scoped <see cref="S131LuaDataProvider"/> instances for the
/// generic <see cref="LuaRuleExecutor"/>.
/// </summary>
internal sealed class S131LuaDataProviderFactory : ILuaDataProviderFactory
{
    private readonly S131Dataset _dataset;
    private readonly FeatureCatalogue _featureCatalogue;

    public S131LuaDataProviderFactory(S131Dataset dataset, FeatureCatalogue featureCatalogue)
    {
        _dataset = dataset;
        _featureCatalogue = featureCatalogue;
    }

    public ILuaDataProvider Create(MarinerSettings mariner)
        => new S131LuaDataProvider(_dataset, _featureCatalogue);
}

/// <summary>
/// Builds the S-131 mapping from <see cref="MarinerSettings"/> to the context
/// parameters declared by the S-131 portrayal catalogue (PC Edition 2.0.0).
/// </summary>
/// <remarks>
/// The S-131 PC declares <c>TwoShades</c> where S-101 declares <c>FourShades</c>;
/// the two are inverses, so the binding maps <c>!FourShades → TwoShades</c>.
/// Bindings are applied by the generic executor only when the catalogue
/// actually declares the corresponding parameter.
/// </remarks>
internal static class S131ContextParameterBindings
{
    public static IReadOnlyList<LuaContextParameterBinding> Build() =>
    [
        new("SafetyContour", m => m.SafetyContour, LuaValueSerializers.Number),
        new("SafetyDepth", m => m.SafetyDepth, LuaValueSerializers.Number),
        new("TwoShades", m => !m.FourShades, LuaValueSerializers.Bool),
    ];
}
