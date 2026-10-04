using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>Visual regression tests for S-111 surface currents rendering.</summary>
public sealed class S111RenderingTests
{
    [Fact]
    public Task SurfaceCurrents_FirstTimeStep_DayPalette()
    {
        var path = Path.Combine(
            TestHelpers.DatasetsRoot, "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");
        Assert.SkipUnless(File.Exists(path), $"S-111 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
            TimeStepIndex = 0,
        });

        // Sparse render (5 arrows, ink ~325 px): hold it to a share of its own
        // ink rather than the default 5 % of the image (18 000 px). The arrows
        // render identically on macOS, linux-arm64 and linux-x64, so one
        // missing or moved arrow (~65 px) fails either limit.
        return TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 280, maxDifferentInkFraction: 0.1);
    }

    [Fact]
    public Task SurfaceCurrents_FirstTimeStep_NightPalette()
    {
        var path = Path.Combine(
            TestHelpers.DatasetsRoot, "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");
        Assert.SkipUnless(File.Exists(path), $"S-111 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 600,
            Height = 600,
            TimeStepIndex = 0,
            Palette = PaletteType.Night,
        });

        // Sparse render (5 arrows, ink ~325 px): hold it to a share of its own
        // ink rather than the default 5 % of the image (18 000 px). The arrows
        // render identically on macOS, linux-arm64 and linux-x64, so one
        // missing or moved arrow (~65 px) fails either limit.
        return TestHelpers.VerifySparseBitmap(bitmap, minimumInkPixels: 280, maxDifferentInkFraction: 0.1);
    }
}
