using EncDotNet.S100.Pipelines.Coverage.Pyramid;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="CoveragePyramidBuilder"/>.
/// </summary>
public class CoveragePyramidBuilderTests
{
    private const float NoData = 1_000_000f;
    private const double SpacingLat = 0.001;
    private const double SpacingLon = 0.001;

    private static CoveragePyramid BuildDepthPyramid(
        int rows, int cols, float[] cells, int maxLevels = 6, IPyramidReducer? reducer = null)
    {
        var fields = new Dictionary<string, (float[], IPyramidReducer, float)>
        {
            ["depth"] = (cells, reducer ?? MinReducer.Instance, NoData),
        };
        return CoveragePyramidBuilder.Build(rows, cols, SpacingLat, SpacingLon, fields, maxLevels);
    }

    // -----------------------------------------------------------------
    // Level count / geometry
    // -----------------------------------------------------------------

    [Fact]
    public void Build_EvenDimensions_BuildsFullChainUpToCap()
    {
        var cells = new float[16 * 16];
        Array.Fill(cells, 5.0f);
        var pyr = BuildDepthPyramid(16, 16, cells, maxLevels: 6);

        // 16 → 8 → 4 → 2 → 1 = five levels (0..4)
        Assert.Equal(5, pyr.Levels.Count);
        Assert.Equal((0, 16, 16), (pyr.Levels[0].Level, pyr.Levels[0].Rows, pyr.Levels[0].Cols));
        Assert.Equal((1, 8, 8), (pyr.Levels[1].Level, pyr.Levels[1].Rows, pyr.Levels[1].Cols));
        Assert.Equal((4, 1, 1), (pyr.Levels[4].Level, pyr.Levels[4].Rows, pyr.Levels[4].Cols));
    }

    [Fact]
    public void Build_HonoursMaxLevelsCap()
    {
        var cells = new float[64 * 64];
        Array.Fill(cells, 5.0f);
        var pyr = BuildDepthPyramid(64, 64, cells, maxLevels: 3);

        // Capped at 3 levels total (0, 1, 2)
        Assert.Equal(3, pyr.Levels.Count);
        Assert.Equal((2, 16, 16), (pyr.Levels[2].Level, pyr.Levels[2].Rows, pyr.Levels[2].Cols));
    }

    [Fact]
    public void Build_OddDimensions_UsesCeilingHalving()
    {
        var cells = new float[5 * 7];
        Array.Fill(cells, 5.0f);
        var pyr = BuildDepthPyramid(5, 7, cells, maxLevels: 6);

        // 5→3→2→1, 7→4→2→1: four levels
        Assert.Equal(4, pyr.Levels.Count);
        Assert.Equal((1, 3, 4), (pyr.Levels[1].Level, pyr.Levels[1].Rows, pyr.Levels[1].Cols));
        Assert.Equal((2, 2, 2), (pyr.Levels[2].Level, pyr.Levels[2].Rows, pyr.Levels[2].Cols));
        Assert.Equal((3, 1, 1), (pyr.Levels[3].Level, pyr.Levels[3].Rows, pyr.Levels[3].Cols));
    }

    [Fact]
    public void Build_SpacingDoublesEachLevel()
    {
        var cells = new float[8 * 8];
        Array.Fill(cells, 1.0f);
        var pyr = BuildDepthPyramid(8, 8, cells);

        Assert.Equal(SpacingLat, pyr.Levels[0].SpacingLatitudinal, precision: 9);
        Assert.Equal(SpacingLat * 2.0, pyr.Levels[1].SpacingLatitudinal, precision: 9);
        Assert.Equal(SpacingLat * 4.0, pyr.Levels[2].SpacingLatitudinal, precision: 9);
    }

    // -----------------------------------------------------------------
    // Reducer semantics through the builder
    // -----------------------------------------------------------------

