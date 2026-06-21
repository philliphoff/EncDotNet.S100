using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    /// How many bands away from the target a cached tile may be and still be
    /// drawn as a fill-the-gap backdrop. Bounding this is both correctness and
    /// safety: it stops tiles from many zoom levels stacking up at different
    /// scales (the multi-band "ghosting" artefact), and it caps the number of
    /// tiles composited in a single frame. Without the cap, zooming far out
    /// (target near band&#160;0) would draw every finer-band tile in the cache —
    /// hundreds at once — which both looks wrong and, under GPU residency, used
    /// to overflow the texture budget and free a texture mid-frame. A single
    /// zoom step is ±1 band, so a small window keeps the smooth-zoom backdrop
    /// while excluding the explosion.
    /// </summary>
    private const int MaxFallbackBandDistance = 2;

    /// <summary>
    /// Whether speculative <b>prediction / pre-warm</b> (Phase&#160;3) is enabled.
    /// When on, each frame also rasterises a velocity-aimed warm set so
    /// newly-exposed perimeter tiles are cached before a pan/zoom reveals them.
    /// Read once from <c>S100_VECTOR_TILE_PREDICT</c> (default on; <c>0</c> or
    /// <c>false</c> disables it, leaving the Phase&#160;2 visible-only behaviour —
    /// a first-class A/B knob, design §4).
    /// </summary>
    public static bool PredictionEnabled { get; } = ReadBool("S100_VECTOR_TILE_PREDICT", true);

    /// <summary>
    /// Whether the persistent <b>disk tile cache</b> (Phase&#160;4) is enabled.
    /// When on, a tile missing from the in-memory cache is looked up on disk
    /// before being re-rasterised, and freshly rasterised tiles are persisted —
    /// so a palette flip-back or a process restart re-uses warm tiles. Read once
    /// from <c>S100_VECTOR_TILE_DISK</c> (default on; <c>0</c>/<c>false</c>
    /// disables). The on-disk key folds a <c>styleStateHash</c> so a tile is
    /// never served for a different mariner/palette state (design §3.4).
    /// </summary>
    public static bool DiskCacheEnabled { get; } = ReadBool("S100_VECTOR_TILE_DISK", true);

    /// <summary>
    /// Whether <b>GPU texture residency</b> (Phase&#160;5) is enabled. When on,
    /// and the live compositor surface is GPU-backed
    /// (<see cref="SKCanvas.Context"/> resolves to a <see cref="GRContext"/>),
    /// each warm raster tile is uploaded <i>once</i> to a GPU-resident texture
    /// (<see cref="SKImage.ToTextureImage(GRContext)"/>) and reused on every
    /// subsequent frame, instead of re-uploading the same pixels each paint —
    /// the dominant per-frame cost identified in Appendix&#160;F. Read once from
    /// <c>S100_VECTOR_TILE_GPU</c> (default on; <c>0</c>/<c>false</c> disables, a
    /// first-class A/B knob). On a software/CPU surface this is inert and the
    /// renderer blits the raster tile directly (universal fallback, no regression).
    /// </summary>
    public static bool GpuResidencyEnabled { get; } = ReadBool("S100_VECTOR_TILE_GPU", true);

    /// <summary>
    /// Per-layer GPU-texture residency budget, in native bytes. The resident
    /// GPU texture set is bounded independently of the CPU hot cache (it holds
    /// only tiles actually blitted, so it tracks the viewport working set). Read
    /// once from <c>S100_VECTOR_TILE_GPU_MB</c> (default 256&#160;MB). Must
    /// comfortably exceed the visible + fallback working set or promotion
    /// thrashes (re-upload each frame); the default holds ~100 guttered tiles.
    /// </summary>
    public static long GpuBudgetBytes { get; } =
        (long)ReadDouble("S100_VECTOR_TILE_GPU_MB", 256.0, 4.0, 4096.0) * 1024 * 1024;

    /// <summary>
    /// When set (<c>S100_VECTOR_TILE_DIAG=1</c>), the compositor logs a
    /// rate-limited (~1&#160;Hz) per-frame summary to <see cref="Console.Error"/>:
    /// the target band, the resolution, how many target-band visible tiles are
    /// present vs missing, the set of fallback bands actually drawn, and the
    /// cache/GPU residency counts. Used to diagnose multi-scale ghosting (target
    /// band incomplete → fallback bands bleed through) and zoom-out blanking
    /// (no band within fallback distance). Diagnostic only.
    /// </summary>
    internal static bool DiagEnabled { get; } = ReadBool("S100_VECTOR_TILE_DIAG", false);

    private static long s_diagLastTick;

    private static long s_diagBailTick;

    /// <summary>
    /// The on-screen rotation, in degrees, to apply to the north-up tile
    /// composite so it aligns with Mapsui's rotated base map. Derived from
    /// Mapsui's own <c>WorldToScreenXY</c> projection (which applies the viewport
    /// rotation internally) rather than hardcoding a sign convention: the screen
    /// direction of world-north is measured and compared against north-up's
    /// straight-up (-90&#176;). Returns 0 for an unrotated viewport.
    /// </summary>
    private static double ScreenRotationDegrees(Viewport viewport)
    {
        if (viewport.Rotation == 0)
        {
            return 0;
        }

        var (cx, cy) = viewport.WorldToScreenXY(viewport.CenterX, viewport.CenterY);
        var (nx, ny) = viewport.WorldToScreenXY(viewport.CenterX, viewport.CenterY + viewport.Resolution);
        var dx = nx - cx;
        var dy = ny - cy;
        if (dx == 0 && dy == 0)
        {
            return 0;
        }

        // North-up draws world-north straight up (screen angle -90&#176;, +Y down).
        // Rotate the canvas so straight-up lands on Mapsui's projected north.
        var northAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        return northAngle + 90.0;
    }

    private static void DiagBail(string reason)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref s_diagBailTick);
        if (last != 0 && now - last < 1000)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref s_diagBailTick, now, last) != last)
        {
            return;
        }

        Console.Error.WriteLine($"[S100.DIAG] Render bailed (layer draws nothing): {reason}");
    }

    /// <summary>
    /// Invoked (on a worker thread) when a tile publishes, so the host can
    /// request a single repaint. The viewer marshals a <c>RefreshGraphics()</c>
    /// onto the UI thread.
    /// </summary>
    public static Action? RequestRedraw { get; set; }

    /// <summary>
    /// Process-wide graceful-shutdown gate for the background tile-rasterisation
    /// workers. Tile workers call into native Skia; if the process begins
    /// tearing down — the managed runtime running the C++ <c>__cxa_finalize</c>
    /// destructors of <c>libSkiaSharp</c> — while a worker is mid-rasterise, the
    /// worker dereferences freed Skia globals and the process dies with a native
    /// SIGSEGV (observed on <c>--exit-after-screenshot</c>). The host MUST call
    /// <see cref="ShutdownAndDrain"/> before letting the process exit. See
    /// <see cref="WorkerDrainGate"/>.
    /// </summary>
    private static readonly WorkerDrainGate s_drainGate = new();

    /// <summary>
    /// Signals every tile worker to stop and blocks until in-flight tile
    /// rasterisation has finished, so the process can safely tear down native
    /// Skia without a worker dereferencing freed Skia globals. Idempotent and
    /// permanent: once called, no further tiles are rasterised for the lifetime
    /// of the process. Call this on the host's shutdown path (before
    /// <c>desktop.Shutdown()</c> / process exit).
    /// </summary>
    /// <param name="timeout">
    /// Maximum time to wait for in-flight workers to drain. A single tile
    /// rasterise is short; a few seconds is ample. If the timeout elapses with
    /// workers still running, returns <see langword="false"/> (the caller may
    /// still proceed, accepting the small teardown-race risk).
    /// </param>
    /// <returns>
    /// <see langword="true"/> if all workers drained within the timeout;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public static bool ShutdownAndDrain(TimeSpan timeout) =>
        s_drainGate.DrainAndWait(timeout);

    private static readonly ConditionalWeakTable<ILayer, TileState> s_states = new();

    /// <summary>
    /// Process-wide registry of every live GPU-texture residency cache, keyed
    /// weakly by its owning <see cref="ILayer"/> (Phase&#160;5). This holds a
    /// <em>strong</em> reference to each <see cref="TileCache"/> of GPU-backed
    /// <see cref="SKImage"/>s so the GC's finalizer thread can never reclaim
    /// them — GPU resources must be freed on the thread that owns the
    /// <see cref="GRContext"/>, and finalizing them off-thread crashes the
    /// native Skia GPU backend. When a layer is torn down (dataset closed,
    /// palette re-portrayal swapping in a fresh layer, or a silent GC of an
    /// abandoned layer) its weak entry goes dead; <see cref="ReconcileGpuCaches"/>
    /// then disposes the orphaned cache on the render thread under the live
    /// context. See <c>docs/design/S100-Render-Subsystem-Design.md</c> Appendix&#160;F.
    /// </summary>
    private static readonly List<(WeakReference<ILayer> Layer, TileCache Cache, GRContext Context)> s_gpuRegistry = new();

    private static readonly object s_gpuRegistrySync = new();

    private static readonly SKSamplingOptions s_sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    // Reusable display-list renderer for the live symbol/text overlay. Render is
    // single-threaded (Mapsui's render thread), so one shared, stateless
    // instance is safe. Transparent background + scale-visibility so SCAMIN is
    // honoured against the live viewport's denominator.
    private static readonly SkiaDisplayListRenderer s_overlayRenderer = new()
    {
        Background = SceneRgbaColor.Transparent,
        HonorScaleVisibility = true,
    };

    private static readonly Lazy<TileDiskCache?> s_diskCache = new(CreateSharedDiskCache);

    /// <summary>
    /// The process-wide warm disk cache, or <see langword="null"/> when disabled
    /// or its root directory could not be established. Shared across every layer
    /// and session.
    /// </summary>
    private static TileDiskCache? SharedDiskCache => s_diskCache.Value;

    private static TileDiskCache? CreateSharedDiskCache()
    {
        if (!DiskCacheEnabled)
        {
            return null;
        }

        try
        {
            var root = Environment.GetEnvironmentVariable("S100_VECTOR_TILE_DISK_DIR");
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(Path.GetTempPath(), "encdotnet-s100", "tiles");
            }

            var budgetMb = ReadDouble("S100_VECTOR_TILE_DISK_MB", 512.0, 16.0, 8192.0);
            return new TileDiskCache(root, (long)budgetMb * 1024 * 1024);
        }
        catch
        {
            // A disk cache is best-effort; failing to create one must not break
            // rendering.
            return null;
        }
    }

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

    private static bool ReadBool(string name, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }

        return !(raw.Equals("0", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("false", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("off", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("no", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Registers this renderer under <see cref="RendererName"/>. Idempotent.</summary>
    public static void Register()
    {
        MapRenderer.RegisterLayerRenderer(RendererName, Render);
    }

    /// <summary>
    /// Binds the fully-resolved <see cref="VectorScene"/> for a layer and
    /// invalidates its tile cache (a new generation), so the next frame
    /// re-rasterises from the new scene. Equivalent to calling
    /// <see cref="BindScene(ILayer, VectorScene, string?, string?)"/> without a
    /// disk-cache key (the warm disk cache is bypassed for this layer).
    /// </summary>
    public static void BindScene(ILayer layer, VectorScene scene) =>
        BindScene(layer, scene, productLayerSet: null, styleStateHash: null);

    /// <summary>
    /// Binds the fully-resolved <see cref="VectorScene"/> for a layer and
    /// invalidates its tile cache (a new generation), so the next frame
    /// re-rasterises from the new scene.
    /// </summary>
    /// <param name="layer">The Mapsui layer this scene portrays.</param>
    /// <param name="scene">The resolved paint operations to rasterise into tiles.</param>
    /// <param name="productLayerSet">
    /// Stable identity of the dataset/cell + product, used (with
    /// <paramref name="styleStateHash"/>) as the persistent disk-cache namespace
    /// so warm tiles are reused across layer rebuilds and sessions. When either
    /// this or <paramref name="styleStateHash"/> is null/empty, the warm disk
    /// cache is bypassed for this layer.
    /// </param>
    /// <param name="styleStateHash">
    /// A hash that fully captures the mariner/palette style state (palette,
    /// display category, safety settings, symbol/text scale, …) so a tile is
    /// never served from disk for a different style state (design §3.4).
    /// </param>
    public static void BindScene(ILayer layer, VectorScene scene, string? productLayerSet, string? styleStateHash)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(scene);

        var diskNamespace =
            DiskCacheEnabled
            && !string.IsNullOrEmpty(productLayerSet)
            && !string.IsNullOrEmpty(styleStateHash)
                ? TileDiskCache.NamespaceFor(productLayerSet, styleStateHash)
                : null;

        var state = s_states.GetValue(layer, static _ => new TileState());
        lock (state.Sync)
        {
            var (baseScene, overlayScene) = PartitionScene(scene);
            state.Scene = baseScene;
            state.OverlayScene = overlayScene;
            state.Generation++;
            state.DiskNamespace = diskNamespace;
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
    /// Splits a bound <see cref="VectorScene"/> into the tiled <i>base</i> plane
    /// (area fills, pattern fills, and lines) and the live <i>overlay</i> plane
    /// (point symbols and point-anchored text such as soundings), preserving the
    /// original S-100 Part 9 draw order within each.
    /// </summary>
    /// <remarks>
    /// Point symbols and soundings must render at a constant on-screen size
    /// regardless of zoom. The base plane is rasterised at a discrete quad-tree
    /// band resolution and then composited scaled by
    /// <c>ResolutionForBand(band) / resolution</c> (see <see cref="TileGrid"/>),
    /// so anything baked into a tile scales with the band fit (and, transiently,
    /// with a coarser fallback band) — symbols would visibly grow and shrink
    /// through a zoom. Drawing them in a live overlay against the real viewport
    /// keeps them scale-stable. This revises the original design's
    /// "position-stable point symbols → tiled with the base" call: position
    /// stability (no decluttering) is not the same as scale stability.
    /// </remarks>
    internal static (VectorScene Base, VectorScene Overlay) PartitionScene(VectorScene scene)
    {
        var baseOps = new List<PaintOp>(scene.Ops.Count);
        var overlayOps = new List<PaintOp>();
        foreach (var op in scene.Ops)
        {
            if (op is PointPaintOp or TextPaintOp)
                overlayOps.Add(op);
            else
                baseOps.Add(op);
        }

        return (new VectorScene(baseOps), new VectorScene(overlayOps));
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
        if (resolution <= 0)
        {
            if (DiagEnabled)
            {
                DiagBail($"resolution={resolution:F3}");
            }

            return;
        }

        var deviceScale = canvas.TotalMatrix.ScaleX;
        if (deviceScale <= 0 || float.IsNaN(deviceScale))
        {
            deviceScale = 1f;
        }

        var state = s_states.GetValue(layer, static _ => new TileState());

        // Resolve the live GPU context from the compositor canvas (null on a
        // software/CPU surface or when residency is disabled). Phase 5: warm
        // tiles are promoted to GPU-resident textures so identical pixels are
        // not re-uploaded every frame (Appendix F).
        var grContext = GpuResidencyEnabled ? (canvas.Context as GRContext) : null;

        var band = TileGrid.BandForResolution(resolution);
        var centerX = viewport.CenterX;
        var centerY = viewport.CenterY;
        var widthDip = viewport.Width;
        var heightDip = viewport.Height;

        // A rotated viewport (e.g. a trackpad pinch that imparts a little spin)
        // is supported by rotating the composite canvas about the screen centre
        // rather than bailing — bailing left the chart permanently blank because
        // the rotation rarely lands back on exactly 0. The on-screen rotation is
        // derived from Mapsui's own projection so it matches its sign/convention
        // without hardcoding it. Tile selection uses the rotated viewport's
        // bounding box so the corners stay covered. See design Appendix F.8.
        var rotationDeg = ScreenRotationDegrees(viewport);
        var (coverWidth, coverHeight) = rotationDeg == 0
            ? (widthDip, heightDip)
            : TileGrid.RotatedCoverSize(widthDip, heightDip, rotationDeg);

        var visible = TileGrid.VisibleTiles(centerX, centerY, coverWidth, coverHeight, resolution, band);

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
            // the worker). Excludes visible / cached / in-flight tiles. Skipped
            // entirely when prediction is disabled (Phase-2 A/B baseline).
            state.PendingPredicted.Clear();
            if (PredictionEnabled)
            {
                var predicted = TileGrid.PredictedTiles(
                    centerX, centerY, coverWidth, coverHeight, resolution, band,
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

            // Phase 5 GPU residency (render thread only): first dispose any
            // GPU-texture caches whose owning layer has been torn down (under
            // the live context), then reconcile this layer's cache with the
            // current context + scene generation before compositing, so stale
            // textures are never blitted.
            //
            // Guard the whole paint block: a throw here would escape the lock and
            // skip the worker-start Task.Run below while state.Rendering stays
            // true, permanently stalling tile production (a blank chart until the
            // layer is rebuilt). A dropped frame is always recoverable; a stalled
            // worker is not.
            try
            {
                if (grContext is not null)
                {
                    ReconcileGpuCaches(grContext);
                }

                ManageGpuResidency(state, grContext, layer);

                Composite(canvas, state, band, centerX, centerY, widthDip, heightDip, coverWidth, coverHeight, resolution, rotationDeg, grContext);

                // Draw point symbols + soundings live, on top of the composited
                // base tiles, at constant on-screen size (the base tiles are
                // band-scaled, so symbols must not be baked into them).
                DrawOverlay(canvas, state, centerX, centerY, widthDip, heightDip, resolution, rotationDeg);
            }
            catch (Exception ex)
            {
                S100Diag.Telemetry.RecordRenderFault(ex);
            }
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
            // Honour a graceful shutdown: TryRegister refuses new Skia work once
            // the process is tearing down. Release the rendering flag we set
            // under the lock so the state is left consistent.
            if (s_drainGate.TryRegister())
            {
                _ = Task.Run(() => Worker(state));
            }
            else
            {
                lock (state.Sync)
                {
                    state.Rendering = false;
                }
            }
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
    /// so the worker cannot evict/dispose an image mid-blit. When the exact
    /// target band does not yet cover the viewport, the single nearest cached
    /// band within <see cref="MaxFallbackBandDistance"/> is drawn first as a
    /// gap-filling backdrop (one scale only, so different-sized symbols never
    /// stack and ghost); once the target band is complete the backdrop is
    /// skipped entirely. Exact target-band tiles are drawn on top, each
    /// hard-clipped to its core. A rotated viewport is handled by rotating the
    /// whole composite about the screen centre (<paramref name="rotationDeg"/>);
    /// tile <i>selection</i> uses the enlarged <paramref name="coverWidth"/> ×
    /// <paramref name="coverHeight"/> so rotated corners stay covered, while the
    /// <i>projection</i> keeps the real <paramref name="widthDip"/> ×
    /// <paramref name="heightDip"/>.
    /// </summary>
    private static void Composite(
        SKCanvas canvas, TileState state, int band,
        double centerX, double centerY, double widthDip, double heightDip,
        double coverWidth, double coverHeight, double resolution, double rotationDeg,
        GRContext? grContext)
    {
        var gpuCache = grContext is not null ? state.GpuTextures : null;

        // Free GPU textures retired by prior frames before recording any draw
        // this frame: SKCanvas.DrawImage is deferred and flushes after Render
        // returns, so a texture must outlive the frame that drew it. Draining
        // here (frame start, pre-draw) frees only already-flushed images.
        gpuCache?.DrainPendingDisposals();

        // Target band visible tiles, and whether the band fully covers the
        // viewport (every visible tile already cached).
        var target = TileGrid.VisibleTiles(centerX, centerY, coverWidth, coverHeight, resolution, band);
        var targetComplete = target.Count > 0;
        foreach (var key in target)
        {
            if (!state.Cache.Contains(key))
            {
                targetComplete = false;
                break;
            }
        }

        // Backdrop: only needed to fill gaps while the target band is still
        // incomplete (during a zoom transition). Draw the SINGLE nearest cached
        // band — never multiple bands at once — so symbols rasterised at
        // different scales cannot stack and ghost. Skipped once the target band
        // is complete (its opaque fills fully occlude any backdrop anyway).
        var fallback = new List<TileKey>();
        if (!targetComplete)
        {
            var nearestDist = int.MaxValue;
            foreach (var key in state.Cache.SnapshotKeys())
            {
                var dist = Math.Abs(key.Band - band);
                if (dist == 0 || dist > MaxFallbackBandDistance)
                {
                    continue;
                }

                var core = TileGrid.TileCoreScreenRect(key, centerX, centerY, widthDip, heightDip, resolution);
                if (!core.IntersectsViewport(coverWidth, coverHeight))
                {
                    continue;
                }

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    fallback.Clear();
                }

                if (dist == nearestDist)
                {
                    fallback.Add(key);
                }
            }
        }

        // Rotate the whole composite about the screen centre so the north-up tile
        // projection aligns with Mapsui's rotated base map (no-op when 0).
        var rotate = rotationDeg != 0;
        if (rotate)
        {
            canvas.Save();
            canvas.RotateDegrees((float)rotationDeg, (float)(widthDip * 0.5), (float)(heightDip * 0.5));
        }

        foreach (var key in fallback)
        {
            BlitTile(canvas, state, key, centerX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
        }

        // Exact target band on top (crisp where present).
        foreach (var key in target)
        {
            BlitTile(canvas, state, key, centerX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
        }

        if (rotate)
        {
            canvas.Restore();
        }

        if (DiagEnabled)
        {
            DiagComposite(state, band, centerX, centerY, coverWidth, coverHeight, resolution, fallback);
        }
    }

    /// <summary>
    /// Diagnostic-only (~1&#160;Hz rate-limited) per-frame composite summary, gated
    /// by <see cref="DiagEnabled"/>. Reports target-band tile completeness, the
    /// fallback bands actually drawn, and cache/GPU residency so multi-scale
    /// ghosting and zoom-out blanking can be root-caused from the log.
    /// </summary>
    private static void DiagComposite(
        TileState state, int band,
        double centerX, double centerY, double widthDip, double heightDip, double resolution,
        List<TileKey> fallback)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref s_diagLastTick);
        if (last != 0 && now - last < 1000)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref s_diagLastTick, now, last) != last)
        {
            return;
        }

        int targetTotal = 0, targetPresent = 0;
        var present = new HashSet<TileKey>(state.Cache.SnapshotKeys());
        var bandHist = new SortedDictionary<int, int>();
        foreach (var key in present)
        {
            bandHist.TryGetValue(key.Band, out var c);
            bandHist[key.Band] = c + 1;
        }

        foreach (var key in TileGrid.VisibleTiles(centerX, centerY, widthDip, heightDip, resolution, band))
        {
            targetTotal++;
            if (present.Contains(key))
            {
                targetPresent++;
            }
        }

        var fallbackBands = new SortedSet<int>();
        foreach (var key in fallback)
        {
            fallbackBands.Add(key.Band);
        }

        var hist = string.Join(",", bandHist.Select(kv => $"b{kv.Key}:{kv.Value}"));
        var fb = fallbackBands.Count == 0 ? "-" : string.Join("+", fallbackBands);
        Console.Error.WriteLine(
            $"[S100.DIAG] band={band} res={resolution:F2} target={targetPresent}/{targetTotal} " +
            $"fallbackBands={fb} cache={state.Cache.Count}tiles/{state.Cache.ResidentBytes / (1024 * 1024)}MB " +
            $"gpu={(state.GpuTextures?.Count.ToString() ?? "-")} bandHist=[{hist}]");
    }

    /// <summary>
    /// Reconciles the per-layer GPU-texture residency cache (Phase&#160;5) with
    /// the live state, on the render thread. GPU-backed <see cref="SKImage"/>s
    /// must be created and disposed on the thread that owns the GPU context, so
    /// every mutation of <see cref="TileState.GpuTextures"/> funnels through here
    /// and <see cref="BlitTile"/> (both render-thread only):
    /// <list type="bullet">
    /// <item>no context (software surface or residency disabled) → drop any
    /// textures we hold and run the raster path;</item>
    /// <item>context changed (first paint, or a device reset handed us a new
    /// <see cref="GRContext"/>) → rebuild the cache, since textures are bound to
    /// the context that created them;</item>
    /// <item>scene/style generation advanced (a <see cref="BindScene"/>
    /// invalidation) → discard the now-stale textures so the re-rasterised
    /// tiles are re-promoted.</item>
    /// </list>
    /// </summary>
    private static void ManageGpuResidency(TileState state, GRContext? grContext, ILayer layer)
    {
        if (grContext is null)
        {
            if (state.GpuTextures is not null)
            {
                UnregisterGpuCache(state.GpuTextures);
                state.GpuTextures.Dispose();
                state.GpuTextures = null;
                state.GpuContext = null;
            }

            return;
        }

        if (!ReferenceEquals(state.GpuContext, grContext))
        {
            if (state.GpuTextures is not null)
            {
                UnregisterGpuCache(state.GpuTextures);
                state.GpuTextures.Dispose();
            }

            state.GpuTextures = new TileCache(GpuBudgetBytes, deferDisposal: true);
            RegisterGpuCache(layer, state.GpuTextures, grContext);
            state.GpuContext = grContext;
            state.GpuGeneration = state.Generation;
        }
        else if (state.GpuGeneration != state.Generation)
        {
            state.GpuTextures!.Clear();
            state.GpuGeneration = state.Generation;
        }
    }

    /// <summary>
    /// Registers a GPU-texture cache in the process-wide registry so it is held
    /// alive against off-thread finalization until its owning layer is collected
    /// (Phase&#160;5). See <see cref="s_gpuRegistry"/>.
    /// </summary>
    private static void RegisterGpuCache(ILayer layer, TileCache cache, GRContext context)
    {
        lock (s_gpuRegistrySync)
        {
            s_gpuRegistry.Add((new WeakReference<ILayer>(layer), cache, context));
        }
    }

    /// <summary>
    /// Removes a GPU-texture cache from the registry once the render thread has
    /// taken ownership of disposing it (a live layer rebuilding or dropping its
    /// cache). See <see cref="s_gpuRegistry"/>.
    /// </summary>
    private static void UnregisterGpuCache(TileCache cache)
    {
        lock (s_gpuRegistrySync)
        {
            for (int i = s_gpuRegistry.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(s_gpuRegistry[i].Cache, cache))
                {
                    s_gpuRegistry.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// Disposes — on the render thread, under the live <see cref="GRContext"/> —
    /// any registered GPU-texture caches whose owning layer has been collected
    /// (Phase&#160;5). This is the teardown path for closed datasets and
    /// re-portrayed (layer-swapped) datasets: the layer is gone so its
    /// <see cref="TileState"/> never renders again, but the registry kept its
    /// GPU images alive so they are freed here on the GPU-owning thread instead
    /// of crashing the native backend on the finalizer thread. Only caches bound
    /// to <paramref name="grContext"/> are touched; a cache from a different
    /// (e.g. lost) context is left for that context's own teardown rather than
    /// freed under the wrong one. See <see cref="s_gpuRegistry"/>.
    /// </summary>
    private static void ReconcileGpuCaches(GRContext grContext)
    {
        lock (s_gpuRegistrySync)
        {
            for (int i = s_gpuRegistry.Count - 1; i >= 0; i--)
            {
                var entry = s_gpuRegistry[i];
                if (entry.Layer.TryGetTarget(out _))
                {
                    continue;
                }

                if (ReferenceEquals(entry.Context, grContext))
                {
                    entry.Cache.Dispose();
                }

                s_gpuRegistry.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Blits one cached tile (if resident): positions its guttered image by
    /// world bounds and hard-clips to the tile core so adjacent tiles meet
    /// exactly with no seam or double-drawn gutter. When a GPU context is
    /// supplied (Phase&#160;5), the tile's raster pixels are uploaded once to a
    /// GPU-resident texture and reused thereafter, so the same pixels are not
    /// re-uploaded every frame; on a software surface (<paramref name="grContext"/>
    /// null) the raster image is drawn directly.
    /// </summary>
    private static void BlitTile(
        SKCanvas canvas, TileState state, TileKey key,
        double centerX, double centerY, double widthDip, double heightDip, double resolution,
        GRContext? grContext, TileCache? gpuCache)
    {
        var image = state.Cache.TryGet(key);
        if (image is null)
        {
            return;
        }

        var toDraw = image;
        if (grContext is not null && gpuCache is not null)
        {
            var gpu = gpuCache.TryGet(key);
            if (gpu is null)
            {
                try
                {
                    gpu = image.ToTextureImage(grContext);
                }
                catch
                {
                    gpu = null;
                }

                if (gpu is not null)
                {
                    gpuCache.Put(key, gpu);
                    S100Diag.Telemetry.TileGpuUploads.Add(1);
                }
            }
            else
            {
                S100Diag.Telemetry.TileGpuHits.Add(1);
            }

            if (gpu is not null)
            {
                toDraw = gpu;
            }
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
        canvas.DrawImage(toDraw, fullRect, s_sampling);
        canvas.Restore();
    }

    private static void Worker(TileState state)
    {
        try
        {
            while (true)
            {
                // Stop before touching Skia once the process is shutting down,
                // so ShutdownAndDrain's wait completes and no tile is rasterised
                // into a half-torn-down Skia. The single finally clears the
                // rendering flag and completes the drain-gate registration.
                if (s_drainGate.IsDraining)
                {
                    return;
                }

                TileKey key;
                float deviceScale;
                long generation;
                VectorScene scene;
                bool isPrediction;
                string? diskNamespace;

                lock (state.Sync)
                {
                    // Visible tiles always drain before speculative ones, so
                    // prediction work yields to anything actually on screen.
                    if (state.Scene is null
                        || (state.PendingVisible.Count == 0 && state.PendingPredicted.Count == 0))
                    {
                        // Drained: leave the loop and let the single finally clear
                        // state.Rendering. Clearing here as well would open a race
                        // where a frame restarts the worker between this point and
                        // the finally, leaving two workers running.
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
                    diskNamespace = state.DiskNamespace;
                    state.InFlight.Add(key);
                }

                // Warm path: a tile rendered under this exact style state in a prior
                // layer/session is decoded from disk instead of re-rasterised. The
                // namespace folds the styleStateHash so this can never be a tile from
                // a different mariner/palette state.
                var disk = SharedDiskCache;
                SKImage? image = null;
                var fromDisk = false;
                if (disk is not null && diskNamespace is not null)
                {
                    image = disk.TryRead(diskNamespace, key);
                    if (image is not null)
                    {
                        fromDisk = true;
                        S100Diag.Telemetry.TileDiskHits.Add(1);
                    }
                }

                if (image is null)
                {
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
                }

                // Persist a freshly-rasterised tile while we still solely own the
                // image (before handing it to the hot cache), so a concurrent
                // eviction can never dispose it mid-encode. Disk-sourced tiles are
                // already persisted. Best-effort: failures are swallowed inside Write.
                if (image is not null && !fromDisk && disk is not null && diskNamespace is not null
                    && generation == state.Generation)
                {
                    disk.Write(diskNamespace, key, image);
                    S100Diag.Telemetry.TileDiskWrites.Add(1);
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

                if (isPrediction && !fromDisk)
                {
                    S100Diag.Telemetry.TilePredictionRasterized.Add(1);
                }

                // Only a newly-published *visible* tile changes what is on
                // screen, so only it warrants a repaint (see ShouldRequestRedraw).
                if (ShouldRequestRedraw(published, isPrediction))
                {
                    RequestRedraw?.Invoke();
                }
            }
        }
        catch (Exception ex)
        {
            // The inner rasterise has its own guard; this catches anything else on
            // the worker (disk I/O, cache, redraw callback). Never let the worker
            // die with state.Rendering stuck true — that would permanently stall
            // tile production (a blank chart until the layer is rebuilt).
            S100Diag.Telemetry.RecordRenderFault(ex);
        }
        finally
        {
            // Always release the rendering flag so the next frame can spin up a
            // fresh worker for any still-pending tiles. Normal drain-exit already
            // cleared it inside the loop; this covers the abnormal-exit path.
            lock (state.Sync)
            {
                state.Rendering = false;
            }

            // Pair the TryRegister at the worker-start site. When the last
            // worker completes, this signals ShutdownAndDrain that Skia is idle.
            s_drainGate.Complete();
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
    /// Decides whether a worker-published tile should request a UI repaint.
    /// Only a newly-published <em>visible</em> tile changes what is on screen, so
    /// only it warrants a redraw. A predicted (off-screen, pre-warm) tile must
    /// <b>not</b> request a redraw: doing so would trigger a frame that re-runs
    /// prediction and re-publishes the next speculative tile, a self-sustaining
    /// repaint loop that never lets the map settle — most visible when frames are
    /// cheap (e.g. GPU residency), where Mapsui does not coalesce the spurious
    /// invalidations. The pre-warmed tile is still resident and is picked up the
    /// moment the viewport actually moves onto it (which itself triggers a frame).
    /// </summary>
    /// <param name="published">Whether the tile was published into the hot cache.</param>
    /// <param name="isPrediction">Whether the tile was produced for the prediction (pre-warm) queue.</param>
    /// <returns><see langword="true"/> only for a published, non-predicted (visible) tile.</returns>
    internal static bool ShouldRequestRedraw(bool published, bool isPrediction) =>
        published && !isPrediction;

    /// <summary>
    /// Draws the live symbol/text overlay (point symbols + soundings) on top of
    /// the composited base tiles, at constant on-screen size against the live
    /// viewport. Unlike the base plane, these ops are <i>not</i> tiled/scaled, so
    /// a buoy or sounding keeps the same pixel size at every zoom. SCAMIN is
    /// applied against the live scale denominator (matching the base tiles' own
    /// per-band culling). A rotated viewport is handled exactly as the tile
    /// composite handles it — rotate the whole overlay about the screen centre so
    /// symbol anchors stay aligned with the rotated base (north-up is the v1 case
    /// and is a no-op here).
    /// </summary>
    private static void DrawOverlay(
        SKCanvas canvas, TileState state,
        double centerX, double centerY, double widthDip, double heightDip,
        double resolution, double rotationDeg)
    {
        var overlay = state.OverlayScene;
        if (overlay is null || overlay.Ops.Count == 0)
        {
            return;
        }

        var rotate = rotationDeg != 0;
        if (rotate)
        {
            canvas.Save();
            canvas.RotateDegrees((float)rotationDeg, (float)(widthDip * 0.5), (float)(heightDip * 0.5));
        }

        try
        {
            // Live full-screen viewport in DIP space: symbol/text sizes are in
            // logical display px, so projecting onto a DIP-sized viewport draws
            // them at their intended on-screen size (the foreground canvas's
            // device-scale matrix then keeps them crisp on HiDPI).
            var halfWorldW = widthDip * resolution * 0.5;
            var halfWorldH = heightDip * resolution * 0.5;
            var (minLon, minLat) = WebMercator.ToLonLat(centerX - halfWorldW, centerY - halfWorldH);
            var (maxLon, maxLat) = WebMercator.ToLonLat(centerX + halfWorldW, centerY + halfWorldH);

            var viewport = new CoreViewport
            {
                MinLatitude = minLat,
                MaxLatitude = maxLat,
                MinLongitude = minLon,
                MaxLongitude = maxLon,
                WidthPixels = Math.Max(1, (int)Math.Round(widthDip)),
                HeightPixels = Math.Max(1, (int)Math.Round(heightDip)),
                ScaleDenominator = S100VectorSceneRenderer.ScaleDenominatorFor(centerX, centerY, resolution),
            };

            s_overlayRenderer.RenderOnto(canvas, overlay, viewport);
        }
        finally
        {
            if (rotate)
            {
                canvas.Restore();
            }
        }
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

        // Live screen-space overlay: point symbols + point-anchored text
        // (soundings) partitioned out of the tiled base plane so they draw at
        // constant on-screen size instead of scaling with the band-resolution
        // tiles. Null until a scene is bound; empty when the scene has no
        // point/text ops.
        public VectorScene? OverlayScene;
        public long Generation;

        // Persistent disk-cache namespace for this layer's current style state
        // (folds productLayerSet + styleStateHash). Null when the disk cache is
        // disabled or no style key was supplied.
        public string? DiskNamespace;

        public readonly TileCache Cache = new(BudgetBytes);
        public readonly HashSet<TileKey> InFlight = new();

        // Phase 5 GPU residency: GPU-resident texture twins of blitted tiles,
        // touched ONLY on the render thread (created/disposed on the GPU-context
        // thread). Null until the first GPU-backed paint; rebuilt when the
        // context changes and cleared when the scene generation advances.
        // Reuses TileCache purely for its LRU + native-byte budgeting; all of
        // its disposes then happen on the render thread, which GPU images require.
        public TileCache? GpuTextures;
        public object? GpuContext;
        public long GpuGeneration = -1;

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
