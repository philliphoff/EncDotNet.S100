using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the centre-first tile order (<see cref="S100VectorTileRenderer.TileUrgency"/>).
/// Each tile job's urgency within its class is its on-screen distance from the
/// view centre, so the work scheduler rasterises central tiles of every layer
/// before the perimeter. Visible-before-predicted is the job's class and is
/// unaffected.
/// </summary>
public class TileCenterPriorityTests
{
    private const int Band = 10;

    /// <summary>The EPSG:3857 centre of a tile, matching TileGrid's bounds math.</summary>
    private static (double X, double Y) TileCenter(TileKey key)
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        return ((minX + maxX) * 0.5, (minY + maxY) * 0.5);
    }

    [Fact]
    public void TileUrgency_IsZeroAtTheCentreTile()
    {
        var centre = new TileKey(Band, 200, 200);
        var (cx, cy) = TileCenter(centre);

        Assert.Equal(0, S100VectorTileRenderer.TileUrgency(centre, cx, cy, TileGrid.ResolutionForBand(Band)), 6);
    }

    [Fact]
    public void TileUrgency_OrdersABlockCentreOut()
    {
        var centreTile = new TileKey(Band, 200, 200);
        var (cx, cy) = TileCenter(centreTile);
        var resolution = TileGrid.ResolutionForBand(Band);
        var tiles = new List<TileKey>();
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                tiles.Add(new TileKey(Band, 200 + dx, 200 + dy));
            }
        }

        var ordered = tiles.OrderBy(k => S100VectorTileRenderer.TileUrgency(k, cx, cy, resolution)).ToList();

        Assert.Equal(centreTile, ordered[0]);
        var ring = (TileKey k) => Math.Max(Math.Abs(k.X - 200), Math.Abs(k.Y - 200));
        Assert.All(ordered.Take(9), k => Assert.True(ring(k) <= 1, $"{k} is outside the inner ring"));
    }

    [Fact]
    public void TileUrgency_IsInPixelsSoBandsCompare()
    {
        // One tile away at the band's own resolution is one tile size in pixels,
        // whichever band: a fine layer's neighbour doesn't outrank a coarse one's.
        var fine = new TileKey(Band + 2, 800, 800);
        var coarse = new TileKey(Band, 200, 200);
        var (fx, fy) = TileCenter(fine);
        var (cx, cy) = TileCenter(coarse);

        var fineNeighbour = S100VectorTileRenderer.TileUrgency(fine with { X = 801 }, fx, fy, TileGrid.ResolutionForBand(Band + 2));
        var coarseNeighbour = S100VectorTileRenderer.TileUrgency(coarse with { X = 201 }, cx, cy, TileGrid.ResolutionForBand(Band));

        Assert.Equal(coarseNeighbour, fineNeighbour, 3);
    }
}
