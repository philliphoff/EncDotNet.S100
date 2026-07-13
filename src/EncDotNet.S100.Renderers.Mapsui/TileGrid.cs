using System;
using System.Collections.Generic;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Identifies one base-plane tile in the <see cref="TileGrid"/>: a
/// power-of-two resolution <paramref name="Band"/> and the tile's
/// <paramref name="X"/>/<paramref name="Y"/> index within the origin-anchored
/// EPSG:3857 grid for that band (XYZ convention: <c>X</c> increases east,
/// <c>Y</c> increases south). See
/// <c>docs/design/S100-Render-Subsystem-Design.md</c> §3.2.
/// </summary>
internal readonly record struct TileKey(int Band, int X, int Y);

/// <summary>
/// A screen-space rectangle in device-independent pixels (DIP), corners
/// measured from the viewport's top-left. Kept Skia-free so the tile math is
/// unit-testable without a graphics surface.
/// </summary>
internal readonly record struct ScreenRect(double Left, double Top, double Right, double Bottom)
{
    /// <summary>Width in DIP (may be negative for an inverted rect).</summary>
    public double Width => Right - Left;

    /// <summary>Height in DIP.</summary>
    public double Height => Bottom - Top;

    /// <summary>True when this rect overlaps the half-open viewport box.</summary>
    public bool IntersectsViewport(double widthDip, double heightDip) =>
        Right > 0 && Bottom > 0 && Left < widthDip && Top < heightDip;
}

/// <summary>
/// An inclusive, world-clamped range of tile indices at one band, as returned by
/// <see cref="TileGrid.VisibleTileRange"/>. <see cref="IsEmpty"/> is true when
/// the source viewport was degenerate.
/// </summary>
internal readonly record struct TileRange(int XStart, int XEnd, int YStart, int YEnd, int PerAxis)
{
    /// <summary>True when the range covers no tiles.</summary>
    public bool IsEmpty => XStart > XEnd || YStart > YEnd;
}

/// <summary>
/// A pure exponential-moving-average estimator of viewport-centre velocity in
/// EPSG:3857 metres/second, used to aim the prediction fan
/// (<see cref="TileGrid.PredictedTiles"/>, design §3.6). Kept Skia-free and
/// allocation-light so it can live in the per-layer state and be unit-tested.
/// </summary>
internal static class VelocityEstimator
{
    /// <summary>The default EMA smoothing factor (0 = ignore new, 1 = no smoothing).</summary>
    public const double DefaultAlpha = 0.4;

    /// <summary>
    /// Folds one centre move into the running velocity EMA. The instantaneous
    /// velocity is <c>(dx, dy) / dtSeconds</c>; the result is
    /// <c>(1-alpha)·previous + alpha·instant</c>. A non-positive
    /// <paramref name="dtSeconds"/> returns the previous estimate unchanged (no
    /// time elapsed → no new information), which also damps jitter from
    /// zero-interval frames.
    /// </summary>
    public static (double VelocityX, double VelocityY) Update(
        double previousVelocityX, double previousVelocityY,
        double dx, double dy, double dtSeconds, double alpha = DefaultAlpha)
    {
        if (dtSeconds <= 0 || double.IsNaN(dtSeconds) || double.IsInfinity(dtSeconds))
        {
            return (previousVelocityX, previousVelocityY);
        }

        alpha = Math.Clamp(alpha, 0.0, 1.0);
        var instantX = dx / dtSeconds;
        var instantY = dy / dtSeconds;
        return (
            (1 - alpha) * previousVelocityX + alpha * instantX,
            (1 - alpha) * previousVelocityY + alpha * instantY);
    }
}


