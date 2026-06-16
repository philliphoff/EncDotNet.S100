using EncDotNet.S100.VisualRegression;
using VerifyTests;

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
        //   osx-arm64               : 2.383% (byte-identical render)
        //   linux-x64 / win-x64     : bimodal — ~2.383% on most runners, but
        //                             ~5.93% on runners whose CPU exposes the
        //                             NEON-equivalent SIMD raster path (#224)
        //   linux-arm64 / win-arm64 : 5.93%  (arm64 Skia AA/raster path)
        //
        // #177's principle: relax ONLY where the platform genuinely diverges from
        // the baseline; keep strict where the render is faithful. Pre-#224 only
        // win-arm64 diverged. #224 (NoDependencies SkiaSharp native + embedded
        // font) switched EVERY non-macOS leg onto that native, which dispatches on
        // runtime CPU features — so linux-x64, win-x64, and linux-arm64 are now
        // CPU-SIMD-heterogeneous too: the SAME code on the SAME OS/arch produces
        // either the faithful ~2.383% render or the arm64-identical 5.93% render
        // depending on the runner microarchitecture. Proof: #226's linux-x64 build
        // job failed EncCell_DayPalette at 28463/480000 px — the EXACT pixel count
        // the arm64 legs produce — while main's linux-x64 build passed the same
        // commit on a different runner. The two variants are ~5.93% apart, so a
        // single committed baseline cannot satisfy both (rebaselining to one makes
        // the other fail).
        //
        // So relax to 8% on ALL non-macOS legs and keep strict 5% on osx-arm64,
        // the only byte-identical render. This APPLIES #177's principle to #224's
        // new reality — it does NOT collapse to a global 0.08 (#186's mistake),
        // because osx-arm64 stays strict. 8% on the non-mac legs is a documented
        // STOPGAP, not permanent erosion: issue #228 tracks making the VR render
        // deterministic (pin the Skia SIMD path / per-variant baselines) so the
        // bound can return to strict 5% everywhere.
        //
        // DELIBERATE per-RID tolerance — do NOT "tidy" this back to a single
        // global 0.08: osx-arm64 stays strict because it is the only byte-identical
        // render; the non-mac legs are heterogeneous under #224's NoDeps native
        // (issues #177, #224, #228).
        var relax = !OperatingSystem.IsMacOS();
        var maxDifferentPixelFraction = relax ? 0.08 : 0.05;
        var settings = TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction);
        return UsePlatformBaseline(settings);
    }

    private static SettingsTask UsePlatformBaseline(SettingsTask settings)
    {
        // PR #292 intentionally moves multipoint sounding glyphs from a shared
        // anchor to their distinct sounding positions. The non-macOS arm64 Skia
        // raster path now legitimately diverges from the default baseline by
        // more than the documented anti-aliasing tolerance, so keep a dedicated
        // baseline for that platform variant instead of weakening the check.
        if (!OperatingSystem.IsMacOS()
            && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
        {
            return settings.UseFileName("S57RenderingTests.EncCell_DayPalette.non-macos-arm64");
        }

        return settings;
    }
}
