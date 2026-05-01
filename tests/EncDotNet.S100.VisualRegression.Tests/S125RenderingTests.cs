using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-125 marine aids to navigation rendering.
/// One test per geometry kind (point / curve / surface) plus a richer
/// Chesapeake Bay mixed dataset that exercises the AtoN status indication
/// portrayal template (the only feature template shipped by the
/// preliminary S-125 1.0.0 development PC).
/// </summary>
public sealed class S125RenderingTests
{
    [SkippableTheory]
    [InlineData("aton_point.gml")]
    [InlineData("aton_curve.gml")]
    [InlineData("aton_surface.gml")]
    [InlineData("aton_chesapeake.gml")]
    public Task AtoN(string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S125", fileName);
        Skip.IfNot(File.Exists(path), $"S-125 test dataset not present: {path}");

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
