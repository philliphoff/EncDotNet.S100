namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-124 navigational warnings rendering. One test
/// per geometry kind (point / curve / surface) plus a mixed dataset.
/// </summary>
public sealed class S124RenderingTests
{
    [Theory]
    // Ink budgets from the measured macOS/Linux drift: line work and the NW
    // symbols are stable, but label glyphs shift a pixel or two, so the
    // labelled fixtures need room for every glyph pixel to differ.
    [InlineData("navwarn_point.gml", 500, 0.85)]   // ink ~1 050 px; label drift 56 % of ink
    [InlineData("navwarn_curve.gml", 1_000, 0.25)] // ink ~2 070 px; no drift
    [InlineData("navwarn_surface.gml", 500, 0.25)] // ink ~1 020 px; no drift
    [InlineData("navwarn_mixed.gml", 800, 0.5)]    // ink ~1 620 px; label drift 29 % of ink
    public Task NavWarning(string fileName, int minimumInkPixels, double maxDifferentInkFraction)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S124", fileName);
        Assert.SkipUnless(File.Exists(path), $"S-124 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
        });

        // Sparse render: hold it to a share of its own ink rather than the
        // default 5 % of the image, which exceeds all the ink present.
        return TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels, maxDifferentInkFraction)
            .UseParameters(Path.GetFileNameWithoutExtension(fileName));
    }
}
