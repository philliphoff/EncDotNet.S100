using EncDotNet.S100.Pipelines.Coverage.Pyramid;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the pyramid reducers introduced by the coverage
/// overview pyramid (issue #486). The <see cref="MinReducer"/> tests
/// include the safety invariant called out in the issue: a reduced
/// depth cell must never appear <em>safer</em> (deeper) than any
/// source cell it pools.
/// </summary>
public class PyramidReducerTests
{
    private const float NoData = 1_000_000f;

    // -----------------------------------------------------------------
    // MinReducer — depth (S-102 safety invariant)
    // -----------------------------------------------------------------

    [Fact]
    public void MinReducer_ReturnsShoalestCellInFullWindow()
    {
        float[] window = [3.5f, 7.2f, 4.1f, 2.9f];
        var result = MinReducer.Instance.Reduce(window, NoData);
        Assert.Equal(2.9f, result);
    }

    [Fact]
    public void MinReducer_ExcludesNoDataFromReduction()
    {
        // 4-cell window; NODATA is the shallowest-looking float but must
        // not participate in the reduction.
        float[] window = [3.5f, NoData, 4.1f, 5.0f];
        var result = MinReducer.Instance.Reduce(window, NoData);
        Assert.Equal(3.5f, result);
    }

    [Fact]
    public void MinReducer_AllNoDataWindow_ReturnsNoData()
    {
        float[] window = [NoData, NoData, NoData, NoData];
        var result = MinReducer.Instance.Reduce(window, NoData);
        Assert.Equal(NoData, result);
    }

    [Fact]
    public void MinReducer_TwoCellEdgeWindow_ReturnsShoalest()
    {
        // Trailing column on an odd-width grid: only the two north cells.
        float[] window = [4.0f, 2.5f];
        var result = MinReducer.Instance.Reduce(window, NoData);
        Assert.Equal(2.5f, result);
    }

    [Fact]
    public void MinReducer_SingleCellEdgeWindow_ReturnsThatCell()
    {
        // South-east corner of an odd-sized grid: a 1-cell window.
        float[] window = [7.7f];
        var result = MinReducer.Instance.Reduce(window, NoData);
        Assert.Equal(7.7f, result);
    }

    /// <summary>
    /// Depth safety invariant from issue #486: for every random 4-cell
    /// window with mixed NODATA + valid cells, the reduced value must
    /// be ≤ the minimum valid cell in the window. i.e. downsampling
    /// cannot make an area look <em>safer</em> (deeper) than any base
    /// cell it represents.
    /// </summary>
    [Fact]
    public void MinReducer_NeverShoalerInvariant_HoldsAcrossRandomWindows()
    {
        var rng = new Random(20260727);
        for (int trial = 0; trial < 500; trial++)
        {
            var window = new float[4];
            float minValid = float.PositiveInfinity;
            bool anyValid = false;
            for (int i = 0; i < 4; i++)
            {
                // ~25% chance of NODATA per cell
                if (rng.NextDouble() < 0.25)
                {
                    window[i] = NoData;
                }
                else
                {
                    window[i] = (float)(rng.NextDouble() * 50.0); // 0..50 metres
                    if (window[i] < minValid) minValid = window[i];
                    anyValid = true;
                }
            }
            var reduced = MinReducer.Instance.Reduce(window, NoData);
            if (anyValid)
            {
                Assert.True(reduced <= minValid,
                    $"Reduced depth {reduced} appears safer (deeper) than the shoalest source cell {minValid}. Trial {trial}, window=[{string.Join(",", window)}]");
            }
            else
            {
                Assert.Equal(NoData, reduced);
            }
        }
    }

    // -----------------------------------------------------------------
    // MaxReducer — uncertainty (worst-case pooled confidence)
    // -----------------------------------------------------------------

    [Fact]
    public void MaxReducer_ReturnsLoosestInFullWindow()
    {
        float[] window = [0.1f, 0.4f, 0.25f, 0.05f];
        var result = MaxReducer.Instance.Reduce(window, NoData);
        Assert.Equal(0.4f, result);
    }

    [Fact]
    public void MaxReducer_ExcludesNoData()
    {
        float[] window = [0.1f, NoData, 0.4f, 0.2f];
        var result = MaxReducer.Instance.Reduce(window, NoData);
        Assert.Equal(0.4f, result);
    }

