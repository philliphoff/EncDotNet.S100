using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Features.Tests;

public class FeatureCatalogueManagerTests
{
    private const string MinimalFcXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <S100FC:S100_FC_FeatureCatalogue
            xmlns:S100FC="http://www.iho.int/S100FC/5.2"
            xmlns:S100Base="http://www.iho.int/S100Base/5.0"
            xmlns:S100CI="http://www.iho.int/S100CI/5.0">
          <S100FC:name>Test Catalogue</S100FC:name>
          <S100FC:versionNumber>2.0.0</S100FC:versionNumber>
          <S100FC:versionDate>2024-10-16</S100FC:versionDate>
          <S100FC:productId>S-101</S100FC:productId>
        </S100FC:S100_FC_FeatureCatalogue>
        """;

    private static Stream Open(string xml) => new MemoryStream(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void GetCatalogue_StringOverload_ReturnsParsedCatalogue()
    {
        var mgr = new FeatureCatalogueManager(spec => spec == "S-101" ? Open(MinimalFcXml) : null);
        var fc = mgr.GetCatalogue("S-101");
        Assert.NotNull(fc);
        Assert.Equal("S-101", fc!.ProductId);
        Assert.Equal("2.0.0", fc.VersionNumber);
    }

    [Fact]
    public void GetCatalogue_SpecRefOverload_ReturnsParsedCatalogue()
    {
        var mgr = new FeatureCatalogueManager(spec => spec == "S-101" ? Open(MinimalFcXml) : null);
        var fc = mgr.GetCatalogue(new SpecRef("S-101", new SpecVersion(1, 2, 0)));
        Assert.NotNull(fc);
        Assert.Equal(new CatalogueRef("S-101", new SpecVersion(2, 0, 0)), fc!.CatalogueRef);
    }

    [Fact]
    public void GetCatalogue_DistinctEditions_GetSeparateCacheSlots()
    {
        int callCount = 0;
        var mgr = new FeatureCatalogueManager((SpecRef _) =>
        {
            callCount++;
            return Open(MinimalFcXml);
        });

        var v1 = mgr.GetCatalogue(new SpecRef("S-101", new SpecVersion(1, 2, 0)));
        var v2 = mgr.GetCatalogue(new SpecRef("S-101", new SpecVersion(2, 0, 0)));
        Assert.NotNull(v1);
        Assert.NotNull(v2);
        Assert.NotSame(v1, v2);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void GetCatalogue_SameSpecRef_IsCachedSingleton()
    {
        int callCount = 0;
        var mgr = new FeatureCatalogueManager((SpecRef _) =>
        {
            callCount++;
            return Open(MinimalFcXml);
        });

        var spec = new SpecRef("S-101", new SpecVersion(1, 2, 0));
        var a = mgr.GetCatalogue(spec);
        var b = mgr.GetCatalogue(spec);
        Assert.Same(a, b);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetCatalogue_StringOverload_NormalisesName()
    {
        // "S101", "s-101", "S-101" must all collapse to the same cache slot.
        int callCount = 0;
        var mgr = new FeatureCatalogueManager((string _) =>
        {
            callCount++;
            return Open(MinimalFcXml);
        });

        var a = mgr.GetCatalogue("S-101");
        var b = mgr.GetCatalogue("S101");
        var c = mgr.GetCatalogue("s-101");
        Assert.Same(a, b);
        Assert.Same(b, c);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetCatalogue_StringOverload_GarbageReturnsNull()
    {
        var mgr = new FeatureCatalogueManager((string _) => Open(MinimalFcXml));
        // SpecName.TryNormalize fails → null without parsing.
        Assert.Null(mgr.GetCatalogue("not-a-spec"));
    }

    [Fact]
    public void GetCatalogue_StringResolver_ReceivesNameFromSpecRef()
    {
        string? lastReceived = null;
        var mgr = new FeatureCatalogueManager(name =>
        {
            lastReceived = name;
            return Open(MinimalFcXml);
        });

        mgr.GetCatalogue(new SpecRef("S-101", new SpecVersion(1, 2, 0)));
        Assert.Equal("S-101", lastReceived);
    }

    [Fact]
    public void GetDecoder_SpecRef_ReturnsDecoderBoundToCatalogue()
    {
        var mgr = new FeatureCatalogueManager((SpecRef _) => Open(MinimalFcXml));
        var dec = mgr.GetDecoder(new SpecRef("S-101", default));
        Assert.NotNull(dec);
        Assert.Equal("S-101", dec!.Catalogue.ProductId);
    }

    [Fact]
    public async Task ICatalogueProvider_GetCatalogueAsync_ReturnsParsedCatalogue()
    {
        var mgr = new FeatureCatalogueManager((SpecRef _) => Open(MinimalFcXml));
        ICatalogueProvider<FeatureCatalogue> provider = mgr;
        var fc = await provider.GetCatalogueAsync(new SpecRef("S-101", new SpecVersion(1, 2, 0)));
        Assert.NotNull(fc);
        Assert.Equal(new CatalogueRef("S-101", new SpecVersion(2, 0, 0)), fc!.CatalogueRef);
    }

    [Fact]
    public async Task ICatalogueProvider_AvailableCatalogues_ListsLoadedRefs()
    {
        var mgr = new FeatureCatalogueManager((SpecRef _) => Open(MinimalFcXml));
        ICatalogueProvider<FeatureCatalogue> provider = mgr;
        Assert.Empty(provider.AvailableCatalogues);

        await provider.GetCatalogueAsync(new SpecRef("S-101", new SpecVersion(1, 2, 0)));
        var refs = provider.AvailableCatalogues;
        Assert.Single(refs);
        Assert.Contains(new CatalogueRef("S-101", new SpecVersion(2, 0, 0)), refs);
    }

    [Fact]
    public async Task ICatalogueProvider_GetCatalogueAsync_HonoursCancellation()
    {
        var mgr = new FeatureCatalogueManager((SpecRef _) => Open(MinimalFcXml));
        ICatalogueProvider<FeatureCatalogue> provider = mgr;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await provider.GetCatalogueAsync(new SpecRef("S-101", default), cts.Token));
    }

    /// <summary>
    /// Minimal <see cref="IAssetSource"/> that hands out a single in-memory
    /// asset on demand and counts how many times it has been opened.
    /// </summary>
    private sealed class CountingSource : IAssetSource
    {
        private readonly byte[] _bytes;
        public int OpenCount;
        public int DisposeCount;
        public CountingSource(string content) => _bytes = Encoding.UTF8.GetBytes(content);
        public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref OpenCount);
            return Task.FromResult<Stream>(new MemoryStream(_bytes));
        }
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    [Fact]
    public void SetSource_OpensFeatureCatalogueXml_WhenResolverReturnsNull()
    {
        var mgr = new FeatureCatalogueManager((string _) => null);
        var source = new CountingSource(MinimalFcXml);
        mgr.SetSource("S-101", source);

        var fc = mgr.GetCatalogue("S-101");
        Assert.NotNull(fc);
        Assert.Equal("S-101", fc!.ProductId);
        Assert.Equal(1, source.OpenCount);

        // Second access hits the parse cache, not the source.
        var fc2 = mgr.GetCatalogue("S-101");
        Assert.Same(fc, fc2);
        Assert.Equal(1, source.OpenCount);
    }

    [Fact]
    public void SetSource_ResolverWins_OverSetSource()
    {
        // Resolver returns a valid FC: SetSource must not be consulted.
        var source = new CountingSource(MinimalFcXml);
        var resolverCalls = 0;
        var mgr = new FeatureCatalogueManager((string _) =>
        {
            Interlocked.Increment(ref resolverCalls);
            return Open(MinimalFcXml);
        });
        mgr.SetSource("S-101", source);

        var fc = mgr.GetCatalogue("S-101");
        Assert.NotNull(fc);
        Assert.Equal(1, resolverCalls);
        Assert.Equal(0, source.OpenCount);
    }

    [Fact]
    public void SetSource_Replace_DisposesPreviousSource()
    {
        var mgr = new FeatureCatalogueManager((string _) => null);
        var first = new CountingSource(MinimalFcXml);
        var second = new CountingSource(MinimalFcXml);

        mgr.SetSource("S-101", first);
        mgr.GetCatalogue("S-101");
        Assert.Equal(1, first.OpenCount);

        mgr.SetSource("S-101", second);
        Assert.Equal(1, first.DisposeCount);

        // Cache was evicted; the new source is opened on the next access.
        mgr.GetCatalogue("S-101");
        Assert.Equal(1, second.OpenCount);
    }

    [Fact]
    public void Dispose_DisposesAllRegisteredSources()
    {
        var mgr = new FeatureCatalogueManager((string _) => null);
        var s101 = new CountingSource(MinimalFcXml);
        var s102 = new CountingSource(MinimalFcXml.Replace("S-101", "S-102"));
        mgr.SetSource("S-101", s101);
        mgr.SetSource("S-102", s102);

        mgr.Dispose();

        Assert.Equal(1, s101.DisposeCount);
        Assert.Equal(1, s102.DisposeCount);
    }
}
