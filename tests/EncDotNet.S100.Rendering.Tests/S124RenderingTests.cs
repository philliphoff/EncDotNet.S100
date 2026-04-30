using EncDotNet.S100.Testing.Rendering;

namespace EncDotNet.S100.Rendering.Tests;

/// <summary>
/// Visual regression tests for S-124 navigational warnings rendering. One test
/// per geometry kind (point / curve / surface) plus a mixed dataset.
/// </summary>
public sealed class S124RenderingTests
{
    [SkippableTheory]
    [InlineData("navwarn_point.gml")]
    [InlineData("navwarn_curve.gml")]
    [InlineData("navwarn_surface.gml")]
    [InlineData("navwarn_mixed.gml")]
    public Task NavWarning(string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S124", fileName);
        Skip.IfNot(File.Exists(path), $"S-124 test dataset not present: {path}");

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
