using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S125;

/// <summary>
/// Portrays S-125 aids to navigation with the bundled S-101 Portrayal
/// Catalogue's Lua rules (S-100 Part 9A) by composing the product-agnostic
/// <see cref="LuaRuleExecutor"/> with an <see cref="S125AtonLuaDataProvider"/>
/// host bridge, the S-101 context-parameter bindings, and a feature-anchor
/// provider for light-sector and all-around-light arcs.
/// </summary>
/// <remarks>
/// The S-125 Portrayal Catalogue portrays only AtoN status indications and
/// data coverage; this executor supplies the AtoN symbology itself as an
/// implementation choice (see <see cref="S125AtonPortrayalProjection"/>).
/// Emitted instructions reference S-125 <c>gml:id</c>s and S-101 symbols,
/// line styles and colour tokens.
/// </remarks>
public sealed class S125AtonLuaRuleExecutor : ILuaVectorRuleExecutor
{
    private readonly LuaRuleExecutor _inner;

    /// <summary>Initialises the executor.</summary>
    /// <param name="luaEngine">The sandboxed Lua engine (S-100 Part 9A).</param>
    /// <param name="dataset">The S-125 dataset whose aids are portrayed.</param>
    /// <param name="s101Catalogue">The S-101 portrayal catalogue (Lua rule source).</param>
    /// <param name="s101FeatureCatalogue">The S-101 feature catalogue the rules are written against.</param>
    public S125AtonLuaRuleExecutor(
        ILuaEngine luaEngine,
        S125Dataset dataset,
        S101PortrayalCatalogue s101Catalogue,
        FeatureCatalogue s101FeatureCatalogue)
    {
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(s101Catalogue);
        ArgumentNullException.ThrowIfNull(s101FeatureCatalogue);

        var aids = S125AtonPortrayalProjection.Project(dataset, s101FeatureCatalogue);
        FeatureCount = aids.Count;

        _inner = new LuaRuleExecutor(
            luaEngine,
            s101Catalogue,
            new ProviderFactory(aids, s101FeatureCatalogue),
            "S-125",
            S101ContextParameterBindings.Build(),
            new AnchorProvider(dataset.Features));
    }

    /// <summary>The number of S-125 features handed to the S-101 rules.</summary>
    public int FeatureCount { get; }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DrawingInstruction>> ExecuteAsync(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
        => _inner.ExecuteAsync(mariner, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<EmittedInstruction>> ExecuteRawAsync(
        MarinerSettings mariner, CancellationToken cancellationToken = default)
        => _inner.ExecuteRawAsync(mariner, cancellationToken);

    private sealed class ProviderFactory(IReadOnlyList<S125ProjectedAton> aids, FeatureCatalogue fc) : ILuaDataProviderFactory
    {
        public ILuaDataProvider Create(MarinerSettings mariner) => new S125AtonLuaDataProvider(aids, fc);
    }

    /// <summary>Supplies a feature's point position (keyed by <c>gml:id</c>) for augmented arcs and rays.</summary>
    private sealed class AnchorProvider(IReadOnlyList<S125Feature> features) : IFeatureAnchorProvider
    {
        private readonly Dictionary<string, GeoPosition> _anchors = features
            .Where(f => f.Points.Count > 0)
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Points[0], StringComparer.Ordinal);

        public GeoPosition? GetAnchor(string featureRef) =>
            _anchors.TryGetValue(featureRef, out var p) ? p : null;
    }
}
