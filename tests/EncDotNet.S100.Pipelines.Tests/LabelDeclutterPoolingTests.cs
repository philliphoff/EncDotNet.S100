using System;
using System.Collections.Generic;
using System.Linq;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Skia.Scene;
using SkiaSharp;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the render-thread-confined buffer pooling in
/// <see cref="LabelDeclutterer"/> (#332 lever a). The declutterer reuses its
/// suppressed set, screen-rect index, and text scratch across frames (clearing,
/// not reallocating) to remove the per-frame allocation that scaled with op
/// count. These tests pin two properties pooling must preserve:
/// <list type="bullet">
/// <item>determinism — a reused (cleared) instance yields the identical
/// suppression set a fresh instance would, with no stale state leaking between
/// frames; and</item>
/// <item>zero steady-state allocation — after warm-up, repeated declutter of a
/// dense scene allocates effectively nothing.</item>
/// </list>
/// </summary>
public class LabelDeclutterPoolingTests
{
    private static TextPaintOp Text(string id, double lon, double lat) =>
        new()
        {
            FeatureReference = id,
            World = WebMercator.FromLonLat(lon, lat),
            Text = "LABEL",
            FontSizePx = 12,
            ForeColor = new RgbaColor(0, 0, 0, 255),
        };

    private static Viewport Centred(double lon, double lat, double span, int px = 512) =>
        new()
        {
            MinLongitude = lon - span / 2,
            MaxLongitude = lon + span / 2,
            MinLatitude = lat - span / 2,
            MaxLatitude = lat + span / 2,
            WidthPixels = px,
            HeightPixels = px,
            ScaleDenominator = 50000,
        };

    private static IReadOnlySet<TextPaintOp> Declutter(LabelDeclutterer d, VectorScene scene, Viewport vp)
    {
        var cull = new SKRect(-256, -256, vp.WidthPixels + 256, vp.HeightPixels + 256);
        return d.Declutter(scene, vp, cull, honorScaleVisibility: false,
            anchorRotationDegrees: 0, centerX: vp.WidthPixels / 2f, centerY: vp.HeightPixels / 2f);
    }

    /// <summary>A clustered scene where several labels overlap so some are suppressed.</summary>
    private static VectorScene ClusteredScene()
    {
        var ops = new List<PaintOp>();
        // Ten labels stacked on nearly the same anchor — most collide and are suppressed.
        for (int i = 0; i < 10; i++)
            ops.Add(Text("c" + i, 10.0 + i * 1e-5, 0.0));
        // One well-separated label that always survives.
        ops.Add(Text("far", 10.20, 0.10));
        return new VectorScene(ops);
    }

    [Fact]
    public void ReusedInstance_SameScene_MatchesFreshInstance()
    {
        var scene = ClusteredScene();
        var vp = Centred(10.0, 0.0, 0.4);

        // Fresh instance, materialised immediately (the returned set is reused).
        using var fresh = new LabelDeclutterer();
        var expected = Declutter(fresh, scene, vp).Select(t => t.FeatureReference).OrderBy(s => s).ToArray();

        // A second instance reused across several frames must match every time.
        using var reused = new LabelDeclutterer();
        for (int frame = 0; frame < 5; frame++)
        {
            var got = Declutter(reused, scene, vp).Select(t => t.FeatureReference).OrderBy(s => s).ToArray();
            Assert.Equal(expected, got);
        }
    }

    [Fact]
    public void ReusedInstance_AlternatingScenes_NoStaleStateLeaks()
    {
        var dense = ClusteredScene();
        var sparse = new VectorScene(new List<PaintOp> { Text("only", 10.0, 0.0) });
        var vp = Centred(10.0, 0.0, 0.4);

        using var fresh1 = new LabelDeclutterer();
        var denseExpected = Declutter(fresh1, dense, vp).Select(t => t.FeatureReference).OrderBy(s => s).ToArray();
        using var fresh2 = new LabelDeclutterer();
        var sparseExpected = Declutter(fresh2, sparse, vp).Count; // a lone label never collides → 0

        // Alternate dense/sparse on one reused instance: clearing must wipe the
        // prior frame's footprints so the sparse frame is never polluted by the
        // dense frame's obstacles (and vice-versa).
        using var reused = new LabelDeclutterer();
        for (int frame = 0; frame < 4; frame++)
        {
            var d = Declutter(reused, dense, vp).Select(t => t.FeatureReference).OrderBy(s => s).ToArray();
            Assert.Equal(denseExpected, d);

            var s = Declutter(reused, sparse, vp).Count;
            Assert.Equal(sparseExpected, s);
            Assert.Equal(0, s);
        }
    }

    [Fact]
    public void ReusedInstance_DenseScene_ZeroSteadyStateAllocation()
    {
        // A dense grid of mostly-colliding labels so both index buckets and the
        // suppressed set are heavily exercised every frame.
        var ops = new List<PaintOp>();
        for (int i = 0; i < 2000; i++)
            ops.Add(Text("g" + i, 10.0 + (i % 50) * 1e-4, 0.0 + (i / 50) * 1e-4));
        var scene = new VectorScene(ops);
        var vp = Centred(10.0, 0.0, 0.4);

        using var d = new LabelDeclutterer();

        // Warm up: let the index dictionary, bucket-list pool, and suppressed
        // HashSet reach steady capacity (first frames legitimately allocate).
        for (int i = 0; i < 20; i++)
            Declutter(d, scene, vp);

        const int frames = 50;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < frames; i++)
            Declutter(d, scene, vp);
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)frames;

        // A fresh-instance declutter of this scene allocates hundreds of KB per
        // frame; pooled steady-state must be a tiny fraction of that. Allow a
        // small slack for incidental boxing/JIT noise.
        Assert.True(perFrame < 4096, $"steady-state allocation {perFrame:F0} B/frame exceeds budget");
    }
}
