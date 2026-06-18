using System;
using System.Collections.Generic;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Rendering.Skia.Extensions;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A drop-in replacement for Mapsui's <see cref="VectorStyleRenderer"/> that
/// caches the projected <see cref="SKPath"/> for solid-filled / solid-outlined
/// polygons <b>and</b> solid-stroked lines in a <b>translation-invariant</b>
/// coordinate frame, so that a pan (which changes only the viewport centre, not
/// its resolution) re-uses the cached path and pays only a canvas translate plus
/// the fill/stroke, instead of re-projecting and rebuilding the path every frame.
/// Lines are additionally <b>simplified at the build resolution</b> (see
/// <see cref="CachedVectorStyleRenderer(ISkiaStyleRenderer, int, double)"/>) so
/// the Skia stroker rasterises far fewer sub-pixel segments.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Mapsui's <c>PolygonRenderer</c> / <c>LineStringRenderer</c>
/// cache the built path in an LRU keyed on <c>(featureId, position, extent,
/// rotation, lineWidth)</c> — and <c>extent</c> is the full viewport extent, which
/// changes on every pan, forcing a rebuild. Profiling the AU S-101 set showed
/// that on dense approach cells the cost is dominated by thousands of
/// <c>LineString</c> features (bathymetry contours); path construction is
/// redundant during a pan, and — more importantly — the Skia stroker spends the
/// bulk of its time on the dense run of <i>sub-pixel</i> segments. See the perf
/// investigation in the session notes (issue #274 context).
/// </para>
/// <para>
/// <b>Coordinate frame.</b> Mapsui projects world → screen as
/// <c>screenX = (worldX − CenterX)/Res + Width/2</c> and
/// <c>screenY = (CenterY − worldY)/Res + Height/2</c>. This renderer builds the
/// path in <i>anchor-relative pixels at the current resolution</i>:
/// <c>px = (worldX − Ax)/Res</c>, <c>py = (Ay − worldY)/Res</c>, where the
/// anchor <c>(Ax, Ay)</c> is the geometry's envelope minimum. The anchor
/// subtraction (done in <see langword="double"/>) keeps the float
/// <see cref="SKPoint"/> coordinates small and precise even at high zoom. On
/// paint the path is drawn under an affine matrix sampled from Mapsui's own
/// <c>WorldToScreenXY</c> at the anchor and one resolution step along each world
/// axis (see <c>BuildAnchorMatrix</c>), which reproduces Mapsui's transform
/// exactly for any rotation, zoom and centre. For an un-rotated viewport this
/// reduces to the pure translation
/// <c>Tx = Width/2 + (Ax − CenterX)/Res</c>,
/// <c>Ty = Height/2 + (CenterY − Ay)/Res</c>. Both the path and the anchor
/// depend only on the resolution, so they survive any pan <i>and any rotation</i>;
/// a zoom changes the resolution and therefore the cache key, forcing a rebuild
/// (crisp, and far rarer than pans).
/// </para>
/// <para>
/// <b>Scope.</b> Polygons whose
/// <see cref="VectorStyle"/> has a solid fill and/or solid outline, and lines
/// whose <see cref="VectorStyle.Line"/> is a solid pen with no separate visible
/// <see cref="VectorStyle.Outline"/> casing, are cached. The cached path is
/// reused under rotation via an affine draw matrix (see <c>BuildAnchorMatrix</c>).
/// Points, patterned/hatched fills, dashed/casing-outlined lines and any geometry
/// that is not a polygon, multi-polygon, line or multi-line are delegated
/// unchanged to the wrapped Mapsui renderer, so visuals are identical outside the
/// fast path.
/// </para>
/// <para>
/// <b>Thread-safety.</b> The cache is guarded by a lock because the offscreen
/// <c>render_to_image</c> / rasterising-tile paths can invoke the shared static
/// renderer concurrently with the on-screen compositor thread. Path
/// construction happens outside the lock; only the dictionary mutation is
/// serialised.
/// </para>
/// </remarks>
public sealed class CachedVectorStyleRenderer : ISkiaStyleRenderer
{
    /// <summary>
    /// Singleton wrapping a fresh, stateless Mapsui <see cref="VectorStyleRenderer"/>
    /// used for delegation of everything outside the fast path.
    /// </summary>
    public static CachedVectorStyleRenderer Instance { get; } = new(new VectorStyleRenderer());

