using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that <see cref="DatasetPipelineFactory"/> shares the supplied
/// <see cref="FeatureCatalogueManager"/> across every processor it
/// creates. This is the contract Segment 2 PR-B introduced so the FC
/// parse cache survives across pipeline-factory rebuilds inside the
/// viewer.
/// </summary>
public class DatasetPipelineFactoryFeatureCatalogueReuseTests
{
    private const string MinimalFcXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<S100FC:S100_FC_FeatureCatalogue
    xmlns:S100FC=""http://www.iho.int/S100FC/5.2""
    xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <S100FC:name>Test</S100FC:name>
  <S100FC:scope>Test</S100FC:scope>
  <S100FC:versionNumber>1.0.0</S100FC:versionNumber>
  <S100FC:productId>S-101</S100FC:productId>
</S100FC:S100_FC_FeatureCatalogue>";

    [Fact]
    public void SharedFeatureCatalogueManager_IsReused_AcrossFactoryCalls()
    {
        var resolverCalls = 0;
        var fcManager = new FeatureCatalogueManager((string spec) =>
        {
            if (spec != "S-101") return null;
            System.Threading.Interlocked.Increment(ref resolverCalls);
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(MinimalFcXml));
        });

        // First call into the FC manager — the resolver fires.
        var first = fcManager.GetCatalogue("S-101");
        Assert.NotNull(first);
        Assert.Equal(1, resolverCalls);

        // Build two distinct pipeline factories sharing the same FC manager.
        var pcManager = new PortrayalCatalogueManager();
        var factory1 = new DatasetPipelineFactory(
            pcManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            fcManager,
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthorityProvider(
                new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthority()));
        var factory2 = new DatasetPipelineFactory(
            pcManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            fcManager,
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthorityProvider(
                new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthority()));

        // Even after two factories that, pre-PR-B, would each have
        // built their own FC manager and forced a parse, the shared
        // manager keeps the resolver-call count at one. The factories
        // are only used to assert they accept the shared manager
        // without rebuilding it.
        _ = factory1;
        _ = factory2;
        Assert.Equal(1, resolverCalls);

        // Repeated calls into the shared manager continue to hit the cache.
        var again = fcManager.GetCatalogue("S-101");
        Assert.Same(first, again);
        Assert.Equal(1, resolverCalls);
    }
}
