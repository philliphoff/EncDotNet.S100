namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-125 marine aids to navigation rendering.
/// One test per geometry kind (point / curve / surface), the synthetic
/// Chesapeake Bay mixed dataset, and a dataset derived from the aids of NOAA
/// ENC US4VA1BF. The S-125 Portrayal Catalogue portrays AtoN status
/// indications and coverage; the aids themselves are portrayed with the
/// bundled S-101 AtoN rules (buoy shapes and colours, topmarks, light flares,
/// sectors and descriptions).
/// </summary>
public sealed class S125RenderingTests
{
    [Theory]
    [InlineData("aton_point.gml", 100)]
    [InlineData("aton_curve.gml", 200)]
    // Coverage-only fixture: S-125 portrays DataCoverage with a null
    // instruction, so an empty render is the correct output.
    [InlineData("aton_surface.gml", 0)]
    [InlineData("aton_chesapeake.gml", 2_000)]
    [InlineData("aton_us4va1bf.gml", 10_000)]
    public Task AtoN(string fileName, int minimumInkPixels)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S125", fileName);
        Assert.SkipUnless(File.Exists(path), $"S-125 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
        });

        // Guard against approving a blank render (the S-125 snapshots once
        // verified pure white because nothing but status indications drew).
        var ink = TestHelpers.CountNonBackgroundPixels(bitmap);
        Assert.True(ink >= minimumInkPixels,
            $"{fileName} rendered only {ink} non-background pixels (expected at least {minimumInkPixels}); the AtoN portrayal produced (almost) nothing.");

        // AtoN renders are sparse point symbology on a white chart, so the
        // default 5 % differing-pixel tolerance (18 000 px here) would absorb
        // the loss of every symbol. Hold them to 0.2 % instead.
        return TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction: 0.002)
            .UseParameters(Path.GetFileNameWithoutExtension(fileName));
    }
}