    private readonly ISkiaStyleRenderer _inner;
    private readonly double _simplifyOverridePx;
    private double _lastSimplifyPx = double.NaN;
    private readonly object _sync = new();
    private readonly PathCache _cache;

    /// <summary>
    /// Effective line-simplification tolerance, in screen pixels. When an
    /// explicit tolerance was supplied to the constructor (≥ 0) it is honoured
    /// verbatim; otherwise the value tracks the live
    /// <see cref="RenderingOptimizations.GeometrySimplificationEnabled"/> knob (its
    /// tolerance when on, <c>0</c> — vertex-exact — when off). A change clears the
    /// path cache so re-built paths reflect the new tolerance.
    /// </summary>
    private double EffectiveSimplifyPx =>
        _simplifyOverridePx >= 0
            ? _simplifyOverridePx
            : (RenderingOptimizations.GeometrySimplificationEnabled
                ? RenderingOptimizations.SimplificationTolerancePx
                : 0.0);

    /// <summary>
    /// Creates a renderer wrapping <paramref name="inner"/> (the real Mapsui
    /// <see cref="VectorStyleRenderer"/>) for delegation.
    /// </summary>
    /// <param name="inner">The renderer to delegate non-fast-path draws to.</param>
    /// <param name="capacity">Maximum number of cached paths before LRU eviction.</param>
    /// <param name="simplifyTolerancePx">
    /// Pixel tolerance for resolution-aware line simplification applied at
    /// cache-build time. Consecutive line vertices that project to within this
    /// many pixels of the last emitted vertex are dropped, collapsing the dense
    /// sub-pixel vertex runs of S-101 bathymetry contours so the Skia stroker
    /// rasterises far fewer segments. Because simplification happens in the
    /// anchored pixel frame at the build resolution and the result is cached,
    /// the cost is paid once per (feature, zoom) and reused across all pans, and
    /// dropped vertices are by construction sub-pixel <i>on screen</i> at that
    /// zoom — so the result is visually indistinguishable at every zoom level.
    /// A value <c>0</c> disables simplification (paths are vertex-exact). When
    /// negative (the default), the tolerance follows the live
    /// <see cref="RenderingOptimizations.GeometrySimplificationEnabled"/> knob, which
    /// the viewer's <c>Settings → Map</c> section binds.
    /// </param>
    /// <param name="maxCachedCoordinates">
    /// Soft upper bound on the total number of geometry coordinates retained
    /// across all cached paths. When exceeded, least-recently-used entries are
    /// evicted until back under budget (in addition to the
    /// <paramref name="capacity"/> entry cap). Bounding by coordinate count —
    /// not entry count — keeps memory predictable now that dense polygon paths
    /// (tens of thousands of vertices) share the cache with tiny features.
    /// Defaults to <c>5_000_000</c> coords (≈ 80&#160;MB of points).
    /// </param>
    public CachedVectorStyleRenderer(
        ISkiaStyleRenderer inner,
        int capacity = 8192,
        double simplifyTolerancePx = -1,
        long maxCachedCoordinates = 5_000_000)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _cache = new PathCache(Math.Max(1, capacity), Math.Max(1, maxCachedCoordinates));
        _simplifyOverridePx = simplifyTolerancePx;
    }

    /// <summary>
    /// The number of distinct geometry paths currently held in the cache. Each
    /// pan re-uses existing entries (constant resolution → cache hit); a zoom
    /// changes the resolution and adds new entries. Exposed for testing the
    /// build-once-per-(feature, zoom) behaviour.
    /// </summary>
    public int CachedPathCount
    {
        get { lock (_sync) { return _cache.Count; } }
    }

    /// <summary>
    /// Total geometry coordinates currently retained across all cached paths.
    /// Bounded by the renderer's coordinate budget; exposed for testing the
    /// coordinate-budget eviction.
    /// </summary>
    public long CachedCoordinateCount
    {
        get { lock (_sync) { return _cache.CoordinateCount; } }
    }

    /// <summary>
    /// Registers <see cref="Instance"/> as the renderer for
    /// <see cref="VectorStyle"/>. Must be called <b>before</b> any
    /// instrumentation wraps the renderer dictionary and never again
    /// afterwards (so the wrapper is preserved). No-op when the path cache is
    /// disabled or when the build/fill split measurement is active (so that
    /// measurement characterises Mapsui's un-cached cost).
    /// </summary>
    public static void Register()
    {
        var measuring = (Environment.GetEnvironmentVariable("S100_MEASURE_VECTOR_SPLIT") ?? string.Empty)
            is "1" or "true" or "TRUE" or "True";
        // Skip registration only when the build/fill split measurement is active
        // (it must characterise Mapsui's un-cached cost) or when the path cache
        // is pinned off by an explicit environment variable (faithful A/B). When
        // the cache is merely toggled off via the Map setting we still register
        // Instance — it delegates to the inner renderer per draw — so the knob
        // can be flipped back on live.
        var pinnedOff = RenderingOptimizations.VectorPathCacheEnvExplicit
            && !RenderingOptimizations.VectorPathCacheEnabled;
        if (measuring || pinnedOff)
        {
            return;
        }

        global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(typeof(VectorStyle), Instance);
    }

    /// <inheritdoc />
    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer,
        IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (!RenderingOptimizations.VectorPathCacheEnabled
            || style is not VectorStyle vectorStyle
            || feature is not GeometryFeature geometryFeature
            || geometryFeature.Geometry is not { } geometry)
        {
            return _inner.Draw(canvas, viewport, layer, feature, style, renderService, iteration);
        }

        var opacity = (float)(layer.Opacity * style.Opacity);
        switch (geometry)
        {
        case Polygon polygon when CanFastPolygon(vectorStyle):
        {
            DrawPolygon(canvas, viewport, vectorStyle, geometryFeature.Id, 0, polygon, opacity);
            return true;
        }
        case MultiPolygon multiPolygon when CanFastPolygon(vectorStyle):
        {
            for (var i = 0; i < multiPolygon.Count; i++)
            {
                if (multiPolygon[i] is Polygon part)
                {
                    DrawPolygon(canvas, viewport, vectorStyle, geometryFeature.Id, i, part, opacity);
                }
            }
            return true;
        }
        case LineString lineString when CanFastLine(vectorStyle):
        {
            DrawLine(canvas, viewport, vectorStyle, geometryFeature.Id, 0, lineString, opacity);
            return true;
        }
        case MultiLineString multiLineString when CanFastLine(vectorStyle):
        {
            for (var i = 0; i < multiLineString.Count; i++)
            {
                if (multiLineString[i] is LineString part)
                {
                    DrawLine(canvas, viewport, vectorStyle, geometryFeature.Id, i, part, opacity);
                }
            }
            return true;
        }
        default:
        {
            // GeometryCollections, points, casing-outlined lines, patterned
            // fills, and any unsupported style: leave to Mapsui.
            return _inner.Draw(canvas, viewport, layer, feature, style, renderService, iteration);
        }
        }
    }

    /// <summary>
    /// True when a polygon's fill and outline are solid (the only cases this
    /// renderer reproduces pixel-for-pixel). Patterned fills and dashed/styled
    /// outlines fall back to Mapsui.
    /// </summary>
    /// <remarks>
    /// <see cref="VectorStyle.Line"/> is deliberately <b>not</b> consulted: it
    /// applies only to line geometries, and Mapsui initialises it to a non-null
    /// default pen that is ignored when filling/stroking a polygon.
    /// </remarks>
    private static bool CanFastPolygon(VectorStyle style)
    {
        if (style.Fill is { FillStyle: not FillStyle.Solid })
        {
            return false;
        }
        if (style.Outline is { PenStyle: not PenStyle.Solid })
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// True when a line's stroke can be reproduced exactly: the
    /// <see cref="VectorStyle.Line"/> pen is visible and there is no separate
    /// visible <see cref="VectorStyle.Outline"/> casing (Mapsui draws a wider
    /// outline pass under the line when an outline is set; that rarer case is
    /// delegated to keep this fast path simple and pixel-identical).
    /// </summary>
    private static bool CanFastLine(VectorStyle style)
    {
        if (style.Line is not { } line || line.Color.A <= 0 || line.Width <= 0)
        {
            return false;
        }
        if (style.Outline is { } outline && outline.Color.A > 0 && outline.Width > 0)
        {
            return false;
        }
        return true;
    }

    private void DrawLine(SKCanvas canvas, Viewport viewport, VectorStyle style,
        long featureId, int position, LineString lineString, float opacity)
    {
        var resolution = viewport.Resolution;
        if (resolution <= 0 || lineString.IsEmpty || lineString.NumPoints < 2)
        {
            return;
        }

        var key = new PathKey(featureId, position, BitConverter.DoubleToInt64Bits(resolution));

        var tol = EffectiveSimplifyPx;

        PathEntry? entry;
        lock (_sync)
        {
            EnsureToleranceCurrent(tol);
            entry = _cache.Get(key);
        }

        if (entry is not null)
        {
            S100Diag.Telemetry.SimplifyCacheHit.Add(1);
        }
        else
        {
            S100Diag.Telemetry.SimplifyCacheMiss.Add(1);
            var built = BuildLineEntry(lineString, resolution, tol);
            lock (_sync)
            {
                var again = _cache.Get(key);
                if (again is not null)
                {
                    built.Dispose();
                    entry = again;
                }
                else
                {
                    _cache.Add(key, built);
                    entry = built;
                }
            }
        }

        var matrix = BuildAnchorMatrix(viewport, entry.AnchorX, entry.AnchorY, resolution);

        var restore = canvas.Save();
        try
        {
            canvas.Concat(in matrix);
            using var strokePaint = CreateLineStrokePaint(style.Line!, opacity);
            canvas.DrawPath(entry.Path, strokePaint);
        }
        finally
        {
            canvas.RestoreToCount(restore);
        }
    }

    /// <summary>
    /// Builds the translation-invariant <see cref="SKPath"/> for a line at a
    /// given resolution, anchored at the line's envelope minimum. The full
    /// (unclipped) line is built; Skia clips it to the canvas at draw time.
    /// Mapsui instead re-projects and Liang-Barsky-clips every frame, which is
    /// exactly the per-pan cost this cache removes.
    /// </summary>
    private static PathEntry BuildLineEntry(LineString lineString, double resolution, double tol)
    {
        var envelope = lineString.EnvelopeInternal;
        var anchorX = envelope.MinX;
        var anchorY = envelope.MinY;

        var path = new SKPath();
        var coordinates = lineString.Coordinates;

        var px0 = (float)((coordinates[0].X - anchorX) / resolution);
        var py0 = (float)((anchorY - coordinates[0].Y) / resolution);
        path.MoveTo(px0, py0);

        var lastX = px0;
        var lastY = py0;
        var lastIndex = coordinates.Length - 1;
        var emitted = 1;
        for (var i = 1; i < coordinates.Length; i++)
        {
            var px = (float)((coordinates[i].X - anchorX) / resolution);
            var py = (float)((anchorY - coordinates[i].Y) / resolution);

            // Drop sub-pixel vertices, but always keep the final vertex so the
            // line's endpoint (and overall length) is preserved exactly.
            if (tol > 0 && i != lastIndex)
            {
                var dx = px - lastX;
                var dy = py - lastY;
                if ((dx * dx) + (dy * dy) < tol * tol)
                {
                    continue;
                }
            }

            path.LineTo(px, py);
            lastX = px;
            lastY = py;
            emitted++;
        }

        return new PathEntry(path, anchorX, anchorY, emitted);
    }

    /// <summary>
    /// Clears the shared path cache when the line simplification tolerance
    /// changes (e.g. a live Settings → Map toggle): cached paths were built at
    /// the previous tolerance and must be rebuilt. Callers must hold
    /// <see cref="_sync"/>.
    /// </summary>
    private void EnsureToleranceCurrent(double lineTol)
    {
        if (_lastSimplifyPx.Equals(lineTol))
        {
            return;
        }

        _cache.Clear();
        _lastSimplifyPx = lineTol;
    }

    /// <summary>
    /// Reproduces Mapsui's <c>LineStringRenderer</c> stroke paint exactly,
    /// reusing the public Mapsui Skia extension helpers for the colour, stroke
    /// cap/join and pen-style dash effect so the rendered stroke is identical.
    /// </summary>
    private static SKPaint CreateLineStrokePaint(Pen line, float opacity)
    {
        var width = (float)line.Width;
        return new SKPaint
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = width,
            Color = line.Color.ToSkia(opacity),
            StrokeCap = line.PenStrokeCap.ToSkia(),
            StrokeJoin = line.StrokeJoin.ToSkia(),
            StrokeMiter = line.StrokeMiterLimit,
            PathEffect = line.PenStyle != PenStyle.Solid
                ? line.PenStyle.ToSkia(width, line.DashArray, line.DashOffset)
                : null,
        };
    }

    private void DrawPolygon(SKCanvas canvas, Viewport viewport, VectorStyle style,
        long featureId, int position, Polygon polygon, float opacity)
    {
        var resolution = viewport.Resolution;
        if (resolution <= 0 || polygon.ExteriorRing is null || polygon.IsEmpty)
        {
            return;
        }

        var key = new PathKey(featureId, position, BitConverter.DoubleToInt64Bits(resolution));

        PathEntry? entry;
        lock (_sync)
        {
            EnsureToleranceCurrent(EffectiveSimplifyPx);
            entry = _cache.Get(key);
        }

        if (entry is not null)
        {
            S100Diag.Telemetry.SimplifyCacheHit.Add(1);
        }
        else
        {
            S100Diag.Telemetry.SimplifyCacheMiss.Add(1);
            var built = BuildEntry(polygon, resolution);
            lock (_sync)
            {
                var again = _cache.Get(key);
                if (again is not null)
                {
                    built.Dispose();
                    entry = again;
                }
                else
                {
                    _cache.Add(key, built);
                    entry = built;
                }
            }
        }

        var matrix = BuildAnchorMatrix(viewport, entry.AnchorX, entry.AnchorY, resolution);

        var restore = canvas.Save();
        try
        {
            canvas.Concat(in matrix);

            if (style.Fill is { } fill && IsVisible(fill))
            {
                using var fillPaint = CreateFillPaint(fill, opacity);
                canvas.DrawPath(entry.Path, fillPaint);
            }

            if (style.Outline is { } outline && IsVisible(outline))
            {
                using var strokePaint = CreateStrokePaint(outline, opacity);
                canvas.DrawPath(entry.Path, strokePaint);
            }
        }
        finally
        {
            canvas.RestoreToCount(restore);
        }
    }

    /// <summary>
    /// Builds the exact affine transform that maps a cached, anchor-relative path
    /// (built as <c>px = (worldX − Ax)/Res</c>, <c>py = (Ay − worldY)/Res</c>) onto
    /// Mapsui screen pixels for the current <paramref name="viewport"/>. It is
    /// sampled directly from <see cref="Viewport.WorldToScreenXY(double, double)"/>
    /// at the anchor and one resolution-sized step along each world axis, so it
    /// reproduces Mapsui's world→screen projection exactly for any rotation, zoom
    /// and centre — including the rotated case the old per-pan translate bailed on.
    /// Because the path is built at the same resolution that keys the cache, the
    /// two basis vectors have unit length, so the matrix is a pure
    /// rotation + translation (isometry): stroke widths and even-odd fills are
    /// undistorted. When the viewport is un-rotated this reduces exactly to the
    /// previous <c>Tx/Ty</c> translation.
    /// </summary>
    private static SKMatrix BuildAnchorMatrix(Viewport viewport, double anchorX, double anchorY, double resolution)
    {
        var (ox, oy) = viewport.WorldToScreenXY(anchorX, anchorY);
        var (ux, uy) = viewport.WorldToScreenXY(anchorX + resolution, anchorY);
        var (vx, vy) = viewport.WorldToScreenXY(anchorX, anchorY - resolution);

        return new SKMatrix(
            (float)(ux - ox), (float)(vx - ox), (float)ox,
            (float)(uy - oy), (float)(vy - oy), (float)oy,
            0f, 0f, 1f);
    }

    /// <summary>
    /// Builds the translation-invariant <see cref="SKPath"/> for a polygon at a
    /// given resolution, anchored at the polygon's envelope minimum. Uses
    /// even-odd fill so interior rings (holes) are subtracted regardless of
    /// ring orientation, avoiding the orientation normalisation Mapsui performs.
    /// </summary>
    private static PathEntry BuildEntry(Polygon polygon, double resolution)
    {
        var envelope = polygon.EnvelopeInternal;
        var anchorX = envelope.MinX;
        var anchorY = envelope.MinY;

        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        var coords = AddRing(path, polygon.ExteriorRing!, resolution, anchorX, anchorY);
        for (var i = 0; i < polygon.NumInteriorRings; i++)
        {
            coords += AddRing(path, polygon.GetInteriorRingN(i), resolution, anchorX, anchorY);
        }

        return new PathEntry(path, anchorX, anchorY, coords);
    }

    private static int AddRing(SKPath path, LineString ring, double resolution, double anchorX, double anchorY)
    {
        var coordinates = ring.Coordinates;
        if (coordinates.Length < 2)
        {
            return 0;
        }

        path.MoveTo(
            (float)((coordinates[0].X - anchorX) / resolution),
            (float)((anchorY - coordinates[0].Y) / resolution));
        for (var i = 1; i < coordinates.Length; i++)
        {
            path.LineTo(
                (float)((coordinates[i].X - anchorX) / resolution),
                (float)((anchorY - coordinates[i].Y) / resolution));
        }
        path.Close();
        return coordinates.Length;
    }

    private static bool IsVisible(Brush fill) => fill.Color is { A: > 0 };

    private static bool IsVisible(Pen outline) => outline.Color.A > 0 && outline.Width > 0;

    private static SKPaint CreateFillPaint(Brush fill, float opacity) => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = ToSkia(fill.Color!.Value, opacity),
    };

    private static SKPaint CreateStrokePaint(Pen outline, float opacity) => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = (float)outline.Width,
        Color = ToSkia(outline.Color, opacity),
        StrokeCap = SKStrokeCap.Butt,
        StrokeJoin = SKStrokeJoin.Miter,
        StrokeMiter = 4f,
    };

    private static SKColor ToSkia(Color color, float opacity)
    {
        var alpha = (byte)Math.Clamp(color.A * opacity, 0, 255);
        return new SKColor((byte)color.R, (byte)color.G, (byte)color.B, alpha);
    }

    /// <summary>Cache key: a feature/part identity paired with the exact resolution bits.</summary>
    private readonly record struct PathKey(long FeatureId, int Position, long ResolutionBits);

    /// <summary>A cached path plus the world-space anchor it was built relative to.</summary>
    private sealed class PathEntry : IDisposable
    {
        public PathEntry(SKPath path, double anchorX, double anchorY, int coordCount)
        {
            Path = path;
            AnchorX = anchorX;
            AnchorY = anchorY;
            CoordCount = coordCount;
        }

        public SKPath Path { get; }
        public double AnchorX { get; }
        public double AnchorY { get; }

        /// <summary>Number of geometry coordinates emitted into <see cref="Path"/>; drives the cache's coordinate budget.</summary>
        public int CoordCount { get; }

        /// <summary>
        /// Releases the native <see cref="SKPath"/>. Only safe to call for a path
        /// that has never been published to the cache (e.g. a duplicate built by a
        /// losing thread in the double-checked build race). Cache eviction must
        /// <b>not</b> dispose entries: another thread may be mid-<c>DrawPath</c> on
        /// a just-evicted entry, so eviction relies on the finalizer instead (see
        /// <see cref="PathCache"/>).
        /// </summary>
        public void Dispose() => Path.Dispose();
    }

    /// <summary>
    /// A small LRU of <see cref="PathEntry"/> values, bounded by <b>both</b> an
    /// entry cap and a total-coordinate budget. Callers must serialise access
    /// (this renderer holds a lock around all <see cref="Get"/>/<see cref="Add"/>
    /// calls). Evicted entries are <b>not</b> disposed: rendering reads
    /// <c>entry.Path</c> under the lock but draws it (and the snapshot-prebuild
    /// thread may draw a different cached path) <i>outside</i> the lock, so a
    /// disposed-on-eviction path could be freed natively while another thread is
    /// still rasterising it — a use-after-free that hangs the render thread inside
    /// Skia. Dropping the managed reference instead lets the GC finalise the
    /// <see cref="SKPath"/> only once no thread can still hold it (a live
    /// <c>entry.Path</c> local keeps it reachable for the duration of the draw).
    /// The cache is bounded, so the evicted-but-not-yet-finalised backlog is small.
    /// </summary>
    /// <remarks>
    /// The coordinate budget complements the entry cap because a handful of dense
    /// polygon paths (tens of thousands of vertices each) cost far more memory
    /// than the same number of tiny features — entry-count bounding alone is
    /// memory-naive for the S-101 workload.
    /// </remarks>
    private sealed class PathCache
    {
        private readonly int _capacity;
        private readonly long _maxCoordinates;
        private readonly Dictionary<PathKey, LinkedListNode<Node>> _map;
        private readonly LinkedList<Node> _lru = new();
        private long _coordinates;

        public PathCache(int capacity, long maxCoordinates)
        {
            _capacity = capacity;
            _maxCoordinates = maxCoordinates;
            _map = new Dictionary<PathKey, LinkedListNode<Node>>(capacity);
        }

        public int Count => _map.Count;

        public long CoordinateCount => _coordinates;

        public void Clear()
        {
            _map.Clear();
            _lru.Clear();
            if (_coordinates > 0)
            {
                S100Diag.Telemetry.SimplifyCacheCoordsTracked.Add(-_coordinates);
            }
            _coordinates = 0;
            // Entries are not disposed: another thread may be drawing a path
            // outside the lock. The GC finalises each SKPath once unreachable.
        }

        public PathEntry? Get(in PathKey key)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Entry;
            }
            return null;
        }

        public void Add(in PathKey key, PathEntry entry)
        {
            var node = _lru.AddFirst(new Node(key, entry));
            _map[key] = node;
            _coordinates += entry.CoordCount;
            S100Diag.Telemetry.SimplifyCacheCoordsTracked.Add(entry.CoordCount);

            // Evict LRU until under both the entry cap and the coordinate budget,
            // always keeping at least the just-added entry.
            while (_map.Count > 1 && (_map.Count > _capacity || _coordinates > _maxCoordinates))
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
                _coordinates -= last.Value.Entry.CoordCount;
                S100Diag.Telemetry.SimplifyCacheCoordsTracked.Add(-last.Value.Entry.CoordCount);
                // Deliberately not disposed: another thread may be drawing this
                // path outside the lock. The GC finalises the SKPath once it is
                // unreachable (see PathCache remarks).
            }
        }

        private readonly record struct Node(PathKey Key, PathEntry Entry);
    }
}
