using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Stored tiles keep only the core plus a 2-pixel apron (map rendering slice
/// 4). They must hold exactly the pixels the old guttered tiles held there, so
/// the clipped blit draws the same picture at every seam.
/// </summary>
[Collection(RenderingOptimizationsCollection.Name)]
public sealed class TileApronTests
{
    private const int Band = 12;

    [Theory]
    [InlineData(1f, 260, 64)]
    [InlineData(2f, 516, 128)]
    [InlineData(1.5f, 388, 96)]
    public void StoredTile_IsCorePlusApron(float deviceScale, int storedPx, int gutterPx)
    {
        Assert.Equal(storedPx, S100VectorTileRenderer.TilePixelSize(deviceScale));
        Assert.Equal(gutterPx, S100VectorTileRenderer.GutterPixelSize(deviceScale));
        Assert.Equal(
            storedPx,
            S100VectorTileRenderer.CorePixelSize(deviceScale) + 2 * S100VectorTileRenderer.ApronPx);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void StoredTile_MatchesTheGutteredRasterItReplaces(float deviceScale)
    {
        var key = new TileKey(Band, 1200, 1500);
        var scene = SeamScene(key);

        using var stored = S100VectorTileRenderer.RasterizeTile(scene, new BaseSpatialIndex(scene), key, deviceScale);
        using var guttered = OldGutteredRaster(scene, key, deviceScale);

        // The old tile put the core at the gutter's width; the stored one at the apron's.
        var offset = (guttered.Width - (stored.Width - 2 * S100VectorTileRenderer.ApronPx)) / 2
            - S100VectorTileRenderer.ApronPx;
        var differing = 0;
        var drawn = 0;
        for (var y = 0; y < stored.Height; y++)
        {
            for (var x = 0; x < stored.Width; x++)
            {
                var actual = stored.GetPixel(x, y);
                if (actual != guttered.GetPixel(x + offset, y + offset))
                {
                    differing++;
                }

                if (actual.Alpha != 0)
                {
                    drawn++;
                }
            }
        }

        Assert.True(drawn > stored.Width * 4, "The scene should draw across the tile.");
        Assert.Equal(0, differing);
    }

    [Fact]
    public void ApronWorld_IsTwoStoredPixels()
    {
        var (minX, _, maxX, _) = TileGrid.TileWorldBounds(new TileKey(Band, 0, 0));
        var coreWorld = maxX - minX;

        Assert.Equal(2 * coreWorld / 256, S100VectorTileRenderer.ApronWorld(260, coreWorld), 9);
        Assert.Equal(2 * coreWorld / 512, S100VectorTileRenderer.ApronWorld(516, coreWorld), 9);
    }

    /// <summary>
    /// The tile as the renderer stored it before slice 4: the scene drawn over
    /// the core plus the full <see cref="S100VectorTileRenderer.GutterDip"/>
    /// gutter, at <c>round((256 + 2 × gutter) × scale)</c> pixels.
    /// </summary>
    private static SKBitmap OldGutteredRaster(VectorScene scene, TileKey key, float deviceScale)
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var gutterWorld = S100VectorTileRenderer.GutterDip * TileGrid.ResolutionForBand(key.Band);
        var (minLon, minLat) = WebMercator.ToLonLat(minX - gutterWorld, minY - gutterWorld, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX + gutterWorld, maxY + gutterWorld, clampLatitude: false);
        var px = (int)Math.Round((TileGrid.TileSizeDip + 2 * S100VectorTileRenderer.GutterDip) * deviceScale);
        var renderer = new SkiaDisplayListRenderer
        {
            Background = RgbaColor.Transparent,
            HonorScaleVisibility = true,
            EnableSeamWrap = false,
        };

        return renderer.Render(scene, new Viewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = px,
            HeightPixels = px,
            ScaleDenominator = S100VectorTileRenderer.TileScaleDenominator(scene, key),
        });
    }

    /// <summary>
    /// A filled, outlined area and a thick diagonal line, both crossing the
    /// tile's right seam and running through its gutter.
    /// </summary>
    private static VectorScene SeamScene(TileKey key)
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var w = maxX - minX;
        PaintOp area = new AreaPaintOp
        {
            FeatureReference = "apron-area",
            WorldShell =
            [
                (minX + 0.3 * w, minY + 0.2 * w),
                (maxX + 0.4 * w, minY + 0.25 * w),
                (maxX + 0.3 * w, maxY - 0.1 * w),
                (minX + 0.35 * w, maxY - 0.3 * w),
                (minX + 0.3 * w, minY + 0.2 * w),
            ],
            Fill = new RgbaColor(40, 100, 180, 200),
            OutlineColor = new RgbaColor(255, 255, 255, 255),
            OutlineWidthPx = 3,
        };
        PaintOp line = new LinePaintOp
        {
            FeatureReference = "apron-line",
            World = [(minX - 0.2 * w, minY + 0.1 * w), (maxX + 0.5 * w, maxY - 0.15 * w)],
            Color = new RgbaColor(200, 30, 30, 255),
            WidthPx = 7,
        };
        return new VectorScene([area, line]);
    }
}
