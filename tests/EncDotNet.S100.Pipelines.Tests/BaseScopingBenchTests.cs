using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Committed micro-measurement guarding the #332 cold tile-gen base-plane
/// scoping invariant (perf line under #347): rasterising a single tile must walk
/// only the base ops intersecting that tile (+ gutter), not the whole cell. The
/// shipped <c>RasterizeTile</c> previously replayed the entire base scene for
/// every tile; <see cref="BaseSpatialIndex"/> implements the long-specified but
/// never-built base-plane index (design §3.3) so an interior tile-sized query
/// returns a candidate set proportional to the <i>covering</i> ops — the
/// structural source of the measured cold-tile reduction. Fidelity is unchanged:
/// the query is a conservative superset and the renderer still applies the exact
/// per-op scale cull and pixel clip.
/// </summary>
public class BaseScopingBenchTests
{
    private static AreaPaintOp Cell(int i, double x, double y, double size) =>
        new()
        {
            FeatureReference = "a" + i,
            WorldShell = new[] { (x, y), (x + size, y), (x + size, y + size), (x, y + size), (x, y) },
            Fill = new RgbaColor(200, 200, 255, 255),
            OutlineColor = new RgbaColor(0, 0, 0, 255),
            OutlineWidthPx = 1.0,
        };

    /// <summary>
    /// Builds a dense <paramref name="side"/>×<paramref name="side"/> grid of
    /// small non-overlapping area ops <paramref name="spacing"/> metres apart
    /// (EPSG:3857), approximating a dense-cell base plane.
    /// </summary>
    private static VectorScene DenseAreaGrid(int side, out double spacing)
    {
        spacing = 100.0;
        var ops = new List<PaintOp>(side * side);
        for (int gy = 0; gy < side; gy++)
            for (int gx = 0; gx < side; gx++)
                ops.Add(Cell(gy * side + gx, gx * spacing, gy * spacing, spacing * 0.8));
        return new VectorScene(ops);
    }

    [Fact]
    public void Query_OnDenseCell_ReturnsCoveringSubsetNotWholeCell()
    {
        // 200×200 = 40 000 ops — a dense base plane.
        var scene = DenseAreaGrid(200, out double spacing);
        var index = new BaseSpatialIndex(scene);

        // A tile-sized world window covering ~10×10 cells.
        double w = 10 * spacing;
        var got = index.Query(0, 0, w, w);

        // Roughly an 11×11 block of cells (inclusive bounds) — two orders of
        // magnitude below the 40 000-op whole cell.
        Assert.InRange(got.Count, 80, 200);
        Assert.True(got.Count < scene.Ops.Count / 100,
            $"scoping returned {got.Count} of {scene.Ops.Count} — not O(covering)");
    }

    [Fact]
    public void Query_PannedAcrossCell_CandidateCountStaysBounded()
    {
        // As the tile window pans across the whole cell, the candidate count must
        // track the (constant-size) tile, never the cell — the property that makes
        // each cold RasterizeTile O(N_covering) instead of O(N_cell).
        var scene = DenseAreaGrid(200, out double spacing);
        var index = new BaseSpatialIndex(scene);
        double w = 10 * spacing;

        int max = 0;
        for (int step = 0; step < 180; step += 20)
        {
            double o = step * spacing;
            max = Math.Max(max, index.Query(o, o, o + w, o + w).Count);
        }

        Assert.True(max < 250, $"panned candidate peak {max} not tile-bounded");
    }
}
