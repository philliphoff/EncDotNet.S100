using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;
using System.Threading;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Validates the translation-invariant vector path cache and resolution-aware
/// line simplification in <see cref="CachedVectorStyleRenderer"/>: it must
/// build a geometry's <see cref="SKPath"/> once per (feature, resolution) and
/// re-use it across pans, render byte-for-byte like Mapsui's
/// <see cref="VectorStyleRenderer"/> when simplification is disabled, drop only
/// sub-pixel vertices when it is enabled (preserving endpoints), and delegate
/// geometries it does not fast-path.
/// </summary>
public class CachedVectorStyleRendererTests
{
    private static readonly RenderService Service = new();

    /// <summary>
    /// Enables the env-only, default-off polygon simplification knob for the
    /// duration of a test and restores it on dispose. Skips no-op when the env
    /// var <c>S100_VECTOR_POLYGON_SIMPLIFY</c> pins the flag (the programmatic
    /// setter is ignored when env-explicit).
    /// </summary>
    private sealed class PolygonSimplificationScope : IDisposable
    {
        private readonly bool _original = RenderingOptimizations.PolygonSimplificationEnabled;

        public PolygonSimplificationScope() => RenderingOptimizations.PolygonSimplificationEnabled = true;

        public bool Enabled => RenderingOptimizations.PolygonSimplificationEnabled;

        public void Dispose() => RenderingOptimizations.PolygonSimplificationEnabled = _original;
    }

    // World coordinates roughly span [0,1000] in both axes so a 1.0 resolution
    // maps them onto a 1000 px canvas with the centre at (500, 500).
    private const int CanvasSize = 256;

    private static GeometryFeature MakeLineFeature(IEnumerable<Coordinate> coords, long id)
    {
        // Each GeometryFeature is auto-assigned a stable Id; reusing the same
        // instance across renders keeps the cache key constant (the id arg is
        // only for test readability).
        _ = id;
        var feature = new GeometryFeature(new LineString(coords.ToArray()));
        feature.Styles.Add(new VectorStyle { Line = new Pen { Color = Color.Black, Width = 2.0 }, Outline = null, Fill = null });
        return feature;
    }

    private static Mapsui.Viewport ViewportFor(double centerX, double centerY, double resolution) =>
        new(centerX, centerY, resolution, rotation: 0, width: CanvasSize, height: CanvasSize);

    private static Mapsui.Viewport ViewportFor(double centerX, double centerY, double resolution, double rotation) =>
        new(centerX, centerY, resolution, rotation, width: CanvasSize, height: CanvasSize);

    /// <summary>
    /// A solid-filled square <paramref name="shell"/> with a single square hole
    /// centred on the world origin of rotation. The shell deliberately overflows
    /// the viewport (the case Mapsui's per-frame clipper used to mangle), so the
    /// only way the hole stays transparent is if the cached even-odd path is
    /// drawn whole and clipped at raster time.
    /// </summary>
    private static GeometryFeature MakeHoledPolygonFeature(long id)
    {
        _ = id;
        var shell = new LinearRing(new[]
        {
            new Coordinate(0, 0), new Coordinate(1000, 0),
            new Coordinate(1000, 1000), new Coordinate(0, 1000), new Coordinate(0, 0),
        });
        var hole = new LinearRing(new[]
        {
            new Coordinate(450, 450), new Coordinate(550, 450),
            new Coordinate(550, 550), new Coordinate(450, 550), new Coordinate(450, 450),
        });
        var feature = new GeometryFeature(new Polygon(shell, new[] { hole }));
        feature.Styles.Add(new VectorStyle
        {
            Fill = new Brush(Color.Black),
            Outline = null,
            Line = null,
        });
        return feature;
    }

