using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-411 sea ice rendering. Covers both the
/// official IHO PC v1.2.1 sample shape (S100 GML 5.0 namespace, plural
/// <c>members</c> wrapper) and the JCOMM/CIS shape with shared
/// <c>gml:id="seaice.None"</c> identifiers exercised by the reader's
/// synthetic-id path.
/// </summary>
public sealed class S411RenderingTests
{
    [SkippableTheory]
    [InlineData("iho_4112C00TDS001.gml")]
    [InlineData("iho_4112C00TDS002.gml")]
    [InlineData("cis_seaice_synthetic.gml")]
    public Task SeaIce(string fileName)
    {
        var path = Path.Combine(TestHelpers.DatasetsRoot, "S411", fileName);
        Skip.IfNot(File.Exists(path), $"S-411 test dataset not present: {path}");

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