/// (S-100 render subsystem, Phase&#160;2). Uses the standard Web-Mercator
/// power-of-two pyramid (256-DIP tiles, the same scheme Mapsui's own tile
/// layers use), so a constant-zoom pan reuses every interior tile and only the
/// newly-exposed perimeter is rasterised. All methods are static and free of
/// SkiaSharp/Mapsui so they can be unit-tested directly.
/// </summary>
/// <remarks>
/// The grid is anchored to the world origin (not the viewport), which is what
/// makes tiles pan-stable: the same world position always falls in the same
/// tile at a given band, so a pan never re-keys interior tiles.
/// </remarks>
internal static class TileGrid
{
    /// <summary>Tile edge length in device-independent pixels.</summary>
    public const int TileSizeDip = 256;

    /// <summary>
    /// Half the EPSG:3857 projected world extent in metres
    /// (<c>π · 6378137</c>); the grid spans <c>[-Extent, +Extent]</c> on both
    /// axes.
    /// </summary>
    public const double Extent = Math.PI * WebMercator.EarthRadius;

    /// <summary>
    /// EPSG:3857 resolution (m/px) at band 0 for 256-px tiles:
    /// <c>2 · Extent / TileSizeDip</c> ≈ 156543.034.
    /// </summary>
    public const double Band0Resolution = 2.0 * Extent / TileSizeDip;

    /// <summary>Smallest (most zoomed-out) band the grid emits.</summary>
    public const int MinBand = 0;

    /// <summary>Largest (most zoomed-in) band the grid emits.</summary>
    public const int MaxBand = 24;

    /// <summary>The canonical EPSG:3857 resolution (m/px) for a band.</summary>
    public static double ResolutionForBand(int band) => Band0Resolution / Math.Pow(2.0, band);

    /// <summary>The world size, in metres, of one tile at a band.</summary>
    public static double TileWorldSize(int band) => 2.0 * Extent / Math.Pow(2.0, band);

    /// <summary>The number of tiles along one axis at a band (<c>2^band</c>).</summary>
    public static int TilesPerAxis(int band) => 1 << band;

    /// <summary>
    /// Selects the band whose canonical resolution is closest (in log-space, so
    /// the choice is symmetric across the octave) to <paramref name="resolution"/>,
    /// clamped to <see cref="MinBand"/>..<see cref="MaxBand"/>. A live viewport
    /// at an arbitrary resolution snaps to this band; the composite scales the
    /// band's tiles by <c>ResolutionForBand(band) / resolution</c> to fit.
    /// </summary>
    public static int BandForResolution(double resolution)
    {
        if (resolution <= 0 || double.IsNaN(resolution) || double.IsInfinity(resolution))
        {
            return MinBand;
        }

        var band = (int)Math.Round(Math.Log2(Band0Resolution / resolution));
        return Math.Clamp(band, MinBand, MaxBand);
    }

    /// <summary>
    /// The EPSG:3857 world bounds (metres) of a tile, gutter excluded.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY) TileWorldBounds(TileKey key)
    {
        var size = TileWorldSize(key.Band);
        var minX = -Extent + key.X * size;
        var maxY = Extent - key.Y * size;
        return (minX, maxY - size, minX + size, maxY);
    }

    /// <summary>
    /// Enumerates every tile at <paramref name="band"/> whose bounds intersect
    /// the north-up viewport centred at (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>) in EPSG:3857, sized
    /// <paramref name="widthDip"/> × <paramref name="heightDip"/> DIP at
    /// <paramref name="resolution"/> m/px. The <b>Y</b> (latitude) index is
    /// clamped to the band's valid range (northing is bounded at the poles), so
    /// a viewport overhanging the top/bottom yields no out-of-range row. The
    /// <b>X</b> (longitude) index is <em>not</em> clamped: EPSG:3857 is periodic
    /// east-west and continuous-frame antimeridian data projects to columns
    /// outside <c>[0, perAxis-1]</c> (see <see cref="VisibleTileRange"/>).
    /// </summary>
    public static IReadOnlyList<TileKey> VisibleTiles(
        double centerX, double centerY, double widthDip, double heightDip, double resolution, int band)
    {
        var result = new List<TileKey>();
        var range = VisibleTileRange(centerX, centerY, widthDip, heightDip, resolution, band);
        for (var y = range.YStart; y <= range.YEnd; y++)
        {
            for (var x = range.XStart; x <= range.XEnd; x++)
            {
                result.Add(new TileKey(band, x, y));
            }
        }

        return result;
    }

