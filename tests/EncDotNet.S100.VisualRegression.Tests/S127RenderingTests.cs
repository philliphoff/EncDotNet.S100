namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-127 marine resources & services rendering.
/// One test per geometry kind (point / curve / surface) plus a mixed dataset.
/// </summary>
public sealed class S127RenderingTests
{
    [SkippableTheory]
    // Unlabelled line work and symbols: no macOS/Linux drift measured.
    [InlineData("marine_point.gml", 180, 0.25)]     // ink ~370 px
    [InlineData("marine_curve.gml", 750, 0.25)]     // ink ~1 540 px
    [InlineData("marine_surface.gml", 3_000, 0.25)] // ink ~6 100 px
    [InlineData("marine_mixed.gml", 1_500, 0.25)]   // ink ~3 150 px
    public Task MarineService(string fileName, int minimumInkPixels, double maxDifferentInkFraction)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S127", fileName);
        Skip.IfNot(File.Exists(path), $"S-127 test dataset not present: {path}");

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
