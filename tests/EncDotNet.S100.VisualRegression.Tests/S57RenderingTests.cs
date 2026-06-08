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
        //   linux-x64 / osx-arm64   : 2.383% (byte-identical renders)
        //   linux-arm64 / win-arm64 : 5.93%  (arm64 Skia AA/raster path)
        //
        // Keep the strict 5% bound on the platforms whose render is byte-identical
        // to the baseline — linux-x64 (the build job, ~2.6% of headroom) and
        // osx-arm64 — which preserves the test's power where it counts. Relax to
        // 8% on the arm64 CI runners (linux-arm64 and win-arm64), whose NEON
        // rasterisation lands ~5.93% off the baseline. linux-arm64 joined
        // win-arm64 here once the NoDependencies SkiaSharp native (#224) replaced
        // the previous Linux native build and switched the arm64 leg onto the same
        // divergent AA path (issues #177, #215, #224). We test ProcessArchitecture
        // (not OSArchitecture) so an x64 process emulated on an arm64 OS is not
        // over-relaxed, and exclude macOS, whose arm64 render is byte-identical.
        //
        // DELIBERATE per-RID tolerance — do NOT "tidy" this back to a single
        // global 0.08 (that is what #186 did and it weakened the test on every
        // platform); the strict 5% is retained on linux-x64, win-x64, and
        // osx-arm64.
        var isArm64CiRunner =
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            && !OperatingSystem.IsMacOS();
        var maxDifferentPixelFraction = isArm64CiRunner ? 0.08 : 0.05;
        return TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction);
    }
}
