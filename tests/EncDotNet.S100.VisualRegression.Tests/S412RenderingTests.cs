namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-412 marine weather warnings, drawn with the
/// bundled draft portrayal catalogue: the upstream sample (one storm-force
/// wind warning) and the synthetic dataset with one warning of each type,
/// whose significant wave height warning is drawn only through the load-time
/// correction of the draft catalogue (<c>S412PortrayalCatalogue</c>).
/// </summary>
public sealed class S412RenderingTests
{
    [Theory]
    // Translucent fills with outlines and no text, so cross-platform drift is
    // limited to anti-aliased edges. Ink measured ~70 600 px (sample) and
    // ~50 800 px (synthetic); the minimums are about half that.
    [InlineData("4122C00_TESTDS1.GML", 35_000, 0.05)]
    [InlineData("synthetic_all_warnings.gml", 25_000, 0.05)]
    public Task WeatherWarnings(string fileName, int minimumInkPixels, double maxDifferentInkFraction)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S412", fileName);
        Assert.SkipUnless(File.Exists(path), $"S-412 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
        });

        return TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels, maxDifferentInkFraction)
            .UseParameters(Path.GetFileNameWithoutExtension(fileName));
    }
}
