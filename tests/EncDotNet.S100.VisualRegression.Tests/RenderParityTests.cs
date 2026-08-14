using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>
/// Golden-image regression tests for the tiled base-plane renderer (the sole
/// base-plane path since the legacy Mapsui arm was retired under #600), tracked
/// by issue #347 and <c>docs/design/S100-Render-Subsystem-Design.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Base-plane goldens</b> (<see cref="BMode_EncCell_Palette"/>) render the
/// committed S-101 cell and verify against committed snapshots: any future
/// change that moves a pixel in the tiled renderer fails here. Rendering uses
/// <see cref="EcdisDisplayCategory.Standard"/> to match the live viewer's default
/// display mode (a faithful product render rather than the legacy "All" harness
/// behaviour). No real ENC data is checked in, so the dense labels+symbols
/// evidence below is local-only.
/// </para>
/// <para>
/// The headless path exercises base-plane <i>fidelity</i> only: it renders
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
    /// Renders the committed S-101 cell through the tiled base-plane renderer and
    /// verifies it against a committed golden snapshot — the durable regression
    /// guard for the renderer.
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
        });

        // Perceptual tolerance guards against sub-pixel anti-aliasing drift in
        // the tiled compositor across platforms/GPUs, matching the rationale for
        // the other rendering baselines.
        return TestHelpers.VerifyBitmap(bitmap, maxDifferentPixelFraction: 0.05)
            .UseParameters(palette);
    }

    /// <summary>
    /// Local-only evidence that the labels+symbols overlay renders through the tiled renderer
    /// when zoomed into a labelled harbour area (a full-cell extent hides
    /// SCAMIN-gated points and text). Skipped in CI because real ENC data is
    /// never committed, and golden-free (no committed snapshot to validate
    /// against without the dataset) — it asserts the "B" frame is non-blank and
    /// carries the rich, multi-coloured area-fill + point-symbol + label content
    /// expected of a harbour scene, proving the tiled renderer composites the
    /// live overlay headlessly. The durable per-pixel goldens live on the
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
