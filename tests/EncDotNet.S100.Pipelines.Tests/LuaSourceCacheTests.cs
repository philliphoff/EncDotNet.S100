using System.Collections.Concurrent;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for the catalogue-level Lua source cache introduced by PR-2 of the
/// asset-caching audit (<c>docs/internal/asset-caching-audit.md</c> §6 PR-2).
/// Verifies that <see cref="S101PortrayalCatalogue.GetLuaSource"/> and
/// <see cref="S131PortrayalCatalogue.GetLuaSource"/> serve repeated reads of
/// the same Lua source file from an in-memory cache without re-opening the
/// underlying <see cref="IAssetSource"/>.
/// </summary>
public class LuaSourceCacheTests
{
    [Fact]
    public async Task S101_GetLuaSource_returns_cached_string_and_opens_underlying_source_once()
    {
        using var inner = Specification.CreatePortrayalCatalogueSource("S-101");
        using var counting = new CountingAssetSource(inner);

        var provider = await PortrayalCatalogueProvider.OpenAsync(counting);
        var catalogue = new S101PortrayalCatalogue(provider);

        // Prime the path counters: snapshot the open count for "Rules/main.lua"
        // *before* the first GetLuaSource call so the catalogue.xml read does
        // not leak in.
        var openCountBefore = counting.GetOpenCount("Rules/main.lua");

        var first = catalogue.GetLuaSource("main.lua");
        var second = catalogue.GetLuaSource("main.lua");
        var third = catalogue.GetLuaSource("main.lua");

        Assert.NotNull(first);
        Assert.NotEmpty(first!);
        // Reference-equal: the cache must return the same string instance,
        // not a freshly decoded copy.
        Assert.Same(first, second);
        Assert.Same(first, third);

        // The underlying asset source must be opened exactly once across the
        // three GetLuaSource calls.
        Assert.Equal(openCountBefore + 1, counting.GetOpenCount("Rules/main.lua"));
    }

    [Fact]
    public async Task S101_GetLuaSource_caches_negative_lookups_so_missing_files_do_not_retry()
    {
        using var inner = Specification.CreatePortrayalCatalogueSource("S-101");
        using var counting = new CountingAssetSource(inner);

        var provider = await PortrayalCatalogueProvider.OpenAsync(counting);
        var catalogue = new S101PortrayalCatalogue(provider);

        const string missing = "definitely-not-a-real-rule.lua";
        var openCountBefore = counting.GetOpenCount($"Rules/{missing}");

        var a = catalogue.GetLuaSource(missing);
        var b = catalogue.GetLuaSource(missing);
        var c = catalogue.GetLuaSource(missing);

        Assert.Null(a);
        Assert.Null(b);
        Assert.Null(c);

        var attemptedOpens = counting.GetOpenCount($"Rules/{missing}") - openCountBefore;
        // At most one attempted underlying open — the second and third calls
        // hit the cached null.
        Assert.True(attemptedOpens <= 1,
            $"Expected ≤1 underlying open for missing file, got {attemptedOpens}.");
    }

    [Fact]
    public async Task S131_GetLuaSource_returns_cached_string_and_opens_underlying_source_once()
    {
        using var inner = Specification.CreatePortrayalCatalogueSource("S-131");
        using var counting = new CountingAssetSource(inner);

        var provider = await PortrayalCatalogueProvider.OpenAsync(counting);
        var catalogue = new S131PortrayalCatalogue(provider);

        var openCountBefore = counting.GetOpenCount("Rules/main.lua");

        var first = catalogue.GetLuaSource("main.lua");
        var second = catalogue.GetLuaSource("main.lua");
        var third = catalogue.GetLuaSource("main.lua");

        Assert.NotNull(first);
        Assert.NotEmpty(first!);
        Assert.Same(first, second);
        Assert.Same(first, third);

        Assert.Equal(openCountBefore + 1, counting.GetOpenCount("Rules/main.lua"));
    }

    [Fact]
    public async Task S101_GetLuaSource_independent_caches_per_catalogue_instance()
    {
        // Sandbox isolation behavioural test: two separate catalogue instances
        // (each its own provider) must each cache independently. This proves
        // the cache lives on the catalogue, not as a hidden static — so two
        // dataset processors with two catalogues each pay one open per file,
        // not zero. (PR-3 will lift these to a shared per-spec cache; PR-2
        // does not.)
        using var innerA = Specification.CreatePortrayalCatalogueSource("S-101");
        using var countingA = new CountingAssetSource(innerA);
        var providerA = await PortrayalCatalogueProvider.OpenAsync(countingA);
        var catalogueA = new S101PortrayalCatalogue(providerA);

        using var innerB = Specification.CreatePortrayalCatalogueSource("S-101");
        using var countingB = new CountingAssetSource(innerB);
        var providerB = await PortrayalCatalogueProvider.OpenAsync(countingB);
        var catalogueB = new S101PortrayalCatalogue(providerB);

        var srcA = catalogueA.GetLuaSource("main.lua");
        var srcB = catalogueB.GetLuaSource("main.lua");

        Assert.NotNull(srcA);
        Assert.NotNull(srcB);
        // The string contents are equal — they read the same bundled bytes.
        Assert.Equal(srcA, srcB);

        // But each catalogue opened its own underlying source exactly once.
        Assert.Equal(1, countingA.GetOpenCount("Rules/main.lua"));
        Assert.Equal(1, countingB.GetOpenCount("Rules/main.lua"));
    }

    /// <summary>
    /// Thin <see cref="IAssetSource"/> decorator that counts <see cref="OpenAsync"/>
    /// calls per relative path. Used to assert that the catalogue's Lua source
    /// cache short-circuits repeated reads.
    /// </summary>
    private sealed class CountingAssetSource : IAssetSource
    {
        private readonly IAssetSource _inner;
        private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

        public CountingAssetSource(IAssetSource inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;
        }

        public int GetOpenCount(string relativePath) =>
            _counts.TryGetValue(relativePath, out var n) ? n : 0;

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            _counts.AddOrUpdate(relativePath, 1, (_, n) => n + 1);
            return _inner.OpenAsync(relativePath, cancellationToken);
        }

        public void Dispose()
        {
            // Do not dispose _inner — the caller owns it.
        }
    }
}
