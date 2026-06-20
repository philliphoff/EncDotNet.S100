using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Renderers.Skia.Scene;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using SkiaSharp;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics;
using CoreViewport = EncDotNet.S100.Pipelines.Viewport;
using SceneRgbaColor = EncDotNet.S100.Pipelines.RgbaColor;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The <b>TiledScene ("B") render subsystem, Phase&#160;2</b>: a Mapsui
/// <i>custom layer renderer</i> that draws the chart base plane as a <b>pyramid
/// of cached tiles</b> rasterised from the backend-agnostic
/// <see cref="VectorScene"/> IR on worker threads, then composited on the
/// UI/render thread. It generalises the Phase&#160;1 single-surface renderer
/// (<see cref="S100VectorSceneRenderer"/>) by partitioning the over-render
/// margin into an origin-anchored <see cref="TileGrid"/> so a constant-zoom pan
/// reuses every interior tile and only rasterises the newly-exposed perimeter —
/// making pan cost scale with perimeter, not viewport area
/// (<c>docs/design/S100-Render-Subsystem-Design.md</c> §3.2–§3.5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Compositor (UI thread, bounded).</b> Each frame the renderer snaps the
/// live resolution to a <see cref="TileGrid"/> band, finds the visible tiles,
/// and blits the <i>best available</i> for every slot: the exact band tile when
/// cached, otherwise cached tiles from other bands scaled to fit (transient
/// zoom blur) so a hole is never shown. Missing visible tiles are enqueued for
/// the worker at visible priority; the compositor itself does zero
/// rasterisation and never blocks.
/// </para>
/// <para>
/// <b>Workers.</b> A single coalescing worker per layer drains the visible-miss
/// set (replaced every frame, so tiles panned out of view are dropped before
/// they render). Each tile is rendered with a <b>gutter</b> (bleed beyond the
/// tile bounds) and hard-clipped to its core on composite, so lines and area
/// fills stay continuous across seams. Finished tiles enter the
/// native-byte-bounded <see cref="TileCache"/> and trigger a single repaint via
/// <see cref="RequestRedraw"/>.
/// </para>
/// <para>
/// <b>Scope (Phase&#160;2).</b> Tiling + LRU cache + best-available compositor.
/// No prediction/pre-warm (Phase&#160;3), no disk cache or separate label plane
/// (Phase&#160;4). North-up only; a rotated viewport draws nothing for that
/// frame.
/// </para>
/// </remarks>
public static class S100VectorTileRenderer
{
    /// <summary>
    /// The <see cref="ILayer.CustomLayerRendererName"/> value that routes a
    /// layer through this renderer.
    /// </summary>
    public const string RendererName = "s100.vector.tile";

    /// <summary>
    /// Gutter, in DIP, rasterised beyond each tile's bounds on every edge and
    /// hard-clipped away on composite, so strokes crossing a tile seam keep
    /// their joins/caps. Read once from <c>S100_VECTOR_TILE_GUTTER</c>
    /// (default 64).
    /// </summary>
    public static double GutterDip { get; } = ReadDouble("S100_VECTOR_TILE_GUTTER", 64.0, 0.0, 256.0);

    /// <summary>
    /// Hot-cache native-byte budget per layer. Read once from
    /// <c>S100_VECTOR_TILE_BUDGET_MB</c> (default 256&#160;MB).
    /// </summary>
    public static long BudgetBytes { get; } =
        (long)ReadDouble("S100_VECTOR_TILE_BUDGET_MB", 256.0, 4.0, 4096.0) * 1024 * 1024;

    /// <summary>Hard cap on either tile-image dimension, in device pixels.</summary>
    private const int MaxImageDimension = 4096;

    /// <summary>
    /// Invoked (on a worker thread) when a tile publishes, so the host can
    /// request a single repaint. The viewer marshals a <c>RefreshGraphics()</c>
    /// onto the UI thread.
    /// </summary>
    public static Action? RequestRedraw { get; set; }

    private static readonly ConditionalWeakTable<ILayer, TileState> s_states = new();

    private static readonly SKSamplingOptions s_sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private static double ReadDouble(string name, double fallback, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            && v >= min && v <= max)
        {
            return v;
        }

