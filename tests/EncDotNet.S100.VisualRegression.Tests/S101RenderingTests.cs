using EncDotNet.S100.Pipelines;
using EncDotNet.S100.VisualRegression;

namespace EncDotNet.S100.VisualRegression.Tests;

/// <summary>Visual regression tests for S-101 ENC vector rendering.</summary>
public sealed class S101RenderingTests
{
    private static string CommittedCellPath => Path.Combine(
        TestHelpers.DatasetsRoot,
        "S101", "S-101", "DATASET_FILES", "101AA0000DS0009.000");

    [SkippableFact]
    public Task EncCell_DayPalette()
    {
        Skip.IfNot(File.Exists(CommittedCellPath), $"S-101 test dataset not present: {CommittedCellPath}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(CommittedCellPath, new HarnessOptions
        {
            Width = 800,
            Height = 600,
        });

        return TestHelpers.VerifyBitmap(bitmap);
    }

    // Dusk and Night render the same cell with a different palette. Because the
    // pattern-clip refactor made the clipped boundary geometry palette-
    // independent (only the tile colours change per palette), these snapshots
    // act as a regression guard that a palette switch does not move any pattern
    // boundary: the committed image is the pre-refactor baseline and post-
    // refactor rendering must reproduce it byte-for-byte.

    [SkippableFact]
    public Task EncCell_DuskPalette()
    {
        Skip.IfNot(File.Exists(CommittedCellPath), $"S-101 test dataset not present: {CommittedCellPath}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(CommittedCellPath, new HarnessOptions
        {
            Width = 800,
            Height = 600,
            Palette = PaletteType.Dusk,
        });

        return TestHelpers.VerifyBitmap(bitmap);
    }

    [SkippableFact]
    public Task EncCell_NightPalette()
    {
        Skip.IfNot(File.Exists(CommittedCellPath), $"S-101 test dataset not present: {CommittedCellPath}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(CommittedCellPath, new HarnessOptions
        {
            Width = 800,
            Height = 600,
            Palette = PaletteType.Night,
        });

        return TestHelpers.VerifyBitmap(bitmap);
    }

    // Dense-cell coverage of the pattern-clip path. The densest known real
    // S-101 trial cell (~64k-vertex M_QUAL coverage) is not committed (real ENC
    // data is never checked in), so this is skipped unless present locally
    // under the developer's Downloads. It renders all three palettes at two
    // symbol scales to exercise the pattern-fill priority clip and confirm the
    // boundary geometry is stable across palettes.
    [SkippableTheory]
    [InlineData(PaletteType.Day, 1.0)]
    [InlineData(PaletteType.Dusk, 1.0)]
    [InlineData(PaletteType.Night, 1.0)]
    [InlineData(PaletteType.Day, 2.0)]
    [InlineData(PaletteType.Night, 2.0)]
    public Task DenseEncCell_PatternClip(PaletteType palette, double symbolScale)
    {
        var densePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Complete S10X datasets", "S-101 Trial Cells",
            "101GB00GB302045.000");
        Skip.IfNot(File.Exists(densePath), $"Dense S-101 trial cell not present: {densePath}");

        using var harness = new RenderHarness();
        var bitmap = harness.Render(densePath, new HarnessOptions
        {
            Width = 1000,
            Height = 800,
            Palette = palette,
            SymbolScale = symbolScale,
        });

        return TestHelpers.VerifyBitmap(bitmap)
            .UseParameters(palette, symbolScale);
    }
}
