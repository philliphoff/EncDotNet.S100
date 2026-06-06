using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-57 ENC rendering. S-57 datasets are translated
/// in-memory into <c>S101Document</c> graphs and rendered through the existing
/// S-101 portrayal pipeline (see <c>EncDotNet.S100.Datasets.S57</c>); this test
/// is therefore an end-to-end check on both the translator and the S-101
/// rendering stack as exercised on legacy data.
/// </summary>
public sealed class S57RenderingTests
{
    [SkippableFact]
    public Task EncCell_DayPalette()
    {
        var path = Path.Combine(
            TestHelpers.DatasetsRoot,
            "S57", "US5MA1BO", "US5MA1BO.000");
        Skip.IfNot(File.Exists(path), $"S-57 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 800,
            Height = 600,
        });

        // S-57 is translated in-memory to S-101 and rendered through the full
        // portrayal stack; the resulting raster drifts slightly across platforms
        // (notably font/anti-aliasing on win-arm64, observed ~5.93% vs the
        // committed baseline). Allow a modest 8% tolerance for this end-to-end
        // snapshot so platform pixel jitter doesn't fail CI.
        return TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction: 0.08);
    }
}