    private static byte[] RenderToPng(Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(CanvasSize, CanvasSize, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        draw(surface.Canvas);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static int OpaqueDarkPixels(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.Alpha > 128 && p.Red < 80 && p.Green < 80 && p.Blue < 80)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Builds the closed coordinate ring of an axis-aligned square whose edges
    /// are subdivided into <paramref name="pointsPerEdge"/> <i>collinear</i>
    /// vertices. The dense collinear runs are exactly the sub-pixel detail a
    /// topology-preserving simplifier collapses to the four corners, so the
    /// build is a deterministic vertex-reduction fixture.
    /// </summary>
    private static Coordinate[] DenseSquareCoords(double minX, double minY, double size, int pointsPerEdge)
    {
        var maxX = minX + size;
        var maxY = minY + size;
        var coords = new List<Coordinate>(pointsPerEdge * 4 + 1);
        for (var i = 0; i < pointsPerEdge; i++) coords.Add(new Coordinate(minX + size * i / pointsPerEdge, minY));
        for (var i = 0; i < pointsPerEdge; i++) coords.Add(new Coordinate(maxX, minY + size * i / pointsPerEdge));
        for (var i = 0; i < pointsPerEdge; i++) coords.Add(new Coordinate(maxX - size * i / pointsPerEdge, maxY));
        for (var i = 0; i < pointsPerEdge; i++) coords.Add(new Coordinate(minX, maxY - size * i / pointsPerEdge));
        coords.Add(coords[0]);
        return coords.ToArray();
    }

    private static GeometryFeature MakeDenseSquareFeature(int pointsPerEdge, double minX = 0, double minY = 0, double size = 1000)
    {
        var ring = new LinearRing(DenseSquareCoords(minX, minY, size, pointsPerEdge));
        var feature = new GeometryFeature(new Polygon(ring));
        feature.Styles.Add(new VectorStyle { Fill = new Brush(Color.Black), Outline = null, Line = null });
        return feature;
    }

    /// <summary>
    /// A dense-ringed version of <see cref="MakeHoledPolygonFeature"/>: a 1000-unit
    /// square shell with a centred 100-unit square hole, each ring subdivided into
    /// collinear vertices so simplification has detail to drop while topology
    /// (the hole) must be preserved.
    /// </summary>
    private static GeometryFeature MakeDenseHoledSquareFeature(int pointsPerEdge)
    {
        var shell = new LinearRing(DenseSquareCoords(0, 0, 1000, pointsPerEdge));
        var hole = new LinearRing(DenseSquareCoords(450, 450, 100, pointsPerEdge));
        var feature = new GeometryFeature(new Polygon(shell, new[] { hole }));
        feature.Styles.Add(new VectorStyle { Fill = new Brush(Color.Black), Outline = null, Line = null });
        return feature;
    }

    /// <summary>A regular polygon approximating a circle: vertices are not
    /// collinear, so larger tolerances drop progressively more of them.</summary>
    private static GeometryFeature MakeDenseCircleFeature(int points, double cx = 500, double cy = 500, double radius = 400)
    {
        var coords = new List<Coordinate>(points + 1);
        for (var i = 0; i < points; i++)
        {
            var a = 2 * Math.PI * i / points;
            coords.Add(new Coordinate(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
        }
        coords.Add(coords[0]);
        var feature = new GeometryFeature(new Polygon(new LinearRing(coords.ToArray())));
        feature.Styles.Add(new VectorStyle { Fill = new Brush(Color.Black), Outline = null, Line = null });
        return feature;
    }

    [Fact]
    public void Pan_AtConstantResolution_BuildsPathOnce()
    {
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeLineFeature(
            new[] { new Coordinate(100, 100), new Coordinate(900, 900) }, id: 1);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        RenderToPng(canvas => renderer.Draw(canvas, ViewportFor(500, 500, 1.0), layer, feature, style, Service, 0));
        RenderToPng(canvas => renderer.Draw(canvas, ViewportFor(540, 500, 1.0), layer, feature, style, Service, 0));
        RenderToPng(canvas => renderer.Draw(canvas, ViewportFor(580, 540, 1.0), layer, feature, style, Service, 0));

        // Three pans at one resolution -> exactly one cached path.
        Assert.Equal(1, renderer.CachedPathCount);

        // A zoom (new resolution) adds a second entry.
        RenderToPng(canvas => renderer.Draw(canvas, ViewportFor(500, 500, 2.0), layer, feature, style, Service, 0));
        Assert.Equal(2, renderer.CachedPathCount);
    }

    [Fact]
    public void DisabledSimplification_MatchesMapsuiOutput()
    {
        var feature = MakeLineFeature(
            new[]
            {
                new Coordinate(100, 200), new Coordinate(300, 700),
                new Coordinate(550, 250), new Coordinate(820, 760),
            },
            id: 2);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 3.0);

        var cached = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var mapsui = new VectorStyleRenderer();

        var cachedPng = RenderToPng(c => cached.Draw(c, viewport, layer, feature, style, Service, 0));
        var mapsuiPng = RenderToPng(c => mapsui.Draw(c, viewport, layer, feature, style, Service, 0));

        Assert.Equal(mapsuiPng, cachedPng);
    }

    [Fact]
    public void Simplification_PreservesEndpoints()
    {
        // A line whose interior vertices are all sub-pixel apart at this
        // resolution; only the endpoints carry geometric meaning.
        var coords = new List<Coordinate> { new(100, 500) };
        for (var i = 1; i < 400; i++)
        {
            coords.Add(new Coordinate(100 + i * 0.05, 500 + ((i % 2) * 0.05)));
        }
        coords.Add(new Coordinate(900, 500));

        var feature = MakeLineFeature(coords, id: 3);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 1.0);

        var simplified = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0.6);
        var exact = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);

        using var simplifiedBmp = SKBitmap.Decode(
            RenderToPng(c => simplified.Draw(c, viewport, layer, feature, style, Service, 0)));
        using var exactBmp = SKBitmap.Decode(
            RenderToPng(c => exact.Draw(c, viewport, layer, feature, style, Service, 0)));

        // Both must draw the same horizontal stroke (endpoints preserved): the
        // dark-pixel counts should be within a few percent of each other.
        var s = OpaqueDarkPixels(simplifiedBmp);
        var e = OpaqueDarkPixels(exactBmp);
        Assert.True(s > 0 && e > 0, $"expected a visible stroke in both (simplified={s}, exact={e})");
        Assert.True(Math.Abs(s - e) <= e * 0.1, $"stroke coverage differs too much: simplified={s}, exact={e}");
    }

