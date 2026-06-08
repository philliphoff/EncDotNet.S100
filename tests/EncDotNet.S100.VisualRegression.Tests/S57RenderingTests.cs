using System.Runtime.InteropServices;
using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Visual regression tests for S-57 ENC rendering. S-57 datasets are translated
/// in-memory into <c>S101Document</c> graphs and rendered through the existing
/// S-101 portrayal pipeline (see <c>EncDotNet.S100.Datasets.S57</c>); this test
/// is therefore an end-to-end check on both the translator and the S-101
/// rendering stack as exercised on legacy data.
/// </summary>
public sealed class S57RenderingTests
{
    [SkippableFact]
    public Task EncCell_DayPalette()
    {
        var path = Path.Combine(
            TestHelpers.DatasetsRoot,
            "S57", "US5MA1BO", "US5MA1BO.000");
        Skip.IfNot(File.Exists(path), $"S-57 test dataset not present: {path}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(path, new HarnessOptions
        {
            Width = 800,
            Height = 600,
        });

        // S-57 is translated in-memory to S-101 and rendered through the full
        // portrayal stack. The committed baseline was baked on win-x64. Measured
        // differences vs that baseline are confined to the anti-aliased fringe of
        // thin vector strokes (depth-contour lines and the diagonal
        // quality-of-data hatching) — geometry, colour, and symbology are
        // identical, and the worst per-channel delta is a partial-coverage ~47.
        //
        //   linux-x64 / osx-arm64 : 2.383% (byte-identical renders)
        //   win-arm64             : 5.93%  (different Skia AA/raster path)
        //
        // Keep the strict 5% bound everywhere — it preserves the test's power on
        // the platforms that actually run it in CI (linux-x64 has ~2.6% of
        // headroom) — and relax to 8% ONLY on win-arm64, whose heterogeneous
        // runner rasterisation lands ~5.93% off the baseline (issue #177). We use
        // ProcessArchitecture (not OSArchitecture) so an x64 process emulated on
        // an arm64 OS is not over-relaxed.
        //
        // DELIBERATE per-RID tolerance — do NOT "tidy" this back to a single
        // global 0.08 (that is what #186 did and it weakened the test on every
        // platform; #177 restores the strict 5% everywhere except win-arm64).
        var maxDifferentPixelFraction =
            OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? 0.08
                : 0.05;
        return TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction);
    }
}
