using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-128 Catalogue of Nautical Products
/// rendering. The official 2.0.0 sample
/// (<c>S128_TDS_sample.gml</c>) exercises the three product feature
/// classes (<c>ElectronicProduct</c>, <c>PhysicalProduct</c>,
/// <c>S100Service</c>) plus a handful of geometry-less metadata
/// features (<c>DistributorInformation</c>, <c>ContactDetails</c>,
/// <c>ProducerInformation</c>, <c>CatalogueSectionHeader</c>).
/// </summary>
public sealed class S128RenderingTests
{
    [SkippableTheory]
    [InlineData("S128_TDS_sample.gml")]
    public Task CatalogueOfNauticalProducts(string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S128", fileName);
        Skip.IfNot(File.Exists(path), $"S-128 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
        });

        return TestHelpers.VerifyBitmap(bitmap)
            .UseParameters(Path.GetFileNameWithoutExtension(fileName));
    }
}
