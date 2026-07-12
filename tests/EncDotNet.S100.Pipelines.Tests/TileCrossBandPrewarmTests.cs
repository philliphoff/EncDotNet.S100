using System.Linq;
using EncDotNet.S100.Renderers.Mapsui;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the idle cross-band pre-warm set
/// (<see cref="TileGrid.CrossBandPrewarmTiles"/>, issue&#160;#428): the
/// band&#160;±&#160;1 tiles covering the current viewport that the renderer warms
/// when otherwise idle so a subsequent zoom starts warm. These pin the
/// properties the renderer relies on: the set covers only the adjacent bands,
/// covers their whole viewport footprint when uncapped, is bounded and
/// centre-first when capped, clamps at the band range ends, and is duplicate-free.
/// </summary>
public class TileCrossBandPrewarmTests
{
    private const double W = 1024, H = 768;

    private static (double X, double Y) TileCenter(TileKey key)
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        return ((minX + maxX) * 0.5, (minY + maxY) * 0.5);
    }

    [Fact]
    public void CrossBandPrewarmTiles_CoversBothAdjacentBandsOnly()
    {
        const int band = 8;
        var res = TileGrid.ResolutionForBand(band);
        var tiles = TileGrid.CrossBandPrewarmTiles(0, 0, W, H, res, band, maxTiles: 1000);

        Assert.NotEmpty(tiles);
        Assert.Contains(tiles, k => k.Band == band - 1);
        Assert.Contains(tiles, k => k.Band == band + 1);
        // Never the current band, and never a band more than one away.
        Assert.All(tiles, k => Assert.InRange(k.Band, band - 1, band + 1));
        Assert.DoesNotContain(tiles, k => k.Band == band);
    }

    [Fact]
    public void CrossBandPrewarmTiles_Uncapped_CoversFullAdjacentBandFootprint()
    {
        const int band = 9;
        var res = TileGrid.ResolutionForBand(band);
        const double cx = 250_000, cy = -120_000;
        var tiles = TileGrid.CrossBandPrewarmTiles(cx, cy, W, H, res, band, maxTiles: 100_000).ToHashSet();

        // The whole viewport footprint of each adjacent band (selected against the
        // same world viewport at the same live resolution) must be present.
        foreach (var neighbour in new[] { band - 1, band + 1 })
        {
            var expected = TileGrid.VisibleTiles(cx, cy, W, H, res, neighbour);
            Assert.All(expected, k => Assert.Contains(k, tiles));
        }
    }

    [Fact]
    public void CrossBandPrewarmTiles_FinerBandHasMoreTilesThanCoarser()
    {
        // band+1 (finer, smaller tiles) covers ~4× the tiles of band-1 (coarser),
        // which is exactly why the centre-first cap matters for the finer band.
        const int band = 7;
        var res = TileGrid.ResolutionForBand(band);
        var tiles = TileGrid.CrossBandPrewarmTiles(0, 0, W, H, res, band, maxTiles: 100_000);

        var finer = tiles.Count(k => k.Band == band + 1);
        var coarser = tiles.Count(k => k.Band == band - 1);
        Assert.True(finer > coarser, $"finer band should have more tiles: finer={finer}, coarser={coarser}");
    }

    [Fact]
    public void CrossBandPrewarmTiles_CapBoundsCountAndKeepsMostCentral()
    {
        const int band = 9;
        var res = TileGrid.ResolutionForBand(band);
        const double cx = 500_000, cy = 300_000;

        var full = TileGrid.CrossBandPrewarmTiles(cx, cy, W, H, res, band, maxTiles: 100_000);
        const int cap = 8;
        var capped = TileGrid.CrossBandPrewarmTiles(cx, cy, W, H, res, band, maxTiles: cap);

        Assert.True(full.Count > cap, "test needs an uncapped set larger than the cap");
        Assert.Equal(cap, capped.Count);

        // The capped set is exactly the `cap` tiles nearest the viewport centre.
        double DistSq(TileKey k)
        {
            var (tx, ty) = TileCenter(k);
            return (tx - cx) * (tx - cx) + (ty - cy) * (ty - cy);
        }

        var expected = full
            .OrderBy(DistSq)
            .ThenBy(k => k.Band).ThenBy(k => k.Y).ThenBy(k => k.X)
            .Take(cap)
            .ToHashSet();
        Assert.Equal(expected, capped.ToHashSet());
    }

    [Fact]
    public void CrossBandPrewarmTiles_IsOrderedCentreFirst()
    {
        const int band = 10;
        var res = TileGrid.ResolutionForBand(band);
        const double cx = 12_345, cy = -67_890;
        var tiles = TileGrid.CrossBandPrewarmTiles(cx, cy, W, H, res, band, maxTiles: 100_000);

        double DistSq(TileKey k)
        {
            var (tx, ty) = TileCenter(k);
            return (tx - cx) * (tx - cx) + (ty - cy) * (ty - cy);
        }

        // Non-decreasing distance from the viewport centre across the whole list.
        for (var i = 1; i < tiles.Count; i++)
        {
            Assert.True(DistSq(tiles[i]) >= DistSq(tiles[i - 1]),
                $"cross-band set must be centre-first: index {i - 1}->{i} distance decreased");
        }
    }

    [Fact]
    public void CrossBandPrewarmTiles_ReturnsNoDuplicates()
    {
        const int band = 8;
        var res = TileGrid.ResolutionForBand(band);
        var tiles = TileGrid.CrossBandPrewarmTiles(100_000, 100_000, W, H, res, band, maxTiles: 100_000);
        Assert.Equal(tiles.Count, tiles.Distinct().Count());
    }

    [Fact]
    public void CrossBandPrewarmTiles_NonPositiveCapIsEmpty()
    {
        var res = TileGrid.ResolutionForBand(8);
        Assert.Empty(TileGrid.CrossBandPrewarmTiles(0, 0, W, H, res, 8, maxTiles: 0));
        Assert.Empty(TileGrid.CrossBandPrewarmTiles(0, 0, W, H, res, 8, maxTiles: -5));
    }

    [Fact]
    public void CrossBandPrewarmTiles_DegenerateViewportIsEmpty()
    {
        var res = TileGrid.ResolutionForBand(8);
        Assert.Empty(TileGrid.CrossBandPrewarmTiles(0, 0, 0, H, res, 8, maxTiles: 100));
        Assert.Empty(TileGrid.CrossBandPrewarmTiles(0, 0, W, 0, res, 8, maxTiles: 100));
    }

    [Fact]
    public void CrossBandPrewarmTiles_ClampsAtMinBand_OnlyWarmsBandPlusOne()
    {
        // At MinBand there is no band-1, so only band+1 is warmed.
        var res = TileGrid.ResolutionForBand(TileGrid.MinBand);
        var tiles = TileGrid.CrossBandPrewarmTiles(0, 0, W, H, res, TileGrid.MinBand, maxTiles: 100_000);

        Assert.NotEmpty(tiles);
        Assert.All(tiles, k => Assert.Equal(TileGrid.MinBand + 1, k.Band));
    }

    [Fact]
    public void CrossBandPrewarmTiles_ClampsAtMaxBand_OnlyWarmsBandMinusOne()
    {
        // At MaxBand there is no band+1, so only band-1 is warmed.
        var res = TileGrid.ResolutionForBand(TileGrid.MaxBand);
        var tiles = TileGrid.CrossBandPrewarmTiles(0, 0, W, H, res, TileGrid.MaxBand, maxTiles: 100_000);

        Assert.NotEmpty(tiles);
        Assert.All(tiles, k => Assert.Equal(TileGrid.MaxBand - 1, k.Band));
    }

    [Fact]
    public void CrossBandPrewarmTiles_ExcludesCurrentVisibleBand()
    {
        // Regression guard: the cross-band set must never re-warm a current-band
        // tile (those are the visible/predicted tiers' responsibility).
        const int band = 8;
        var res = TileGrid.ResolutionForBand(band);
        var visible = TileGrid.VisibleTiles(0, 0, W, H, res, band).ToHashSet();
        var tiles = TileGrid.CrossBandPrewarmTiles(0, 0, W, H, res, band, maxTiles: 100_000);

        Assert.DoesNotContain(tiles, visible.Contains);
    }
}
