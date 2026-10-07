using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #774: base tiles judged each op's own SCAMIN at the snapped band's
/// scale, up to √2 off the live scale. Tiles are now keyed by a scale class,
/// the interval between op thresholds the live scale falls in. The numbers
/// follow the issue: a contour with SCAMIN 1:20 000 at 46.8°N, viewed at
/// 1:17 000, which snaps to a band at about 1:23 300.
/// </summary>
public sealed class TileScaleClassTests
{
    private const double Latitude = 46.845;
    private const double Longitude = -71.175;

    [Theory]
    [InlineData(17000, true)]
    [InlineData(19500, true)]
    [InlineData(20500, false)]
    [InlineData(23000, false)]
    public void ScaleMinimum_IsJudgedAtTheLiveScale(int liveDenominator, bool expected)
    {
        var (band, ratio, gridKey) = SnappedTile(liveDenominator);
        var scene = LineScene(band, scaleMinimum: 20000, scaleMaximum: null);

        var key = S100VectorTileRenderer.WithScaleClass(scene, gridKey, ratio);
        using var bitmap = S100VectorTileRenderer.RasterizeTile(scene, baseIndex: null, key, deviceScale: 1f);

        Assert.Equal(expected, CountPaintedPixels(bitmap) > 0);
    }

    [Theory]
    [InlineData(17000, false)]
    [InlineData(19500, false)]
    [InlineData(20500, true)]
    [InlineData(23000, true)]
    public void ScaleMaximum_IsJudgedAtTheLiveScale(int liveDenominator, bool expected)
    {
        var (band, ratio, gridKey) = SnappedTile(liveDenominator);
        var scene = LineScene(band, scaleMinimum: null, scaleMaximum: 20000);

        var key = S100VectorTileRenderer.WithScaleClass(scene, gridKey, ratio);
        using var bitmap = S100VectorTileRenderer.RasterizeTile(scene, baseIndex: null, key, deviceScale: 1f);

        Assert.Equal(expected, CountPaintedPixels(bitmap) > 0);
    }

    [Fact]
    public void LiveScalesOnEitherSideOfAThreshold_GetDistinctKeys()
    {
        var (band, finer, gridKey) = SnappedTile(17000);
        var (coarserBand, coarser, _) = SnappedTile(23000);
        Assert.Equal(band, coarserBand);
        var scene = LineScene(band, scaleMinimum: 20000, scaleMaximum: null);

        var finerKey = S100VectorTileRenderer.WithScaleClass(scene, gridKey, finer);
        var coarserKey = S100VectorTileRenderer.WithScaleClass(scene, gridKey, coarser);

        Assert.NotEqual(finerKey, coarserKey);
        Assert.Equal((gridKey.Band, gridKey.X, gridKey.Y), (finerKey.Band, finerKey.X, finerKey.Y));
    }

    [Fact]
    public void NoThresholdInTheBandWindow_KeepsClassZeroAndTheBandScale()
    {
        var (band, ratio, gridKey) = SnappedTile(17000);

        // 1:5 000 and 1:90 000 are both outside the band's √2 window.
        var scene = new VectorScene([
            Line(band, scaleMinimum: 90000, scaleMaximum: null),
            Line(band, scaleMinimum: null, scaleMaximum: 5000),
        ]);

        var key = S100VectorTileRenderer.WithScaleClass(scene, gridKey, ratio);

        Assert.Equal(0, key.ScaleClass);
        Assert.Equal(
            S100VectorTileRenderer.BandDenominator(key),
            S100VectorTileRenderer.TileScaleDenominator(scene, key));
    }

    [Fact]
    public void Denominator_StaysInsideItsClass()
    {
        const int band = 14;
        const double bandDenominator = 23300;
        double[] thresholds = [18000, 20000, 30000];

        Assert.Equal(4, TileScaleClass.Count(thresholds, band, bandDenominator));
        for (var c = 0; c < 4; c++)
        {
            var denominator = TileScaleClass.Denominator(thresholds, band, bandDenominator, c);
            var ratio = denominator / bandDenominator;
            Assert.Equal(c, TileScaleClass.For(thresholds, band, bandDenominator, ratio));
        }

        // The band's own scale lies inside class 2 (20 000 … 30 000), so that
        // class keeps it.
        Assert.Equal(bandDenominator, TileScaleClass.Denominator(thresholds, band, bandDenominator, 2));
    }