    [Fact]
    public void Build_WithMinReducer_LevelOne_PoolsShallowestPerWindow()
    {
        // 4×4 grid with distinct depths per 2×2 window so we can predict
        // the level-1 values.
        float[] cells =
        [
            10f, 20f, 30f, 40f,
            50f, 60f, 70f, 80f,
            15f, 25f, 35f, 45f,
            55f, 65f, 75f, 85f,
        ];
        var pyr = BuildDepthPyramid(4, 4, cells);

        var lvl1 = pyr.GetField(1, "depth");
        Assert.Equal(2 * 2, lvl1.Length);
        // NW 2×2 → min(10,20,50,60) = 10
        Assert.Equal(10f, lvl1[0]);
        // NE 2×2 → min(30,40,70,80) = 30
        Assert.Equal(30f, lvl1[1]);
        // SW 2×2 → min(15,25,55,65) = 15
        Assert.Equal(15f, lvl1[2]);
        // SE 2×2 → min(35,45,75,85) = 35
        Assert.Equal(35f, lvl1[3]);
    }

    [Fact]
    public void Build_AllNoDataInWindow_PropagatesNoDataToCoarseLevel()
    {
        // 4×4 grid where the NE 2×2 window is entirely NODATA; other
        // windows have valid values.
        float[] cells =
        [
            10f, 20f, NoData, NoData,
            50f, 60f, NoData, NoData,
            15f, 25f, 35f, 45f,
            55f, 65f, 75f, 85f,
        ];
        var pyr = BuildDepthPyramid(4, 4, cells);

        var lvl1 = pyr.GetField(1, "depth");
        Assert.Equal(10f, lvl1[0]);
        Assert.Equal(NoData, lvl1[1]);
        Assert.Equal(15f, lvl1[2]);
        Assert.Equal(35f, lvl1[3]);
    }

    [Fact]
    public void Build_OddDimensions_TrailingEdgeUsesShortWindow()
    {
        // 3×3 grid → 2×2 level-1. Trailing row/col are 1-cell strips.
        float[] cells =
        [
            10f, 20f, 100f,
            30f, 40f, 200f,
            50f, 60f, 300f,
        ];
        var pyr = BuildDepthPyramid(3, 3, cells);

        var lvl1 = pyr.GetField(1, "depth");
        Assert.Equal(2 * 2, lvl1.Length);
        // NW 2×2 → min(10,20,30,40) = 10
        Assert.Equal(10f, lvl1[0]);
        // NE column strip (2 cells) → min(100,200) = 100
        Assert.Equal(100f, lvl1[1]);
        // SW row strip (2 cells) → min(50,60) = 50
        Assert.Equal(50f, lvl1[2]);
        // SE corner (1 cell) → 300
        Assert.Equal(300f, lvl1[3]);
    }

    // -----------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------

    [Fact]
    public void Build_MismatchedCellCount_Throws()
    {
        var cells = new float[10]; // Not 4*4
        var fields = new Dictionary<string, (float[], IPyramidReducer, float)>
        {
            ["depth"] = (cells, MinReducer.Instance, NoData),
        };
        Assert.Throws<ArgumentException>(() =>
            CoveragePyramidBuilder.Build(4, 4, SpacingLat, SpacingLon, fields));
    }

    [Fact]
    public void Build_EmptyFields_Throws()
    {
        var fields = new Dictionary<string, (float[], IPyramidReducer, float)>();
        Assert.Throws<ArgumentException>(() =>
            CoveragePyramidBuilder.Build(4, 4, SpacingLat, SpacingLon, fields));
    }

    [Fact]
    public void Build_MaxLevelsZero_Throws()
    {
        var cells = new float[16];
        var fields = new Dictionary<string, (float[], IPyramidReducer, float)>
        {
            ["depth"] = (cells, MinReducer.Instance, NoData),
        };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CoveragePyramidBuilder.Build(4, 4, SpacingLat, SpacingLon, fields, maxLevels: 0));
    }
}
