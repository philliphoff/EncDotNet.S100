using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EncDotNet.S100.Features;
using EncDotNet.S100.Specifications;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies that <see cref="Specification.CreateFeatureCatalogueSource(string)"/>
/// produces an <see cref="EncDotNet.S100.Core.IAssetSource"/> that the
/// <see cref="FeatureCatalogueManager"/> can consume through its
/// <c>SetSource</c> API, parallel to the existing
/// <see cref="Specification.CreatePortrayalCatalogueSource(string)"/> +
/// <c>PortrayalCatalogueManager.SetSource</c> integration.
/// </summary>
public class SpecificationFeatureCatalogueSourceTests
{
    [Fact]
    public async Task CreateFeatureCatalogueSource_OpensFeatureCatalogueXml()
    {
        using var source = Specification.CreateFeatureCatalogueSource("S-101");
        using var stream = await source.OpenAsync("FeatureCatalogue.xml");
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void FeatureCatalogueManager_ConsumesBundledFcSource()
    {
        using var mgr = new FeatureCatalogueManager((string _) => null);
        mgr.SetSource("S-101", Specification.CreateFeatureCatalogueSource("S-101"));

        var fc = mgr.GetCatalogue("S-101");
        Assert.NotNull(fc);
        Assert.Equal("S-101", fc!.ProductId);
    }

    [Fact]
    public void HasFeatureCatalogue_AgreesWithAvailableSpecs()
    {
        foreach (var spec in Specification.AvailableSpecs)
        {
            // Every advertised spec must have a bundled FeatureCatalogue.xml.
            Assert.True(Specification.HasFeatureCatalogue(spec),
                $"Expected bundled FC for '{spec}'.");
        }
        Assert.False(Specification.HasFeatureCatalogue("S-999"));
    }
}
