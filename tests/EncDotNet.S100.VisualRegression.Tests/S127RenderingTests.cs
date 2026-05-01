using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-127 marine resources & services rendering.
/// One test per geometry kind (point / curve / surface) plus a mixed dataset.
/// </summary>
public sealed class S127RenderingTests
{
    [SkippableTheory]
    [InlineData("marine_point.gml")]
    [InlineData("marine_curve.gml")]
    [InlineData("marine_surface.gml")]
    [InlineData("marine_mixed.gml")]
    public Task MarineService(string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S127", fileName);
        Skip.IfNot(File.Exists(path), $"S-127 test dataset not present: {path}");

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
