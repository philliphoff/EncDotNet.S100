using System.Linq;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
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
    public void VisibleTiles_ClampsYButNotXAtWorldEdge()
    {
        // Viewport hard against the north-west corner. The Y (latitude) index is
        // clamped at the pole, but the X (longitude) index is intentionally NOT
        // clamped: EPSG:3857 is periodic east-west, so a viewport overhanging the
        // west edge legitimately yields negative columns (their TileWorldBounds
        // map to the continuous world position west of -Extent). The origin tile
        // must still be emitted.
        var band = 3;
        var perAxis = TileGrid.TilesPerAxis(band);
        var res = TileGrid.ResolutionForBand(band);
        var tiles = TileGrid.VisibleTiles(-TileGrid.Extent, TileGrid.Extent, 512, 512, res, band);
        Assert.All(tiles, t => Assert.InRange(t.Y, 0, perAxis - 1));
        Assert.Contains(new TileKey(band, 0, 0), tiles);
        // The overhang past the west edge produces at least one negative column.
        Assert.Contains(tiles, t => t.X < 0);
    }

    [Fact]
    public void VisibleTiles_ContinuousAntimeridianFrame_EmitsColumnsBeyondWorld()
    {
        // Regression for the viewer's tiled subsystem drawing a thin ±180° sliver
        // for antimeridian datasets kept in a continuous longitude frame (the US
        // NWS S-411 sea-ice product, ~175°E → ~225°E). Geometry east of +180°
        // projects to world-X beyond +Extent; the tile enumeration must emit the
        // columns at index >= perAxis that cover it (not clamp them into the
        // boundary column), and TileWorldBounds must map those columns back to the
        // correct continuous world position.
        var band = 6;
        var perAxis = TileGrid.TilesPerAxis(band);
        var res = TileGrid.ResolutionForBand(band);

        // Centre the viewport at ~lon 200° (continuous, east of +180°).
        var (centerX, _) = WebMercator.FromLonLat(200.0, 72.0);
        Assert.True(centerX > TileGrid.Extent, "sanity: lon 200° projects east of +Extent");

        var tiles = TileGrid.VisibleTiles(centerX, 0, 512, 512, res, band);
        Assert.NotEmpty(tiles);
        // At least one column lies beyond the standard world (index >= perAxis).
        Assert.Contains(tiles, t => t.X >= perAxis);
        // Every emitted column maps to a continuous world position east of the
        // boundary — none is collapsed back to the ±180° edge column.
        var eastTile = tiles.First(t => t.X >= perAxis);
        var bounds = TileGrid.TileWorldBounds(eastTile);
        Assert.True(bounds.MinX >= TileGrid.Extent,
            $"east tile world MinX {bounds.MinX} should be >= +Extent {TileGrid.Extent}");
        // The whole selection spans no more than one world (the X span is capped).
        var span = tiles.Max(t => t.X) - tiles.Min(t => t.X) + 1;
        Assert.True(span <= perAxis, $"X span {span} exceeds one world ({perAxis})");
    }

    [Fact]
    public void VisibleTileRange_CapsXSpanToOneWorldOnExtremeZoomOut()
    {
        // A whole-world-wide viewport at band 0 (perAxis == 1) must not enumerate
        // repeated columns: the span cap keeps it to a single column, matching the
        // pre-existing single-world behaviour for normal (non-antimeridian) data.
        var band = 0;
        var perAxis = TileGrid.TilesPerAxis(band);
        var res = TileGrid.ResolutionForBand(band);
        var range = TileGrid.VisibleTileRange(0, 0, 4096, 4096, res, band);
        Assert.Equal(perAxis, range.XEnd - range.XStart + 1);
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

    [Fact]
    public void RotatedCoverSize_ZeroRotation_ReturnsOriginalSize()
    {
        // No rotation must leave the selection box exactly the projection box so
        // the north-up path is bit-for-bit unchanged.
        var (w, h) = TileGrid.RotatedCoverSize(800, 600, 0);
        Assert.Equal(800, w, 9);
        Assert.Equal(600, h, 9);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(-90)]
    [InlineData(270)]
    public void RotatedCoverSize_QuarterTurn_SwapsWidthAndHeight(double degrees)
    {
        // A 90° turn maps the viewport's width onto the screen's height axis and
        // vice-versa, so the bounding box is the transposed size.
        var (w, h) = TileGrid.RotatedCoverSize(800, 600, degrees);
        Assert.Equal(600, w, 6);
        Assert.Equal(800, h, 6);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(0.08)]
    [InlineData(358.7)]
    public void RotatedCoverSize_NeverShrinksBelowOriginal(double degrees)
    {
        // The rotated viewport's corners always poke outside the north-up box, so
        // the cover box must grow (never shrink) — otherwise rotated corners go
        // uncovered and blank. This is the property the blanking fix relies on.
        const double srcW = 800, srcH = 600;
        var (w, h) = TileGrid.RotatedCoverSize(srcW, srcH, degrees);
        Assert.True(w >= srcW - 1e-9, $"cover width {w} < {srcW}");
        Assert.True(h >= srcH - 1e-9, $"cover height {h} < {srcH}");
    }

    [Fact]
    public void RotatedCoverSize_45Degrees_MatchesAabbFormula()
    {
        // AABB of a w×h rect rotated by θ: w·|cosθ| + h·|sinθ| by w·|sinθ| + h·|cosθ|.
        const double srcW = 800, srcH = 600;
        var r = 45 * System.Math.PI / 180.0;
        var c = System.Math.Abs(System.Math.Cos(r));
        var s = System.Math.Abs(System.Math.Sin(r));
        var (w, h) = TileGrid.RotatedCoverSize(srcW, srcH, 45);
        Assert.Equal(srcW * c + srcH * s, w, 6);
        Assert.Equal(srcW * s + srcH * c, h, 6);
    }

    [Fact]
    public void RotationCompositeLayout_NorthUp_MatchesViewportAtDeviceScale()
    {
        // At north-up the cover box equals the viewport, so the off-screen origin
        // is (0,0) and the pixel size is the DIP size scaled by the device scale.
        var (originX, originY, pxW, pxH) =
            TileGrid.RotationCompositeLayout(800, 600, 800, 600, 2.0);

        Assert.Equal(0, originX, 6);
        Assert.Equal(0, originY, 6);
        Assert.Equal(1600, pxW);
        Assert.Equal(1200, pxH);
    }

    [Fact]
    public void RotationCompositeLayout_CentresCoverBoxOnScreenCentre()
    {
        // A cover box larger than the viewport (rotated corners poke out) is
        // centred on the screen centre, so the origin is negative and symmetric.
        var (originX, originY, pxW, pxH) =
            TileGrid.RotationCompositeLayout(800, 600, 990, 990, 1.0);

        Assert.Equal((800 - 990) / 2.0, originX, 6);
        Assert.Equal((600 - 990) / 2.0, originY, 6);
        Assert.Equal(990, pxW);
        Assert.Equal(990, pxH);

        // The cover box's centre coincides with the screen centre.
        Assert.Equal(400, originX + 990 / 2.0, 6);
        Assert.Equal(300, originY + 990 / 2.0, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void RotationCompositeLayout_NonPositiveDeviceScale_FallsBackToOnePixelPerDip(double deviceScale)
    {
        var (_, _, pxW, pxH) =
            TileGrid.RotationCompositeLayout(800, 600, 800, 600, deviceScale);

        Assert.Equal(800, pxW);
        Assert.Equal(600, pxH);
    }

    [Fact]
    public void RotationCompositeLayout_RoundsPixelSizeUp()
    {
        // Fractional device pixels round up so the surface never clips the cover box.
        var (_, _, pxW, pxH) =
            TileGrid.RotationCompositeLayout(800, 600, 100.2, 100.6, 1.5);

        Assert.Equal((int)System.Math.Ceiling(100.2 * 1.5), pxW);
        Assert.Equal((int)System.Math.Ceiling(100.6 * 1.5), pxH);
    }

    // --- Prediction/selection under rotation (issue #347 §F.8 regression) ---
    // §F.8: a rotated viewport early-returned "north-up only" and the chart
    // blanked permanently on a trackpad pinch-rotate. The fix selects tiles
    // against RotatedCoverSize so the rotated corners stay filled. These pin the
    // *composition* the renderer performs (S100VectorTileRenderer.Render lines
    // ~604-608): RotatedCoverSize → VisibleTiles. A revert to passing the raw
    // DIP size would drop the rotated corners and re-introduce the blank.

    /// <summary>
    /// The world-tile key containing the EPSG:3857 point (<paramref name="wx"/>,
    /// <paramref name="wy"/>) at <paramref name="band"/>, using the same flooring
    /// convention as <see cref="TileGrid.VisibleTileRange"/> (Y inverted).
    /// </summary>
    private static TileKey TileAt(double wx, double wy, int band)
    {
        var size = TileGrid.TileWorldSize(band);
        var x = (int)System.Math.Floor((wx + TileGrid.Extent) / size);
        var y = (int)System.Math.Floor((TileGrid.Extent - wy) / size);
        return new TileKey(band, x, y);
    }

    [Fact]
    public void VisibleTiles_RotatedViewport_FillsExtentThatRawSizeMisses()
    {
        // An interior viewport, band 8, native resolution so tiles are far smaller
        // than the viewport (multi-tile cover). Rotating 45° pokes the corners well
        // beyond the north-up box: the cover-box height grows from 1600 to ~2941 DIP.
        const int band = 8;
        var res = TileGrid.ResolutionForBand(band);
        const double cx = 100_000, cy = 100_000, wDip = 2560, hDip = 1600, deg = 45;

        var raw = TileGrid.VisibleTiles(cx, cy, wDip, hDip, res, band).ToHashSet();
        var (coverW, coverH) = TileGrid.RotatedCoverSize(wDip, hDip, deg);
        var cover = TileGrid.VisibleTiles(cx, cy, coverW, coverH, res, band).ToHashSet();

        // The rotated cover strictly contains the north-up selection and adds tiles.
        Assert.True(raw.IsSubsetOf(cover), "rotated cover must be a superset of the north-up selection");
        Assert.True(cover.Count > raw.Count, "rotated cover must add corner tiles the north-up size omits");

        // A probe at the rotated viewport's extreme +Y extent (just inside the cover
        // box, but well outside the north-up box) must be covered by the rotated
        // selection and NOT by the raw-size selection — the exact §F.8 corner gap.
        var probeY = cy + coverH * 0.5 * res * 0.95;
        var probe = TileAt(cx, probeY, band);
        Assert.Contains(probe, cover);
        Assert.DoesNotContain(probe, raw);
    }
}