    /// <summary>
    /// The inclusive tile-index range covering the north-up viewport at
    /// <paramref name="band"/>. A degenerate viewport yields an empty range
    /// (<c>XStart &gt; XEnd</c>). Shared by <see cref="VisibleTiles"/> and
    /// <see cref="PredictedTiles"/> so both tile the viewport identically.
    /// </summary>
    /// <remarks>
    /// The <b>Y</b> (latitude) axis is clamped to <c>[0, perAxis-1]</c>: the
    /// Web-Mercator northing is physically bounded at the poles, so a viewport
    /// overhanging the top/bottom yields no out-of-range row. The <b>X</b>
    /// (longitude) axis is <em>not</em> clamped to the standard world, because
    /// EPSG:3857 is periodic east-west and an antimeridian-spanning dataset
    /// kept in a continuous longitude frame (e.g. the US NWS S-411 sea-ice
    /// product, ~175°E → ~225°E) projects to world-X beyond ±<see cref="Extent"/>.
    /// Clamping X collapsed such data into the boundary tile column (issue: the
    /// viewer's tiled render subsystem drew a thin sliver at ±180° while the
    /// non-tiled and headless paths framed it correctly). Instead the raw
    /// column indices are kept — <see cref="TileWorldBounds"/> maps them to the
    /// correct continuous world position — so a dataset kept in a continuous
    /// frame is reachable at its true columns wherever the viewport is panned.
    /// The span is <em>not</em> capped to one world: the chart data is drawn
    /// exactly once at its true position (it is not world-copied like the
    /// basemap), so every visible column must be enumerable or the data would
    /// pop between world-copies as the pan crossed a world boundary. Only a
    /// generous absolute guard bounds a pathological zoom-out, and it keeps the
    /// window anchored at the left edge (never re-centred) so panning slides the
    /// columns smoothly instead of flipping the enumerated world.
    /// </remarks>
    public static TileRange VisibleTileRange(
        double centerX, double centerY, double widthDip, double heightDip, double resolution, int band)
    {
        var perAxis = TilesPerAxis(band);
        if (widthDip <= 0 || heightDip <= 0 || resolution <= 0)
        {
            return new TileRange(0, -1, 0, -1, perAxis);
        }

        var halfW = widthDip * 0.5 * resolution;
        var halfH = heightDip * 0.5 * resolution;
        var size = TileWorldSize(band);

        var xStart = (int)Math.Floor((centerX - halfW + Extent) / size);
        var xEnd = (int)Math.Floor((centerX + halfW + Extent) / size);
        // Enumerate every visible column (including those beyond the standard
        // world for antimeridian data); never clamp X into [0, perAxis-1] and
        // never re-anchor the window. A large absolute guard only bounds a
        // pathological zoom-out — it is far beyond any realistic viewport, so it
        // does not truncate the visible copies in practice.
        const int MaxColumns = 4096;
        if (xEnd - xStart + 1 > MaxColumns)
        {
            xEnd = xStart + MaxColumns - 1;
        }
        // Y is inverted (XYZ): the top row (Y=0) is the northernmost.
        var yStart = (int)Math.Floor((Extent - (centerY + halfH)) / size);
        var yEnd = (int)Math.Floor((Extent - (centerY - halfH)) / size);

        return new TileRange(
            xStart,
            xEnd,
            Math.Clamp(yStart, 0, perAxis - 1),
            Math.Clamp(yEnd, 0, perAxis - 1),
            perAxis);
    }