    [Fact]
    public void Point_IsDelegatedToInnerRenderer()
    {
        var inner = new CountingRenderer();
        var renderer = new CachedVectorStyleRenderer(inner);
        var feature = new GeometryFeature(new Point(500, 500));
        var style = new SymbolStyle();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        RenderToPng(c => renderer.Draw(c, ViewportFor(500, 500, 1.0), layer, feature, style, Service, 0));

        Assert.Equal(1, inner.DrawCalls);
        Assert.Equal(0, renderer.CachedPathCount);
    }

    [Fact]
    public void Line_IsFastPathed_NotDelegated()
    {
        var inner = new CountingRenderer();
        var renderer = new CachedVectorStyleRenderer(inner, simplifyTolerancePx: 0);
        var feature = MakeLineFeature(
            new[] { new Coordinate(100, 100), new Coordinate(900, 900) }, id: 4);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        var handled = false;
        RenderToPng(c => handled = renderer.Draw(c, ViewportFor(500, 500, 1.0), layer, feature, style, Service, 0));

        Assert.True(handled);
        Assert.Equal(0, inner.DrawCalls);
        Assert.Equal(1, renderer.CachedPathCount);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(30.0)]
    [InlineData(45.0)]
    [InlineData(90.0)]
    public void Polygon_WithHole_KeepsHoleTransparent_UnderRotation(double rotation)
    {
        // Regression: rotated S-101 land areas (skin-of-the-earth polygons with
        // island/lake holes) must keep their holes cut. The cached even-odd path
        // is rotation-independent; only the per-frame draw matrix changes, so the
        // hole at the centre of rotation must stay background at every angle.
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var feature = MakeHoledPolygonFeature(id: 5);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        // Rotation pivots about the viewport centre, which is the world point
        // (500,500) — the hole's centre — so the hole stays at the screen centre.
        var viewport = ViewportFor(500, 500, 1.0, rotation);

        using var bmp = SKBitmap.Decode(
            RenderToPng(c => renderer.Draw(c, viewport, layer, feature, style, Service, 0)));

        var centre = bmp.GetPixel(CanvasSize / 2, CanvasSize / 2);
        Assert.True(
            centre.Red > 200 && centre.Green > 200 && centre.Blue > 200,
            $"hole should be background at rotation {rotation}, was {centre}");

        // A point well inside the shell but outside the hole must be filled.
        var filled = bmp.GetPixel(CanvasSize / 2, CanvasSize / 2 - 90);
        Assert.True(
            filled.Red < 80 && filled.Green < 80 && filled.Blue < 80,
            $"shell should be filled at rotation {rotation}, was {filled}");
    }

