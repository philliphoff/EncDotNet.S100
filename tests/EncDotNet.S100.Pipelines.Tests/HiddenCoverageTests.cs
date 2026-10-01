using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Verifies the hidden-region skip for coarser cells (issue #691): which
/// rectangles the active finer coverages hide (<see cref="CoverageClip.GetHiddenCoverage"/>),
/// and that every tile the renderer skips as hidden would have been erased
/// entirely by the coverage clip it already applies.
/// </summary>
public class HiddenCoverageTests
{
    private static readonly GeometryFactory Gf = new();

    private static Polygon Rect(double minX, double minY, double maxX, double maxY) =>
        (Polygon)Gf.ToGeometry(new Envelope(minX, maxX, minY, maxY));

    private static HiddenCoverage? Hidden(FinerCoverage[] regions, double resolution)
    {
        var layer = new MemoryLayer();
        CoverageClip.Set(layer, regions);
        return CoverageClip.GetHiddenCoverage(layer, resolution);
    }

    [Fact]
    public void GetHiddenCoverage_NoAttachment_ReturnsNull()
    {
        Assert.Null(CoverageClip.GetHiddenCoverage(new MemoryLayer(), resolution: 1));
    }

    [Fact]
    public void GetHiddenCoverage_AllFinerZoomedOut_ReturnsNull()
    {
        Assert.Null(Hidden([new FinerCoverage(Rect(0, 0, 100, 100), CutoffResolution: 0.5)], resolution: 1));
    }

    [Fact]
    public void Covers_RectStraddlingAdjacentFinerCells_IsNotHidden()
    {
        // Each finer coverage is clipped separately with anti-aliasing, so a
        // hairline of the coarser cell survives along the seam of two adjacent
        // finer cells. A rectangle across the seam must stay drawn.
        var hidden = Hidden(
        [
            new FinerCoverage(Rect(0, 0, 100, 100), CutoffResolution: 2),
            new FinerCoverage(Rect(100, 0, 200, 100), CutoffResolution: 2),
        ], resolution: 1);

        Assert.NotNull(hidden);
        Assert.False(hidden.Covers(50, 10, 150, 90));
        Assert.True(hidden.Covers(10, 10, 90, 90));
        Assert.True(hidden.Covers(110, 10, 190, 90));
    }

    [Fact]
    public void Covers_RectOverFinerHole_IsNotHidden()
    {
        // The coarser cell still shows through a finer cell's no-coverage hole.
        var shell = Rect(0, 0, 100, 100).Shell;
        var hole = Rect(40, 40, 60, 60).Shell;
        var hidden = Hidden([new FinerCoverage(Gf.CreatePolygon(shell, [hole]), CutoffResolution: 2)], resolution: 1);

        Assert.NotNull(hidden);
        Assert.False(hidden.Covers(30, 30, 70, 70));
        Assert.True(hidden.Covers(5, 5, 35, 35));
    }

    [Fact]
    public void Covers_FinerCellOutOfBand_DropsFromUnion()
    {
        FinerCoverage[] regions =
        [
            new FinerCoverage(Rect(0, 0, 100, 100), CutoffResolution: 10),
            new FinerCoverage(Rect(100, 0, 200, 100), CutoffResolution: 2),
        ];

        // At 1 m/px both are drawing; at 5 m/px the second has zoomed out of its
        // band, stops clipping, and so must stop hiding.
        Assert.True(Hidden(regions, resolution: 1)!.Covers(110, 10, 190, 90));
        Assert.False(Hidden(regions, resolution: 5)!.Covers(110, 10, 190, 90));
        Assert.True(Hidden(regions, resolution: 5)!.Covers(10, 10, 90, 90));
    }

    [Fact]
    public void GetHiddenCoverage_InvalidCoverage_IsLeftOut()
    {
        // A self-intersecting "bow tie" fills differently under the clip's
        // even-odd rule than as an NTS area, so it never counts as hidden.
        var bowTie = Gf.CreatePolygon(
        [
            new Coordinate(0, 0), new Coordinate(100, 100), new Coordinate(100, 0),
            new Coordinate(0, 100), new Coordinate(0, 0),
        ]);

        var hidden = Hidden([new FinerCoverage(bowTie, CutoffResolution: 2)], resolution: 1);
        Assert.NotNull(hidden);
        Assert.False(hidden.Covers(10, 1, 20, 2));
    }

    [Fact]
    public void GetHiddenCoverage_SameActiveSet_ReturnsSameInstance()
    {
        // The renderer memoises per-tile answers against the instance.
        var layer = new MemoryLayer();
        CoverageClip.Set(layer,
        [
            new FinerCoverage(Rect(0, 0, 100, 100), CutoffResolution: 10),
            new FinerCoverage(Rect(100, 0, 200, 100), CutoffResolution: 2),
        ]);

        Assert.Same(CoverageClip.GetHiddenCoverage(layer, 1), CoverageClip.GetHiddenCoverage(layer, 1.5));
        Assert.NotSame(CoverageClip.GetHiddenCoverage(layer, 1), CoverageClip.GetHiddenCoverage(layer, 5));
    }

    [Fact]
    public void OverlapSuppression_Apply_SharesFinerSetAcrossCellLayers()
    {
        // One finer-coverage array per cell, so its prepared hidden-region
        // geometries are built once rather than once per layer.
        var coarseA = new MemoryLayer();
        var coarseB = new MemoryLayer();
        OverlapSuppression.Apply(
        [
            new OverlapSuppressionCell { Layers = [coarseA, coarseB], Coverage = Rect(0, 0, 1000, 1000), ScaleDenominator = 90000 },
            new OverlapSuppressionCell { Layers = [new MemoryLayer()], Coverage = Rect(0, 0, 100, 100), ScaleDenominator = 12000 },
        ]);

        Assert.NotNull(CoverageClip.Get(coarseA));
        Assert.Same(CoverageClip.Get(coarseA), CoverageClip.Get(coarseB));
    }

    [Fact]
    public void IsViewportHidden_RequiresWholeViewportUnderFinerCoverage()
    {
        var hidden = Hidden([new FinerCoverage(Rect(-1000, -1000, 1000, 1000), CutoffResolution: 10)], resolution: 1)!;

        Assert.True(S100VectorTileRenderer.IsViewportHidden(hidden, new Mapsui.Viewport(0, 0, 1, 0, 400, 300), 1));
        Assert.False(S100VectorTileRenderer.IsViewportHidden(hidden, new Mapsui.Viewport(900, 0, 1, 0, 400, 300), 1));
        // Rotation enlarges the footprint: a 1600 x 1600 DIP view fits unrotated
        // but not at 45 degrees.
        Assert.True(S100VectorTileRenderer.IsViewportHidden(hidden, new Mapsui.Viewport(0, 0, 1, 0, 1600, 1600), 1));
        Assert.False(S100VectorTileRenderer.IsViewportHidden(hidden, new Mapsui.Viewport(0, 0, 1, 45, 1600, 1600), 1));
    }

    public static TheoryData<double, double> ResolutionAndRotation()
    {
        var data = new TheoryData<double, double>();
        // Band 16's own resolution and both ends of the range it is displayed
        // at (BandForResolution is log-nearest, so up to a factor of √2 either way).
        var bandResolution = TileGrid.ResolutionForBand(16);
        foreach (var factor in new[] { 1 / Math.Sqrt(2) * 1.001, 1.0, Math.Sqrt(2) * 0.999 })
        {
            foreach (var rotation in new[] { 0.0, 17.0 })
                data.Add(bandResolution * factor, rotation);
        }

        return data;
    }

    /// <summary>
    /// The invariant that makes the skip safe: every tile reported hidden, drawn
    /// fully opaque under the same anti-aliased difference clip the renderer
    /// applies, leaves no pixel at all, including at device scale 2. The
    /// coverages include adjacent cells, a diagonal edge and a hole, and the
    /// test fails if nothing (or everything) is hidden, so it can't pass vacuously.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResolutionAndRotation))]
    public void HiddenTiles_DrawNothingUnderTheCoverageClip(double resolution, double rotation)
    {
        // Near New York harbour (EPSG:3857), band 16 tiles are ~611 m.
        const double cx = -8_240_000;
        const double cy = 4_970_000;
        AssertHiddenTilesDrawNothing(resolution, rotation, cx, cy, Regions(cx, cy));
    }

    /// <summary>
    /// The same invariant where coverage edges run within a pixel of tile
    /// boundaries, which is where an anti-aliased edge pixel could leak.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResolutionAndRotation))]
    public void HiddenTiles_DrawNothingWhenCoverageEdgesHugTileBoundaries(double resolution, double rotation)
    {
        const int band = 16;
        var size = TileGrid.TileWorldSize(band);
        // Snap the centre to a tile corner, then put each coverage edge a few
        // pixels or less outside a tile boundary (inside for the last). Only
        // the widest offsets clear the skip margin and get hidden.
        var cx = -TileGrid.Extent + Math.Round((-8_240_000 + TileGrid.Extent) / size) * size;
        var cy = TileGrid.Extent - Math.Round((TileGrid.Extent - 4_970_000) / size) * size;
        FinerCoverage[] regions = [.. new[] { 0.05, 0.4, 0.9, 1.6, 4.5, 6.0, -0.3 }.Select((pixels, i) =>
        {
            var d = pixels * resolution;
            var x0 = cx + (i - 4) * size;
            return new FinerCoverage(
                Rect(x0 - d, cy - 2 * size - d, x0 + size + d, cy + 2 * size + d),
                CutoffResolution: 100);
        })];

        AssertHiddenTilesDrawNothing(resolution, rotation, cx, cy, regions);
    }

    private static FinerCoverage[] Regions(double cx, double cy)
    {
        var shellWithHole = Rect(cx - 3000, cy + 100, cx + 300, cy + 2500).Shell;
        var hole = Rect(cx - 1700, cy + 900, cx - 1000, cy + 1600).Shell;
        var diagonal = Gf.CreatePolygon(
        [
            new Coordinate(cx + 300, cy - 2500), new Coordinate(cx + 3500, cy - 2500),
            new Coordinate(cx + 3500, cy + 2500), new Coordinate(cx + 300, cy - 200),
            new Coordinate(cx + 300, cy - 2500),
        ]);
        return
        [
            new FinerCoverage(Rect(cx - 3000, cy - 2500, cx + 300, cy + 100), CutoffResolution: 100),
            new FinerCoverage(Gf.CreatePolygon(shellWithHole, [hole]), CutoffResolution: 100),
            new FinerCoverage(diagonal, CutoffResolution: 100),
        ];
    }

    private static void AssertHiddenTilesDrawNothing(
        double resolution, double rotation, double cx, double cy, FinerCoverage[] regions)
    {
        var layer = new MemoryLayer();
        CoverageClip.Set(layer, regions);
        var hidden = CoverageClip.GetHiddenCoverage(layer, resolution);
        Assert.NotNull(hidden);

        const int widthDip = 1600;
        const int heightDip = 1200;
        const float deviceScale = 2f;
        var viewport = new Mapsui.Viewport(cx, cy, resolution, rotation, widthDip, heightDip);

        using var surface = SKSurface.Create(new SKImageInfo(
            (int)(widthDip * deviceScale), (int)(heightDip * deviceScale), SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(deviceScale);

        var clipPaths = CoverageClip.BuildActiveDifferencePaths(layer, viewport, resolution);
        foreach (var path in clipPaths)
            canvas.ClipPath(path, SKClipOperation.Difference, antialias: true);

        const int band = 16;
        var extent = viewport.ToExtent();
        var size = TileGrid.TileWorldSize(band);
        int hiddenCount = 0, shownCount = 0;
        using var paint = new SKPaint { Color = SKColors.Red, IsAntialias = true };
        for (var x = (int)Math.Floor((extent.MinX + TileGrid.Extent) / size); x * size - TileGrid.Extent < extent.MaxX; x++)
        {
            for (var y = (int)Math.Floor((TileGrid.Extent - extent.MaxY) / size); TileGrid.Extent - y * size > extent.MinY; y++)
            {
                var key = new TileKey(band, x, y);
                if (!S100VectorTileRenderer.IsTileCoreHidden(hidden, key))
                {
                    shownCount++;
                    continue;
                }

                hiddenCount++;
                var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
                using var tile = new SKPath();
                MoveOrLine(tile, viewport, minX, minY, move: true);
                MoveOrLine(tile, viewport, maxX, minY);
                MoveOrLine(tile, viewport, maxX, maxY);
                MoveOrLine(tile, viewport, minX, maxY);
                tile.Close();
                canvas.DrawPath(tile, paint);
            }
        }

        foreach (var path in clipPaths)
            path.Dispose();

        Assert.True(hiddenCount > 0, "expected some hidden tiles");
        Assert.True(shownCount > 0, "expected some visible tiles");

        using var image = surface.Snapshot();
        if (Environment.GetEnvironmentVariable("HIDDEN_DUMP") is { } dump)
        {
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(dump, $"leak-{resolution:F2}-{rotation}.png"), data.ToArray());
        }
        using var pixmap = image.PeekPixels();
        var pixels = pixmap.GetPixelSpan<uint>();
        var leaked = 0;
        foreach (var pixel in pixels)
        {
            if (pixel >> 24 != 0)
                leaked++;
        }

        Assert.True(leaked == 0, $"{leaked} pixels of {hiddenCount} hidden tiles escaped the coverage clip");
    }

    private static void MoveOrLine(SKPath path, Mapsui.Viewport viewport, double x, double y, bool move = false)
    {
        var (sx, sy) = viewport.WorldToScreenXY(x, y);
        if (move)
            path.MoveTo((float)sx, (float)sy);
        else
            path.LineTo((float)sx, (float)sy);
    }
}
