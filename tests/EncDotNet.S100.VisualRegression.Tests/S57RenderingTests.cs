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
        // portrayal stack. The committed baseline is the deterministic
        // osx-arm64 render (the only byte-identical leg; see below). It was
        // re-baked once CATZOC began mapping to the QualityOfBathymetricData
        // `zoneOfConfidence` complex, which replaces the earlier fallback
        // quality-of-data hatch with the proper zone-of-confidence symbology —
        // a legitimate portrayal improvement, not a rendering regression.
        //
        // The render is BIMODAL on every non-macOS leg. #224 (NoDependencies
        // SkiaSharp native + embedded font) put all non-macOS legs onto a native
        // that dispatches on runtime CPU features, so the SAME code on the SAME
        // OS/arch emits one of two renders depending on the runner
        // microarchitecture: the "faithful" variant (≈2.4% from baseline) or the
        // "SIMD" raster-path variant. A single linux-arm64 runner has been
        // observed producing EITHER variant on different runs (see #294).
        //
        // Issue #294 evidence: the two variants are 40759/480000 px = 8.49% apart
        // (this PR's denser sounding labels widened the historical ~5.93% gap).
        // Because a single runner is non-deterministic, no static per-platform
        // baseline can reliably pass. The honest stopgap is therefore a SINGLE
        // baseline (the faithful render) with the non-macOS tolerance raised just
        // above the variant gap so it absorbs BOTH variants; osx-arm64 — the only
        // byte-identical render — stays strict at 5%.
        //
        // This is a documented STOPGAP, not permanent erosion: #294 tracks the
        // real fix (deterministic Skia SIMD path, or a multi-acceptable-baseline
        // comparer) after which the non-macOS bound returns to strict 5%. Do NOT
        // "tidy" osx-arm64 onto the relaxed bound — it must stay strict.
        var relax = !OperatingSystem.IsMacOS();
        var maxDifferentPixelFraction = relax ? 0.10 : 0.05;
        var settings = TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction);
        return settings;
    }
}
