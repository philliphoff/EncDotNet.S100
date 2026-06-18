using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Executes the S-101 Lua portrayal stage (S-100 Part 9A) by composing the
/// product-agnostic <see cref="LuaRuleExecutor"/> with the S-101-specific
/// seams: an <see cref="S101LuaDataProvider"/> host bridge, the S-101 context
/// parameter bindings (mapping <see cref="MarinerSettings"/> onto the declared
/// catalogue parameters), a feature-anchor provider for augmented line
/// tessellation, and the SAFCON contour-label post-parse transform.
/// </summary>
/// <remarks>
/// This is a thin wrapper that preserves the historic S-101 construction
/// signature (<c>luaEngine, dataset, catalogue, featureCatalogue</c>) while
/// delegating the full Lua pass to the shared Core executor. S-57 reuses this
/// executor after translating to an <see cref="S101Dataset"/>.
/// </remarks>
public sealed class S101LuaRuleExecutor : ILuaVectorRuleExecutor
{
    private readonly LuaRuleExecutor _inner;

    /// <summary>Initialises the S-101 Lua rule executor.</summary>
    /// <param name="luaEngine">The sandboxed Lua engine (S-100 Part 9A).</param>
    /// <param name="dataset">The S-101 dataset to portray.</param>
    /// <param name="catalogue">The S-101 portrayal catalogue (Lua rule source).</param>
    /// <param name="featureCatalogue">The S-101 feature catalogue (ISO 19110).</param>
    public S101LuaRuleExecutor(
        ILuaEngine luaEngine,
        S101Dataset dataset,
        S101PortrayalCatalogue catalogue,
        FeatureCatalogue featureCatalogue)
    {
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(featureCatalogue);

        _inner = new LuaRuleExecutor(
            luaEngine,
            catalogue,
            new S101LuaDataProviderFactory(dataset, featureCatalogue),
            "S-101",
            S101ContextParameterBindings.Build(),
            new S101FeatureAnchorProvider(dataset),
            [new S101SafconTransform()]);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DrawingInstruction>> ExecuteAsync(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
        => _inner.ExecuteAsync(mariner, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<EmittedInstruction>> ExecuteRawAsync(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
        => _inner.ExecuteRawAsync(mariner, cancellationToken);
}

/// <summary>
/// Creates render-scoped <see cref="S101LuaDataProvider"/> instances for the
/// generic <see cref="LuaRuleExecutor"/>.
/// </summary>
internal sealed class S101LuaDataProviderFactory : ILuaDataProviderFactory
{
    private readonly S101Dataset _dataset;
    private readonly FeatureCatalogue _featureCatalogue;

    public S101LuaDataProviderFactory(S101Dataset dataset, FeatureCatalogue featureCatalogue)
    {
        _dataset = dataset;
        _featureCatalogue = featureCatalogue;
    }

    public ILuaDataProvider Create(MarinerSettings mariner)
        => new S101LuaDataProvider(_dataset, _featureCatalogue);
}

/// <summary>
/// Supplies the primary point anchor of an S-101 feature for augmented line
/// tessellation during drawing-instruction parsing (sector lights, all-around
/// lights). Wraps a <see cref="FeatureGeometryProvider{TFeature}"/> over the
/// dataset's resolved vector features.
/// </summary>
internal sealed class S101FeatureAnchorProvider : IFeatureAnchorProvider
{
    private readonly IFeatureGeometryProvider _geometryProvider;

    public S101FeatureAnchorProvider(S101Dataset dataset)
    {
        _geometryProvider = new FeatureGeometryProvider<Feature>(new S101VectorSource(dataset).GetFeatures());
    }

    public (double Latitude, double Longitude)? GetAnchor(string featureRef)
    {
        var geom = _geometryProvider.GetGeometry(featureRef);
        if (geom is null || geom.Coordinates.Count == 0)
            return null;
        return geom.Coordinates[0];
    }
}

/// <summary>
/// Post-parse transform that merges adjacent SAFCON (safety contour) labels
/// per S-101 portrayal conventions. Wraps <see cref="S101SafconLabelMerger"/>.
/// </summary>
internal sealed class S101SafconTransform : IDrawingInstructionTransform
{
    public IReadOnlyList<DrawingInstruction> Transform(IReadOnlyList<DrawingInstruction> instructions)
        => S101SafconLabelMerger.Merge(instructions);
}

/// <summary>
/// Builds the S-101 mapping from <see cref="MarinerSettings"/> to the context
/// parameters declared by the S-101 portrayal catalogue (S-100 Part 9A). Each
/// binding is applied by the generic executor only when the catalogue declares
/// the corresponding parameter.
/// </summary>
internal static class S101ContextParameterBindings
{
    public static IReadOnlyList<LuaContextParameterBinding> Build() =>
    [
        new("SafetyContour", m => m.SafetyContour, LuaValueSerializers.Number),
        new("SafetyDepth", m => m.SafetyDepth, LuaValueSerializers.Number),
        new("ShallowContour", m => m.ShallowContour, LuaValueSerializers.Number),
        new("DeepContour", m => m.DeepContour, LuaValueSerializers.Number),
        new("FourShades", m => m.FourShades, LuaValueSerializers.Bool),
        new("ShallowWaterDangers", m => m.ShallowWaterDangers, LuaValueSerializers.Bool),
        new("PlainBoundaries", m => m.PlainBoundaries, LuaValueSerializers.Bool),
        new("SimplifiedSymbols", m => m.SimplifiedSymbols, LuaValueSerializers.Bool),
        new("FullLightLines", m => m.FullLightLines, LuaValueSerializers.Bool),
        new("RadarOverlay", m => m.RadarOverlay, LuaValueSerializers.Bool),
        new("IgnoreScaleMinimum", m => m.IgnoreScaleMinimum, LuaValueSerializers.Bool),
        // NationalLanguage is only sent when explicitly chosen — a blank value
        // leaves the catalogue's declared default (eng) in place.
        new("NationalLanguage",
            m => string.IsNullOrWhiteSpace(m.NationalLanguage) ? null : m.NationalLanguage,
            LuaValueSerializers.Str),
    ];
}
