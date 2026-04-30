using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>Visual regression tests for S-111 surface currents rendering.</summary>
public sealed class S111RenderingTests
{
    [SkippableFact]
    public Task SurfaceCurrents_FirstTimeStep_DayPalette()
    {
        var path = Path.Combine(
            TestHelpers.DatasetsRoot, "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");
        Skip.IfNot(File.Exists(path), $"S-111 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
            TimeStepIndex = 0,
        });

        return TestHelpers.VerifyBitmap(bitmap);
    }
}
