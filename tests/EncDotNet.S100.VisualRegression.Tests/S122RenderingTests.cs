namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-122 marine protected areas rendering. The
/// official 2.0.0 sample (<c>122TESTDATASET.gml</c>) exercises the four
/// area / line feature types present in the bundled portrayal catalogue
/// (<c>MarineProtectedArea</c>, <c>RestrictedArea</c>,
/// <c>VesselTrafficServiceArea</c>, <c>InformationArea</c>).
/// </summary>
public sealed class S122RenderingTests
{
    [SkippableTheory]
    // Ink ~1 450 px. Line work only: macOS/Linux drift measured at 2 px.
    [InlineData("122TESTDATASET.gml", 700, 0.25)]
    public Task MarineProtectedArea(string fileName, int minimumInkPixels, double maxDifferentInkFraction)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S122", fileName);
        Skip.IfNot(File.Exists(path), $"S-122 test dataset not present: {path}");

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
