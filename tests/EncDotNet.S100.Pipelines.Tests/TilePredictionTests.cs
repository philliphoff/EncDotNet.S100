using EncDotNet.S100.Renderers.Mapsui;

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
    public void PredictedTiles_ClampsYButNotXAtWorldCorner()
    {
        // Viewport hard against the world corner with a fan pointing off-world.
        // The Y (latitude) index is clamped at the poles, but the X (longitude)
        // index is intentionally NOT clamped: EPSG:3857 is periodic east-west and
        // continuous-frame antimeridian data (and a fan pointing past the seam)
        // legitimately produces columns outside [0, perAxis-1]. See
        // TileGrid.VisibleTileRange for the rationale.
        var band = 4;
        var res = TileGrid.ResolutionForBand(band);
        var size = TileGrid.TileWorldSize(band);
        var predicted = TileGrid.PredictedTiles(
            -TileGrid.Extent, TileGrid.Extent, 512, 512, res, band, -size * 10, size * 10);

        Assert.All(predicted, k =>
        {
            var perAxis = TileGrid.TilesPerAxis(k.Band);
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

    [Fact]
    public void NextViewportEpoch_AdvancesOnlyWhenViewportChanges()
    {
        var viewport = new S100VectorTileRenderer.TileViewport(
            CenterX: 10,
            CenterY: 20,
            CoverWidth: 800,
            CoverHeight: 600,
            Resolution: 4,
            DeviceScale: 2);

        var first = S100VectorTileRenderer.NextViewportEpoch(
            currentEpoch: 0,
            previous: null,
            viewport);
        var unchanged = S100VectorTileRenderer.NextViewportEpoch(
            first,
            viewport,
            viewport);
        var moved = S100VectorTileRenderer.NextViewportEpoch(
            unchanged,
            viewport,
            viewport with { CenterX = 11 });

        Assert.Equal(1, first);
        Assert.Equal(first, unchanged);
        Assert.Equal(first + 1, moved);
    }

    [Theory]
    [InlineData(true, true, false, 2)]
    [InlineData(true, false, true, 1)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, true, true, 0)]
    public void ClassifyTileRelevance_PromotesDemotesAndDropsStaleWork(
        bool generationMatches,
        bool isVisible,
        bool isSpeculative,
        int expected)
    {
        Assert.Equal(
            (S100VectorTileRenderer.TileRelevance)expected,
            S100VectorTileRenderer.ClassifyTileRelevance(
                generationMatches,
                isVisible,
                isSpeculative));
    }

    [Theory]
    [InlineData(true, false, true)]   // published visible tile -> repaint
    [InlineData(true, true, false)]   // published predicted (pre-warm) tile -> NO repaint
    [InlineData(false, false, false)] // nothing published -> no repaint
    [InlineData(false, true, false)]  // predicted, not published -> no repaint
    public void ShouldRequestRedraw_RepaintsOnlyForVisiblePublishedTiles(
        bool published, bool isPrediction, bool expected)
    {
        // Regression guard: a predicted (off-screen pre-warm) publish must never
        // request a redraw. If it does, each pre-warm tile triggers a frame that
        // re-runs prediction and re-publishes the next speculative tile — a
        // self-sustaining repaint loop that prevents the map from ever settling
        // (observed as partial zoom-out "rendering never stops" under GPU residency).
        Assert.Equal(expected, S100VectorTileRenderer.ShouldRequestRedraw(published, isPrediction));
    }

    /// <summary>
    /// The world-tile key containing the EPSG:3857 point at <paramref name="band"/>,
    /// using the same flooring convention as <see cref="TileGrid.VisibleTileRange"/>.
    /// </summary>
    private static TileKey TileAt(double wx, double wy, int band)
    {
        var size = TileGrid.TileWorldSize(band);
        var x = (int)System.Math.Floor((wx + TileGrid.Extent) / size);
        var y = (int)System.Math.Floor((TileGrid.Extent - wy) / size);
        return new TileKey(band, x, y);
    }

    [Fact]
    public void PredictedTiles_RotatedViewport_WarmFrameExpandsWithCoverSize()
    {
        // §F.8 regression (issue #347): the prediction frame consumes the same
        // RotatedCoverSize the visible selection does, so the pre-warm ring tracks
        // the rotated footprint rather than the (smaller) north-up box. Mirrors the
        // renderer composition (S100VectorTileRenderer.Render ~lines 604/655). At
        // rest (zero velocity) PredictedTiles is the 1-ring halo around the cover
        // range plus the z±1 centre tiles.
        const int band = 8;
        var res = TileGrid.ResolutionForBand(band);
        const double cx = 100_000, cy = 100_000, wDip = 2560, hDip = 1600, deg = 45;

        TileKey[] Footprint(double w, double h)
        {
            var visible = TileGrid.VisibleTiles(cx, cy, w, h, res, band);
            var predicted = TileGrid.PredictedTiles(cx, cy, w, h, res, band, 0, 0);
            return visible.Concat(predicted).ToArray();
        }

        var raw = Footprint(wDip, hDip).ToHashSet();
        var (coverW, coverH) = TileGrid.RotatedCoverSize(wDip, hDip, deg);
        var cover = Footprint(coverW, coverH).ToHashSet();

        // The total warm+visible footprint grows with rotation and contains the
        // north-up footprint (the rotated frame never warms *fewer* tiles).
        Assert.True(raw.IsSubsetOf(cover), "rotated warm footprint must contain the north-up footprint");
        Assert.True(cover.Count > raw.Count, "rotated warm footprint must add tiles beyond the north-up frame");

        // A probe half a tile beyond the rotated +Y extent is warmed by the rotated
        // prediction halo, but lies outside the north-up footprint entirely — proving
        // the pre-warm frame follows the rotated footprint, not the north-up box.
        var coverHalfHeightWorld = coverH * 0.5 * res;
        var probeY = cy + coverHalfHeightWorld + TileGrid.TileWorldSize(band) * 0.5;
        var probe = TileAt(cx, probeY, band);
        Assert.Contains(probe, cover);
        Assert.DoesNotContain(probe, raw);
    }
}
