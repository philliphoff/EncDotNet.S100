using EncDotNet.S100.VisualRegression;

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
    [InlineData("122TESTDATASET.gml")]
    public Task MarineProtectedArea(string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S122", fileName);
        Skip.IfNot(File.Exists(path), $"S-122 test dataset not present: {path}");

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
