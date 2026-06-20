using System.Linq;
using EncDotNet.S100.Renderers.Mapsui;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for the pure, origin-anchored EPSG:3857 tile-grid math
/// (<see cref="TileGrid"/>) backing the Phase&#160;2 tiled base plane. These pin
/// the invariants the compositor relies on: band/resolution round-trips,
/// world-bounds tiling, world→screen projection, and — most importantly — the
/// pan-stability of interior tile keys that makes pan cost perimeter-bounded.
/// </summary>
public class TileGridTests
{
    [Fact]
    public void Band0Resolution_MatchesWebMercatorTopLevel()
    {
        // 2 * π * R / 256 ≈ 156543.034 m/px is the canonical web-mercator band-0
        // resolution; pin it so the pyramid stays aligned with standard schemes.
        Assert.Equal(156543.03392, TileGrid.Band0Resolution, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(20)]
    public void BandForResolution_RoundTripsCanonicalBandResolution(int band)
    {
        var resolution = TileGrid.ResolutionForBand(band);
        Assert.Equal(band, TileGrid.BandForResolution(resolution));
    }

    [Fact]
    public void BandForResolution_SnapsInLogSpaceAndClamps()
    {
        // Just inside an octave rounds to the nearer band.
        var band = 8;
        var res = TileGrid.ResolutionForBand(band);
        Assert.Equal(band, TileGrid.BandForResolution(res * 1.2));
        Assert.Equal(band, TileGrid.BandForResolution(res * 0.85));

        // Out-of-range / degenerate inputs clamp.
        Assert.Equal(TileGrid.MinBand, TileGrid.BandForResolution(double.PositiveInfinity));
        Assert.Equal(TileGrid.MinBand, TileGrid.BandForResolution(0));
        Assert.Equal(TileGrid.MaxBand, TileGrid.BandForResolution(1e-9));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 8)]
    [InlineData(10, 1024)]
    public void TilesPerAxis_IsPowerOfTwo(int band, int expected)
    {
        Assert.Equal(expected, TileGrid.TilesPerAxis(band));
    }

    [Fact]
    public void TileWorldBounds_TilesAtBand0CoverWholeWorld()
    {
        // Band 0 is a single tile spanning [-Extent, +Extent] on both axes.
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(new TileKey(0, 0, 0));
        Assert.Equal(-TileGrid.Extent, minX, 3);
        Assert.Equal(-TileGrid.Extent, minY, 3);
        Assert.Equal(TileGrid.Extent, maxX, 3);
        Assert.Equal(TileGrid.Extent, maxY, 3);
    }

    [Fact]
    public void TileWorldBounds_TopLeftTileIsNorthWestCorner()
    {
        // XYZ convention: X=0,Y=0 is the north-west tile.
        var size = TileGrid.TileWorldSize(2);
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(new TileKey(2, 0, 0));
        Assert.Equal(-TileGrid.Extent, minX, 3);
        Assert.Equal(TileGrid.Extent, maxY, 3);
        Assert.Equal(-TileGrid.Extent + size, maxX, 3);
        Assert.Equal(TileGrid.Extent - size, minY, 3);
    }

    [Fact]
    public void TileWorldBounds_AdjacentTilesTileWithoutGapOrOverlap()
    {
        var a = TileGrid.TileWorldBounds(new TileKey(4, 3, 5));
        var b = TileGrid.TileWorldBounds(new TileKey(4, 4, 5));
        // Right edge of (3,5) equals left edge of (4,5).
        Assert.Equal(a.MaxX, b.MinX, 6);
        Assert.Equal(a.MinY, b.MinY, 6);
        Assert.Equal(a.MaxY, b.MaxY, 6);
    }

    [Fact]
    public void VisibleTiles_CoversViewportAndClampsToWorld()
    {
        // A viewport at the world origin, band 2 (4x4 grid). The centre falls on
        // the seam of the four central tiles, so a small viewport touches them.
        var band = 2;
        var res = TileGrid.ResolutionForBand(band);
        var tiles = TileGrid.VisibleTiles(0, 0, 256, 256, res, band);
        Assert.NotEmpty(tiles);
        Assert.All(tiles, t =>
        {
            Assert.Equal(band, t.Band);
            Assert.InRange(t.X, 0, TileGrid.TilesPerAxis(band) - 1);
            Assert.InRange(t.Y, 0, TileGrid.TilesPerAxis(band) - 1);
        });
    }

