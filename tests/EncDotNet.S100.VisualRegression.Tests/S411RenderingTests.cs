namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-411 sea ice rendering. Covers both the
/// official IHO PC v1.2.1 sample shape (S100 GML 5.0 namespace, plural
/// <c>members</c> wrapper) and the JCOMM/CIS shape with shared
/// <c>gml:id="seaice.None"</c> identifiers exercised by the reader's
/// synthetic-id path.
/// </summary>
public sealed class S411RenderingTests
{
    [SkippableTheory]
    [InlineData("iho_4112C00TDS001.gml", 3_300, 0.25)] // sparse, ink ~6 700 px; no drift
    [InlineData("iho_4112C00TDS002.gml", 4_200, 0.25)] // sparse, ink ~8 500 px; no drift
    // Dense area fills (ink ~101 000 px): the whole-image 5 % default is
    // already under a fifth of its ink.
    [InlineData("cis_seaice_synthetic.gml", 50_000, null)]
    public Task SeaIce(string fileName, int minimumInkPixels, double? maxDifferentInkFraction)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S411", fileName);
        Skip.IfNot(File.Exists(path), $"S-411 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
        });

        var name = Path.GetFileNameWithoutExtension(fileName);
        if (maxDifferentInkFraction is { } inkFraction)
        {
            return TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels, inkFraction)
                .UseParameters(name);
        }

        var ink = TestHelpers.CountNonBackgroundPixels(bitmap);
        Assert.True(ink >= minimumInkPixels,
            $"{fileName} rendered only {ink} non-background pixels (expected at least {minimumInkPixels}).");
        return TestHelpers.VerifyBitmap(bitmap).UseParameters(name);
    }
}
