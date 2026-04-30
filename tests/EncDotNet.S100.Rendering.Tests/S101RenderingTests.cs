using EncDotNet.S100.Testing.Rendering;

namespace EncDotNet.S100.Rendering.Tests;

/// <summary>Visual regression tests for S-101 ENC vector rendering.</summary>
public sealed class S101RenderingTests
{
    [SkippableFact]
    public Task EncCell_DayPalette()
    {
        var path = Path.Combine(
            TestHelpers.DatasetsRoot,
            "S101", "S-101", "DATASET_FILES", "101AA0000DS0009.000");
        Skip.IfNot(File.Exists(path), $"S-101 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 800,
            Height = 600,
        });

        return TestHelpers.VerifyBitmap(bitmap);
    }
}
