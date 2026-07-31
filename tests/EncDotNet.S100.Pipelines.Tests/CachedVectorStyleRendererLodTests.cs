using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Behaviour of the precomputed line LOD pyramid path in
/// <see cref="CachedVectorStyleRenderer"/> when
/// <see cref="RenderingOptimizations.PrecomputedLineLodEnabled"/> is on:
/// pans within a zoom band still hit the SKPath cache, band changes miss
/// but re-use the pyramid (no fresh simplification pass), the pyramid
/// cache is populated per feature, and toggling the flag off restores the
/// original raw-resolution key so no state is orphaned.
/// </summary>
public class CachedVectorStyleRendererLodTests : IDisposable
{
    private static readonly RenderService Service = new();
    private const int CanvasSize = 256;

    private readonly bool _wasEnabled;

    public CachedVectorStyleRendererLodTests()
    {
        _wasEnabled = RenderingOptimizations.PrecomputedLineLodEnabled;
    }

    public void Dispose()
    {
        RenderingOptimizations.PrecomputedLineLodEnabled = _wasEnabled;
    }

    private static GeometryFeature MakeLineFeature(int vertices)
    {
        // A wiggly line spanning [0..1000] on both axes so simplification at
        // real tolerances actually drops vertices.
        var coords = new Coordinate[vertices];
        for (var i = 0; i < vertices; i++)
        {
            var t = i / (double)(vertices - 1);
            coords[i] = new Coordinate(
                1000.0 * t,
                500.0 + Math.Sin(t * Math.PI * 8) * 5.0);
        }
        var feature = new GeometryFeature(new LineString(coords));
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen { Color = Color.Black, Width = 2.0 },
            Outline = null,
            Fill = null,
        });
        return feature;
    }

    /// <summary>
    /// Attaches a real Core <see cref="LineLodPyramid"/> to
    /// <paramref name="feature"/> under
    /// <see cref="CachedVectorStyleRenderer.LineLodPyramidKey"/> so the
    /// fast-line path takes the LOD branch in PR-3. The pyramid vertex count
    /// matches the LineString so the renderer's passthrough-count guard
    /// succeeds; the specific WGS-84 values are irrelevant to the SKPath
    /// cache key, only to the projected pixels (which these tests don't
    /// inspect).
    /// </summary>
    private static void AttachPyramid(GeometryFeature feature, int vertices)
    {
        var geo = new GeoPosition[vertices];
        for (var i = 0; i < vertices; i++)
        {
            var t = i / (double)(vertices - 1);
            geo[i] = new GeoPosition(
                Latitude: 0.01 * Math.Sin(t * Math.PI * 8),
                Longitude: 0.1 * t);
        }
        var pyramid = LineLodPyramid.Build(geo, LineLodTolerances.HalfOctaveDefault);
        feature[CachedVectorStyleRenderer.LineLodPyramidKey] = pyramid;
    }

    private static Mapsui.Viewport ViewportFor(double centerX, double centerY, double resolution) =>
        new(centerX, centerY, resolution, rotation: 0, width: CanvasSize, height: CanvasSize);

    private static void Draw(CachedVectorStyleRenderer renderer, Mapsui.Viewport viewport, GeometryFeature feature)
    {
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        using var surface = SKSurface.Create(new SKImageInfo(CanvasSize, CanvasSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        renderer.Draw(surface.Canvas, viewport, layer, feature, style, Service, 0);
        surface.Canvas.Flush();
    }

    [Fact]
    public void LodEnabled_PansWithinBandReuseSinglePath()
    {
        RenderingOptimizations.PrecomputedLineLodEnabled = true;
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(vertices: 200);
        AttachPyramid(feature, vertices: 200);

        // Three pans at one resolution — the LOD bucket is stable so exactly
        // one path is cached.
        Draw(renderer, ViewportFor(500, 500, 32.0), feature);
        Draw(renderer, ViewportFor(540, 500, 32.0), feature);
        Draw(renderer, ViewportFor(580, 540, 32.0), feature);

        Assert.Equal(1, renderer.CachedPathCount);
    }

    [Fact]
    public void LodEnabled_TinyResolutionChangeWithinBandStillHits()
    {
        RenderingOptimizations.PrecomputedLineLodEnabled = true;
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(vertices: 200);
        AttachPyramid(feature, vertices: 200);

        // Two "pans" at almost-identical resolutions that differ only in the
        // low bits (as happens when Mapsui accumulates float error). Without
        // the LOD path this would miss the cache; with LOD, the same lod
        // bucket keys both.
        Draw(renderer, ViewportFor(500, 500, 32.0), feature);
        Draw(renderer, ViewportFor(500, 500, 32.0 + 1e-9), feature);

        Assert.Equal(1, renderer.CachedPathCount);
    }

    [Fact]
    public void LodEnabled_LargeBandChangeMissesButReusesPyramid()
    {
        RenderingOptimizations.PrecomputedLineLodEnabled = true;
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(vertices: 200);
        AttachPyramid(feature, vertices: 200);

        Draw(renderer, ViewportFor(500, 500, 512.0), feature);
        var afterFirst = renderer.CachedPathCount;

        Draw(renderer, ViewportFor(500, 500, 32.0), feature);
        var afterSecond = renderer.CachedPathCount;

        Assert.Equal(1, afterFirst);
        Assert.Equal(2, afterSecond);
    }

    [Fact]
    public void ToggleFlag_ClearsCache()
    {
        RenderingOptimizations.PrecomputedLineLodEnabled = true;
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(vertices: 200);
        AttachPyramid(feature, vertices: 200);

        Draw(renderer, ViewportFor(500, 500, 32.0), feature);
        Assert.Equal(1, renderer.CachedPathCount);

        // Toggle off — next draw must invalidate the cache (the key shape
        // just changed from lod bucket to raw resolution bits).
        RenderingOptimizations.PrecomputedLineLodEnabled = false;
        Draw(renderer, ViewportFor(500, 500, 32.0), feature);
        Assert.Equal(1, renderer.CachedPathCount);
    }

    [Fact]
    public void LodEnabled_ButNoAttachedPyramid_BuildsInlineCartesianPyramid()
    {
        // PR-3 (coordinator binding #3): when the LOD flag is on but the
        // feature was not seeded with a pyramid at open (non-S-101, missing
        // shared cache, multi-part), the renderer falls back to PR-2's
        // on-demand Cartesian DP over the already-projected NTS coords —
        // still producing an LOD bucket key. Tiny resolution changes within
        // one band therefore reuse the cache. This preserves the PR-2 warm
        // frame behaviour so the memo isn't lost when attachments are
        // absent.
        RenderingOptimizations.PrecomputedLineLodEnabled = true;
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(vertices: 200);
        // Deliberately NO AttachPyramid — miss-path exercised.

        Draw(renderer, ViewportFor(500, 500, 32.0), feature);
        Draw(renderer, ViewportFor(500, 500, 32.0 + 1e-9), feature);

        Assert.Equal(1, renderer.CachedPathCount);
    }

    [Fact]
    public void LodEnabled_AttachedPyramidVertexMismatch_FallsBackToInlineCartesianPyramid()
    {
        // PR-3 guard: an attached pyramid whose passthrough vertex count
        // differs from the LineString's must NOT be used (dateline splits,
        // deduplication, etc. would give the wrong geometry). The renderer
        // silently falls back to on-demand Cartesian DP over the NTS coords
        // — still LOD-keyed, matching PR-2 behaviour.
        RenderingOptimizations.PrecomputedLineLodEnabled = true;
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(vertices: 200);
        AttachPyramid(feature, vertices: 150); // vertex-count mismatch

        Draw(renderer, ViewportFor(500, 500, 32.0), feature);
        Draw(renderer, ViewportFor(500, 500, 32.0 + 1e-9), feature);

        Assert.Equal(1, renderer.CachedPathCount);
    }

    [Fact]
    public void LodDisabled_MatchesLegacyBehaviour()
    {
        RenderingOptimizations.PrecomputedLineLodEnabled = false;
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(vertices: 200);

        Draw(renderer, ViewportFor(500, 500, 32.0), feature);
        Draw(renderer, ViewportFor(500, 500, 32.0), feature);

        // Two draws at identical resolution -> one cached path.
        Assert.Equal(1, renderer.CachedPathCount);

        // Zoom -> new cache entry keyed by resolution bits.
        Draw(renderer, ViewportFor(500, 500, 64.0), feature);
        Assert.Equal(2, renderer.CachedPathCount);
    }
}
