namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>Visual regression tests for S-129 under keel clearance rendering.</summary>
public sealed class S129RenderingTests
{
    [SkippableFact]
    public Task UkcDataset()
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S129", "12900MCTDS130TS.gml");
        Skip.IfNot(File.Exists(path), $"S-129 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
        });

        // Sparse render (ink ~5 700 px): hold it to a share of its own ink
        // rather than the default 5 % of the image (18 000 px). Its labels
        // drift up to 14 % of the ink between macOS and Linux.
        return TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 2_800, maxDifferentInkFraction: 0.3);
    }
}
