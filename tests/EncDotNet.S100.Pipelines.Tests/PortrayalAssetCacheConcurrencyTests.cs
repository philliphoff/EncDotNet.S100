using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the thread-safety of <see cref="PortrayalAssetCache"/>.
/// A single cache instance is shared by every catalogue that resolves the same
/// specification (one cache per <c>SpecRef</c>), so concurrent dataset render
/// pipelines read and write its slots in parallel. Before the PR-6 hardening of
/// the asset-caching audit these slots were plain <see cref="Dictionary{TKey,TValue}"/>
/// instances, and concurrent multi-cell loads corrupted them — surfacing as
/// <see cref="InvalidOperationException"/> ("Operations that change non-concurrent
/// collections must have exclusive access") thrown from
/// <c>S101PortrayalCatalogue.GetLuaSourceAsync</c>. The slots are now backed by
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public class PortrayalAssetCacheConcurrencyTests
{
    [Fact]
    public async Task LuaSources_ConcurrentCheckThenLoad_DoesNotThrow()
    {
        var cache = new PortrayalAssetCache();
        var exceptions = new List<Exception>();
        var sync = new object();

        var tasks = new Task[32];
        for (var t = 0; t < tasks.Length; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < 2000; i++)
                    {
                        // Mirror the catalogue's check-then-load-then-set access
                        // pattern (including caching a null negative lookup).
                        var key = "module" + (i % 64);
                        if (!cache.LuaSources.TryGetValue(key, out _))
                        {
                            cache.LuaSources[key] = (i % 2 == 0) ? "return {}" : null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (sync)
                    {
                        exceptions.Add(ex);
                    }
                }
            });
        }

        await Task.WhenAll(tasks);

        Assert.Empty(exceptions);
    }
}
