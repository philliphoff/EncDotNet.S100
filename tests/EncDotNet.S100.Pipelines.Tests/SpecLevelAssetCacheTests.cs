using System.Collections.Concurrent;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S131;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for the spec-level portrayal asset cache introduced by PR-3 of
/// the asset-caching audit
/// (<c>docs/internal/asset-caching-audit.md</c> §6 PR-3).
/// </summary>
/// <remarks>
/// <para>
/// PR-3 hoists the per-catalogue caches (compiled XSLT, SVG symbols,
/// line styles, area fills, palettes, Lua sources, compiled Lua
/// scripts) up to a shared <see cref="IPortrayalAssetCache"/> owned by
/// <see cref="PortrayalCatalogueManager"/> per <see cref="SpecRef"/>.
/// </para>
/// <para>
/// These tests verify that:
/// <list type="bullet">
///   <item>two catalogue instances for the same spec share decoded
///   assets when constructed against the manager's provider, so the
///   underlying <see cref="IAssetSource"/> is opened exactly once per
///   asset (S-101 Lua portrayal + S-124 XSLT/GML portrayal);</item>
///   <item>negative-cached Lua source lookups (PR-2) survive the
///   hoist;</item>
///   <item>two specs registered with the same manager keep their
///   caches segmented;</item>
///   <item>providers opened directly (not through the manager) still
///   get isolated per-provider caches, preserving the existing
///   contract verified by
///   <see cref="LuaSourceCacheTests.S101_GetLuaSource_independent_caches_per_catalogue_instance"/>.</item>
/// </list>
/// </para>
/// </remarks>
public class SpecLevelAssetCacheTests
{
    [Fact]
    public void S101_two_catalogues_via_manager_share_xslt_symbol_lineStyle_areaFill_and_lua_source_caches()
    {
        using var inner = Specification.CreatePortrayalCatalogueSource("S-101");
        using var counting = new CountingAssetSource(inner);

        using var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-101", counting);

        var providerA = manager.GetProvider("S-101");
        var providerB = manager.GetProvider("S-101");

        // The manager hands out one provider per spec — and stamps one
        // shared IPortrayalAssetCache on it. The two catalogue wrappers
        // below therefore see the same cache, which is the whole point
        // of PR-3.
        var catalogueA = new S101PortrayalCatalogue(providerA);
        var catalogueB = new S101PortrayalCatalogue(providerB);

        Assert.Same(providerA, providerB);
        Assert.Same(providerA.AssetCache, providerB.AssetCache);

        // Lua source: the most representative asset, also exercises the
        // PR-2 cache that we just lifted.
        var openMainLuaBefore = counting.GetOpenCount("Rules/main.lua");
        var srcA = catalogueA.GetLuaSource("main.lua");
        var srcB = catalogueB.GetLuaSource("main.lua");
        Assert.NotNull(srcA);
        Assert.Same(srcA, srcB);
        Assert.Equal(openMainLuaBefore + 1, counting.GetOpenCount("Rules/main.lua"));

        // Compiled XSLT — pick the first catalogued .xsl rule.
        var xsltRule = providerA.Catalogue.RuleFiles
            .FirstOrDefault(r => r.FileName.EndsWith(".xsl", StringComparison.OrdinalIgnoreCase));
        if (xsltRule is not null)
        {
            var xsltAssetPath = $"Rules/{xsltRule.FileName}";
            var openXsltBefore = counting.GetOpenCount(xsltAssetPath);
            var transformA = catalogueA.GetCompiledRule(xsltRule.Id);
            var transformB = catalogueB.GetCompiledRule(xsltRule.Id);
            Assert.Same(transformA, transformB);
            Assert.Equal(openXsltBefore + 1, counting.GetOpenCount(xsltAssetPath));
        }

        // SVG symbol.
        var symbol = providerA.Catalogue.Symbols.FirstOrDefault();
        if (symbol is not null)
        {
            var assetPath = $"Symbols/{symbol.FileName}";
            var openBefore = counting.GetOpenCount(assetPath);
            var svgA = catalogueA.GetSymbol(symbol.Id);
            var svgB = catalogueB.GetSymbol(symbol.Id);
            Assert.Same(svgA, svgB);
            Assert.Equal(openBefore + 1, counting.GetOpenCount(assetPath));
        }

        // Line style.
        var lineStyle = providerA.Catalogue.LineStyles.FirstOrDefault();
        if (lineStyle is not null)
        {
            var assetPath = $"LineStyles/{lineStyle.FileName}";
            var openBefore = counting.GetOpenCount(assetPath);
            var lsA = catalogueA.GetLineStyle(lineStyle.Id);
            var lsB = catalogueB.GetLineStyle(lineStyle.Id);
            Assert.Same(lsA, lsB);
            Assert.Equal(openBefore + 1, counting.GetOpenCount(assetPath));
        }

        // Area fill.
        var areaFill = providerA.Catalogue.AreaFills.FirstOrDefault();
        if (areaFill is not null)
        {
            var assetPath = $"AreaFills/{areaFill.FileName}";
            var openBefore = counting.GetOpenCount(assetPath);
            var afA = catalogueA.GetAreaFill(areaFill.Id);
            var afB = catalogueB.GetAreaFill(areaFill.Id);
            Assert.Same(afA, afB);
            Assert.Equal(openBefore + 1, counting.GetOpenCount(assetPath));
        }
    }

