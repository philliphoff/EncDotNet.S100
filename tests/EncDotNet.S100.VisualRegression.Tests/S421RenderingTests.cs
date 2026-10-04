namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>Visual regression tests for S-421 route plan rendering.</summary>
public sealed class S421RenderingTests
{
    [Theory]
    [InlineData("RTE-TEST-GMIN.s421.gml", 100, 0.25)]     // ink ~200 px; no drift
    [InlineData("RTE-TEST-GBASIC.s421.gml", 1_700, 0.25)] // ink ~3 500 px; no drift
    // Ink ~7 600 px; the boxed action-point labels shift a pixel or two
    // between macOS and Linux, measured at 41 % of the ink.
    [InlineData("RTE-TEST-GFULL.s421.gml", 3_700, 0.65)]
    public Task RoutePlan(string fileName, int minimumInkPixels, double maxDifferentInkFraction)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S421", fileName);
        Assert.SkipUnless(File.Exists(path), $"S-421 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 800,
            Height = 600,
        });

        // Strip the .s421 segment so the verified-snapshot filename stays clean.
        var name = fileName.Replace(".s421.gml", "");
        // Sparse render: hold it to a share of its own ink rather than the
        // default 5 % of the image, which exceeds all the ink present.
        return TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels, maxDifferentInkFraction)
            .UseParameters(name);
    }
}
