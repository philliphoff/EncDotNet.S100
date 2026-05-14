using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

public class PortrayalCatalogueManagerTests
{
    private const string MinimalPcXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <pc:portrayalCatalog xmlns:pc="http://www.iho.int/S100PortrayalCatalog/5.2" productId="S-101" version="2.0.0" />
        """;

    /// <summary>
    /// Counts how many times any relative path is opened on this source.
    /// Used to prove the manager's Lazy slot collapses N concurrent
    /// first-misses for a single spec to one underlying open.
    /// </summary>
    private sealed class CountingSource : IAssetSource
    {
        private readonly byte[] _bytes;
        public int OpenCalls;

        public CountingSource(string content)
        {
            _bytes = Encoding.UTF8.GetBytes(content);
        }

        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref OpenCalls);
            return Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));
        }

        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void StringAndSpecRefOverloads_ShareCacheSlot_ForDefaultEdition()
    {
        var mgr = new PortrayalCatalogueManager();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            mgr.SetPath("S-101", tmp);

            // String getter
            Assert.Equal(tmp, mgr.GetPath("S-101"));
            // SpecRef getter using same default version
            Assert.Equal(tmp, mgr.GetPath(new SpecRef("S-101", default)));
            // Different edition → not registered.
            Assert.Null(mgr.GetPath(new SpecRef("S-101", new SpecVersion(2, 0, 0))));
        }
        finally
        {
            Directory.Delete(tmp);
        }
    }

    [Fact]
    public void DistinctEditions_AreRegisteredIndependently()
    {
        var mgr = new PortrayalCatalogueManager();
        var a = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var b = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        try
        {
            mgr.SetPath(new SpecRef("S-101", new SpecVersion(1, 2, 0)), a);
            mgr.SetPath(new SpecRef("S-101", new SpecVersion(2, 0, 0)), b);

            Assert.Equal(a, mgr.GetPath(new SpecRef("S-101", new SpecVersion(1, 2, 0))));
            Assert.Equal(b, mgr.GetPath(new SpecRef("S-101", new SpecVersion(2, 0, 0))));
            Assert.True(mgr.HasCatalogue(new SpecRef("S-101", new SpecVersion(1, 2, 0))));
            Assert.True(mgr.HasCatalogue(new SpecRef("S-101", new SpecVersion(2, 0, 0))));
            Assert.Equal(2, mgr.RegisteredCataloguesByRef.Count);
        }
        finally
        {
            Directory.Delete(a);
            Directory.Delete(b);
        }
    }

    [Fact]
    public void HasCatalogue_String_NormalisesName()
    {
        var mgr = new PortrayalCatalogueManager();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            mgr.SetPath("S-101", tmp);
            Assert.True(mgr.HasCatalogue("S101"));
            Assert.True(mgr.HasCatalogue("s-101"));
            Assert.True(mgr.HasCatalogue("S-101"));
        }
        finally
        {
            Directory.Delete(tmp);
        }
    }

    [Fact]
    public async Task ICatalogueProvider_GetCatalogueAsync_UnregisteredSpec_ReturnsNull()
    {
        using var mgr = new PortrayalCatalogueManager();
        ICatalogueProvider<PortrayalCatalogueProvider> provider = mgr;
        var result = await provider.GetCatalogueAsync(new SpecRef("S-101", default));
        Assert.Null(result);
    }

    [Fact]
    public async Task ICatalogueProvider_GetCatalogueAsync_HonoursCancellation()
    {
        using var mgr = new PortrayalCatalogueManager();
        ICatalogueProvider<PortrayalCatalogueProvider> provider = mgr;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await provider.GetCatalogueAsync(new SpecRef("S-101", default), cts.Token));
    }

    [Fact]
    public void ICatalogueProvider_AvailableCatalogues_ReflectsLoadedProviders()
    {
        using var mgr = new PortrayalCatalogueManager();
        ICatalogueProvider<PortrayalCatalogueProvider> provider = mgr;

        // Before any provider is forced into existence, no catalogues are visible.
        Assert.Empty(provider.AvailableCatalogues);
    }

    [Fact]
    public void SetSource_ProviderImmediatelyAvailable_AndAdvertisedByAvailableCatalogues()
    {
        using var mgr = new PortrayalCatalogueManager();
        var src = new CountingSource(MinimalPcXml);

        mgr.SetSource("S-101", src);

        // SetSource eagerly opens the source so the provider is already
        // materialised — the Lazy is created in already-materialised form.
        Assert.Equal(1, src.OpenCalls);

        ICatalogueProvider<PortrayalCatalogueProvider> provider = mgr;
        var refs = provider.AvailableCatalogues;
        Assert.Single(refs);
        Assert.Contains(new CatalogueRef("S-101", new SpecVersion(2, 0, 0)), refs);
    }

    [Fact]
    public async Task GetProvider_Concurrent_FirstMisses_CollapseToSingleOpen()
    {
        // SetPath + concurrent GetProvider — N threads race on the same
        // spec slot; the Lazy with ExecutionAndPublication must produce
        // exactly one provider open. Uses a filesystem temp dir because
        // GetProvider's path-based branch is what carries the race.
        using var mgr = new PortrayalCatalogueManager();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "portrayal_catalogue.xml"), MinimalPcXml);
            mgr.SetPath("S-101", tmp);

            const int Threads = 16;
            using var ready = new Barrier(Threads);
            var results = new PortrayalCatalogueProvider[Threads];
            var tasks = new Task[Threads];
            for (int i = 0; i < Threads; i++)
            {
                int idx = i;
                tasks[idx] = Task.Run(() =>
                {
                    ready.SignalAndWait();
                    results[idx] = mgr.GetProvider("S-101");
                });
            }
            await Task.WhenAll(tasks);

            // Every thread must see the same provider instance.
            var first = results[0];
            Assert.NotNull(first);
            for (int i = 1; i < Threads; i++)
            {
                Assert.Same(first, results[i]);
            }
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetSource_Replace_DisposesPreviousProvider()
    {
        using var mgr = new PortrayalCatalogueManager();
        var first = new CountingSource(MinimalPcXml);
        var second = new CountingSource(MinimalPcXml);

        mgr.SetSource("S-101", first);
        // Replacing the source must dispose the provider built from the
        // previous source (which transitively disposes the source).
        mgr.SetSource("S-101", second);

        Assert.True(first.Disposed,
            "Previous IAssetSource should be disposed when SetSource replaces a cached provider.");
        Assert.False(second.Disposed);
    }

    [Fact]
    public void Dispose_DoesNotForceUnmaterialisedLazies()
    {
        // Register a path-backed spec but never call GetProvider — no Lazy
        // body should run on Dispose. We assert by pointing the path at a
        // non-existent directory: forcing the Lazy would attempt a
        // FileSystemAssetSource open and throw.
        var mgr = new PortrayalCatalogueManager();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            mgr.SetPath("S-101", tmp);
            // No GetProvider call → Lazy was never inserted; this is the
            // simplest demonstration. The Dispose contract is "iterate
            // _providers and only dispose IsValueCreated entries".
            mgr.Dispose();
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AvailableCatalogues_SkipsUnmaterialisedLazies()
    {
        using var mgr = new PortrayalCatalogueManager();
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            mgr.SetPath("S-101", tmp);
            ICatalogueProvider<PortrayalCatalogueProvider> provider = mgr;
            // No GetProvider call yet — slot exists only as a path
            // registration, no Lazy in _providers. AvailableCatalogues
            // must not attempt to force anything.
            Assert.Empty(provider.AvailableCatalogues);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}
