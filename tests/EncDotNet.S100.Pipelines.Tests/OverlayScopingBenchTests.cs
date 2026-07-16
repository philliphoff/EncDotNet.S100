using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Committed micro-measurement guarding the #332 overlay scoping invariants:
/// querying a small viewport over a large (dense-cell-scale) overlay returns a
/// candidate set proportional to the <i>visible</i> ops, not the whole cell, and
/// a steady-state query allocates nothing. This is the regression guard for the
/// synthetic curve that justified lever b (viewport-scoping) — on a 50k-op cell
/// the whole-cell walk both touched every op and allocated per frame; scoping
/// bounds both to the visible set.
/// </summary>
public class OverlayScopingBenchTests
{
    private static PointPaintOp Point(int i, double x, double y) =>
        new()
        {
            FeatureReference = "p" + i,
            World = (x, y),
            FallbackColor = new RgbaColor(220, 20, 60, 255),
            FallbackScale = 1.0,
        };

    /// <summary>Builds a dense N×N grid of point ops 100 m apart (EPSG:3857).</summary>
    private static VectorScene DenseGrid(int side, out double spacing)
    {
        spacing = 100.0;
        var ops = new List<PaintOp>(side * side);
        for (int gy = 0; gy < side; gy++)
            for (int gx = 0; gx < side; gx++)
                ops.Add(Point(gy * side + gx, gx * spacing, gy * spacing));
        return new VectorScene(ops);
    }

    [Fact]
    public void Query_OnDenseCell_ReturnsVisibleSubsetNotWholeCell()
    {
        // 200×200 = 40 000 ops — the #332 dense regime the trial cells never reach.
        var scene = DenseGrid(200, out double spacing);
        var index = new OverlaySpatialIndex(scene);

        // A viewport covering ~10×10 cells worth of world.
        double w = 10 * spacing;
        var scratch = new List<int>();
        var results = new List<PaintOp>();
        index.Query(0, 0, w, w, scratch, results);

        // Roughly an 11×11 block of anchors (inclusive bounds) — two orders of
        // magnitude below the 40 000-op whole cell.
        Assert.InRange(results.Count, 80, 160);
        Assert.True(results.Count < scene.Ops.Count / 100,
            $"scoping returned {results.Count} of {scene.Ops.Count} — not O(visible)");
    }

    [Fact]
    public void Query_SteadyState_AllocatesNothing()
    {
        var scene = DenseGrid(200, out double spacing);
        var index = new OverlaySpatialIndex(scene);
        double w = 10 * spacing;

        var scratch = new List<int>();
        var results = new List<PaintOp>();

        // Warm up: let the reusable buffers grow to their steady-state capacity.
        for (int i = 0; i < 50; i++)
            index.Query(0, 0, w, w, scratch, results);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
            index.Query(0, 0, w, w, scratch, results);
        long delta = GC.GetAllocatedBytesForCurrentThread() - before;

        // 200 queries over a 40k-op cell must not allocate (buffers are reused).
        Assert.True(delta < 4096, $"steady-state query allocated {delta} B over 200 frames");
    }

    [Fact]
    public void Query_PannedAcrossCell_CandidateCountStaysBounded()
    {
        // As the viewport pans across the whole cell, the candidate count must
        // track the (constant-size) viewport, never the cell — the property that
        // makes the per-frame walk O(N_visible).
        var scene = DenseGrid(200, out double spacing);
        var index = new OverlaySpatialIndex(scene);
        double w = 10 * spacing;
        var scratch = new List<int>();
        var results = new List<PaintOp>();

        int max = 0;
        for (int step = 0; step < 180; step += 20)
        {
            double o = step * spacing;
            index.Query(o, o, o + w, o + w, scratch, results);
            max = Math.Max(max, results.Count);
        }

        Assert.True(max < 200, $"panned candidate peak {max} not viewport-bounded");
    }
}
