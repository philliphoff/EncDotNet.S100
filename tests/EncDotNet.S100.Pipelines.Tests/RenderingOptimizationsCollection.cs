namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// xUnit collection that serialises every test which reads or writes the
/// process-global <see cref="EncDotNet.S100.Renderers.Mapsui.RenderingOptimizations"/>
/// configuration. xUnit runs distinct test classes in parallel by default, so
/// without this a writer (e.g. <see cref="CachedVectorStyleRendererLodTests"/>
/// flipping <c>PrecomputedLineLodEnabled</c> on) can mutate the shared static
/// mid-flight through a count-sensitive reader in another class (e.g.
/// <see cref="CachedVectorStyleRendererTests"/>), producing order-/timing-
/// dependent failures. Membership in one collection makes these classes run
/// sequentially, so each test observes only the global state it establishes.
/// No shared fixture is needed — the coupling is the static config itself.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RenderingOptimizationsCollection
{
    public const string Name = "RenderingOptimizations serial";
}
