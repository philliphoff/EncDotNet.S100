using EncDotNet.S100.Core;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The chart reference service (#1635, symbol dictionary design note §3.4,
/// R2, D4): one index per product, reused while the catalogues are
/// unchanged, dropped when one is replaced.
/// </summary>
public sealed class ChartReferenceServiceTests
{
    internal static readonly SpecRef S101 = new("S-101", default);

    /// <summary>The viewer's catalogue managers over the bundled catalogues.</summary>
    internal sealed class Fixture : IDisposable
    {
        public Fixture(params string[] specs)
        {
            Features = new FeatureCatalogueManager((string spec) => Specification.TryOpenFeatureCatalogue(spec));
            Portrayals = new PortrayalCatalogueManager();
            foreach (var spec in specs.Length > 0 ? specs : Specification.AvailableSpecs)
            {
                if (Specification.HasPortrayalCatalogue(spec))
                    Portrayals.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
            }

            Service = new ChartReferenceService(Features, Portrayals, () => Loaded);
        }

        public FeatureCatalogueManager Features { get; }

        public PortrayalCatalogueManager Portrayals { get; }

        public ChartReferenceService Service { get; }

        public List<string> Loaded { get; } = [];

        public void Dispose()
        {
            Service.Dispose();
            Portrayals.Dispose();
            Features.Dispose();
        }
    }

    [Fact]
    public async Task The_index_is_reused_while_the_hashes_are_unchanged()
    {
        using var f = new Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;

        var first = await f.Service.GetIndexAsync(S101, ct);
        var second = await f.Service.GetIndexAsync(S101, ct);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(S101, first.Spec);
        Assert.NotEmpty(first.FeatureTypes);
    }

    [Fact]
    public async Task The_bundled_S101_index_has_the_committed_probe_results()
    {
        using var f = new Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;

        var index = await f.Service.GetIndexAsync(S101, ct);

        Assert.NotNull(index?.Probes);
        Assert.Equal(await f.Features.GetCatalogueHashAsync(S101, ct), index.Probes.FeatureCatalogueHash);
        Assert.NotNull(index.Probes.Find("LateralBeacon"));
    }

    [Fact]
    public async Task A_changed_catalogue_is_probed_in_the_background_and_cached()
    {
        var ct = TestContext.Current.CancellationToken;
        var cache = Directory.CreateTempSubdirectory("rule-probe-cache-").FullName;

        // The bundled feature catalogue with a comment added: another hash,
        // so the committed results don't apply.
        using var features = new FeatureCatalogueManager((string spec) =>
        {
            using var bundled = Specification.TryOpenFeatureCatalogue(spec);
            if (bundled is null) return null;
            var changed = new MemoryStream();
            bundled.CopyTo(changed);
            changed.Write("\n<!-- changed -->\n"u8);
            changed.Position = 0;
            return changed;
        });
        using var portrayals = new PortrayalCatalogueManager();
        portrayals.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        try
        {
            using var service = new ChartReferenceService(
                features, portrayals, () => [], bundledSpecs: ["S-101"], probeCacheDirectory: cache,
                traceEngine: () => ViewerLuaEngine.Create(EncDotNet.S100.Scripting.LuaEngineKind.LuaCSharp));
            var ready = new TaskCompletionSource();
            service.ProbesReady += (_, e) => ready.TrySetResult();

            var first = await service.GetIndexAsync(S101, ct);
            Assert.NotNull(first);
            Assert.Null(first.Probes);

            await ready.Task.WaitAsync(TimeSpan.FromMinutes(5), ct);
            var probed = await service.GetIndexAsync(S101, ct);

            Assert.NotNull(probed?.Probes);
            Assert.Same(first.FeatureTypes, probed.FeatureTypes);
            Assert.True(probed.Probes.Matches(await features.GetCatalogueHashAsync(S101, ct), await portrayals.GetCatalogueHashAsync(S101, ct)));
            Assert.Single(Directory.GetFiles(cache));
        }
        finally
        {
            Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task Callers_asking_at_once_share_one_build()
    {
        using var f = new Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;

        var indexes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.Service.GetIndexAsync(S101, ct)));

        Assert.All(indexes, i => Assert.Same(indexes[0], i));
    }

    [Fact]
    public async Task SetSource_drops_the_index_raises_Changed_and_the_next_call_rebuilds()
    {
        using var f = new Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;
        var before = await f.Service.GetIndexAsync(S101, ct);
        var changes = new List<SpecRef>();
        f.Service.Changed += (_, e) => changes.Add(e.Spec);

        f.Portrayals.SetSource(S101, Specification.CreatePortrayalCatalogueSource("S-101"));
        var after = await f.Service.GetIndexAsync(S101, ct);

        Assert.Equal([S101], changes);
        Assert.NotNull(after);
        Assert.NotSame(before, after);
        Assert.Equal(before!.FeatureTypes.Count, after.FeatureTypes.Count);
    }

    [Fact]
    public async Task A_feature_catalogue_change_also_drops_the_index()
    {
        using var f = new Fixture("S-101");
        var ct = TestContext.Current.CancellationToken;
        var before = await f.Service.GetIndexAsync(S101, ct);
        var changes = new List<SpecRef>();
        f.Service.Changed += (_, e) => changes.Add(e.Spec);

        f.Features.SetSource(S101, Specification.CreateFeatureCatalogueSource("S-101"));
        var after = await f.Service.GetIndexAsync(S101, ct);

        Assert.Equal([S101], changes);
        Assert.NotSame(before, after);
    }

    [Fact]
    public async Task A_product_without_a_portrayal_catalogue_has_no_index()
    {
        using var f = new Fixture("S-101");

        Assert.Null(await f.Service.GetIndexAsync(new SpecRef("S-102", default), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Products_list_loaded_products_first_then_every_bundled_catalogue()
    {
        using var f = new Fixture();
        // An inland S-57 cell portrays as S-401; S-102 has no portrayal
        // catalogue of its own here, so it isn't listed.
        f.Loaded.AddRange(["S-401", "S-201", "S-401", "S-57"]);

        var products = f.Service.Products;

        var bundled = Specification.AvailableSpecs.Where(Specification.HasPortrayalCatalogue).ToList();
        Assert.Equal(bundled.Count, products.Count);
        Assert.Equal(["S-201", "S-401"], products.Take(2).Select(p => p.Spec.Name));
        Assert.All(products.Take(2), p => Assert.True(p.IsLoaded));
        Assert.All(products.Skip(2), p => Assert.False(p.IsLoaded));
        Assert.Equal(
            bundled.Except(["S-201", "S-401"]).Order(StringComparer.Ordinal),
            products.Skip(2).Select(p => p.Spec.Name));
        Assert.Contains(products, p => p.Spec.Name == "S-401");
    }

    [Fact]
    public void With_nothing_loaded_products_are_in_spec_order_S401_included()
    {
        using var f = new Fixture();

        var names = f.Service.Products.Select(p => p.Spec.Name).ToList();

        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Contains("S-101", names);
        Assert.Contains("S-401", names);
    }
}
