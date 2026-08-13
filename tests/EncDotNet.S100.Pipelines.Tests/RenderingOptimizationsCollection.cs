namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// xUnit collection for tests that read or write the process-global
/// <see cref="EncDotNet.S100.Renderers.Mapsui.RenderingOptimizations"/>
/// configuration; a test class opts in with <c>[Collection(Name)]</c>. xUnit
/// runs distinct test classes in parallel by default, so without this a writer
/// (e.g. <see cref="CachedVectorStyleRendererLodTests"/> flipping
/// <c>PrecomputedLineLodEnabled</c> on) can mutate the shared static mid-flight
/// through a count-sensitive reader in another class (e.g.
/// <see cref="CachedVectorStyleRendererTests"/>), producing order-/timing-
/// dependent failures. No shared fixture is needed — the coupling is the static
/// config itself.
/// </summary>
/// <remarks>
/// <c>DisableParallelization</c> is set so the collection also never runs
/// alongside <em>non-member</em> classes that touch the same statics — e.g.
/// <c>TileMetatileTests</c> temporarily sets
/// <c>RenderingOptimizations.RenderSubsystem</c>, which
/// <see cref="RenderingOptimizationsTests"/> reads and asserts on. Serializing
/// only the members against each other would leave that cross-class race open,
/// and it would silently reopen whenever a new test starts touching these
/// statics; disabling parallelization for the collection closes both without
/// having to enumerate every mutator.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RenderingOptimizationsCollection
{
    public const string Name = "RenderingOptimizations serial";
}
