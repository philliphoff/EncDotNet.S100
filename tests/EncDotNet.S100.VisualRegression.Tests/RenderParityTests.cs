using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Golden-image parity tests for the tiled async "B" base-plane renderer
/// (<see cref="RenderSubsystemKind.TiledScene"/>) tracked by issue #347 and
/// <c>docs/design/S100-Render-Subsystem-Design.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two durable CI gates run against the committed S-101 cell (no real ENC data
/// is checked in, so the dense labels+symbols evidence below is local-only):
/// </para>
/// <list type="number">
///   <item><b>B-arm goldens</b> (<see cref="BMode_EncCell_Palette"/>) — render
///         the committed cell through the "B" subsystem and verify against
///         committed snapshots. This is the regression guard for "B": any future
///         change that moves a pixel in the tiled renderer fails here.</item>
///   <item><b>A/B close-match</b> (<see cref="AbParity_EncCell_Palette"/>) —
///         render the same cell through both "A" (Mapsui per-feature) and "B"
///         and assert the two frames match within the perceptual tolerance.
///         Per the #347 design decision, most datasets are expected to match
///         closely; a divergence beyond tolerance is a <i>failure</i> that
///         surfaces a real fidelity gap in one arm.</item>
/// </list>
/// <para>
/// Both arms render with <see cref="EcdisDisplayCategory.Standard"/> to match
/// the live viewer's default display mode (a faithful product comparison rather
/// than the legacy "All" harness behaviour).
/// </para>
/// <para>
/// <b>Why this is not a perpetual "A == B" equality assertion across all
/// cells:</b> on dense real cells, "B" is known to <i>fix</i> "A" draw-order
/// bugs (e.g. a supplementary depth area flooding the Isle of Wight land area in
/// the Solent trial cell) — there "B" &gt; "A" and an equality gate would
/// produce false failures. The committed cell is a pure area-pattern fill with
/// no such ordering hazard, so it is a stable apples-to-apples close-match
/// fixture; the dense divergence is captured as documented in-viewer evidence,
/// not a unit assertion.
/// </para>
/// <para>
/// The headless "B" path exercises base-plane <i>fidelity</i> only: it renders
/// north-up on a software surface, so viewport rotation uprightness and GPU
/// residency are out of scope here and must be checked with the in-viewer Metal
/// capture recipe documented in <c>README.md</c>.
/// </para>
/// </remarks>
public sealed class RenderParityTests
{
    private static string CommittedCellPath => Path.Combine(
        TestHelpers.DatasetsRoot,
        "S101", "S-101", "DATASET_FILES", "101AA0000DS0009.000");

    /// <summary>
    /// Renders the committed S-101 cell through the "B" (TiledScene) subsystem
    /// and verifies it against a committed B-arm golden snapshot — the durable
    /// regression guard for the tiled renderer.
    /// </summary>
    [SkippableTheory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public Task BMode_EncCell_Palette(PaletteType palette)
    {
        Skip.IfNot(File.Exists(CommittedCellPath), $"S-101 test dataset not present: {CommittedCellPath}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(CommittedCellPath, new HarnessOptions
        {
            Width = 800,
            Height = 600,
            Palette = palette,
            DisplayCategory = EcdisDisplayCategory.Standard,
            RenderSubsystem = RenderSubsystemKind.TiledScene,
        });

        // Perceptual tolerance guards against sub-pixel anti-aliasing drift in
        // the tiled compositor across platforms/GPUs, matching the rationale for
        // the other rendering baselines.
        return TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction: 0.05)
            .UseParameters(palette);
    }

