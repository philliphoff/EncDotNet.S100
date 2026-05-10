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
}