    [Fact]
    public void S101_negative_lua_lookup_is_shared_across_catalogues_via_manager()
    {
        using var inner = Specification.CreatePortrayalCatalogueSource("S-101");
        using var counting = new CountingAssetSource(inner);

        using var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-101", counting);

        var catalogueA = new S101PortrayalCatalogue(manager.GetProvider("S-101"));
        var catalogueB = new S101PortrayalCatalogue(manager.GetProvider("S-101"));

        const string missing = "definitely-not-a-real-rule.lua";
        var openCountBefore = counting.GetOpenCount($"Rules/{missing}");

        Assert.Null(catalogueA.GetLuaSource(missing));
        Assert.Null(catalogueB.GetLuaSource(missing));
        Assert.Null(catalogueA.GetLuaSource(missing));

        var attempts = counting.GetOpenCount($"Rules/{missing}") - openCountBefore;
        // At most one underlying open across both catalogues: PR-2's
        // negative cache survives the PR-3 hoist.
        Assert.True(attempts <= 1,
            $"Expected ≤1 underlying open for missing file across two manager-shared catalogues, got {attempts}.");
    }

    [Fact]
    public void S124_two_GML_catalogues_via_manager_share_xslt_symbol_lineStyle_and_areaFill_caches()
    {
        using var inner = Specification.CreatePortrayalCatalogueSource("S-124");
        using var counting = new CountingAssetSource(inner);

        using var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-124", counting);

        var providerA = manager.GetProvider("S-124");
        var providerB = manager.GetProvider("S-124");
        Assert.Same(providerA.AssetCache, providerB.AssetCache);

        var catalogueA = new S124PortrayalCatalogue(providerA);
        var catalogueB = new S124PortrayalCatalogue(providerB);

        var xsltRule = providerA.Catalogue.RuleFiles
            .FirstOrDefault(r => r.RuleType.Equals("TopLevelTemplate", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(xsltRule);
        var xsltAssetPath = $"Rules/{xsltRule!.FileName}";
        var openBefore = counting.GetOpenCount(xsltAssetPath);

        var transformA = catalogueA.GetCompiledRule(xsltRule.Id);
        var transformB = catalogueB.GetCompiledRule(xsltRule.Id);
        Assert.Same(transformA, transformB);

        // Exactly one underlying open of the XSLT rule across both
        // catalogues. (The XSLT compiler may open included sub-templates
        // through the AssetSourceXmlResolver — those don't count toward
        // the top-level rule asset.)
        Assert.Equal(openBefore + 1, counting.GetOpenCount(xsltAssetPath));

        var symbol = providerA.Catalogue.Symbols.FirstOrDefault();
        if (symbol is not null)
        {
            var assetPath = $"Symbols/{symbol.FileName}";
            var symOpenBefore = counting.GetOpenCount(assetPath);
            var svgA = catalogueA.GetSymbol(symbol.Id);
            var svgB = catalogueB.GetSymbol(symbol.Id);
            Assert.Same(svgA, svgB);
            Assert.Equal(symOpenBefore + 1, counting.GetOpenCount(assetPath));
        }
    }

    [Fact]
    public void S101_and_S131_specs_keep_independent_caches()
    {
        using var s101Inner = Specification.CreatePortrayalCatalogueSource("S-101");
        using var s101Counting = new CountingAssetSource(s101Inner);
        using var s131Inner = Specification.CreatePortrayalCatalogueSource("S-131");
        using var s131Counting = new CountingAssetSource(s131Inner);

        using var manager = new PortrayalCatalogueManager();
        manager.SetSource("S-101", s101Counting);
        manager.SetSource("S-131", s131Counting);

        var s101Provider = manager.GetProvider("S-101");
        var s131Provider = manager.GetProvider("S-131");
        Assert.NotSame(s101Provider.AssetCache, s131Provider.AssetCache);

        var s101Catalogue = new S101PortrayalCatalogue(s101Provider);
        var s131Catalogue = new S131PortrayalCatalogue(s131Provider);

        var s101OpenBefore = s101Counting.GetOpenCount("Rules/main.lua");
        var s131OpenBefore = s131Counting.GetOpenCount("Rules/main.lua");

        var s101Src = s101Catalogue.GetLuaSource("main.lua");
        var s131Src = s131Catalogue.GetLuaSource("main.lua");

        Assert.NotNull(s101Src);
        Assert.NotNull(s131Src);

        // Each spec opens its own bundled main.lua exactly once. Cache
        // segmentation by SpecRef means a hit on one doesn't satisfy
        // the other.
        Assert.Equal(s101OpenBefore + 1, s101Counting.GetOpenCount("Rules/main.lua"));
        Assert.Equal(s131OpenBefore + 1, s131Counting.GetOpenCount("Rules/main.lua"));
    }

    [Fact]
    public async Task Provider_opened_directly_outside_manager_gets_its_own_cache()
    {
        // Direct callers of OpenAsync (tests, ad-hoc tooling) must not
        // be coupled to whatever cache the manager would have stamped
        // — this is what keeps
        // LuaSourceCacheTests.S101_GetLuaSource_independent_caches_per_catalogue_instance
        // honest after PR-3.
        using var innerA = Specification.CreatePortrayalCatalogueSource("S-101");
        using var countingA = new CountingAssetSource(innerA);
        var providerA = await PortrayalCatalogueProvider.OpenAsync(countingA);

        using var innerB = Specification.CreatePortrayalCatalogueSource("S-101");
        using var countingB = new CountingAssetSource(innerB);
        var providerB = await PortrayalCatalogueProvider.OpenAsync(countingB);

        Assert.NotSame(providerA.AssetCache, providerB.AssetCache);
    }

    /// <summary>
    /// Thin <see cref="IAssetSource"/> decorator that counts
    /// <see cref="OpenAsync"/> calls per relative path.
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
            // The caller owns the inner source.
        }
    }
}
