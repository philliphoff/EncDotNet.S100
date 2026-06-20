using System.Linq;
using EncDotNet.S100.Renderers.Mapsui;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the Phase&#160;3 prediction math: the velocity EMA
/// (<see cref="VelocityEstimator"/>) and the predicted-tile warm set
/// (<see cref="TileGrid.PredictedTiles"/> / <see cref="TileGrid.VisibleTileRange"/>).
/// These pin the properties the pre-warm relies on: a stable velocity estimate,
/// a halo that surrounds the viewport, a fan that leads the velocity, z±1 bias,
/// and exclusion of visible/out-of-range keys.
/// </summary>
public class TilePredictionTests
{
    [Fact]
    public void VelocityEstimator_ConvergesTowardSteadyInstantVelocity()
    {
        // Constant 100 m per 0.1 s step = 1000 m/s; the EMA should approach it.
        double vx = 0, vy = 0;
        for (var i = 0; i < 50; i++)
        {
            (vx, vy) = VelocityEstimator.Update(vx, vy, 100, 0, 0.1);
        }

        Assert.Equal(1000, vx, 0);
        Assert.Equal(0, vy, 6);
    }

    [Fact]
    public void VelocityEstimator_NonPositiveDtReturnsPreviousUnchanged()
    {
        var (vx, vy) = VelocityEstimator.Update(12.0, -3.0, 999, 999, 0);
        Assert.Equal(12.0, vx);
        Assert.Equal(-3.0, vy);
    }

    [Fact]
    public void VelocityEstimator_BlendsPreviousAndInstantByAlpha()
    {
        // alpha=0.5, prev=(0,0), instant = (10/1, 0) = 10 → result 5.
        var (vx, vy) = VelocityEstimator.Update(0, 0, 10, 0, 1.0, alpha: 0.5);
        Assert.Equal(5, vx, 6);
        Assert.Equal(0, vy, 6);
    }

    [Fact]
    public void VisibleTileRange_RoundTripsVisibleTiles()
    {
        var band = 6;
        var res = TileGrid.ResolutionForBand(band);
        var range = TileGrid.VisibleTileRange(0, 0, 800, 600, res, band);
        Assert.False(range.IsEmpty);

        var expected = TileGrid.VisibleTiles(0, 0, 800, 600, res, band).Count;
        var actual = (range.XEnd - range.XStart + 1) * (range.YEnd - range.YStart + 1);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void VisibleTileRange_DegenerateViewportIsEmpty()
    {
        Assert.True(TileGrid.VisibleTileRange(0, 0, 0, 600, 100, 5).IsEmpty);
        Assert.True(TileGrid.VisibleTileRange(0, 0, 800, 0, 100, 5).IsEmpty);
    }

    [Fact]
    public void PredictedTiles_ExcludesVisibleTiles()
    {
        var band = 8;
        var res = TileGrid.ResolutionForBand(band);
        const double w = 1024, h = 768;
        var visible = TileGrid.VisibleTiles(0, 0, w, h, res, band).ToHashSet();
        var predicted = TileGrid.PredictedTiles(0, 0, w, h, res, band, 0, 0);

        Assert.DoesNotContain(predicted, visible.Contains);
    }

    [Fact]
    public void PredictedTiles_AtRestIncludesOneRingHalo()
    {
        var band = 8;
        var res = TileGrid.ResolutionForBand(band);
        const double w = 1024, h = 768;
        var visible = TileGrid.VisibleTileRange(0, 0, w, h, res, band);
        var predicted = TileGrid.PredictedTiles(0, 0, w, h, res, band, 0, 0, haloRings: 1);

        // The tile just left of the visible range's left edge, on a visible row,
        // is part of the 1-ring halo.
        var haloKey = new TileKey(band, visible.XStart - 1, visible.YStart);
        Assert.Contains(haloKey, predicted);
    }

    [Fact]
    public void PredictedTiles_FanLeadsTheVelocityDirection()
    {
        var band = 10;
        var res = TileGrid.ResolutionForBand(band);
        const double w = 1024, h = 768;
        var size = TileGrid.TileWorldSize(band);

        // Strong eastward velocity: the warm set must reach further east than a
        // 1-ring halo would (a fan tile several tiles to the east).
        var velX = size * 6; // 6 tiles/sec → lookAhead 0.5s ⇒ depth ~3 (capped 4)
        var predicted = TileGrid.PredictedTiles(0, 0, w, h, res, band, velX, 0);

        var visible = TileGrid.VisibleTileRange(0, 0, w, h, res, band);
        var maxX = predicted.Where(k => k.Band == band).Max(k => k.X);
        Assert.True(maxX > visible.XEnd + 1,
            $"fan should lead east past the halo: maxX={maxX}, visibleXEnd={visible.XEnd}");
    }

    [Fact]
    public void PredictedTiles_FanDepthGrowsWithSpeedAndCaps()
    {
        var band = 10;
        var res = TileGrid.ResolutionForBand(band);
        const double w = 512, h = 512;
        var size = TileGrid.TileWorldSize(band);
        var visible = TileGrid.VisibleTileRange(0, 0, w, h, res, band);

        int Reach(double tilesPerSec)
        {
            var p = TileGrid.PredictedTiles(0, 0, w, h, res, band, size * tilesPerSec, 0, maxFanDepth: 4);
            return p.Where(k => k.Band == band).Max(k => k.X) - visible.XEnd;
        }

        var slow = Reach(2);
        var fast = Reach(8);
        Assert.True(fast >= slow, $"faster pan should reach at least as far: slow={slow}, fast={fast}");
        // Capped at maxFanDepth (4) plus the halo ring (1).
        Assert.True(Reach(100) <= 4 + 1 + 1);
    }

    [Fact]
    public void PredictedTiles_IncludesZoomNeighbourCenterTiles()
    {
        var band = 8;
        var res = TileGrid.ResolutionForBand(band);
        var predicted = TileGrid.PredictedTiles(0, 0, 800, 600, res, band, 0, 0);

        Assert.Contains(predicted, k => k.Band == band - 1);
        Assert.Contains(predicted, k => k.Band == band + 1);
    }

    [Fact]
    public void PredictedTiles_NeverEmitsOutOfRangeKeys()
    {
        // Viewport hard against the world corner with a fan pointing off-world.
        var band = 4;
        var res = TileGrid.ResolutionForBand(band);
        var size = TileGrid.TileWorldSize(band);
        var predicted = TileGrid.PredictedTiles(
            -TileGrid.Extent, TileGrid.Extent, 512, 512, res, band, -size * 10, size * 10);

        Assert.All(predicted, k =>
        {
            var perAxis = TileGrid.TilesPerAxis(k.Band);
            Assert.InRange(k.X, 0, perAxis - 1);
            Assert.InRange(k.Y, 0, perAxis - 1);
        });
    }

    [Fact]
    public void PredictedTiles_ReturnsNoDuplicates()
    {
        var band = 9;
        var res = TileGrid.ResolutionForBand(band);
        var size = TileGrid.TileWorldSize(band);
        var predicted = TileGrid.PredictedTiles(0, 0, 1200, 900, res, band, size * 3, size * 2);

        Assert.Equal(predicted.Count, predicted.Distinct().Count());
    }

    [Fact]
    public void PredictedTiles_DegenerateViewportYieldsNothing()
    {
        Assert.Empty(TileGrid.PredictedTiles(0, 0, 0, 600, 100, 5, 10, 10));
    }
}
