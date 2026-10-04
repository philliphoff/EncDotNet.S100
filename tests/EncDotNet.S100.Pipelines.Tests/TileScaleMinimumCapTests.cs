using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #761: a cell's line work vanished from part of its own display band
/// because base tiles were rasterised at the snapped band's scale, which can
/// lie past the cell's out-of-band cap (<c>DataCoverage.minimumDisplayScale</c>,
/// S-101 PS §4.6) while the live scale is still inside it. The geometry is
/// synthetic; the numbers mirror the CHS Quebec harbour cell that exposed it
/// (46.8°N, band ending at 1:22 000, blank line work at 1:20 000).
/// </summary>
public sealed class TileScaleMinimumCapTests
{
    private const double Latitude = 46.845;
    private const double Longitude = -71.175;
    private const int Cap = 22000;

    [Fact]
    public void InBandScale_SnappingToABandPastTheCap_StillPaintsCappedLineWork()
    {
        var (x, y, band, key) = SnappedTile(liveDenominator: 20000);

        // Precondition: the live map shows a band whose own scale is past the
        // cap, even though 1:20 000 is inside the cell's band.
        var bandDenominator = S100VectorSceneRenderer.ScaleDenominatorFor(x, y, TileGrid.ResolutionForBand(band));
        Assert.True(bandDenominator > Cap, $"band {band} is at 1:{bandDenominator:F0}, inside the cap");

        // A line with no SCAMIN of its own inherits the cap.
        var scene = LineScene(x, y, band, scaleMinimum: Cap, cap: Cap);

        using var bitmap = S100VectorTileRenderer.RasterizeTile(scene, baseIndex: null, key, deviceScale: 1f);

        Assert.True(CountPaintedPixels(bitmap) > 0, "the capped line was culled from an in-band tile");
    }

    [Fact]
    public void InBandScale_KeepsAnOpsOwnTighterScaleMinimum()
    {
        var (x, y, band, key) = SnappedTile(liveDenominator: 20000);

        // SCAMIN 1:15 000 is finer than both the band and the live scale.
        var scene = LineScene(x, y, band, scaleMinimum: 15000, cap: Cap);

        using var bitmap = S100VectorTileRenderer.RasterizeTile(scene, baseIndex: null, key, deviceScale: 1f);

        Assert.Equal(0, CountPaintedPixels(bitmap));
    }

    [Theory]
    [InlineData(20000, false)]
    [InlineData(21900, false)]
    [InlineData(22100, true)]
    [InlineData(40000, true)]
    public void LiveScale_PastTheCap_HidesTheLayer(int liveDenominator, bool expected)
    {
        var (x, y) = WebMercator.FromLonLat(Longitude, Latitude);
        var resolution = LiveResolution(liveDenominator);
        var scene = new VectorScene([]) { ScaleMinimumCap = Cap };

        Assert.Equal(expected, S100VectorTileRenderer.IsPastScaleMinimumCap(scene, x, y, resolution));
    }

    [Fact]
    public void LiveScale_WithoutACap_NeverHidesTheLayer()
    {
        var (x, y) = WebMercator.FromLonLat(Longitude, Latitude);

        Assert.False(S100VectorTileRenderer.IsPastScaleMinimumCap(new VectorScene([]), x, y, LiveResolution(1_000_000)));
        Assert.False(S100VectorTileRenderer.IsPastScaleMinimumCap(null, x, y, LiveResolution(1_000_000)));
    }

    [Fact]
    public void PartitionScene_KeepsTheCapOnBothPlanes()
    {
        var scene = new VectorScene([]) { ScaleMinimumCap = Cap };

        var (baseScene, overlayScene) = S100VectorTileRenderer.PartitionScene(scene);

        Assert.Equal(Cap, baseScene.ScaleMinimumCap);
        Assert.Equal(Cap, overlayScene.ScaleMinimumCap);
    }

    private static double LiveResolution(int denominator) =>
        MapsuiDisplayListRenderer.DenominatorToResolution(denominator, Latitude * Math.PI / 180.0);

    private static (double X, double Y, int Band, TileKey Key) SnappedTile(int liveDenominator)
    {
        var (x, y) = WebMercator.FromLonLat(Longitude, Latitude);
        var resolution = LiveResolution(liveDenominator);
        var band = TileGrid.BandForResolution(resolution);
        var key = Assert.Single(TileGrid.VisibleTiles(x, y, 1, 1, resolution, band));
        return (x, y, band, key);
    }

    private static VectorScene LineScene(double x, double y, int band, double scaleMinimum, int cap)
    {
        var halfTile = TileGrid.TileWorldSize(band) / 2.0;
        PaintOp line = new LinePaintOp
        {
            FeatureReference = "contour",
            World = [(x - halfTile, y), (x + halfTile, y)],
            Color = new RgbaColor(0, 0, 0, 255),
            WidthPx = 4,
            ScaleMinimum = scaleMinimum,
        };
        return new VectorScene([line]) { ScaleMinimumCap = cap };
    }

    private static long CountPaintedPixels(SKBitmap bitmap)
    {
        long count = 0;
        foreach (var pixel in bitmap.Pixels)
        {
            if (pixel.Alpha != 0)
                count++;
        }

        return count;
    }
}