    /// <summary>
    /// Renders the committed S-101 cell through both "A" and "B" and asserts the
    /// two frames match within the perceptual tolerance — the close-match parity
    /// gate. A divergence beyond tolerance fails the test, surfacing a real
    /// fidelity gap in one of the two arms.
    /// </summary>
    [SkippableTheory]
    [InlineData(PaletteType.Day)]
    [InlineData(PaletteType.Dusk)]
    [InlineData(PaletteType.Night)]
    public void AbParity_EncCell_Palette(PaletteType palette)
    {
        Skip.IfNot(File.Exists(CommittedCellPath), $"S-101 test dataset not present: {CommittedCellPath}");

        using var harness = new RenderHarness();
        using var aBitmap = harness.Render(CommittedCellPath, new HarnessOptions
        {
            Width = 800,
            Height = 600,
            Palette = palette,
            DisplayCategory = EcdisDisplayCategory.Standard,
        });
        using var bBitmap = harness.Render(CommittedCellPath, new HarnessOptions
        {
            Width = 800,
            Height = 600,
            Palette = palette,
            DisplayCategory = EcdisDisplayCategory.Standard,
            RenderSubsystem = RenderSubsystemKind.TiledScene,
        });

        var result = PerceptualImageComparer.Default.Compare(
            TestHelpers.EncodePng(aBitmap),
            TestHelpers.EncodePng(bBitmap));

        Assert.True(
            result.AreEqual,
            $"A/B parity ({palette}) diverged beyond tolerance: {result.Reason}");
    }

    /// <summary>
    /// Local-only evidence that the labels+symbols overlay renders through "B"
    /// when zoomed into a labelled harbour area (a full-cell extent hides
    /// SCAMIN-gated points and text). Skipped in CI because real ENC data is
    /// never committed, and golden-free (no committed snapshot to validate
    /// against without the dataset) — it asserts the "B" frame is non-blank and
    /// carries the rich, multi-coloured area-fill + point-symbol + label content
    /// expected of a harbour scene, proving the tiled renderer composites the
    /// live overlay headlessly. The durable per-pixel B-arm goldens live on the
    /// committed cell (<see cref="BMode_EncCell_Palette"/>); the in-viewer Metal
    /// recipe in <c>README.md</c> covers what headless cannot (rotation, GPU).
    /// </summary>
    [SkippableFact]
    public void BMode_DenseCell_LabelsAndSymbols()
    {
        var densePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Complete S10X datasets", "S-101 Trial Cells",
            "101GB00302045", "101GB00GB302045", "101GB00GB302045.000");
        Skip.IfNot(File.Exists(densePath), $"Dense S-101 trial cell not present: {densePath}");

        using var harness = new RenderHarness();
        using var bitmap = harness.Render(densePath, new HarnessOptions
        {
            Width = 1000,
            Height = 800,
            Palette = PaletteType.Day,
            DisplayCategory = EcdisDisplayCategory.Standard,
            RenderSubsystem = RenderSubsystemKind.TiledScene,
            // Frame Portsmouth / Spithead so harbour point symbols and text are
            // above their SCAMIN threshold and actually portrayed.
            Viewport = new GeographicBounds(West: -1.20, South: 50.74, East: -1.00, North: 50.84),
        });

        var (distinctColors, dominantFraction) = SummarizeColors(bitmap);

        // A faithful harbour scene draws land, water and intertidal area fills,
        // depth contours, and a scatter of point symbols and labels — far more
        // than a handful of colours, and no single colour fills the frame.
        Assert.True(distinctColors >= 64, $"Expected a rich labels+symbols frame, saw only {distinctColors} distinct colours.");
        Assert.True(dominantFraction < 0.95, $"Frame is nearly a flat fill ({dominantFraction:P0} one colour) — overlay likely missing.");
    }

    /// <summary>
    /// Returns the number of distinct pixel colours in <paramref name="bitmap"/>
    /// and the fraction of pixels occupied by the single most common colour.
    /// </summary>
    private static (int DistinctColors, double DominantFraction) SummarizeColors(SkiaSharp.SKBitmap bitmap)
    {
        var counts = new Dictionary<uint, int>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                uint c = (uint)bitmap.GetPixel(x, y);
                counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;
            }
        }

        int total = bitmap.Width * bitmap.Height;
        int max = 0;
        foreach (int n in counts.Values)
        {
            if (n > max) max = n;
        }

        return (counts.Count, total == 0 ? 0 : (double)max / total);
    }
}