    /// <summary>
    /// The <b>warm set</b> for prediction/pre-warm (design §3.6): tiles likely to
    /// become visible soon, so the worker can rasterise them <i>before</i> a pan
    /// or zoom exposes them. It is the union of
    /// <list type="bullet">
    /// <item>a <paramref name="haloRings"/>-ring halo around the visible range
    /// (covers a pan in any direction);</item>
    /// <item>a <b>directional fan</b> projected along the velocity
    /// (<paramref name="velocityX"/>, <paramref name="velocityY"/> in EPSG:3857
    /// m/s), whose depth grows with speed up to <paramref name="maxFanDepth"/>
    /// tiles (anticipates a fling); and</item>
    /// <item>the centre tiles at <c>band ± 1</c> (a slight zoom bias).</item>
    /// </list>
    /// Visible tiles are excluded (the caller rasterises those at higher
    /// priority). All indices are world-clamped, so no out-of-range key escapes.
    /// </summary>
    public static IReadOnlyList<TileKey> PredictedTiles(
        double centerX, double centerY, double widthDip, double heightDip, double resolution, int band,
        double velocityX, double velocityY,
        double lookAheadSeconds = 0.5, int maxFanDepth = 4, int haloRings = 1)
    {
        var result = new List<TileKey>();
        var visible = VisibleTileRange(centerX, centerY, widthDip, heightDip, resolution, band);
        if (visible.IsEmpty)
        {
            return result;
        }

        var seen = new HashSet<TileKey>();
        var perAxis = visible.PerAxis;

        // Mark the visible range so we never emit a visible key as "predicted".
        for (var y = visible.YStart; y <= visible.YEnd; y++)
        {
            for (var x = visible.XStart; x <= visible.XEnd; x++)
            {
                seen.Add(new TileKey(band, x, y));
            }
        }

        // 1) Halo: expand the visible range by haloRings on every side.
        haloRings = Math.Max(haloRings, 0);
        AddRange(
            result, seen, band,
            visible.XStart - haloRings, visible.XEnd + haloRings,
            visible.YStart - haloRings, visible.YEnd + haloRings,
            perAxis);

        // 2) Directional fan: step the viewport forward along the velocity,
        //    depth proportional to speed (capped), and add each shifted range.
        var speed = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
        if (speed > 0 && lookAheadSeconds > 0 && maxFanDepth > 0)
        {
            var size = TileWorldSize(band);
            var aheadTiles = speed * lookAheadSeconds / size;
            var depth = Math.Clamp((int)Math.Ceiling(aheadTiles), 1, maxFanDepth);
            var stepX = velocityX / speed * size;
            var stepY = velocityY / speed * size;
            for (var d = 1; d <= depth; d++)
            {
                var shifted = VisibleTileRange(
                    centerX + stepX * d, centerY + stepY * d, widthDip, heightDip, resolution, band);
                if (!shifted.IsEmpty)
                {
                    AddRange(
                        result, seen, band,
                        shifted.XStart, shifted.XEnd, shifted.YStart, shifted.YEnd, perAxis);
                }
            }
        }

        // 3) Zoom bias: the centre tiles at band ± 1.
        AddCenterTile(result, seen, centerX, centerY, band - 1);
        AddCenterTile(result, seen, centerX, centerY, band + 1);

        return result;
    }

    private static void AddRange(
        List<TileKey> result, HashSet<TileKey> seen, int band,
        int xStart, int xEnd, int yStart, int yEnd, int perAxis)
    {
        // X is not clamped to the standard world (EPSG:3857 is periodic east-west
        // and continuous-frame antimeridian data lives beyond ±Extent — see
        // VisibleTileRange); only Y (latitude) is bounded at the poles.
        yStart = Math.Clamp(yStart, 0, perAxis - 1);
        yEnd = Math.Clamp(yEnd, 0, perAxis - 1);
        for (var y = yStart; y <= yEnd; y++)
        {
            for (var x = xStart; x <= xEnd; x++)
            {
                var key = new TileKey(band, x, y);
                if (seen.Add(key))
                {
                    result.Add(key);
                }
            }
        }
    }