        return fallback;
    }

    /// <summary>Registers this renderer under <see cref="RendererName"/>. Idempotent.</summary>
    public static void Register()
    {
        MapRenderer.RegisterLayerRenderer(RendererName, Render);
    }

    /// <summary>
    /// Binds the fully-resolved <see cref="VectorScene"/> for a layer and
    /// invalidates its tile cache (a new generation), so the next frame
    /// re-rasterises from the new scene.
    /// </summary>
    public static void BindScene(ILayer layer, VectorScene scene)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(scene);

        var state = s_states.GetValue(layer, static _ => new TileState());
        lock (state.Sync)
        {
            state.Scene = scene;
            state.Generation++;
            state.Cache.Clear();
            state.InFlight.Clear();
            state.PendingVisible.Clear();
            state.PendingPredicted.Clear();
            state.PredictedInCache.Clear();
            // A new scene is a teleport for prediction: drop the stale velocity
            // anchor so the first frame doesn't fling the fan in a random
            // direction.
            state.HasLastCenter = false;
            state.VelocityX = 0;
            state.VelocityY = 0;
        }
    }

    /// <summary>
    /// The render handler Mapsui invokes for tagged layers. Composites the best
    /// available tiles for the live viewport and enqueues missing visible tiles.
    /// </summary>
    public static void Render(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layer);

        if (!viewport.HasSize())
        {
            return;
        }

        var resolution = viewport.Resolution;
        if (resolution <= 0 || viewport.Rotation != 0)
        {
            // North-up only (v1); a rotated viewport breaks the axis-aligned blit.
            return;
        }

        var deviceScale = canvas.TotalMatrix.ScaleX;
        if (deviceScale <= 0 || float.IsNaN(deviceScale))
        {
            deviceScale = 1f;
        }

        var state = s_states.GetValue(layer, static _ => new TileState());

        var band = TileGrid.BandForResolution(resolution);
        var centerX = viewport.CenterX;
        var centerY = viewport.CenterY;
        var widthDip = viewport.Width;
        var heightDip = viewport.Height;

        var visible = TileGrid.VisibleTiles(centerX, centerY, widthDip, heightDip, resolution, band);

        var startWorker = false;
        var coldExposure = 0;
        var predictionHits = 0L;
        var compositeStart = Stopwatch.GetTimestamp();
        lock (state.Sync)
        {
            if (state.Scene is null)
            {
                return;
            }

            UpdateVelocity(state, centerX, centerY);

            // Enqueue visible misses at high priority (replace the pending set
            // every frame so tiles panned out of view are dropped — cancellation).
            state.PendingVisible.Clear();
            var visibleSet = new HashSet<TileKey>(visible.Count);
            foreach (var key in visible)
            {
                visibleSet.Add(key);
                if (state.Cache.Contains(key))
                {
                    // A tile we rasterised speculatively is now actually visible:
                    // a prediction hit. Count it once.
                    if (state.PredictedInCache.Remove(key))
                    {
                        predictionHits++;
                    }
                }
                else
                {
                    coldExposure++;
                    if (!state.InFlight.Contains(key))
                    {
                        state.PendingVisible.Add(key);
                    }
                }
            }

            // Enqueue the prediction warm set at low priority (visible-first in
            // the worker). Excludes visible / cached / in-flight tiles.
            state.PendingPredicted.Clear();
            var predicted = TileGrid.PredictedTiles(
                centerX, centerY, widthDip, heightDip, resolution, band,
                state.VelocityX, state.VelocityY);
            foreach (var key in predicted)
            {
                if (!visibleSet.Contains(key)
                    && !state.Cache.Contains(key)
                    && !state.InFlight.Contains(key))
                {
                    state.PendingPredicted.Add(key);
                }
            }

            if (state.PendingVisible.Count > 0 || state.PendingPredicted.Count > 0)
            {
                state.PendingDeviceScale = deviceScale;
                state.PendingGeneration = state.Generation;
                if (!state.Rendering)
                {
                    state.Rendering = true;
                    startWorker = true;
                }
            }

            // Bound the prediction-hit bookkeeping: a tile predicted then
            // evicted before it was ever shown would otherwise linger. When the
            // set grows past the cache's own capacity, drop keys no longer
            // resident (those can never score a hit).
            if (state.PredictedInCache.Count > state.Cache.Count + 256)
            {
                state.PredictedInCache.RemoveWhere(k => !state.Cache.Contains(k));
            }

            Composite(canvas, state, band, centerX, centerY, widthDip, heightDip, resolution);
        }

        S100Diag.Telemetry.TileCompositeDuration.Record(
            Stopwatch.GetElapsedTime(compositeStart).TotalMilliseconds);
        S100Diag.Telemetry.TileColdExposure.Record(coldExposure);
        if (predictionHits > 0)
        {
            S100Diag.Telemetry.TilePredictionHits.Add(predictionHits);
        }

        if (startWorker)
        {
            _ = Task.Run(() => Worker(state));
        }
    }

    /// <summary>
    /// Folds the current viewport centre into the per-layer velocity EMA (held
    /// under <c>state.Sync</c>), aiming the prediction fan. A teleport (no prior
    /// sample, or the scene/generation just changed) seeds the anchor without
    /// emitting a spurious velocity.
    /// </summary>
    private static void UpdateVelocity(TileState state, double centerX, double centerY)
    {
        var now = Stopwatch.GetTimestamp();
        if (state.HasLastCenter)
        {
            var dt = Stopwatch.GetElapsedTime(state.LastCenterTimestamp).TotalSeconds;
            var (vx, vy) = VelocityEstimator.Update(
                state.VelocityX, state.VelocityY,
                centerX - state.LastCenterX, centerY - state.LastCenterY, dt);
            state.VelocityX = vx;
            state.VelocityY = vy;
        }

        state.LastCenterX = centerX;
        state.LastCenterY = centerY;
        state.LastCenterTimestamp = now;
        state.HasLastCenter = true;
    }

    /// <summary>
    /// Draws the best-available tiles for the frame, holding <c>state.Sync</c>
    /// so the worker cannot evict/dispose an image mid-blit. Coarser/finer
    /// cached bands are drawn first as a backdrop (nearest band last, just under
    /// the target), then exact target-band tiles on top, each hard-clipped to
    /// its core.
    /// </summary>
    private static void Composite(
        SKCanvas canvas, TileState state, int band,
        double centerX, double centerY, double widthDip, double heightDip, double resolution)
    {
        // Backdrop: cached tiles from other bands that intersect the viewport,
        // farthest band first so the closest resolution ends up on top.
        var fallback = new List<TileKey>();
        foreach (var key in state.Cache.SnapshotKeys())
        {
            if (key.Band == band)
            {
                continue;
            }

            var core = TileGrid.TileCoreScreenRect(key, centerX, centerY, widthDip, heightDip, resolution);
            if (core.IntersectsViewport(widthDip, heightDip))
            {
                fallback.Add(key);
            }
        }

        fallback.Sort((a, b) => Math.Abs(b.Band - band).CompareTo(Math.Abs(a.Band - band)));
        foreach (var key in fallback)
        {
            BlitTile(canvas, state, key, centerX, centerY, widthDip, heightDip, resolution);
        }

        // Exact target band on top (crisp where present).
        foreach (var key in TileGrid.VisibleTiles(centerX, centerY, widthDip, heightDip, resolution, band))
        {
            BlitTile(canvas, state, key, centerX, centerY, widthDip, heightDip, resolution);
        }
    }

    /// <summary>
    /// Blits one cached tile (if resident): positions its guttered image by
    /// world bounds and hard-clips to the tile core so adjacent tiles meet
    /// exactly with no seam or double-drawn gutter.
    /// </summary>
    private static void BlitTile(
        SKCanvas canvas, TileState state, TileKey key,
        double centerX, double centerY, double widthDip, double heightDip, double resolution)
    {
        var image = state.Cache.TryGet(key);
        if (image is null)
        {
            return;
        }

        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var gutterWorld = GutterDip * TileGrid.ResolutionForBand(key.Band);

        var core = TileGrid.WorldToScreenRect(minX, minY, maxX, maxY, centerX, centerY, widthDip, heightDip, resolution);
        var full = TileGrid.WorldToScreenRect(
            minX - gutterWorld, minY - gutterWorld, maxX + gutterWorld, maxY + gutterWorld,
            centerX, centerY, widthDip, heightDip, resolution);

        var coreRect = new SKRect((float)core.Left, (float)core.Top, (float)core.Right, (float)core.Bottom);
        var fullRect = new SKRect((float)full.Left, (float)full.Top, (float)full.Right, (float)full.Bottom);

        canvas.Save();
        canvas.ClipRect(coreRect, SKClipOperation.Intersect, antialias: false);
        canvas.DrawImage(image, fullRect, s_sampling);
        canvas.Restore();
    }

    private static void Worker(TileState state)
    {
        while (true)
        {
            TileKey key;
            float deviceScale;
            long generation;
            VectorScene scene;
            bool isPrediction;

            lock (state.Sync)
            {
                // Visible tiles always drain before speculative ones, so
                // prediction work yields to anything actually on screen.
                if (state.Scene is null
                    || (state.PendingVisible.Count == 0 && state.PendingPredicted.Count == 0))
                {
                    state.Rendering = false;
                    return;
                }

                if (state.PendingVisible.Count > 0)
                {
                    key = TakeOne(state.PendingVisible);
                    isPrediction = false;
                }
                else
                {
                    key = TakeOne(state.PendingPredicted);
                    isPrediction = true;
                }

                deviceScale = state.PendingDeviceScale;
                generation = state.PendingGeneration;
                scene = state.Scene;
                state.InFlight.Add(key);
            }

            SKImage? image = null;
            try
            {
                var rasterStart = Stopwatch.GetTimestamp();
                using var bitmap = RasterizeTile(scene, key, deviceScale);
                image = SKImage.FromBitmap(bitmap);
                S100Diag.Telemetry.TileRasterizeDuration.Record(
                    Stopwatch.GetElapsedTime(rasterStart).TotalMilliseconds);
            }
            catch
            {
                image?.Dispose();
                image = null;
            }

            var published = false;
            lock (state.Sync)
            {
                state.InFlight.Remove(key);
                if (image is not null && generation == state.Generation)
                {
                    state.Cache.Put(key, image);
                    published = true;
                    if (isPrediction)
                    {
                        // Track so a later visible frame can score it as a hit.
                        state.PredictedInCache.Add(key);
                    }
                }
                else
                {
                    image?.Dispose();
                }
            }

            if (isPrediction)
            {
                S100Diag.Telemetry.TilePredictionRasterized.Add(1);
            }

            if (published)
            {
                RequestRedraw?.Invoke();
            }
        }
    }

    private static TileKey TakeOne(HashSet<TileKey> pending)
    {
        foreach (var k in pending)
        {
            pending.Remove(k);
            return k;
        }

        return default;
    }

    /// <summary>
    /// Rasterises a single tile (core + gutter) from the scene at its band
    /// resolution and the frame's device scale.
    /// </summary>
    private static SKBitmap RasterizeTile(VectorScene scene, TileKey key, float deviceScale)
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var bandResolution = TileGrid.ResolutionForBand(key.Band);
        var gutterWorld = GutterDip * bandResolution;

        var fullMinX = minX - gutterWorld;
        var fullMaxX = maxX + gutterWorld;
        var fullMinY = minY - gutterWorld;
        var fullMaxY = maxY + gutterWorld;

        var (minLon, minLat) = WebMercator.ToLonLat(fullMinX, fullMinY);
        var (maxLon, maxLat) = WebMercator.ToLonLat(fullMaxX, fullMaxY);

        var sizeDip = TileGrid.TileSizeDip + 2 * GutterDip;
        var px = (int)Math.Round(sizeDip * deviceScale);
        px = Math.Clamp(px, 1, MaxImageDimension);

        var denom = S100VectorSceneRenderer.ScaleDenominatorFor(
            (minX + maxX) * 0.5, (minY + maxY) * 0.5, bandResolution);

        var viewport = new CoreViewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = px,
            HeightPixels = px,
            ScaleDenominator = denom,
        };

        var renderer = new SkiaDisplayListRenderer
        {
            Background = SceneRgbaColor.Transparent,
            HonorScaleVisibility = true,
        };

        return renderer.Render(scene, viewport);
    }

    /// <summary>Per-layer tiling state, held in a weak table keyed by layer.</summary>
    private sealed class TileState
    {
        public readonly object Sync = new();

        public VectorScene? Scene;
        public long Generation;

        public readonly TileCache Cache = new(BudgetBytes);
        public readonly HashSet<TileKey> InFlight = new();

        // Visible misses drain before speculative (predicted) tiles.
        public readonly HashSet<TileKey> PendingVisible = new();
        public readonly HashSet<TileKey> PendingPredicted = new();

        // Speculatively-rasterised tiles still resident and not yet shown; a
        // later visible frame that finds one scores a prediction hit.
        public readonly HashSet<TileKey> PredictedInCache = new();

        // Viewport-centre velocity EMA (EPSG:3857 m/s) for the prediction fan.
        public double VelocityX;
        public double VelocityY;
        public double LastCenterX;
        public double LastCenterY;
        public long LastCenterTimestamp;
        public bool HasLastCenter;

        public bool Rendering;
        public float PendingDeviceScale = 1f;
        public long PendingGeneration;
    }
}