    [Fact]
    public void Polygon_IsFastPathed_UnderRotation()
    {
        var inner = new CountingRenderer();
        var renderer = new CachedVectorStyleRenderer(inner, simplifyTolerancePx: 0);
        var feature = MakeHoledPolygonFeature(id: 6);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        var handled = false;
        RenderToPng(c => handled = renderer.Draw(c, ViewportFor(500, 500, 1.0, 30.0), layer, feature, style, Service, 0));

        Assert.True(handled);
        Assert.Equal(0, inner.DrawCalls);
        Assert.Equal(1, renderer.CachedPathCount);
    }

    [Fact]
    public void ConcurrentDraw_WithEviction_DoesNotCrash()
    {
        // Regression: rendering reads a cached entry under the lock but draws it
        // outside the lock, while another thread (e.g. the off-thread snapshot
        // prebuild) may evict that same entry. Eviction must not dispose the
        // SKPath or the drawing thread faults inside Skia (use-after-free) and
        // wedges the render thread, hanging the viewer. A tiny cache forces
        // constant eviction churn; eight threads draw the shared features
        // concurrently. Before the fix this aborts the process; after it, it
        // completes cleanly.
        var renderer = new CachedVectorStyleRenderer(new VectorStyleRenderer(), capacity: 2, simplifyTolerancePx: 0);
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var features = Enumerable.Range(0, 8).Select(i => MakeHoledPolygonFeature(i)).ToArray();
        var styles = features.Select(f => (VectorStyle)f.Styles.First()).ToArray();
        var resolutions = new[] { 1.0, 2.0, 4.0, 8.0 };

        Exception? error = null;
        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            try
            {
                for (var iter = 0; iter < 200; iter++)
                {
                    using var surface = SKSurface.Create(
                        new SKImageInfo(CanvasSize, CanvasSize, SKColorType.Rgba8888, SKAlphaType.Premul));
                    surface.Canvas.Clear(SKColors.White);
                    for (var i = 0; i < features.Length; i++)
                    {
                        var res = resolutions[(iter + i) % resolutions.Length];
                        renderer.Draw(surface.Canvas, ViewportFor(500, 500, res, (iter * 13) % 360),
                            layer, features[i], styles[i], Service, 0);
                    }
                    surface.Canvas.Flush();
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref error, ex, null);
            }
        })).ToArray();

        foreach (var th in threads)
        {
            th.Start();
        }
        foreach (var th in threads)
        {
            th.Join();
        }

        Assert.Null(error);
    }

    [Fact]
    public void Polygon_AtTolerance_ReducesCachedCoordinates()
    {
        // A 50-points-per-edge square (201 coords) of collinear runs collapses to
        // its four corners when simplified, but is copied vertex-for-vertex when
        // tolerance is 0. CachedCoordinateCount exposes the built path's vertices.
        var feature = MakeDenseSquareFeature(pointsPerEdge: 50);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 1.0);

        var exact = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var simplified = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0.6);

        using var scope = new PolygonSimplificationScope();
        RenderToPng(c => exact.Draw(c, viewport, layer, feature, style, Service, 0));
        RenderToPng(c => simplified.Draw(c, viewport, layer, feature, style, Service, 0));

        if (!scope.Enabled)
        {
            return; // env pins polygon simplification off; strict reduction not exercisable
        }

        Assert.Equal(201, exact.CachedCoordinateCount);
        Assert.True(simplified.CachedCoordinateCount < exact.CachedCoordinateCount,
            $"expected fewer coords, simplified={simplified.CachedCoordinateCount} exact={exact.CachedCoordinateCount}");
        Assert.True(simplified.CachedCoordinateCount >= 4, "the four corners must survive");
    }

    [Fact]
    public void Polygon_BelowMinVertexCount_NotSimplified()
    {
        // 2 points/edge → 9 coords, below MinPolygonVertexCount (32): the cheap
        // NTS pass is bypassed and the path stays vertex-exact.
        var feature = MakeDenseSquareFeature(pointsPerEdge: 2);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 1.0);

        var exact = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var simplified = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0.6);

        using var scope = new PolygonSimplificationScope();
        RenderToPng(c => exact.Draw(c, viewport, layer, feature, style, Service, 0));
        RenderToPng(c => simplified.Draw(c, viewport, layer, feature, style, Service, 0));

        Assert.Equal(exact.CachedCoordinateCount, simplified.CachedCoordinateCount);
    }

    [Fact]
    public void Polygon_LargerTolerance_YieldsFewerOrEqualCoordinates()
    {
        // A 360-gon (non-collinear) drops more vertices as tolerance grows.
        var feature = MakeDenseCircleFeature(points: 360);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 4.0);

        var small = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0.5);
        var large = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 4.0);

        using var scope = new PolygonSimplificationScope();
        RenderToPng(c => small.Draw(c, viewport, layer, feature, style, Service, 0));
        RenderToPng(c => large.Draw(c, viewport, layer, feature, style, Service, 0));

        if (!scope.Enabled)
        {
            return; // env pins polygon simplification off; strict reduction not exercisable
        }

        Assert.True(small.CachedCoordinateCount < 361, "some vertices should be dropped");
        Assert.True(large.CachedCoordinateCount <= small.CachedCoordinateCount,
            $"coarser tolerance must not add vertices: large={large.CachedCoordinateCount} small={small.CachedCoordinateCount}");
    }

    [Fact]
    public void Polygon_Simplified_RendersEquivalentFill()
    {
        // Validity preservation: the simplified circle stays a well-formed filled
        // polygon, so its painted area matches the exact polygon within a few
        // percent (no self-intersection collapse, no dropped fill).
        var feature = MakeDenseCircleFeature(points: 360);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 4.0);

        var exact = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var simplified = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0.6);

        using var scope = new PolygonSimplificationScope();
        using var exactBmp = SKBitmap.Decode(RenderToPng(c => exact.Draw(c, viewport, layer, feature, style, Service, 0)));
        using var simplifiedBmp = SKBitmap.Decode(RenderToPng(c => simplified.Draw(c, viewport, layer, feature, style, Service, 0)));

        var e = OpaqueDarkPixels(exactBmp);
        var s = OpaqueDarkPixels(simplifiedBmp);
        Assert.True(e > 0 && s > 0, "both renders must fill");
        Assert.True(Math.Abs(e - s) <= e * 0.02,
            $"simplified fill area must match exact within 2%: exact={e} simplified={s}");
    }

    [Fact]
    public void Polygon_Simplified_PreservesHole()
    {
        // Topology preservation: after simplification the centred hole is still
        // cut (background at the centre) while the surrounding shell is filled.
        var feature = MakeDenseHoledSquareFeature(pointsPerEdge: 40);
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 1.0);

        var simplified = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0.6);
        using var scope = new PolygonSimplificationScope();
        using var bmp = SKBitmap.Decode(
            RenderToPng(c => simplified.Draw(c, viewport, layer, feature, style, Service, 0)));

        var centre = bmp.GetPixel(CanvasSize / 2, CanvasSize / 2);
        Assert.True(centre.Red > 200 && centre.Green > 200 && centre.Blue > 200,
            $"hole should stay background after simplification, was {centre}");

        var filled = bmp.GetPixel(CanvasSize / 2, CanvasSize / 2 - 90);
        Assert.True(filled.Red < 80 && filled.Green < 80 && filled.Blue < 80,
            $"shell should be filled after simplification, was {filled}");
    }

    [Fact]
    public void MultiPolygon_SimplifiesEachPart_KeyedByPosition()
    {
        var part1 = new Polygon(new LinearRing(DenseSquareCoords(0, 0, 400, 50)));
        var part2 = new Polygon(new LinearRing(DenseSquareCoords(600, 600, 400, 50)));
        var feature = new GeometryFeature(new MultiPolygon(new[] { part1, part2 }));
        feature.Styles.Add(new VectorStyle { Fill = new Brush(Color.Black), Outline = null, Line = null });
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };
        var viewport = ViewportFor(500, 500, 1.0);

        var exact = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0);
        var simplified = new CachedVectorStyleRenderer(new VectorStyleRenderer(), simplifyTolerancePx: 0.6);

        using var scope = new PolygonSimplificationScope();
        RenderToPng(c => exact.Draw(c, viewport, layer, feature, style, Service, 0));
        RenderToPng(c => simplified.Draw(c, viewport, layer, feature, style, Service, 0));

        // One cache entry per part (keyed by position) for both renderers.
        Assert.Equal(2, simplified.CachedPathCount);
        Assert.Equal(2, exact.CachedPathCount);
        if (!scope.Enabled)
        {
            return; // env pins polygon simplification off; strict reduction not exercisable
        }

        Assert.True(simplified.CachedCoordinateCount < exact.CachedCoordinateCount,
            "each part should be simplified independently");
    }

    [Fact]
    public void Polygon_CoordinateBudget_EvictsLeastRecentlyUsed()
    {
        // A 201-coord polygon drawn at four resolutions yields four distinct keys;
        // a 250-coord budget can hold only one, so LRU eviction keeps the cache at
        // a single entry and the tracked-coordinate gauge bounded.
        var renderer = new CachedVectorStyleRenderer(
            new VectorStyleRenderer(), capacity: 100, simplifyTolerancePx: 0, maxCachedCoordinates: 250);
        var feature = MakeDenseSquareFeature(pointsPerEdge: 50); // 201 coords
        var style = (VectorStyle)feature.Styles.First();
        var layer = new MemoryLayer("l") { Opacity = 1.0 };

        foreach (var res in new[] { 1.0, 2.0, 4.0, 8.0 })
        {
            RenderToPng(c => renderer.Draw(c, ViewportFor(500, 500, res), layer, feature, style, Service, 0));
        }

        Assert.Equal(1, renderer.CachedPathCount);
        Assert.Equal(201, renderer.CachedCoordinateCount);
    }

    private sealed class CountingRenderer : ISkiaStyleRenderer
    {
        public int DrawCalls { get; private set; }

        public bool Draw(SKCanvas canvas, Mapsui.Viewport viewport, Mapsui.Layers.ILayer layer,
            Mapsui.IFeature feature, Mapsui.Styles.IStyle style, Mapsui.Rendering.RenderService renderService, long iteration)
        {
            DrawCalls++;
            return true;
        }
    }
}