    [Fact]
    public void MaxReducer_AllNoData_ReturnsNoData()
    {
        float[] window = [NoData, NoData, NoData, NoData];
        var result = MaxReducer.Instance.Reduce(window, NoData);
        Assert.Equal(NoData, result);
    }

    // -----------------------------------------------------------------
    // MeanReducer — S-104 waterLevelHeight etc.
    // -----------------------------------------------------------------

    [Fact]
    public void MeanReducer_ReturnsArithmeticMeanExcludingNoData()
    {
        float[] window = [1.0f, 2.0f, 3.0f, NoData];
        var result = MeanReducer.Instance.Reduce(window, NoData);
        Assert.Equal(2.0f, result, precision: 5);
    }

    [Fact]
    public void MeanReducer_AllNoData_ReturnsNoData()
    {
        float[] window = [NoData, NoData];
        var result = MeanReducer.Instance.Reduce(window, NoData);
        Assert.Equal(NoData, result);
    }

    // -----------------------------------------------------------------
    // VectorMeanReducer — S-111 speed/direction (branch cut)
    // -----------------------------------------------------------------

    [Fact]
    public void VectorMeanReducer_HandlesBranchCut_At0Degrees()
    {
        // Two currents flowing due-north-ish: 10° and 350°. Naive
        // arithmetic mean would give 180° (due south!) — a vector mean
        // gives 0° with almost the full speed preserved.
        float[] speeds = [1.0f, 1.0f];
        float[] directions = [10.0f, 350.0f];
        var (s, d) = VectorMeanReducer.Instance.Reduce(speeds, directions, NoData, NoData);

        // 360° ≡ 0° — the mean must be within 1° of north from either side.
        Assert.True(d <= 1.0f || d >= 359.0f,
            $"Expected direction near 0° or 360°, got {d}°");
        // Vector magnitude of two 1.0-magnitude vectors 20° apart is
        // ~0.985 — close to full 1.0 speed, definitely not 0.
        Assert.InRange(s, 0.98f, 1.01f);
    }

    [Fact]
    public void VectorMeanReducer_HandlesBranchCut_At180Degrees()
    {
        // Currents at 170° and 190° should mean to 180°.
        float[] speeds = [2.0f, 2.0f];
        float[] directions = [170.0f, 190.0f];
        var (s, d) = VectorMeanReducer.Instance.Reduce(speeds, directions, NoData, NoData);

        Assert.InRange(d, 179.0f, 181.0f);
        Assert.InRange(s, 1.96f, 2.01f);
    }

    [Fact]
    public void VectorMeanReducer_OpposingCurrents_Cancel()
    {
        // Equal-magnitude currents at 0° and 180° should net to zero
        // speed (the direction result is arbitrary at ‖r‖=0; we only
        // assert the speed).
        float[] speeds = [1.0f, 1.0f];
        float[] directions = [0.0f, 180.0f];
        var (s, _) = VectorMeanReducer.Instance.Reduce(speeds, directions, NoData, NoData);
        Assert.InRange(s, 0.0f, 0.001f);
    }

    [Fact]
    public void VectorMeanReducer_ExcludesNoDataInEitherField()
    {
        // Second sample has NODATA direction → excluded. First sample
        // stands alone.
        float[] speeds = [3.0f, 2.5f];
        float[] directions = [90.0f, NoData];
        var (s, d) = VectorMeanReducer.Instance.Reduce(speeds, directions, NoData, NoData);
        Assert.Equal(3.0f, s, precision: 4);
        Assert.Equal(90.0f, d, precision: 2);
    }

    [Fact]
    public void VectorMeanReducer_AllNoData_ReturnsBothNoData()
    {
        float[] speeds = [NoData, NoData];
        float[] directions = [NoData, NoData];
        var (s, d) = VectorMeanReducer.Instance.Reduce(speeds, directions, NoData, NoData);
        Assert.Equal(NoData, s);
        Assert.Equal(NoData, d);
    }

    [Fact]
    public void VectorMeanReducer_MismatchedLengths_Throws()
    {
        float[] speeds = [1.0f, 2.0f];
        float[] directions = [90.0f];
        Assert.Throws<ArgumentException>(() =>
            VectorMeanReducer.Instance.Reduce(speeds, directions, NoData, NoData));
    }
}
