using EncDotNet.S100.Testing.Rendering;

namespace EncDotNet.S100.Rendering.Tests;

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

        return TestHelpers.VerifyBitmap(bitmap);
    }
}
