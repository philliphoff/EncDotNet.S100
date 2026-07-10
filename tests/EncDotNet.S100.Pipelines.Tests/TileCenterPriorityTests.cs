using System.Collections.Generic;
using EncDotNet.S100.Renderers.Mapsui;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the centre-priority tile dequeue
/// (<see cref="S100VectorTileRenderer.TakeNearest"/>). These pin the properties
/// the worker relies on when draining a priority tier: the tile nearest the
/// viewport centre is picked first, the pick is removed from the pending set,
/// ties are broken deterministically (independent of hash iteration order), and
/// draining the whole set yields a centre-out ordering. Visible-before-predicted
/// tiering is the caller's concern and is unaffected.
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
    public void TakeNearest_EmptySetReturnsDefault()
    {
        var pending = new HashSet<TileKey>();
        Assert.Equal(default, S100VectorTileRenderer.TakeNearest(pending, 0, 0));
        Assert.Empty(pending);
    }

    [Fact]
    public void TakeNearest_PicksTileNearestViewportCentre()
    {
        var near = new TileKey(Band, 100, 100);
        var far = new TileKey(Band, 140, 100);
        var farther = new TileKey(Band, 180, 60);
        var pending = new HashSet<TileKey> { far, farther, near };

        var (cx, cy) = TileCenter(near);
        var picked = S100VectorTileRenderer.TakeNearest(pending, cx, cy);

        Assert.Equal(near, picked);
    }

    [Fact]
    public void TakeNearest_RemovesReturnedTile()
    {
        var a = new TileKey(Band, 10, 10);
        var b = new TileKey(Band, 11, 10);
        var pending = new HashSet<TileKey> { a, b };

        var (cx, cy) = TileCenter(a);
        var picked = S100VectorTileRenderer.TakeNearest(pending, cx, cy);

        Assert.Equal(a, picked);
        Assert.DoesNotContain(a, pending);
        Assert.Contains(b, pending);
        Assert.Single(pending);
    }

    [Fact]
    public void TakeNearest_BreaksEqualDistanceTiesDeterministically()
    {
        // Two tiles equidistant from a centre exactly between them: the tie-break
        // is (Band, Y, X), so the lower (Y, X) wins regardless of set order.
        var left = new TileKey(Band, 50, 50);
        var right = new TileKey(Band, 51, 50);
        var size = TileGrid.TileWorldSize(Band);
        var (lx, _) = TileCenter(left);
        var (_, ly) = TileCenter(left);
        var midX = lx + size * 0.5; // exactly on the shared edge → equal distance

        var forward = S100VectorTileRenderer.TakeNearest(
            new HashSet<TileKey> { left, right }, midX, ly);
        var reversed = S100VectorTileRenderer.TakeNearest(
            new HashSet<TileKey> { right, left }, midX, ly);

        Assert.Equal(left, forward);
        Assert.Equal(left, reversed);
    }

    [Fact]
    public void TakeNearest_DrainingYieldsCentreOutOrdering()
    {
        // A 5x5 block of tiles centred on (cx, cy); draining one at a time must
        // produce non-decreasing distance from the centre (centre tile first,
        // corners last).
        var centreTile = new TileKey(Band, 200, 200);
        var (cx, cy) = TileCenter(centreTile);
        var pending = new HashSet<TileKey>();
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                pending.Add(new TileKey(Band, 200 + dx, 200 + dy));
            }
        }

        double DistSq(TileKey k)
        {
            var (tx, ty) = TileCenter(k);
            return (tx - cx) * (tx - cx) + (ty - cy) * (ty - cy);
        }

        var first = S100VectorTileRenderer.TakeNearest(pending, cx, cy);
        Assert.Equal(centreTile, first);

        var previous = DistSq(first);
        while (pending.Count > 0)
        {
            var next = S100VectorTileRenderer.TakeNearest(pending, cx, cy);
            var d = DistSq(next);
            Assert.True(d >= previous, $"drain order must be non-decreasing distance: {previous} -> {d}");
            previous = d;
        }
    }
}
