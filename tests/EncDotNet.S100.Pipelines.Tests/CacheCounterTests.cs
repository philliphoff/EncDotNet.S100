using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that the cache hit / miss <see cref="Counter{T}"/> instruments
/// fire from the catalogue-level caches added by PRs #64/#66/#67/#68.
/// The audit (<c>docs/internal/asset-caching-audit.md</c> §6 PR-CACHE-7)
/// recommended these counters so a per-scenario perf summary can confirm
/// the caches are actually being reused, instead of silently rebuilt every
/// call.
/// </summary>
public sealed class CacheCounterTests
{
    [Fact]
    public async Task S101_GetLuaSource_emits_one_miss_and_subsequent_hits_with_product_tag()
    {
        var hits = new List<KeyValuePair<string, object?>[]>();
        var misses = new List<KeyValuePair<string, object?>[]>();
        using var listener = StartListening(
            "s100.lua.source.cache.hit.count", hits,
            "s100.lua.source.cache.miss.count", misses);

        using var inner = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(inner);
        var catalogue = new S101PortrayalCatalogue(provider);

        _ = catalogue.GetLuaSource("main.lua");
        _ = catalogue.GetLuaSource("main.lua");
        _ = catalogue.GetLuaSource("main.lua");

        Assert.Single(misses);
        Assert.Equal(2, hits.Count);
        Assert.All(hits, tags => Assert.Contains(tags,
            t => t.Key == "s100.product" && (string?)t.Value == "S-101"));
        Assert.All(misses, tags => Assert.Contains(tags,
            t => t.Key == "s100.product" && (string?)t.Value == "S-101"));
    }

    [Fact]
    public async Task S101_GetCompiledRule_emits_portrayal_cache_counters_with_xslt_kind()
    {
        // S-101 portrayal is Lua, but the cache slot still exists; we exercise
        // the SVG slot instead since it's used by every S-101 rule. Same
        // counter, different asset-kind.
        var hits = new List<KeyValuePair<string, object?>[]>();
        var misses = new List<KeyValuePair<string, object?>[]>();
        using var listener = StartListening(
            "s100.portrayal.cache.hit.count", hits,
            "s100.portrayal.cache.miss.count", misses);

        using var inner = Specification.CreatePortrayalCatalogueSource("S-101");
        var provider = await PortrayalCatalogueProvider.OpenAsync(inner);
        var catalogue = new S101PortrayalCatalogue(provider);

        // Find a real symbol id to ensure we hit the cache miss/hit path.
        var symbolId = provider.Catalogue.Symbols.First().Id;
        _ = catalogue.GetSymbol(symbolId);
        _ = catalogue.GetSymbol(symbolId);

        Assert.Contains(misses, tags =>
            tags.Any(t => t.Key == "s100.product" && (string?)t.Value == "S-101") &&
            tags.Any(t => t.Key == "s100.asset.kind" && (string?)t.Value == "svg"));
        Assert.Contains(hits, tags =>
            tags.Any(t => t.Key == "s100.product" && (string?)t.Value == "S-101") &&
            tags.Any(t => t.Key == "s100.asset.kind" && (string?)t.Value == "svg"));
    }

    [Fact]
    public void FeatureCatalogueManager_emits_miss_then_hit_per_spec()
    {
        var hits = new List<KeyValuePair<string, object?>[]>();
        var misses = new List<KeyValuePair<string, object?>[]>();
        using var listener = StartListening(
            "s100.featurecatalogue.cache.hit.count", hits,
            "s100.featurecatalogue.cache.miss.count", misses);

        using var manager = new FeatureCatalogueManager();
        using var source = Specification.CreateFeatureCatalogueSource("S-101");
        manager.SetSource("S-101", source);

        var spec = new SpecRef("S-101", default);
        var first = manager.GetCatalogue(spec);
        var second = manager.GetCatalogue(spec);
        var third = manager.GetCatalogue(spec);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Same(first, third);

        Assert.Single(misses);
        Assert.Equal(2, hits.Count);
        Assert.All(hits, tags => Assert.Contains(tags,
            t => t.Key == "s100.product" && (string?)t.Value == "S-101"));
        Assert.All(misses, tags => Assert.Contains(tags,
            t => t.Key == "s100.product" && (string?)t.Value == "S-101"));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static MeterListener StartListening(
        string hitName, List<KeyValuePair<string, object?>[]> hitSink,
        string missName, List<KeyValuePair<string, object?>[]> missSink)
    {
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == hitName || instrument.Name == missName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            var sink = instrument.Name == hitName ? hitSink : missSink;
            lock (sink)
            {
                sink.Add(tags.ToArray());
            }
        });

        listener.Start();
        return listener;
    }
}