    [Fact]
    public void BandsAtTheGridEnds_HaveOpenWindows()
    {
        // The finest band is also shown past its window when overzoomed, so a
        // threshold far below its scale still splits it.
        const double finest = 1000;
        double[] thresholds = [100];
        Assert.Equal(2, TileScaleClass.Count(thresholds, TileGrid.MaxBand, finest));
        Assert.Equal(0, TileScaleClass.For(thresholds, TileGrid.MaxBand, finest, ratio: 0.05));
        Assert.Equal(1, TileScaleClass.For(thresholds, TileGrid.MaxBand, finest, ratio: 0.5));
        Assert.True(TileScaleClass.Denominator(thresholds, TileGrid.MaxBand, finest, 0) < 100);

        const double coarsest = 1e8;
        thresholds = [1e9];
        Assert.Equal(2, TileScaleClass.Count(thresholds, TileGrid.MinBand, coarsest));
        Assert.Equal(1, TileScaleClass.For(thresholds, TileGrid.MinBand, coarsest, ratio: 20));
        Assert.True(TileScaleClass.Denominator(thresholds, TileGrid.MinBand, coarsest, 1) > 1e9);
    }

    [Fact]
    public void Collect_IsDistinctAndAscending()
    {
        var thresholds = TileScaleClass.Collect([
            Line(10, scaleMinimum: 45000, scaleMaximum: 8000),
            Line(10, scaleMinimum: 22000, scaleMaximum: null),
            Line(10, scaleMinimum: 45000, scaleMaximum: null),
            Line(10, scaleMinimum: null, scaleMaximum: null),
        ]);

        Assert.Equal([8000, 22000, 45000], thresholds);
    }

    [Fact]
    public void Metatile_RowsOfDifferentClasses_AreSplit()
    {
        var (band, ratio, gridKey) = SnappedTile(17000);
        var scene = LineScene(band, scaleMinimum: 20000, scaleMaximum: null);
        var classed = S100VectorTileRenderer.WithScaleClass(scene, gridKey, ratio);
        var peer = classed with { Y = classed.Y + 1, ScaleClass = classed.ScaleClass + 1 };

        var groups = S100VectorTileRenderer.PartitionMetatileForScale(scene, baseIndex: null, [classed, peer]);

        Assert.Equal(2, groups.Count);
    }

    private static (int Band, double Ratio, TileKey Key) SnappedTile(int liveDenominator)
    {
        var (x, y) = WebMercator.FromLonLat(Longitude, Latitude);
        var resolution = MapsuiDisplayListRenderer.DenominatorToResolution(liveDenominator, Latitude * Math.PI / 180.0);
        var band = TileGrid.BandForResolution(resolution);
        var key = Assert.Single(TileGrid.VisibleTiles(x, y, 1, 1, resolution, band));
        return (band, resolution / TileGrid.ResolutionForBand(band), key);
    }

    private static VectorScene LineScene(int band, double? scaleMinimum, double? scaleMaximum) =>
        new([Line(band, scaleMinimum, scaleMaximum)]);

    private static PaintOp Line(int band, double? scaleMinimum, double? scaleMaximum)
    {
        var (x, y) = WebMercator.FromLonLat(Longitude, Latitude);
        var halfTile = TileGrid.TileWorldSize(band) / 2.0;
        return new LinePaintOp
        {
            FeatureReference = "contour",
            World = [(x - halfTile, y), (x + halfTile, y)],
            Color = new RgbaColor(0, 0, 0, 255),
            WidthPx = 4,
            ScaleMinimum = scaleMinimum,
            ScaleMaximum = scaleMaximum,
        };
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