    [Fact]
    public void VisibleTiles_NeverEmitsOutOfRangeKeysAtWorldEdge()
    {
        // Viewport hard against the north-west corner; indices must clamp to 0.
        var band = 3;
        var res = TileGrid.ResolutionForBand(band);
        var tiles = TileGrid.VisibleTiles(-TileGrid.Extent, TileGrid.Extent, 512, 512, res, band);
        Assert.All(tiles, t =>
        {
            Assert.InRange(t.X, 0, TileGrid.TilesPerAxis(band) - 1);
            Assert.InRange(t.Y, 0, TileGrid.TilesPerAxis(band) - 1);
        });
        Assert.Contains(new TileKey(band, 0, 0), tiles);
    }

    [Fact]
    public void VisibleTiles_DegenerateViewportYieldsNoTiles()
    {
        Assert.Empty(TileGrid.VisibleTiles(0, 0, 0, 500, 100, 5));
        Assert.Empty(TileGrid.VisibleTiles(0, 0, 500, 0, 100, 5));
        Assert.Empty(TileGrid.VisibleTiles(0, 0, 500, 500, 0, 5));
    }

    [Fact]
    public void VisibleTiles_InteriorKeysAreStableUnderConstantZoomPan()
    {
        // This is the core property: panning at a fixed zoom must re-use the
        // same interior tile keys, so only newly-exposed perimeter tiles are new.
        var band = 8;
        var res = TileGrid.ResolutionForBand(band);
        const double w = 1600, h = 1000;

        var before = TileGrid.VisibleTiles(0, 0, w, h, res, band).ToHashSet();
        // Pan east by half a tile (less than one tile, so the set overlaps).
        var dx = TileGrid.TileWorldSize(band) * 0.5;
        var after = TileGrid.VisibleTiles(dx, 0, w, h, res, band).ToHashSet();

        var shared = before.Intersect(after).ToList();
        Assert.NotEmpty(shared);
        // Most tiles are shared (only the leading/trailing column changes).
        Assert.True(shared.Count > before.Count / 2,
            $"expected majority of {before.Count} tiles re-used, shared={shared.Count}");
    }

    [Fact]
    public void WorldToScreenRect_CentreWorldMapsToViewportCentre()
    {
        const double w = 800, h = 600, res = 100;
        const double cx = 1_000_000, cy = 2_000_000;
        // A 1px world box at the centre projects to the viewport centre.
        var rect = TileGrid.WorldToScreenRect(cx, cy, cx, cy, cx, cy, w, h, res);
        Assert.Equal(w / 2, rect.Left, 6);
        Assert.Equal(h / 2, rect.Top, 6);
    }

    [Fact]
    public void WorldToScreenRect_IsYInverted()
    {
        const double w = 800, h = 600, res = 100;
        // A world box north of centre (greater Y) lands above the centre (smaller screen Y).
        var rect = TileGrid.WorldToScreenRect(0, res * 100, res, res * 200, 0, 0, w, h, res);
        Assert.True(rect.Top < h / 2);
        Assert.True(rect.Bottom < h / 2);
    }

    [Fact]
    public void TileCoreScreenRect_TileSizeMatchesBandScaleAtNativeResolution()
    {
        // Rendered at the band's own resolution, a 256-DIP tile spans 256 screen DIP.
        var band = 6;
        var res = TileGrid.ResolutionForBand(band);
        var key = new TileKey(band, 10, 12);
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var rect = TileGrid.TileCoreScreenRect(key, (minX + maxX) / 2, (minY + maxY) / 2, 1024, 1024, res);
        Assert.Equal(TileGrid.TileSizeDip, rect.Width, 3);
        Assert.Equal(TileGrid.TileSizeDip, rect.Height, 3);
    }

    [Fact]
    public void ScreenRect_IntersectsViewport_HalfOpenBox()
    {
        Assert.True(new ScreenRect(10, 10, 20, 20).IntersectsViewport(100, 100));
        Assert.False(new ScreenRect(-30, 10, -10, 20).IntersectsViewport(100, 100));
        Assert.False(new ScreenRect(110, 10, 130, 20).IntersectsViewport(100, 100));
    }
}
