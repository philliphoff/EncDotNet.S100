using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Mapsui;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Tests for <see cref="BaseSpatialIndex"/> (#332 cold tile-gen — base-plane
/// scoping, a perf line under #347). The index bounds the off-thread
/// <c>RasterizeTile</c> walk to the base ops intersecting a tile (+ gutter)
/// while guaranteeing it never drops an op the whole-cell walk would draw: a
/// query is a conservative <b>superset</b> (bbox intersection only) returned in
/// original draw (priority/z) order, de-duplicated even though an op straddles
/// many grid cells, and a query covering the whole extent reproduces the full
/// op list exactly.
/// </summary>
public class BaseSpatialIndexTests
{
    private static AreaPaintOp Area(string id, double x0, double y0, double x1, double y1) =>
        new()
        {
            FeatureReference = id,
            WorldShell = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0) },
            Fill = new RgbaColor(200, 200, 255, 255),
            OutlineColor = new RgbaColor(0, 0, 0, 255),
            OutlineWidthPx = 1.0,
        };

    private static LinePaintOp Line(string id, params (double X, double Y)[] pts) =>
        new()
        {
            FeatureReference = id,
            World = pts,
            Color = new RgbaColor(0, 0, 0, 255),
            WidthPx = 1.0,
        };

    private static List<string> Query(BaseSpatialIndex idx,
        double minX, double minY, double maxX, double maxY) =>
        idx.Query(minX, minY, maxX, maxY).Select(o => o.FeatureReference!).ToList();

    [Fact]
    public void Query_CoveringWholeExtent_ReturnsAllOpsInOriginalOrder()
    {
        // A 10×10 grid of small distinct area ops, interleaved with lines.
        var ops = new List<PaintOp>();
        for (int i = 0; i < 100; i++)
        {
            double x = (i % 10) * 100.0;
            double y = (i / 10) * 100.0;
            ops.Add(i % 2 == 0
                ? Area("a" + i, x, y, x + 10, y + 10)
                : Line("l" + i, (x, y), (x + 10, y + 10)));
        }
        var idx = new BaseSpatialIndex(new VectorScene(ops));

        var got = Query(idx, -1e6, -1e6, 1e6, 1e6);

        // Degrades to the whole-cell walk, in the exact original draw order.
        Assert.Equal(ops.Select(o => o.FeatureReference).ToList(), got);
    }

    [Fact]
    public void Query_SubRegion_ReturnsOnlyIntersectingOpsInOriginalOrder()
    {
        var ops = new List<PaintOp>
        {
            Area("a", 0, 0, 20, 20),
            Area("b", 500, 500, 520, 520),
            Area("c", 30, 30, 60, 60),
            Area("d", 950, 0, 980, 30),
            Line("e", (10, 10), (40, 40)),
        };
        var idx = new BaseSpatialIndex(new VectorScene(ops));

        // A box around the lower-left cluster: a, c, e (original order); not b/d.
        var got = Query(idx, -10, -10, 70, 70);

        Assert.Equal(new[] { "a", "c", "e" }, got);
    }

    [Fact]
    public void Query_IsConservativeSupersetOfBruteForceBboxIntersect()
    {
        // Random ops + random queries: the index result must equal the brute-force
        // bbox-intersection set exactly (a tight superset — never a false negative).
        var rng = new System.Random(1234);
        var ops = new List<PaintOp>();
        for (int i = 0; i < 400; i++)
        {
            double x = rng.NextDouble() * 10_000;
            double y = rng.NextDouble() * 10_000;
            double w = rng.NextDouble() * 500;
            double h = rng.NextDouble() * 500;
            ops.Add(Area("a" + i, x, y, x + w, y + h));
        }
        var scene = new VectorScene(ops);
        var idx = new BaseSpatialIndex(scene);

        for (int q = 0; q < 100; q++)
        {
            double qx = rng.NextDouble() * 10_000;
            double qy = rng.NextDouble() * 10_000;
            double qw = rng.NextDouble() * 2_000;
            double qh = rng.NextDouble() * 2_000;
            double qx1 = qx + qw, qy1 = qy + qh;

            var expected = new List<string>();
            foreach (var op in ops.Cast<AreaPaintOp>())
            {
                double minX = op.WorldShell.Min(p => p.X), maxX = op.WorldShell.Max(p => p.X);
                double minY = op.WorldShell.Min(p => p.Y), maxY = op.WorldShell.Max(p => p.Y);
                if (!(maxX < qx || minX > qx1 || maxY < qy || minY > qy1))
                    expected.Add(op.FeatureReference!);
            }

            var got = Query(idx, qx, qy, qx1, qy1);
            Assert.Equal(expected, got);
        }
    }

    [Fact]
    public void Query_OpSpanningManyCells_ReturnedExactlyOnce()
    {
        // A huge area op covers the whole extent (lands in every grid cell); the
        // de-dup must still return it once, alongside a few small ops.
        var ops = new List<PaintOp>
        {
            Area("big", 0, 0, 10_000, 10_000),
            Area("s1", 100, 100, 120, 120),
            Area("s2", 5_000, 5_000, 5_020, 5_020),
        };
        var idx = new BaseSpatialIndex(new VectorScene(ops));

        var got = Query(idx, 0, 0, 10_000, 10_000);

        Assert.Equal(new[] { "big", "s1", "s2" }, got);
        Assert.Equal(1, got.Count(r => r == "big"));
    }

    [Fact]
    public void Query_Disjoint_ReturnsNothing()
    {
        var ops = new List<PaintOp> { Area("a", 0, 0, 10, 10), Area("b", 1000, 1000, 1010, 1010) };
        var idx = new BaseSpatialIndex(new VectorScene(ops));

        Assert.Empty(Query(idx, 400, 400, 500, 500));
    }

    [Fact]
    public void Query_EmptyScene_ReturnsNothing()
    {
        var idx = new BaseSpatialIndex(new VectorScene(new List<PaintOp>()));
        Assert.Empty(Query(idx, -1e6, -1e6, 1e6, 1e6));
    }

    [Fact]
    public void Query_GeometrylessOp_IsAlwaysCandidate()
    {
        // A base op whose geometry the index cannot bound (here an area with an
        // empty shell) must never be dropped: it is served on every query (an
        // always-candidate), keeping its original draw-order slot.
        var ghost = new AreaPaintOp
        {
            FeatureReference = "ghost",
            WorldShell = System.Array.Empty<(double, double)>(),
            Fill = new RgbaColor(0, 0, 0, 0),
        };
        var ops = new List<PaintOp>
        {
            Area("a", 0, 0, 10, 10),
            ghost,
            Area("b", 1000, 1000, 1010, 1010),
        };
        var idx = new BaseSpatialIndex(new VectorScene(ops));

        // A query far from any real geometry still includes the geometry-less op.
        Assert.Equal(new[] { "ghost" }, Query(idx, 50_000, 50_000, 50_100, 50_100));
        // And it keeps its draw-order slot when real ops also match.
        Assert.Equal(new[] { "a", "ghost" }, Query(idx, -10, -10, 20, 20));
    }
}
