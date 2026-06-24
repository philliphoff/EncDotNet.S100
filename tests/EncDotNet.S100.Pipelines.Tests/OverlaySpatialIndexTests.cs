using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Mapsui;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="OverlaySpatialIndex"/> (#332 lever b — viewport scoping).
/// The index bounds the per-frame overlay walk to ops near the viewport while
/// guaranteeing it never drops a feature that the whole-cell walk would draw:
/// a query is a conservative superset returned in original (priority/z) order,
/// and a query covering the whole extent reproduces the full op list exactly.
/// </summary>
public class OverlaySpatialIndexTests
{
    private static PointPaintOp Point(string id, double x, double y, double offX = 0, double offY = 0) =>
        new()
        {
            FeatureReference = id,
            World = (x, y),
            FallbackColor = new RgbaColor(220, 20, 60, 255),
            FallbackScale = 1.0,
            OffsetXpx = offX,
            OffsetYpx = offY,
        };

    private static TextPaintOp Text(string id, double x, double y) =>
        new()
        {
            FeatureReference = id,
            World = (x, y),
            Text = "12",
            FontSizePx = 10,
            ForeColor = new RgbaColor(0, 0, 0, 255),
        };

    private static List<string> Query(OverlaySpatialIndex idx,
        double minX, double minY, double maxX, double maxY)
    {
        var scratch = new List<int>();
        var results = new List<PaintOp>();
        idx.Query(minX, minY, maxX, maxY, scratch, results);
        return results.Select(o => o.FeatureReference!).ToList();
    }

    [Fact]
    public void Query_CoveringWholeExtent_ReturnsAllOpsInOriginalOrder()
    {
        // Interleave point/text across a 10×10 grid of distinct cells.
        var ops = new List<PaintOp>();
        for (int i = 0; i < 100; i++)
        {
            double x = (i % 10) * 100.0;
            double y = (i / 10) * 100.0;
            ops.Add(i % 2 == 0 ? Point("p" + i, x, y) : Text("t" + i, x, y));
        }
        var idx = new OverlaySpatialIndex(new VectorScene(ops));

        var got = Query(idx, -1e6, -1e6, 1e6, 1e6);

        // Degrades to the whole-cell walk, in the exact original draw order.
        Assert.Equal(ops.Select(o => o.FeatureReference).ToList(), got);
    }

    [Fact]
    public void Query_SubRegion_ReturnsOnlyAnchorsInsideInOriginalOrder()
    {
        var ops = new List<PaintOp>
        {
            Point("a", 0, 0),
            Point("b", 500, 500),
            Point("c", 50, 50),
            Point("d", 950, 50),
            Point("e", 60, 40),
        };
        var idx = new OverlaySpatialIndex(new VectorScene(ops));

        // A box around the lower-left cluster: a, c, e (in original order); not b/d.
        var got = Query(idx, -10, -10, 100, 100);

        Assert.Equal(new[] { "a", "c", "e" }, got);
    }

    [Fact]
    public void Query_AnchorOutsideBoundsExcluded_PartiallyHandledByCallerInflation()
    {
        var ops = new List<PaintOp> { Point("in", 0, 0), Point("out", 1000, 1000) };
        var idx = new OverlaySpatialIndex(new VectorScene(ops));

        var got = Query(idx, -10, -10, 10, 10);

        Assert.Equal(new[] { "in" }, got);
    }

    [Fact]
    public void MaxOffsetPx_IsLargestAbsoluteOpOffset()
    {
        var ops = new List<PaintOp>
        {
            Point("a", 0, 0, offX: 3, offY: -7),
            Point("b", 1, 1, offX: -12, offY: 4),
            Text("t", 2, 2),
        };
        var idx = new OverlaySpatialIndex(new VectorScene(ops));

        Assert.Equal(12.0, idx.MaxOffsetPx);
    }

    [Fact]
    public void Query_ReusedBuffers_ClearBetweenCalls()
    {
        var ops = new List<PaintOp> { Point("a", 0, 0), Point("b", 1000, 1000) };
        var idx = new OverlaySpatialIndex(new VectorScene(ops));

        var scratch = new List<int>();
        var results = new List<PaintOp>();

        idx.Query(-10, -10, 10, 10, scratch, results);
        Assert.Equal(new[] { "a" }, results.Select(o => o.FeatureReference));

        // A second query reusing the same buffers must reflect only the new region.
        idx.Query(990, 990, 1010, 1010, scratch, results);
        Assert.Equal(new[] { "b" }, results.Select(o => o.FeatureReference));
    }

    [Fact]
    public void Query_EmptyOverlay_ReturnsNothing()
    {
        var idx = new OverlaySpatialIndex(new VectorScene(new List<PaintOp>()));
        Assert.Empty(Query(idx, -1e6, -1e6, 1e6, 1e6));
    }

    [Fact]
    public void Query_CollapsedExtent_AllAnchorsCoincident()
    {
        // All ops at the same point — degenerate zero-area extent must still index.
        var ops = new List<PaintOp> { Point("a", 5, 5), Point("b", 5, 5), Point("c", 5, 5) };
        var idx = new OverlaySpatialIndex(new VectorScene(ops));

        Assert.Equal(new[] { "a", "b", "c" }, Query(idx, 4, 4, 6, 6));
        Assert.Empty(Query(idx, 100, 100, 200, 200));
    }
}