    private static void AddCenterTile(
        List<TileKey> result, HashSet<TileKey> seen, double centerX, double centerY, int band)
    {
        if (band < MinBand || band > MaxBand)
        {
            return;
        }

        var size = TileWorldSize(band);
        var perAxis = TilesPerAxis(band);
        // X unclamped (periodic world / continuous-frame antimeridian data); Y
        // clamped at the poles.
        var x = (int)Math.Floor((centerX + Extent) / size);
        var y = Math.Clamp((int)Math.Floor((Extent - centerY) / size), 0, perAxis - 1);
        var key = new TileKey(band, x, y);
        if (seen.Add(key))
        {
            result.Add(key);
        }
    }

    /// <summary>
    /// The <b>idle cross-band pre-warm set</b> (design §3.6, issue&#160;#428): the
    /// tiles of the immediately adjacent bands (<paramref name="band"/>&#160;±&#160;1)
    /// that cover the <em>current</em> viewport, so a subsequent zoom-in or
    /// zoom-out starts warm instead of paying full cold-tile latency at the new
    /// band. Unlike <see cref="PredictedTiles"/> (which biases only the two
    /// band&#160;±&#160;1 <i>centre</i> tiles), this warms the whole viewport
    /// footprint of each adjacent band.
    /// </summary>
    /// <remarks>
    /// The result is ordered <b>centre-first</b> (nearest tile-centre to the
    /// viewport centre first, ties broken deterministically on
    /// <c>(Band, Y, X)</c>) and truncated to <paramref name="maxTiles"/>, so a
    /// bounded warm budget keeps the most-central — most-likely-next-zoom-target —
    /// tiles. Out-of-range neighbour bands (<c>&lt; MinBand</c> or
    /// <c>&gt; MaxBand</c>) are skipped. The band&#160;±&#160;1 tiles are selected
    /// against the same world viewport at the same live <paramref name="resolution"/>;
    /// only the tile size differs by band (band+1 yields ~4× the tiles, band-1
    /// ~¼×), which is exactly why the centre-first cap matters for the finer band.
    /// Callers run this only when otherwise idle and drain it at the lowest
    /// worker priority, so it never competes with visible or same-band predicted
    /// work.
    /// </remarks>
    /// <param name="centerX">Viewport centre X (EPSG:3857 metres).</param>
    /// <param name="centerY">Viewport centre Y (EPSG:3857 metres).</param>
    /// <param name="widthDip">Viewport width in DIP.</param>
    /// <param name="heightDip">Viewport height in DIP.</param>
    /// <param name="resolution">Live resolution in metres/DIP.</param>
    /// <param name="band">The current (live-fit) band; neighbours are <paramref name="band"/>&#160;±&#160;1.</param>
    /// <param name="maxTiles">The maximum number of tiles to return (bounds the warm budget). Values ≤ 0 yield an empty set.</param>
    /// <returns>The centre-first, capped adjacent-band warm set (may be empty).</returns>
    public static IReadOnlyList<TileKey> CrossBandPrewarmTiles(
        double centerX, double centerY, double widthDip, double heightDip, double resolution, int band,
        int maxTiles)
    {
        if (maxTiles <= 0)
        {
            return Array.Empty<TileKey>();
        }

        var found = false;

        // Keep only the maxTiles nearest candidates via a bounded max-heap
        // (worst-first) rather than materialising and sorting every adjacent-band
        // tile: VisibleTileRange can enumerate thousands of cells for band + 1, so a
        // full O(n log n) sort plus O(n) allocation on an idle frame is wasteful when
        // the renderer only ever keeps the top maxTiles (24). This is O(n log K) time
        // and O(K) space; the drained order is identical to the previous full sort.
        var heap = new PriorityQueue<TileKey, (double Dist, int Band, int Y, int X)>(
            CrossBandWorstFirstComparer);
        for (var neighbour = band - 1; neighbour <= band + 1; neighbour += 2)
        {
            if (neighbour < MinBand || neighbour > MaxBand)
            {
                continue;
            }

            var range = VisibleTileRange(centerX, centerY, widthDip, heightDip, resolution, neighbour);
            if (range.IsEmpty)
            {
                continue;
            }

            for (var y = range.YStart; y <= range.YEnd; y++)
            {
                for (var x = range.XStart; x <= range.XEnd; x++)
                {
                    var key = new TileKey(neighbour, x, y);
                    var priority = (CenterDistanceSquared(key, centerX, centerY), neighbour, y, x);
                    found = true;
                    if (heap.Count < maxTiles)
                    {
                        heap.Enqueue(key, priority);
                    }
                    else
                    {
                        // Heap is full: push the candidate then evict the current
                        // worst (farthest) of the K + 1, so the heap always retains
                        // the K nearest tiles seen so far. If the candidate is itself
                        // the worst, EnqueueDequeue drops it straight back out.
                        heap.EnqueueDequeue(key, priority);
                    }
                }
            }
        }

        if (!found)
        {
            return Array.Empty<TileKey>();
        }

        // Drain worst-first (the comparer puts the farthest tile on top) and fill the
        // result back-to-front, producing the centre-first order — identical to the
        // previous Sort's (Dist, Band, Y, X) ordering. Ties are impossible because
        // each TileKey is unique, so the order is fully deterministic and matches the
        // (Band, Y, X) tie-break S100VectorTileRenderer.TakeNearest also uses.
        var result = new TileKey[heap.Count];
        for (var i = result.Length - 1; i >= 0; i--)
        {
            result[i] = heap.Dequeue();
        }

        return result;
    }

    /// <summary>
    /// Worst-first ordering (farthest tile-centre, then largest <c>(Band, Y, X)</c>)
    /// for the bounded top-K max-heap in <see cref="CrossBandPrewarmTiles"/>: a
    /// <see cref="PriorityQueue{TElement,TPriority}"/> is a min-heap, so inverting the
    /// nearest-first order surfaces the most-evictable tile on top and lets
    /// <see cref="PriorityQueue{TElement,TPriority}.EnqueueDequeue"/> retain the K
    /// nearest. The drained result is the exact inverse — centre-first.
    /// </summary>
    private static readonly IComparer<(double Dist, int Band, int Y, int X)> CrossBandWorstFirstComparer =
        Comparer<(double Dist, int Band, int Y, int X)>.Create(static (a, b) =>
        {
            var byDist = b.Dist.CompareTo(a.Dist);
            if (byDist != 0)
            {
                return byDist;
            }

            if (a.Band != b.Band)
            {
                return b.Band.CompareTo(a.Band);
            }

            if (a.Y != b.Y)
            {
                return b.Y.CompareTo(a.Y);
            }

            return b.X.CompareTo(a.X);
        });

    /// <summary>The squared EPSG:3857 distance from a tile's world centre to a point.</summary>
    private static double CenterDistanceSquared(TileKey key, double centerX, double centerY)
    {
        var (minX, minY, maxX, maxY) = TileWorldBounds(key);
        var dx = (minX + maxX) * 0.5 - centerX;
        var dy = (minY + maxY) * 0.5 - centerY;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// Projects EPSG:3857 world bounds to the north-up viewport's DIP screen
    /// rectangle (top-left origin, +Y down). Used both to place a tile's core
    /// and to place its guttered image.
    /// </summary>
    public static ScreenRect WorldToScreenRect(
        double worldMinX, double worldMinY, double worldMaxX, double worldMaxY,
        double centerX, double centerY, double widthDip, double heightDip, double resolution)
    {
        var halfW = widthDip * 0.5;
        var halfH = heightDip * 0.5;
        var left = halfW + (worldMinX - centerX) / resolution;
        var right = halfW + (worldMaxX - centerX) / resolution;
        var top = halfH - (worldMaxY - centerY) / resolution;
        var bottom = halfH - (worldMinY - centerY) / resolution;
        return new ScreenRect(left, top, right, bottom);
    }

    /// <summary>
    /// The axis-aligned DIP size that bounds the <paramref name="widthDip"/> ×
    /// <paramref name="heightDip"/> viewport after it is rotated by
    /// <paramref name="rotationDegrees"/> about its centre. Tile selection
    /// (<see cref="VisibleTiles"/>, <see cref="PredictedTiles"/>) uses this
    /// enlarged size so a rotated viewport's corners — which poke outside the
    /// north-up box — are still covered by rasterised tiles instead of going
    /// blank. The projection itself (<see cref="WorldToScreenRect"/>) keeps the
    /// real DIP size; the canvas is rotated about the centre at composite time.
    /// </summary>
    public static (double Width, double Height) RotatedCoverSize(
        double widthDip, double heightDip, double rotationDegrees)
    {
        var rad = rotationDegrees * Math.PI / 180.0;
        var c = Math.Abs(Math.Cos(rad));
        var s = Math.Abs(Math.Sin(rad));
        return (widthDip * c + heightDip * s, widthDip * s + heightDip * c);
    }

    /// <summary>
    /// Computes the off-screen layout for compositing a rotated viewport's tiles
    /// north-up before rotating the finished image as a unit (issue&#160;#330). The
    /// north-up composite must span the rotated cover box
    /// (<paramref name="coverWidth"/> × <paramref name="coverHeight"/> DIP, from
    /// <see cref="RotatedCoverSize"/>) centred on the screen centre so the corners
    /// are filled once rotated, and is rasterised at device resolution
    /// (<paramref name="deviceScale"/>) to stay crisp on HiDPI.
    /// </summary>
    /// <param name="widthDip">Live viewport width in DIP.</param>
    /// <param name="heightDip">Live viewport height in DIP.</param>
    /// <param name="coverWidth">Rotated cover-box width in DIP.</param>
    /// <param name="coverHeight">Rotated cover-box height in DIP.</param>
    /// <param name="deviceScale">DIP→device-pixel scale from the canvas matrix.</param>
    /// <returns>
    /// The cover box's top-left in screen DIP coordinates
    /// (<c>OriginX</c>, <c>OriginY</c>) — also the rotated blit's destination
    /// origin — and the off-screen surface size in device pixels
    /// (<c>PixelWidth</c>, <c>PixelHeight</c>).
    /// </returns>
    public static (double OriginX, double OriginY, int PixelWidth, int PixelHeight) RotationCompositeLayout(
        double widthDip, double heightDip, double coverWidth, double coverHeight, double deviceScale)
    {
        var scale = deviceScale > 0 && !double.IsNaN(deviceScale) ? deviceScale : 1.0;
        var originX = widthDip * 0.5 - coverWidth * 0.5;
        var originY = heightDip * 0.5 - coverHeight * 0.5;
        var pixelWidth = (int)Math.Ceiling(coverWidth * scale);
        var pixelHeight = (int)Math.Ceiling(coverHeight * scale);
        return (originX, originY, pixelWidth, pixelHeight);
    }

    /// <summary>The DIP screen rect of a tile's core (gutter excluded).</summary>
    public static ScreenRect TileCoreScreenRect(
        TileKey key, double centerX, double centerY, double widthDip, double heightDip, double resolution)
    {
        var (minX, minY, maxX, maxY) = TileWorldBounds(key);
        return WorldToScreenRect(minX, minY, maxX, maxY, centerX, centerY, widthDip, heightDip, resolution);
    }
}
