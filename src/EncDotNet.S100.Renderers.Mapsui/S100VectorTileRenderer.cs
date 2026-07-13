using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
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
/// <b>Workers.</b> A tier-sized pool of coalescing workers per layer drains the
/// visible-miss set (replaced every frame, so tiles panned out of view are
/// dropped before they render) in parallel. The pool size is
/// <see cref="RenderingOptimizations.TileWorkerCount"/> (one on low-end hosts,
/// scaling with cores on high-end). Each tile is rendered with a <b>gutter</b>
/// (bleed beyond the tile bounds) and hard-clipped to its core on composite, so
/// lines and area fills stay continuous across seams. Finished tiles enter the
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
    /// their joins/caps. Sourced from
    /// <see cref="RenderingOptimizations.TileGutterDip"/> (seeded from
    /// <c>S100_VECTOR_TILE_GUTTER</c>, default 64); only newly-rasterised tiles
    /// pick up a live change.
    /// </summary>
    public static double GutterDip => RenderingOptimizations.TileGutterDip;

    /// <summary>
    /// Hot-cache native-byte budget per layer. Sourced from
    /// <see cref="RenderingOptimizations.TileBudgetMb"/> (seeded from
    /// <c>S100_VECTOR_TILE_BUDGET_MB</c>, default 256&#160;MB); captured per layer
    /// when its tile state is created (applies on the next dataset reload).
    /// </summary>
    public static long BudgetBytes => (long)(RenderingOptimizations.TileBudgetMb * 1024 * 1024);

    /// <summary>Hard cap on either tile-image dimension, in device pixels.</summary>
    private const int MaxImageDimension = 4096;

    /// <summary>
    /// Over-render halo, in screen pixels, added around the viewport before the
    /// per-layer extent-cull test (<see cref="LayerExtentCulling.ShouldRender"/>).
    /// A cell whose data extent — grown by this halo — does not reach the
    /// viewport is skipped for the frame. Sized at one tile (<see
    /// cref="TileGrid.TileSizeDip"/>) so a cell whose geometry sits just off the
    /// edge, but whose point symbols / gutter reach in, is never wrongly culled.
    /// </summary>
    private const double CullMarginPx = TileGrid.TileSizeDip;

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
    /// Sourced from <see cref="RenderingOptimizations.TilePredictionEnabled"/>
    /// (seeded from <c>S100_VECTOR_TILE_PREDICT</c>, default on); read every frame
    /// so a change takes effect live.
    /// </summary>
    public static bool PredictionEnabled => RenderingOptimizations.TilePredictionEnabled;

    /// <summary>
    /// Whether idle <b>cross-band pre-warm</b> (issue&#160;#428) is enabled. When
    /// on, and the tiled base plane is otherwise idle for a layer (no cold
    /// visible misses this frame and cache headroom to spare), the renderer
    /// speculatively rasterises the band&#160;±&#160;1 tiles covering the current
    /// viewport at the <em>lowest</em> worker priority (behind visible and
    /// same-band predicted work), so a subsequent zoom-in or zoom-out starts warm
    /// instead of paying full cold-tile latency at the new band. Sourced from
    /// <see cref="RenderingOptimizations.TileCrossBandPrewarmEnabled"/> (seeded
    /// from <c>S100_VECTOR_TILE_XBAND</c>, default on except a no-op on the
    /// LowEnd tier); read every frame so a change takes effect live. Like the
    /// same-band prediction warm set, its tiles never trigger a redraw and are
    /// cancelled (rebuilt) every frame.
    /// </summary>
    public static bool CrossBandPrewarmEnabled => RenderingOptimizations.TileCrossBandPrewarmEnabled;

    /// <summary>
    /// The maximum number of adjacent-band tiles enqueued per frame for idle
    /// cross-band pre-warm (issue&#160;#428). Bounds the speculative warm budget
    /// so pre-warm cannot churn the hot cache: the band&#160;+&#160;1 footprint
    /// alone is ~4× the visible tile count, so an uncapped warm set could evict a
    /// large share of the working set. The centre-first ordering
    /// (<see cref="TileGrid.CrossBandPrewarmTiles"/>) keeps the most-central tiles
    /// under this cap.
    /// </summary>
    private const int CrossBandPrewarmMaxTiles = 24;

    /// <summary>
    /// The fraction of the hot-cache byte budget below which idle cross-band
    /// pre-warm may run (issue&#160;#428). When the resident set already exceeds
    /// this, pre-warm is skipped for the frame so its speculative inserts never
    /// force eviction of the current working set — the "LRU/hot-cache aware"
    /// bound the feature calls for. Visible target-band tiles are additionally
    /// pinned via <see cref="TileCache.Protect"/>, so they are never evicted
    /// regardless; this guard protects the same-band predicted and fallback tiles.
    /// </summary>
    private const double CrossBandPrewarmHeadroomFraction = 0.75;

    /// <summary>
    /// Whether the persistent <b>disk tile cache</b> (Phase&#160;4) is enabled.
    /// When on, a tile missing from the in-memory cache is looked up on disk
    /// before being re-rasterised, and freshly rasterised tiles are persisted —
    /// so a palette flip-back or a process restart re-uses warm tiles. Sourced
    /// from <see cref="RenderingOptimizations.TileDiskCacheEnabled"/> (seeded from
    /// <c>S100_VECTOR_TILE_DISK</c>, default on); the shared cache is created once
    /// per process, so a change applies on restart. The on-disk key folds a
    /// <c>styleStateHash</c> so a tile is never served for a different
    /// mariner/palette state (design §3.4).
    /// </summary>
    public static bool DiskCacheEnabled => RenderingOptimizations.TileDiskCacheEnabled;

    /// <summary>
    /// Whether <b>GPU texture residency</b> (Phase&#160;5) is enabled. When on,
    /// and the live compositor surface is GPU-backed
    /// (<see cref="SKCanvas.Context"/> resolves to a <see cref="GRContext"/>),
    /// each warm raster tile is uploaded <i>once</i> to a GPU-resident texture
    /// (<see cref="SKImage.ToTextureImage(GRContext)"/>) and reused on every
    /// subsequent frame, instead of re-uploading the same pixels each paint —
    /// the dominant per-frame cost identified in Appendix&#160;F. Sourced from
    /// <see cref="RenderingOptimizations.TileGpuResidencyEnabled"/> (seeded from
    /// <c>S100_VECTOR_TILE_GPU</c>, default on); read every frame so a change
    /// takes effect live. On a software/CPU surface this is inert and the
    /// renderer blits the raster tile directly (universal fallback, no regression).
    /// </summary>
    public static bool GpuResidencyEnabled => RenderingOptimizations.TileGpuResidencyEnabled;

    /// <summary>
    /// Per-layer GPU-texture residency budget, in native bytes. The resident
    /// GPU texture set is bounded independently of the CPU hot cache (it holds
    /// only tiles actually blitted, so it tracks the viewport working set).
    /// Sourced from <see cref="RenderingOptimizations.TileGpuBudgetMb"/> (seeded
    /// from <c>S100_VECTOR_TILE_GPU_MB</c>, default 256&#160;MB); sized when the
    /// resident-texture cache is first created (applies on the next dataset
    /// reload). Must comfortably exceed the visible + fallback working set or
    /// promotion thrashes (re-upload each frame); the default holds ~100 guttered
    /// tiles.
    /// </summary>
    public static long GpuBudgetBytes => (long)(RenderingOptimizations.TileGpuBudgetMb * 1024 * 1024);

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
    /// Process-wide cap on the total number of concurrent tile workers across
    /// <em>all</em> layers. A single dense layer gets the full per-layer pool
    /// (<see cref="RenderingOptimizations.TileWorkerCount"/>) to parallelise its
    /// cold-miss burst, but N layers × N workers must not exceed the core count or
    /// they oversubscribe the CPU/GPU and starve the UI paint thread (a paint-p95
    /// blow-up on big multi-cell exchange sets). Read once at start-up.
    /// </summary>
    private static readonly int s_maxTotalWorkers =
        Math.Max(RenderingOptimizations.TileWorkerCount, Environment.ProcessorCount);

    private static int s_activeWorkerTotal;

    /// <summary>
    /// Sliding window, in seconds, over which a layer that had visible cold work
    /// is still counted as an active competitor for the fairness reservation used
    /// by elastic borrowing (issue #432). A layer that stops painting (culled,
    /// resolution-hidden, torn down) or that fully caches its visible tiles ages
    /// out of the <see cref="s_visibleLayerStamps"/> registry within this window,
    /// so the "active visible layers" divisor never inflates with dead layers.
    /// A couple of frames' worth is enough to bridge the serialized per-layer
    /// paint of one frame without over-holding.
    /// </summary>
    private const double ElasticFairnessWindowSeconds = 0.5;

    /// <summary>
    /// Guards <see cref="s_visibleLayerStamps"/> and <see cref="s_visibleLayerPruneScratch"/>.
    /// Only ever taken on the render/UI thread (layer paint is serialized there),
    /// so it is effectively uncontended; tile workers never touch it. Always
    /// acquired <em>after</em> a layer's <c>state.Sync</c> and never the reverse,
    /// and workers never take it, so it introduces no lock-order cycle.
    /// </summary>
    private static readonly object s_visibleLayerSync = new();

    /// <summary>
    /// Registry of layers that recently had visible cold work, keyed by their
    /// <see cref="TileState"/>. Each entry records the <see cref="Stopwatch"/> tick
    /// of the layer's last such paint and the worker count it held at that point,
    /// so elastic borrowing can reserve each <em>other</em> active-visible layer
    /// only its <em>shortfall</em> to the <see cref="RenderingOptimizations.TileWorkerCount"/>
    /// floor before lending idle capacity (issue #432 fairness floor). Counting a
    /// competitor's own workers — rather than all other layers' workers — stops an
    /// unrelated predicted-only layer's workers from wrongly satisfying an
    /// active-visible sibling's reservation.
    /// <para>
    /// Keyed <b>weakly</b> via <see cref="ConditionalWeakTable{TKey,TValue}"/> so it
    /// never roots a <see cref="TileState"/>. <see cref="TileState"/> is otherwise
    /// held only weakly (in <see cref="s_states"/>, keyed by <see cref="ILayer"/>);
    /// a plain <see cref="Dictionary{TKey,TValue}"/> here would keep a removed
    /// layer's tiling state — including its rasterised tile cache and scenes — alive
    /// for the process lifetime if the layer disappeared while still registered and
    /// no later paint pruned it. With a weak key the GC reclaims a dead layer's
    /// entry even when no further paint occurs.
    /// </para>
    /// </summary>
    private static readonly ConditionalWeakTable<TileState, ActiveVisibleEntry> s_visibleLayerStamps = new();

    /// <summary>Reusable scratch for time-pruning <see cref="s_visibleLayerStamps"/> without per-call allocation.</summary>
    private static readonly List<TileState> s_visibleLayerPruneScratch = new();

    /// <summary>
    /// A layer's active-visible registry entry: the <see cref="Stopwatch"/> tick of
    /// its last paint with visible cold work, and the workers it held then. A mutable
    /// reference type because <see cref="ConditionalWeakTable{TKey,TValue}"/> requires
    /// a reference value; it is only ever read/written under <see cref="s_visibleLayerSync"/>.
    /// </summary>
    private sealed class ActiveVisibleEntry(long stampTicks, int activeWorkers)
    {
        public long StampTicks = stampTicks;
        public int ActiveWorkers = activeWorkers;
    }


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
    /// weakly by its owning <see cref="ILayer"/> (Phase&#160;5). Each entry holds
    /// a <em>strong</em> reference to that layer's <see cref="TileCache"/> of
    /// GPU-backed <see cref="SKImage"/> tiles <em>and</em> to its current
    /// GPU-backed rotation composite (<see cref="GpuRegistryEntry.RotationSurface"/>
    /// / <see cref="GpuRegistryEntry.RotationImage"/>), so the GC's finalizer
    /// thread can never reclaim any of them. GPU resources must be freed on the
    /// thread that owns the <see cref="GRContext"/>, and finalizing them
    /// off-thread crashes the native Skia GPU backend (observed as a
    /// finalizer/render-thread race when switching the render subsystem from "B"
    /// tiled to "A" Mapsui, which re-portrays and swaps in fresh layers, leaving
    /// the old layer's GPU rotation surface to be finalized off-thread). Only GPU
    /// objects are pinned here — the layer's far larger CPU tile cache stays on
    /// the weakly-held <see cref="TileState"/> so it remains GC-collectible. When
    /// a layer is torn down (dataset closed, palette re-portrayal or subsystem
    /// switch swapping in a fresh layer, or a silent GC of an abandoned layer)
    /// its weak entry goes dead; <see cref="ReconcileGpuCaches"/> then disposes
    /// the orphaned cache and rotation composite on the render thread under the
    /// live context. See <c>docs/design/S100-Render-Subsystem-Design.md</c>
    /// Appendix&#160;F.
    /// </summary>
    private static readonly List<GpuRegistryEntry> s_gpuRegistry = new();

    /// <summary>
    /// A single layer's pinned GPU residency: its texture cache and — mirrored
    /// from the owning <see cref="TileState"/> in lockstep — its current rotated
    /// off-screen composite. The mirror exists purely to keep those GPU objects
    /// strongly reachable (off the weakly-held <see cref="TileState"/>) so they
    /// survive to be disposed on the render thread rather than the finalizer
    /// thread. See <see cref="s_gpuRegistry"/>.
    /// </summary>
    internal sealed class GpuRegistryEntry
    {
        public required WeakReference<ILayer> Layer;
        public required TileCache Cache;

        // The owning GRContext (typed as object so the teardown lifecycle can be
        // unit-tested with a sentinel context — a real GRContext needs a GPU and
        // is unavailable headlessly). Compared only by reference identity.
        public required object Context;

        // Strong mirror of TileState.RotationSurface/RotationImage for GPU-backed
        // rotated frames, kept in lockstep with the TileState (set and cleared
        // together in Composite). Null on north-up frames.
        public SKSurface? RotationSurface;
        public SKImage? RotationImage;
    }


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
        // The overlay draws the live symbol/text layer over the base tiles,
        // which carry already-continuous EPSG:3857 geometry (antimeridian data
        // keeps longitudes beyond ±180° without wrapping). The seam-wrap is a
        // headless single-viewport auto-fit concern; enabling it here would
        // left-edge-wrap the overlay off the fixed tile positions, so the
        // symbols/labels would slide away from their features. Keep it off so
        // the overlay stays locked to the base's continuous frame.
        EnableSeamWrap = false,
    };

    // Stateless, render-thread-only label declutter for the live overlay. S-100
    // Part 9 makes overlap avoidance the portrayal engine's job; this resolves it
    // deterministically each frame from the ops' drawing priority / SCAMIN.
    private static readonly LabelDeclutterer s_labelDeclutterer = new();

    private static readonly Lazy<TileDiskCache?> s_diskCache = new(CreateSharedDiskCache);

    /// <summary>
    /// The process-wide warm disk cache, or <see langword="null"/> when disabled
    /// or its root directory could not be established. Shared across every layer
    /// and session.
    /// </summary>
    private static TileDiskCache? SharedDiskCache => s_diskCache.Value;

    /// <summary>
    /// The effective warm-tile disk-cache root directory for this process:
    /// the <see cref="RenderingOptimizations.TileDiskDirectory"/> override
    /// (env var or host-assigned), or an OS-temp subdirectory when unset.
    /// Exposed so the host can locate the cache for a "clear caches" sweep.
    /// </summary>
    public static string ResolveTileDiskDirectory() =>
        string.IsNullOrEmpty(RenderingOptimizations.TileDiskDirectory)
            ? Path.Combine(Path.GetTempPath(), "encdotnet-s100", "tiles")
            : RenderingOptimizations.TileDiskDirectory!;

    private static TileDiskCache? CreateSharedDiskCache()
    {
        if (!DiskCacheEnabled)
        {
            return null;
        }

        try
        {
            var root = ResolveTileDiskDirectory();
            var budgetMb = RenderingOptimizations.TileDiskMb;
            return new TileDiskCache(root, (long)budgetMb * 1024 * 1024);
        }
        catch
        {
            // A disk cache is best-effort; failing to create one must not break
            // rendering.
            return null;
        }
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
            state.BaseIndex = new BaseSpatialIndex(baseScene);
            state.OverlayScene = overlayScene;
            state.OverlayIndex = new OverlaySpatialIndex(overlayScene);
            state.OverlayCandidates.Clear();
            state.OverlayScopedScene = new VectorScene(state.OverlayCandidates);
            state.Generation++;
            state.DiskNamespace = diskNamespace;
            state.Cache.Clear();
            state.InFlight.Clear();
            state.PendingVisible.Clear();
            state.PendingPredicted.Clear();
            state.PendingCrossBand.Clear();
            state.PredictedInCache.Clear();
            state.VisibleEnqueueTicks.Clear();
            // A new scene is a teleport for prediction: drop the stale velocity
            // anchor so the first frame doesn't fling the fan in a random
            // direction.
            state.HasLastCenter = false;
            state.VelocityX = 0;
            state.VelocityY = 0;
        }
    }

    /// <summary>
    /// Attempts to retrieve the partitioned <i>base</i> and <i>overlay</i>
    /// <see cref="VectorScene"/>s most recently bound to <paramref name="layer"/>
    /// via <see cref="BindScene(ILayer, VectorScene, string?, string?)"/>.
    /// </summary>
    /// <remarks>
    /// A fidelity-verification / diagnostics accessor (no frame is rendered): it
    /// exposes the exact resolved paint operations the tiled subsystem will
    /// rasterise into the base plane and composite into the live overlay plane.
    /// It backs the issue #347 multi-product parity guard, which inspects the
    /// per-product overlay (point symbols + labels) at the op level rather than
    /// through the coarse perceptual pixel gate.
    /// </remarks>
    /// <param name="layer">The layer a scene was bound to.</param>
    /// <param name="baseScene">The tiled base plane (area/pattern fills and lines).</param>
    /// <param name="overlayScene">The live overlay plane (point symbols and point-anchored text).</param>
    /// <returns><see langword="true"/> when a scene is bound; otherwise <see langword="false"/>.</returns>
    public static bool TryGetPartitionedScene(
        ILayer layer, out VectorScene baseScene, out VectorScene overlayScene)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (s_states.TryGetValue(layer, out var state))
        {
            lock (state.Sync)
            {
                if (state.Scene is not null && state.OverlayScene is not null)
                {
                    baseScene = state.Scene;
                    overlayScene = state.OverlayScene;
                    return true;
                }
            }
        }

        baseScene = null!;
        overlayScene = null!;
        return false;
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

        // Skip a cell whose data extent lies entirely outside the viewport.
        // Mapsui invokes this custom renderer for every enabled, in-resolution
        // layer each frame without extent-culling (VisibleFeatureIterator only
        // filters Enabled/Min/MaxVisible), so an exchange set of many S-101 cells
        // would otherwise run the full per-frame path — empty-tile scheduling,
        // GPU residency, composite, live overlay, and a redraw on every off-view
        // worker publish — once per off-view cell. Culling here makes off-view
        // cells cost nothing. The CullMarginPx halo keeps edge cells (whose
        // symbols/over-render reach into the view) rendering.
        if (!LayerExtentCulling.ShouldRender(layer, viewport, resolution, CullMarginPx))
        {
            return;
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

        var workersToStart = 0;
        var coldExposure = 0;
        var visibleQueueDepth = 0;
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
            var frameTicks = Stopwatch.GetTimestamp();
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
                    // Stamp the first frame this tile was seen cold-visible so the
                    // worker can report end-to-end cold latency (queue wait +
                    // rasterise) on publish, not just raster CPU cost.
                    if (!state.VisibleEnqueueTicks.ContainsKey(key))
                    {
                        state.VisibleEnqueueTicks[key] = frameTicks;
                    }

                    if (!state.InFlight.Contains(key))
                    {
                        state.PendingVisible.Add(key);
                    }
                }
            }

            visibleQueueDepth = state.PendingVisible.Count;

            // Refresh this layer's active-visible-layer registry entry and read the
            // worker reservation owed to OTHER layers that currently have visible
            // cold work, so the worker-start block below lends only leftover global
            // capacity to this layer (issue #432 fairness floor). A layer counts as
            // active-visible whenever it has visible cold tiles (pending OR already
            // in flight), so a layer mid-raster of its visible burst still keeps its
            // reservation; the reservation is each sibling's shortfall to its floor,
            // so siblings already running their share owe nothing.
            var reservedForOtherLayers = RefreshActiveVisibleLayers(state, coldExposure > 0, state.ActiveWorkers, frameTicks);

            // Drop enqueue stamps for tiles no longer visible (panned away before
            // they landed) so the dictionary stays bounded by the visible set.
            if (state.VisibleEnqueueTicks.Count > visibleSet.Count)
            {
                state.EnqueuePruneScratch.Clear();
                foreach (var k in state.VisibleEnqueueTicks.Keys)
                {
                    if (!visibleSet.Contains(k))
                    {
                        state.EnqueuePruneScratch.Add(k);
                    }
                }

                foreach (var k in state.EnqueuePruneScratch)
                {
                    state.VisibleEnqueueTicks.Remove(k);
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

            // Enqueue the idle cross-band (±1) pre-warm set at the lowest priority
            // (issue #428). Only when the layer is otherwise idle — no cold
            // visible misses this frame — so pre-warm never competes with an
            // on-screen fill; and only with cache headroom to spare so its
            // speculative inserts cannot evict the current working set. The warm
            // set covers the whole viewport footprint of band ± 1 (centre-first,
            // capped), so a subsequent zoom starts warm. Drained strictly behind
            // PendingVisible and PendingPredicted in the worker, so even though it
            // is enqueued in the same frame as the same-band predicted set it only
            // rasterises once that has drained. Excludes cached / in-flight tiles.
            //
            // The idle gate keys on coldExposure (every cold visible tile this
            // frame, pending OR already in flight), not PendingVisible.Count:
            // PendingVisible excludes cold tiles already handed to a worker, so a
            // PendingVisible.Count == 0 test would let pre-warm start while the
            // viewport still had cold holes mid-raster and steal a worker from
            // finishing the on-screen fill — breaking the guarantee above.
            state.PendingCrossBand.Clear();
            if (CrossBandPrewarmEnabled
                && coldExposure == 0
                && state.Cache.ResidentBytes < (long)(state.Cache.BudgetBytes * CrossBandPrewarmHeadroomFraction))
            {
                var crossBand = TileGrid.CrossBandPrewarmTiles(
                    centerX, centerY, coverWidth, coverHeight, resolution, band,
                    CrossBandPrewarmMaxTiles);
                foreach (var key in crossBand)
                {
                    // Also exclude keys already queued in a higher tier this frame:
                    // the band ± 1 centre tiles overlap TileGrid.PredictedTiles, so
                    // without this guard the same key would sit in both PendingPredicted
                    // and PendingCrossBand and be rasterised twice (predicted tier first,
                    // then cross-band), double-counting TilePredictionRasterized and
                    // wasting CPU / disk writes (issue #428 review follow-up).
                    if (!state.Cache.Contains(key)
                        && !state.InFlight.Contains(key)
                        && !state.PendingVisible.Contains(key)
                        && !state.PendingPredicted.Contains(key))
                    {
                        state.PendingCrossBand.Add(key);
                    }
                }
            }

            if (state.PendingVisible.Count > 0 || state.PendingPredicted.Count > 0
                || state.PendingCrossBand.Count > 0)
            {
                state.PendingDeviceScale = deviceScale;
                state.PendingGeneration = state.Generation;
                state.PendingCenterX = centerX;
                state.PendingCenterY = centerY;
                // Spin up workers to cover the pending tiles. The per-layer
                // TileWorkerCount is a *floor* (reservation), not a hard ceiling:
                // a layer with a visible backlog may borrow idle global capacity
                // toward s_maxTotalWorkers (issue #432), but only for *visible*
                // work — predicted/speculative tiles (including the idle cross-band
                // ±1 pre-warm, issue #428) never justify borrowing, so a busy
                // layer's prewarm can't occupy cores a sibling wants for on-screen
                // tiles. On a LowEnd (single-worker) host there is nothing to
                // borrow, so the elastic ceiling collapses to the floor and this
                // reduces to the pre-elastic behaviour. A per-layer floor is
                // reserved for every other active-visible layer before any elastic
                // extra is granted, so a dense bottom-of-z-order layer can't starve
                // siblings that paint later in the frame.
                var baseline = RenderingOptimizations.TileWorkerCount;
                var elasticCeiling = RenderingOptimizations.ResolvedProfile == PerformanceProfile.LowEnd
                    ? baseline
                    : s_maxTotalWorkers;

                workersToStart = ComputeWorkersToStart(
                    baseline,
                    elasticCeiling,
                    s_maxTotalWorkers,
                    Volatile.Read(ref s_activeWorkerTotal),
                    state.ActiveWorkers,
                    state.PendingVisible.Count,
                    state.PendingPredicted.Count + state.PendingCrossBand.Count,
                    reservedForOtherLayers);

                if (workersToStart > 0)
                {
                    state.ActiveWorkers += workersToStart;
                    Interlocked.Add(ref s_activeWorkerTotal, workersToStart);

                    // Publish the post-grant worker count so a sibling painting later
                    // in this same frame sees this layer's true share and reserves
                    // only its own shortfall against it.
                    if (coldExposure > 0)
                    {
                        RecordActiveVisibleLayerWorkers(state, state.ActiveWorkers, frameTicks);
                    }
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
            // skip the worker-start Task.Run below while ActiveWorkers stays
            // elevated, permanently stalling tile production (a blank chart until
            // the layer is rebuilt). A dropped frame is always recoverable; a
            // stalled worker is not.
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
                DrawOverlay(canvas, state, centerX, centerY, widthDip, heightDip, resolution, rotationDeg, deviceScale);
            }
            catch (Exception ex)
            {
                S100Diag.Telemetry.RecordRenderFault(ex);
            }
        }

        S100Diag.Telemetry.TileCompositeDuration.Record(
            Stopwatch.GetElapsedTime(compositeStart).TotalMilliseconds);
        S100Diag.Telemetry.TileColdExposure.Record(coldExposure);
        if (visibleQueueDepth > 0)
        {
            S100Diag.Telemetry.TileVisibleQueueDepth.Record(visibleQueueDepth);
        }
        if (predictionHits > 0)
        {
            S100Diag.Telemetry.TilePredictionHits.Add(predictionHits);
        }

        if (workersToStart > 0)
        {
            // Spawn the worker pool. Each worker is independently registered with
            // the drain gate; ShutdownAndDrain waits for all of them. If a register
            // is refused (process tearing down), give back the slot we reserved so
            // ActiveWorkers reflects only the workers actually running, and undo the
            // remaining reservations in one go.
            var started = 0;
            for (var i = 0; i < workersToStart; i++)
            {
                if (s_drainGate.TryRegister())
                {
                    _ = Task.Run(() => Worker(state));
                    started++;
                }
                else
                {
                    break;
                }
            }

            if (started < workersToStart)
            {
                lock (state.Sync)
                {
                    state.ActiveWorkers -= workersToStart - started;
                }

                Interlocked.Add(ref s_activeWorkerTotal, -(workersToStart - started));
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

        // Free the previous frame's rotated off-screen composite. DrawImage is
        // deferred and flushes only after Render returns, so the surface/image
        // that backed the last rotated blit had to outlive that frame; by now it
        // has flushed and can be released (mirrors the GPU tile cache's
        // DrainPendingDisposals discipline). Harmless on north-up frames. The
        // GPU-registry mirror is cleared in lockstep so it never references a
        // disposed surface (see GpuRegistryEntry).
        state.RotationImage?.Dispose();
        state.RotationImage = null;
        state.RotationSurface?.Dispose();
        state.RotationSurface = null;
        if (state.GpuEntry is { } entryAtStart)
        {
            entryAtStart.RotationImage = null;
            entryAtStart.RotationSurface = null;
        }

        // Target band visible tiles, and whether the band fully covers the
        // viewport (every visible tile already cached).
        var target = TileGrid.VisibleTiles(centerX, centerY, coverWidth, coverHeight, resolution, band);

        // Pin the visible set so neither the hot nor the GPU cache can evict a
        // tile that is on screen this frame, no matter how small the budget is:
        // a tile in active use must never be evicted by speculative/predicted
        // inserts, or it would flicker between rendered and blank.
        state.Cache.Protect(target);
        gpuCache?.Protect(target);

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

        // A rotated viewport is composited north-up into an off-screen surface and
        // then rotated as a SINGLE image about the screen centre, rather than
        // rotating the live canvas and blitting each tile under it. Rotating
        // per-tile turned every hard clip-to-core edge — and the cross-band
        // backdrop/target boundary — into an independently rasterised rotated
        // seam, so a non-north-up zoom transition revealed banding/seams between
        // tiles and bands (issue #330). Compositing north-up first keeps those
        // joins in the clean axis-aligned space (where they abut exactly) and
        // carries no internal seam through the one rotated blit. North-up (the
        // common case) is unchanged: tiles are blitted straight onto the canvas.
        if (rotationDeg != 0)
        {
            CompositeRotated(
                canvas, state, fallback, target,
                centerX, centerY, widthDip, heightDip, coverWidth, coverHeight,
                resolution, rotationDeg, grContext, gpuCache);
        }
        else
        {
            foreach (var key in fallback)
            {
                BlitTile(canvas, state, key, centerX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
            }

            // Exact target band on top (crisp where present).
            foreach (var key in target)
            {
                BlitTile(canvas, state, key, centerX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
            }
        }

        if (DiagEnabled)
        {
            DiagComposite(state, band, centerX, centerY, coverWidth, coverHeight, resolution, fallback);
        }
    }

    /// <summary>
    /// Composites the backdrop + target tiles north-up into an off-screen surface
    /// sized to the rotated viewport's cover box, then draws that single image
    /// rotated about the screen centre. Doing the join work (per-tile clip-to-core
    /// and the cross-band backdrop/target boundary) in the unrotated space — where
    /// adjacent tiles abut exactly — and rotating only the finished composite
    /// removes the per-tile rotated seams/banding that a non-north-up zoom
    /// transition otherwise revealed (issue #330, design Appendix&#160;F.8).
    /// </summary>
    /// <remarks>
    /// The off-screen is allocated at device resolution (the canvas matrix's
    /// device scale) so the rotated blit stays crisp on HiDPI, and spans
    /// <paramref name="coverWidth"/>&#160;&#215;&#160;<paramref name="coverHeight"/>
    /// — the rotated bounding box — so the screen corners are filled once rotated.
    /// The surface/image are held on <paramref name="state"/> and freed at the next
    /// frame's composite because <c>DrawImage</c> is deferred and flushes only
    /// after <c>Render</c> returns. If the (GPU) off-screen cannot be allocated the
    /// method falls back to rotating the live canvas and blitting per-tile, so the
    /// chart stays visible rather than dropping the frame.
    /// </remarks>
    private static void CompositeRotated(
        SKCanvas canvas, TileState state,
        IReadOnlyList<TileKey> fallback, IReadOnlyList<TileKey> target,
        double centerX, double centerY, double widthDip, double heightDip,
        double coverWidth, double coverHeight, double resolution, double rotationDeg,
        GRContext? grContext, TileCache? gpuCache)
    {
        // Device scale baked into the live canvas matrix (DIP -> device px).
        var deviceScale = canvas.TotalMatrix.ScaleX;
        if (deviceScale <= 0 || float.IsNaN(deviceScale))
        {
            deviceScale = 1f;
        }

        // The north-up composite must cover the rotated viewport's bounding box,
        // centred on the screen centre, so the screen corners are filled once the
        // image is rotated back. Layout is a pure function (unit-tested on
        // TileGrid) so the off-screen sizing stays verifiable without a canvas.
        var (originX, originY, pxW, pxH) = TileGrid.RotationCompositeLayout(
            widthDip, heightDip, coverWidth, coverHeight, deviceScale);

        SKSurface? surface = null;
        if (pxW > 0 && pxH > 0)
        {
            var info = new SKImageInfo(pxW, pxH, SKColorType.Rgba8888, SKAlphaType.Premul);
            surface = grContext is not null
                ? SKSurface.Create(grContext, budgeted: false, info)
                : SKSurface.Create(info);
        }

        // Off-screen allocation can fail (zero size, GPU context loss/budget):
        // fall back to rotating the live canvas and blitting per-tile so the base
        // plane stays visible. Any hairline rotated seams beat a blank chart.
        if (surface is null)
        {
            canvas.Save();
            canvas.RotateDegrees((float)rotationDeg, (float)(widthDip * 0.5), (float)(heightDip * 0.5));
            foreach (var key in fallback)
            {
                BlitTile(canvas, state, key, centerX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
            }

            foreach (var key in target)
            {
                BlitTile(canvas, state, key, centerX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
            }

            canvas.Restore();
            return;
        }

        // Map screen DIP -> off-screen pixels: translate the cover box to the
        // surface origin, then scale to device pixels. BlitTile keeps using live
        // screen DIP coordinates, so the composite lands identically to north-up.
        var offCanvas = surface.Canvas;
        offCanvas.Clear(SKColors.Transparent);
        offCanvas.Scale(deviceScale);
        offCanvas.Translate((float)-originX, (float)-originY);

        foreach (var key in fallback)
        {
            BlitTile(offCanvas, state, key, centerX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
        }

        // Exact target band on top (crisp where present).
        foreach (var key in target)
        {
            BlitTile(offCanvas, state, key, centerX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
        }

        var image = surface.Snapshot();

        canvas.Save();
        canvas.RotateDegrees((float)rotationDeg, (float)(widthDip * 0.5), (float)(heightDip * 0.5));
        var dest = new SKRect(
            (float)originX, (float)originY,
            (float)(originX + coverWidth), (float)(originY + coverHeight));
        canvas.DrawImage(image, dest, s_sampling);
        canvas.Restore();

        // DrawImage is deferred until the frame flushes (after Render returns), so
        // the image and its backing surface must outlive this frame; they are freed
        // at the start of the next composite. Mirror GPU-backed resources into the
        // GPU registry entry (in lockstep with the TileState) so that, if this
        // layer is torn down before the next composite, ReconcileGpuCaches frees
        // them on the render thread rather than the finalizer thread crashing the
        // native backend. CPU-backed (no grContext) composites are safe to
        // finalize off-thread and need no mirror.
        state.RotationImage = image;
        state.RotationSurface = surface;
        if (grContext is not null && state.GpuEntry is { } entryAtEnd)
        {
            entryAtEnd.RotationImage = image;
            entryAtEnd.RotationSurface = surface;
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
                state.GpuEntry = null;
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
            state.GpuEntry = RegisterGpuCache(layer, state.GpuTextures, grContext);
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
    /// Registers a GPU-texture cache in the process-wide registry so it — and the
    /// owning layer's GPU rotation composite, mirrored into the returned entry —
    /// are held alive against off-thread finalization until the owning layer is
    /// collected (Phase&#160;5). Returns the entry so the caller can mirror its
    /// rotation resources into it. See <see cref="s_gpuRegistry"/>.
    /// </summary>
    private static GpuRegistryEntry RegisterGpuCache(ILayer layer, TileCache cache, object context)
    {
        var entry = new GpuRegistryEntry
        {
            Layer = new WeakReference<ILayer>(layer),
            Cache = cache,
            Context = context,
        };

        lock (s_gpuRegistrySync)
        {
            s_gpuRegistry.Add(entry);
        }

        return entry;
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
    /// the GPU resources of any registered entry whose owning layer has been
    /// collected (Phase&#160;5): the GPU-texture residency cache and the GPU
    /// rotation composite (<see cref="GpuRegistryEntry.RotationSurface"/> /
    /// <see cref="GpuRegistryEntry.RotationImage"/>). This is the teardown path
    /// for closed datasets, re-portrayed (layer-swapped) datasets, and
    /// render-subsystem switches: the layer is gone so its <see cref="TileState"/>
    /// never renders again, but the registry kept its GPU objects alive so they
    /// are freed here on the GPU-owning thread instead of crashing the native
    /// backend on the finalizer thread. Only resources bound to
    /// <paramref name="grContext"/> are touched; a cache from a different (e.g.
    /// lost) context is left for that context's own teardown rather than freed
    /// under the wrong one. See <see cref="s_gpuRegistry"/>.
    /// </summary>
    private static void ReconcileGpuCaches(object grContext)
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

                    // Free the dead layer's GPU rotation composite (if a rotated
                    // frame left one set) on the render thread too; like the
                    // texture cache, it must not be finalized off-thread.
                    entry.RotationImage?.Dispose();
                    entry.RotationImage = null;
                    entry.RotationSurface?.Dispose();
                    entry.RotationSurface = null;
                }

                s_gpuRegistry.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Test-only seam: registers a GPU-registry entry for <paramref name="layer"/>
    /// (with a sentinel <paramref name="context"/> and CPU-backed rotation
    /// resources) and returns it, so the off-thread-finalization teardown
    /// (<see cref="ReconcileGpuCaches(object)"/>) can be exercised deterministically
    /// without a GPU <see cref="GRContext"/>. The lifecycle is identical for CPU-
    /// and GPU-backed resources. Not for production use.
    /// </summary>
    internal static GpuRegistryEntry RegisterGpuEntryForTest(
        ILayer layer, TileCache cache, object context, SKSurface? rotationSurface, SKImage? rotationImage)
    {
        var entry = RegisterGpuCache(layer, cache, context);
        entry.RotationSurface = rotationSurface;
        entry.RotationImage = rotationImage;
        return entry;
    }

    /// <summary>
    /// Test-only seam: runs the dead-layer GPU teardown for the given sentinel
    /// <paramref name="context"/>. See <see cref="ReconcileGpuCaches(object)"/>.
    /// </summary>
    internal static void ReconcileGpuCachesForTest(object context) => ReconcileGpuCaches(context);

    /// <summary>Test-only seam: the current GPU-registry entry count.</summary>
    internal static int GpuRegistryEntryCountForTest
    {
        get
        {
            lock (s_gpuRegistrySync)
            {
                return s_gpuRegistry.Count;
            }
        }
    }

    /// <summary>
    /// Test-only seam: disposes and clears every GPU-registry entry, so a test
    /// leaves the process-wide registry clean for the next one. Not for
    /// production use.
    /// </summary>
    internal static void ClearGpuRegistryForTest()
    {
        lock (s_gpuRegistrySync)
        {
            foreach (var entry in s_gpuRegistry)
            {
                entry.Cache.Dispose();
                entry.RotationImage?.Dispose();
                entry.RotationSurface?.Dispose();
            }

            s_gpuRegistry.Clear();
        }
    }
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

    /// <summary>
    /// Computes how many new tile workers a layer may start this paint, turning
    /// the per-layer <paramref name="baseline"/> from a hard ceiling into a floor
    /// that a busy layer may exceed by borrowing idle global capacity for
    /// <em>visible</em> work (issue #432). Pure and deterministic given its inputs
    /// so the borrow policy is unit-testable without a live render.
    /// </summary>
    /// <param name="baseline">
    /// The per-layer worker reservation (<see cref="RenderingOptimizations.TileWorkerCount"/>).
    /// Always reachable (subject to <paramref name="maxTotalWorkers"/>); predicted
    /// work is never served above this count.
    /// </param>
    /// <param name="elasticCeiling">
    /// The maximum workers a single layer may reach when borrowing for visible
    /// work. Equal to <paramref name="baseline"/> on a LowEnd host (no borrowing),
    /// otherwise <paramref name="maxTotalWorkers"/>.
    /// </param>
    /// <param name="maxTotalWorkers">The process-wide worker cap (<see cref="s_maxTotalWorkers"/>).</param>
    /// <param name="activeWorkerTotal">Current total live workers across all layers.</param>
    /// <param name="layerActiveWorkers">This layer's current live workers.</param>
    /// <param name="pendingVisible">This layer's pending visible (on-screen) cold tiles.</param>
    /// <param name="pendingPredicted">This layer's pending predicted (speculative) tiles.</param>
    /// <param name="reservedForOthers">
    /// Total workers reserved for <em>other</em> active-visible layers — each such
    /// layer's shortfall to its <paramref name="baseline"/> floor — which this
    /// layer may not borrow. The fairness floor that stops a dense low-z layer
    /// starving later-painting siblings.
    /// </param>
    /// <returns>The number of new workers to start (never negative).</returns>
    internal static int ComputeWorkersToStart(
        int baseline,
        int elasticCeiling,
        int maxTotalWorkers,
        int activeWorkerTotal,
        int layerActiveWorkers,
        int pendingVisible,
        int pendingPredicted,
        int reservedForOthers)
    {
        var totalPending = pendingVisible + pendingPredicted;
        if (totalPending <= 0)
        {
            return 0;
        }

        var globalRoom = maxTotalWorkers - activeWorkerTotal;
        if (globalRoom <= 0)
        {
            return 0;
        }

        // Floor grant: reach the per-layer reservation regardless of siblings,
        // bounded only by global room (the pre-elastic behaviour). Serves visible
        // and predicted work alike.
        var desiredBaseline = Math.Min(baseline, totalPending);
        var baselineStart = Math.Clamp(desiredBaseline - layerActiveWorkers, 0, globalRoom);

        // Elastic grant: borrow toward the elastic ceiling for VISIBLE work only,
        // from whatever room remains after this layer's own floor and the floor
        // reservation owed to every other active-visible layer.
        var elasticStart = 0;
        var layerCeiling = Math.Max(desiredBaseline, Math.Min(elasticCeiling, pendingVisible));
        if (layerCeiling > desiredBaseline)
        {
            var elasticRoom = globalRoom - baselineStart - Math.Max(0, reservedForOthers);
            var elasticWant = layerCeiling - Math.Max(layerActiveWorkers, desiredBaseline);
            if (elasticWant > 0 && elasticRoom > 0)
            {
                elasticStart = Math.Min(elasticWant, elasticRoom);
            }
        }

        return baselineStart + elasticStart;
    }

    /// <summary>
    /// Decides whether a tile worker should leave its drain loop this iteration.
    /// A worker exits when its layer's scene is gone or all pending work has
    /// drained, and — the elastic addition (issue #432) — when it is an
    /// above-<paramref name="baseline"/> ("borrowed") worker that has reached the
    /// visible→predicted boundary: with no visible work left it sheds instead of
    /// falling through to speculative work, so borrowed global capacity returns to
    /// the pool within roughly one tile's raster time rather than leaking into
    /// prewarm. Pure so the shed policy is unit-testable.
    /// </summary>
    /// <param name="sceneNull">True when the layer's scene has been cleared.</param>
    /// <param name="hasVisible">True when the layer has pending visible tiles.</param>
    /// <param name="hasPredicted">True when the layer has pending predicted tiles.</param>
    /// <param name="layerActiveWorkers">This layer's current live workers.</param>
    /// <param name="baseline">The per-layer floor (<see cref="RenderingOptimizations.TileWorkerCount"/>).</param>
    /// <returns>True when the worker should exit its loop.</returns>
    internal static bool ShouldWorkerExit(
        bool sceneNull,
        bool hasVisible,
        bool hasPredicted,
        int layerActiveWorkers,
        int baseline) =>
        sceneNull
        || (!hasVisible && !hasPredicted)
        || (!hasVisible && layerActiveWorkers > baseline);

    /// <summary>
    /// Refreshes <paramref name="state"/>'s entry in the active-visible-layer
    /// registry and returns the worker reservation owed to <em>other</em> layers
    /// that have had visible cold work within <see cref="ElasticFairnessWindowSeconds"/>
    /// — the sum over those layers of their shortfall to the per-layer floor
    /// (<see cref="RenderingOptimizations.TileWorkerCount"/>). Counting each
    /// competitor's own workers, rather than all other layers', keeps an unrelated
    /// predicted-only layer's workers from wrongly satisfying an active-visible
    /// sibling's reservation. Stamps (or removes) this layer, then prunes stale
    /// entries so layers that stopped painting — culled, resolution-hidden, or torn
    /// down — age out of the reservation. Called only on the render thread under
    /// the layer's <c>state.Sync</c>; takes <see cref="s_visibleLayerSync"/> second,
    /// never the reverse.
    /// </summary>
    /// <param name="state">The painting layer's tile state.</param>
    /// <param name="hasVisibleWork">True when the layer has visible cold tiles (pending or in flight) this paint.</param>
    /// <param name="layerActiveWorkers">This layer's current live workers (its own entry excludes itself from the reservation).</param>
    /// <param name="nowTicks">The current <see cref="Stopwatch"/> tick.</param>
    /// <returns>The total worker reservation owed to other active-visible layers.</returns>
    private static int RefreshActiveVisibleLayers(TileState state, bool hasVisibleWork, int layerActiveWorkers, long nowTicks)
    {
        var windowTicks = (long)(Stopwatch.Frequency * ElasticFairnessWindowSeconds);
        var baseline = RenderingOptimizations.TileWorkerCount;
        lock (s_visibleLayerSync)
        {
            if (hasVisibleWork)
            {
                if (s_visibleLayerStamps.TryGetValue(state, out var box))
                {
                    box.StampTicks = nowTicks;
                    box.ActiveWorkers = layerActiveWorkers;
                }
                else
                {
                    s_visibleLayerStamps.Add(state, new ActiveVisibleEntry(nowTicks, layerActiveWorkers));
                }
            }
            else
            {
                s_visibleLayerStamps.Remove(state);
            }

            var reserved = 0;
            s_visibleLayerPruneScratch.Clear();
            // A dead layer's weak key drops out of the table on its own; this pass
            // only evicts still-live layers whose last visible paint aged out.
            foreach (var entry in s_visibleLayerStamps)
            {
                if (nowTicks - entry.Value.StampTicks > windowTicks)
                {
                    s_visibleLayerPruneScratch.Add(entry.Key);
                }
                else if (!ReferenceEquals(entry.Key, state))
                {
                    reserved += Math.Max(0, baseline - entry.Value.ActiveWorkers);
                }
            }

            foreach (var stale in s_visibleLayerPruneScratch)
            {
                s_visibleLayerStamps.Remove(stale);
            }

            return reserved;
        }
    }

    /// <summary>
    /// Updates <paramref name="state"/>'s registry entry with its post-grant worker
    /// count, so a sibling painting later in the same frame sees this layer's true
    /// share and reserves only its own shortfall against it. No-op if the layer is
    /// not currently registered as active-visible.
    /// </summary>
    private static void RecordActiveVisibleLayerWorkers(TileState state, int layerActiveWorkers, long nowTicks)
    {
        lock (s_visibleLayerSync)
        {
            if (s_visibleLayerStamps.TryGetValue(state, out var box))
            {
                box.StampTicks = nowTicks;
                box.ActiveWorkers = layerActiveWorkers;
            }
        }
    }

    private static void Worker(TileState state)
    {
        // Set true when this worker releases its pool slot under state.Sync at the
        // exit decision below, so the finally does not decrement a second time.
        // Guarding the decrement under the same lock that reads ActiveWorkers is
        // what lets a cascade of shedding elastic workers converge on the baseline
        // instead of every one observing the pre-decrement count and over-shedding
        // (which would stall the predicted drain and thrash the pool).
        var slotReleased = false;
        try
        {
            while (true)
            {
                // Stop before touching Skia once the process is shutting down,
                // so ShutdownAndDrain's wait completes and no tile is rasterised
                // into a half-torn-down Skia. slotReleased is still false on this
                // path, so the finally below releases the slot and completes the
                // drain-gate registration.
                if (s_drainGate.IsDraining)
                {
                    return;
                }

                TileKey key;
                float deviceScale;
                long generation;
                VectorScene scene;
                BaseSpatialIndex? baseIndex;
                bool isPrediction;
                string? diskNamespace;

                lock (state.Sync)
                {
                    var currentScene = state.Scene;
                    var hasVisible = state.PendingVisible.Count > 0;
                    var hasPredicted = state.PendingPredicted.Count > 0;
                    var hasCrossBand = state.PendingCrossBand.Count > 0;

                    // Exit when the scene is gone or all work has drained, and — the
                    // elastic addition (issue #432) — shed an above-baseline
                    // ("borrowed") worker the moment visible work runs out rather
                    // than letting it fall through to speculative work, so borrowed
                    // global capacity returns to the pool within ~one tile raster.
                    // The idle cross-band ±1 pre-warm (issue #428) is speculative
                    // like the predicted set: it keeps a baseline worker alive to
                    // drain it, but never holds a borrowed worker.
                    // Release the slot under this same lock so concurrent sheds can't
                    // all read the pre-decrement count and over-shed below baseline.
                    if (ShouldWorkerExit(
                            sceneNull: currentScene is null,
                            hasVisible: hasVisible,
                            hasPredicted: hasPredicted || hasCrossBand,
                            layerActiveWorkers: state.ActiveWorkers,
                            baseline: RenderingOptimizations.TileWorkerCount))
                    {
                        state.ActiveWorkers--;
                        Interlocked.Decrement(ref s_activeWorkerTotal);
                        slotReleased = true;
                        return;
                    }

                    // Visible tiles always drain before speculative ones, so
                    // prediction work yields to anything actually on screen.
                    if (hasVisible)
                    {
                        key = TakeNearest(state.PendingVisible, state.PendingCenterX, state.PendingCenterY);
                        isPrediction = false;
                    }
                    else if (state.PendingPredicted.Count > 0)
                    {
                        key = TakeNearest(state.PendingPredicted, state.PendingCenterX, state.PendingCenterY);
                        isPrediction = true;
                    }
                    else
                    {
                        // Lowest tier: idle cross-band pre-warm. Treated as a
                        // prediction (tracked in PredictedInCache, never triggers a
                        // redraw) so a published adjacent-band tile cannot start a
                        // repaint loop; it is picked up when a later zoom makes it
                        // visible.
                        key = TakeNearest(state.PendingCrossBand, state.PendingCenterX, state.PendingCenterY);
                        isPrediction = true;
                    }

                    deviceScale = state.PendingDeviceScale;
                    generation = state.PendingGeneration;
                    // ShouldWorkerExit returns true for a null scene, so reaching
                    // here means the scene is live; the null-coalescing throw is
                    // unreachable but gives the compiler its non-null narrowing
                    // (no scene can vanish while we hold state.Sync).
                    scene = currentScene ?? throw new InvalidOperationException(
                        "Scene became null after ShouldWorkerExit returned false.");
                    baseIndex = state.BaseIndex;
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
                        using var bitmap = RasterizeTile(scene, baseIndex, key, deviceScale);
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
                        else if (state.VisibleEnqueueTicks.Remove(key, out var enqueuedAt))
                        {
                            // End-to-end cold latency: queue wait + this tile's
                            // rasterise/disk read, from the frame it was first
                            // seen visible-cold to landing in the hot cache.
                            S100Diag.Telemetry.TileColdLatency.Record(
                                Stopwatch.GetElapsedTime(enqueuedAt).TotalMilliseconds);
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
            // die without decrementing ActiveWorkers — that would permanently
            // under-count the pool and starve tile production.
            S100Diag.Telemetry.RecordRenderFault(ex);
        }
        finally
        {
            // Release this worker's pool slot so the next frame can spin up a fresh
            // worker for any still-pending tiles, unless the shed/drain path above
            // already released it under state.Sync. Both the normal drain-exit and
            // the abnormal-exit (shutdown / exception) paths land here. Mirror the
            // per-layer slot in the process-wide total so other layers can reclaim
            // the headroom.
            if (!slotReleased)
            {
                lock (state.Sync)
                {
                    state.ActiveWorkers--;
                }

                Interlocked.Decrement(ref s_activeWorkerTotal);
            }

            // Pair the TryRegister at the worker-start site. When the last
            // worker completes, this signals ShutdownAndDrain that Skia is idle.
            s_drainGate.Complete();
        }
    }

    /// <summary>
    /// Removes and returns the pending tile whose world centre is nearest the
    /// viewport centre (<paramref name="centerX"/>, <paramref name="centerY"/> in
    /// EPSG:3857 metres), so central tiles rasterise before the perimeter within
    /// a priority tier — cutting time-to-centre-fill on a cold pan/zoom without
    /// disturbing the visible-before-predicted ordering (that is the caller's
    /// two-tier drain). Ties are broken deterministically on
    /// (<c>Band</c>, <c>Y</c>, <c>X</c>) so the drain order never depends on the
    /// set's hash iteration order. Returns <see langword="default"/> for an empty
    /// set. O(n) per call over a viewport-sized set (tens of tiles).
    /// </summary>
    /// <param name="pending">The pending tile set to draw from; mutated in place.</param>
    /// <param name="centerX">Viewport centre X in EPSG:3857 metres.</param>
    /// <param name="centerY">Viewport centre Y in EPSG:3857 metres.</param>
    /// <returns>The removed nearest-to-centre tile, or <see langword="default"/> when empty.</returns>
    internal static TileKey TakeNearest(HashSet<TileKey> pending, double centerX, double centerY)
    {
        var found = false;
        var best = default(TileKey);
        var bestScore = double.PositiveInfinity;
        foreach (var k in pending)
        {
            var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(k);
            var dx = (minX + maxX) * 0.5 - centerX;
            var dy = (minY + maxY) * 0.5 - centerY;
            var score = dx * dx + dy * dy;
            if (!found)
            {
                found = true;
                best = k;
                bestScore = score;
                continue;
            }

            // Squared distances in EPSG:3857 metres are large and involve π and
            // division, so exact equality is unreliable for detecting a tie.
            // Distinct tiles differ by at least ~a tile edge, which dwarfs this
            // relative tolerance, so genuine ties (including mirror-image tiles)
            // fall through to the deterministic (Band, Y, X) order while nearer
            // tiles still win outright.
            var tolerance = 1e-9 * Math.Max(Math.Abs(score), Math.Abs(bestScore));
            var isTie = Math.Abs(score - bestScore) <= tolerance;
            if (isTie ? TileOrderLess(k, best) : score < bestScore)
            {
                best = k;
                bestScore = score;
            }
        }

        if (found)
        {
            pending.Remove(best);
        }

        return best;
    }

    /// <summary>
    /// Deterministic total order on tiles by (<c>Band</c>, <c>Y</c>, <c>X</c>),
    /// used only to break equal centre-distance ties in <see cref="TakeNearest"/>
    /// so the drain order is stable across runs.
    /// </summary>
    private static bool TileOrderLess(TileKey a, TileKey b)
    {
        if (a.Band != b.Band)
        {
            return a.Band < b.Band;
        }

        if (a.Y != b.Y)
        {
            return a.Y < b.Y;
        }

        return a.X < b.X;
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
    /// Draws the live symbol/text overlay (point symbols + soundings + labels)
    /// on top of the composited base tiles, at constant on-screen size against
    /// the live viewport. Unlike the base plane, these ops are <i>not</i>
    /// tiled/scaled, so a buoy, sounding, or label keeps the same pixel size at
    /// every zoom. SCAMIN is applied against the live scale denominator (matching
    /// the base tiles' own per-band culling).
    /// <para>
    /// Labels are <b>decluttered</b> each frame (S-100 Part 9 overlap avoidance;
    /// see <see cref="LabelDeclutterer"/>) and kept <b>upright</b> under a rotated
    /// viewport: point symbols are drawn under the rotated canvas (as the tile
    /// composite rotates tiles), while text is drawn on an unrotated canvas with
    /// its anchor rotated in code so glyphs stay horizontal. North-up is the v1
    /// case and draws points and text in a single unrotated pass.
    /// </para>
    /// </summary>
    private static void DrawOverlay(
        SKCanvas canvas, TileState state,
        double centerX, double centerY, double widthDip, double heightDip,
        double resolution, double rotationDeg, float deviceScale)
    {
        var overlay = state.OverlayScene;
        if (overlay is null || overlay.Ops.Count == 0)
        {
            return;
        }

        // Live full-screen viewport in DIP space: symbol/text sizes are in
        // logical display px, so projecting onto a DIP-sized viewport draws
        // them at their intended on-screen size (the foreground canvas's
        // device-scale matrix then keeps them crisp on HiDPI).
        var halfWorldW = widthDip * resolution * 0.5;
        var halfWorldH = heightDip * resolution * 0.5;
        // Build the viewport's lat/lon corners with the lossless (unclamped)
        // inverse so WorldToScreen re-projects back to these exact EPSG:3857
        // bounds. Clamping here would pull a top/bottom edge that overhangs the
        // Web-Mercator pole limit (common when a high-latitude dataset is zoomed
        // out) back to ±85°, compressing the overlay's vertical span so labels
        // drift poleward off their features. See WebMercator.ToLonLat.
        var (minLon, minLat) = WebMercator.ToLonLat(centerX - halfWorldW, centerY - halfWorldH, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(centerX + halfWorldW, centerY + halfWorldH, clampLatitude: false);

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

        var rotate = rotationDeg != 0;
        float cx = (float)(widthDip * 0.5);
        float cy = (float)(heightDip * 0.5);
        const float margin = SkiaDisplayListRenderer.PointCullMarginPx;

        // The plain (unrotated) viewport cull rect, in real screen space. Both
        // the declutter pass and the upright text pass work in this frame because
        // they rotate anchors in code rather than rotating the canvas.
        var screenCull = new SKRect(
            -margin, -margin,
            (float)widthDip + margin, (float)heightDip + margin);

        // The rotated point pass culls against the rotated viewport footprint,
        // which is larger than the axis-aligned screen rect. Compute it up-front
        // so it can also bound the viewport scoping below (the declutter and
        // upright-text passes rotate anchors into the same footprint).
        SKRect rotatedCull = default;
        if (rotate)
        {
            var rad = rotationDeg * Math.PI / 180.0;
            var cosR = Math.Abs(Math.Cos(rad));
            var sinR = Math.Abs(Math.Sin(rad));
            var extentX = (float)(widthDip * 0.5 * cosR + heightDip * 0.5 * sinR);
            var extentY = (float)(widthDip * 0.5 * sinR + heightDip * 0.5 * cosR);
            rotatedCull = new SKRect(
                cx - extentX - margin, cy - extentY - margin,
                cx + extentX + margin, cy + extentY + margin);
        }

        // #332 lever b — scope the per-frame walk to the ops near the viewport.
        // The query bounds are the world preimage of the (rotated) cull rect, so
        // the candidate set is a conservative superset of everything any pass
        // would draw; the precise per-op screen cull still runs downstream, so no
        // visible feature is dropped (fidelity neutral). Falls back to the whole
        // overlay if the index is somehow absent.
        var scene = ScopeOverlay(
            state, overlay, rotate ? rotatedCull : screenCull,
            centerX, centerY, widthDip, heightDip, resolution);

        // Declutter labels deterministically: footprints are computed in final
        // on-screen space (anchors rotated by the same angle as the overlay), so
        // collisions are correct under rotation. Points reserve space first;
        // lower-priority labels that overlap an occupied footprint are skipped.
        var suppressed = s_labelDeclutterer.Declutter(
            scene, viewport, screenCull, s_overlayRenderer.HonorScaleVisibility,
            rotationDeg, cx, cy);

        if (!rotate)
        {
            s_overlayRenderer.RenderOnto(canvas, scene, viewport, new OverlayDrawOptions
            {
                PointCullBounds = screenCull,
                SuppressedText = suppressed,
                DeviceScale = deviceScale,
            });
            return;
        }

        // Rotated viewport: draw symbols under the rotated canvas (anchors stay
        // aligned with the rotated base), then draw labels upright by rotating
        // the anchor in code while keeping glyphs axis-aligned.
        canvas.Save();
        canvas.RotateDegrees((float)rotationDeg, cx, cy);
        try
        {
            s_overlayRenderer.RenderOnto(canvas, scene, viewport, new OverlayDrawOptions
            {
                PointCullBounds = rotatedCull,
                DrawText = false,
                DeviceScale = deviceScale,
            });
        }
        finally
        {
            canvas.Restore();
        }

        s_overlayRenderer.RenderOnto(canvas, scene, viewport, new OverlayDrawOptions
        {
            PointCullBounds = screenCull,
            SuppressedText = suppressed,
            TextAnchorRotationDegrees = rotationDeg,
            ScreenCenterX = cx,
            ScreenCenterY = cy,
            DrawPoints = false,
        });
    }

    /// <summary>
    /// Scopes the overlay to the ops whose anchor lies within the world preimage
    /// of <paramref name="queryCull"/> (inflated by the largest op offset), using
    /// the scene's <see cref="OverlaySpatialIndex"/> and the layer's reusable
    /// candidate buffers. Returns the whole <paramref name="overlay"/> unchanged
    /// when no index is available.
    /// </summary>
    private static VectorScene ScopeOverlay(
        TileState state, VectorScene overlay, SKRect queryCull,
        double centerX, double centerY, double widthDip, double heightDip, double resolution)
    {
        var index = state.OverlayIndex;
        if (index is null || state.OverlayScopedScene is null)
        {
            return overlay;
        }

        // Inflate by the max op offset (px) because the per-op screen cull tests
        // the anchor plus its offset, while the index keys on the anchor alone.
        double inflate = index.MaxOffsetPx;
        double sx0 = queryCull.Left - inflate;
        double sy0 = queryCull.Top - inflate;
        double sx1 = queryCull.Right + inflate;
        double sy1 = queryCull.Bottom + inflate;

        // Invert the north-up viewport projection (screen px → EPSG:3857 metres):
        //   screenX = widthDip/2 + (worldX - centerX) / resolution
        //   screenY = heightDip/2 - (worldY - centerY) / resolution
        double wxA = centerX + (sx0 - widthDip * 0.5) * resolution;
        double wxB = centerX + (sx1 - widthDip * 0.5) * resolution;
        double wyA = centerY - (sy0 - heightDip * 0.5) * resolution;
        double wyB = centerY - (sy1 - heightDip * 0.5) * resolution;

        index.Query(
            Math.Min(wxA, wxB), Math.Min(wyA, wyB),
            Math.Max(wxA, wxB), Math.Max(wyA, wyB),
            state.OverlayQueryScratch, state.OverlayCandidates);

        return state.OverlayScopedScene;
    }

    /// <summary>
    /// Rasterises a single tile (core + gutter) from the scene at its band
    /// resolution and the frame's device scale.
    /// </summary>
    private static SKBitmap RasterizeTile(VectorScene scene, BaseSpatialIndex? baseIndex, TileKey key, float deviceScale)
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var bandResolution = TileGrid.ResolutionForBand(key.Band);
        var gutterWorld = GutterDip * bandResolution;

        var fullMinX = minX - gutterWorld;
        var fullMaxX = maxX + gutterWorld;
        var fullMinY = minY - gutterWorld;
        var fullMaxY = maxY + gutterWorld;

        // Scope the base-plane walk to the ops whose world bounds intersect this
        // tile (+ gutter). The index is a conservative superset and the renderer
        // still applies the exact per-op scale cull and pixel clip, so the result
        // is pixel-identical to rasterising the whole scene — only fewer ops are
        // walked (#332 cold tile-gen, perf line under #347). A null index (before
        // the first BindScene, defensive) falls back to the full scene.
        var tileScene = baseIndex is null
            ? scene
            : new VectorScene(baseIndex.Query(fullMinX, fullMinY, fullMaxX, fullMaxY));

        // Lossless (unclamped) inverse so WorldToScreen reproduces these exact
        // tile bounds. The top tile row's gutter pushes fullMaxY just past the
        // Web-Mercator pole limit (±π·EarthRadius); clamping there would squash
        // the tile's vertical mapping and drift the base geometry poleward. See
        // WebMercator.ToLonLat.
        var (minLon, minLat) = WebMercator.ToLonLat(fullMinX, fullMinY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(fullMaxX, fullMaxY, clampLatitude: false);

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
            // Tiles carry already-continuous EPSG:3857 geometry (longitudes may
            // exceed +180° without wrapping). The seam-wrap is a headless
            // single-viewport auto-fit concern; under a narrow per-tile viewport
            // east of +180° it would teleport far vertices of large polygons and
            // smear them across the tile, so it is disabled here.
            EnableSeamWrap = false,
        };

        return renderer.Render(tileScene, viewport);
    }

    /// <summary>Per-layer tiling state, held in a weak table keyed by layer.</summary>
    private sealed class TileState
    {
        public readonly object Sync = new();

        public VectorScene? Scene;

        // Spatial index over the base-plane op extents (#332 cold tile-gen,
        // perf line under #347), built once when a scene is bound so each
        // off-thread RasterizeTile walk can be scoped to the ops intersecting
        // the tile (+ gutter) instead of the whole cell. Stable after
        // construction, so it is safe to query from the multiple worker threads
        // that rasterise tiles concurrently. Null until a scene is bound.
        public BaseSpatialIndex? BaseIndex;

        // Live screen-space overlay: point symbols + point-anchored text
        // (soundings) partitioned out of the tiled base plane so they draw at
        // constant on-screen size instead of scaling with the band-resolution
        // tiles. Null until a scene is bound; empty when the scene has no
        // point/text ops.
        public VectorScene? OverlayScene;

        // Spatial index over the overlay anchors (#332 lever b), built once when
        // the scene is bound so the per-frame overlay walk can be scoped to the
        // viewport instead of the whole cell. The candidate list + sort scratch
        // are render-thread-only reusable buffers; the scoped scene is a stable
        // wrapper over the candidate list so a frame allocates nothing here.
        public OverlaySpatialIndex? OverlayIndex;
        public readonly List<int> OverlayQueryScratch = new();
        public readonly List<PaintOp> OverlayCandidates = new();
        public VectorScene? OverlayScopedScene;
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

        // The process-wide GPU-registry entry for this layer's current GPU
        // residency (Phase 5), or null on a software surface / before the first
        // GPU-backed paint. The registry strong-references this entry, so
        // mirroring the GPU-backed RotationSurface/RotationImage into it (in
        // lockstep with the fields below) keeps them reachable for render-thread
        // disposal even after the weakly-held TileState is collected. See
        // GpuRegistryEntry / s_gpuRegistry.
        public GpuRegistryEntry? GpuEntry;

        // Visible misses drain before speculative (predicted) tiles.
        public readonly HashSet<TileKey> PendingVisible = new();
        public readonly HashSet<TileKey> PendingPredicted = new();

        // Idle cross-band (±1) pre-warm tiles (issue #428): the lowest-priority
        // tier, drained only after PendingVisible and PendingPredicted are empty.
        // Populated only when the layer is otherwise idle (no cold visible misses
        // this frame, cache headroom to spare); rebuilt (cancelled) every frame.
        public readonly HashSet<TileKey> PendingCrossBand = new();

        // First-enqueue Stopwatch ticks for each cold visible tile still awaiting
        // publish, so the worker can record end-to-end cold latency (queue wait +
        // rasterise) when it lands. Entry added the first frame a tile is enqueued
        // visible-cold; removed on publish. Bounded by the visible tile count.
        public readonly Dictionary<TileKey, long> VisibleEnqueueTicks = new();

        // Reusable scratch for pruning VisibleEnqueueTicks without per-frame
        // allocation (cannot mutate a dictionary while enumerating its keys).
        public readonly List<TileKey> EnqueuePruneScratch = new();

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

        public int ActiveWorkers;
        public float PendingDeviceScale = 1f;
        public long PendingGeneration;

        // Viewport centre (EPSG:3857 metres) of the frame that produced the
        // current pending sets, so the worker can dequeue centre-first within
        // each priority tier (nearest pending tile to the viewport centre
        // rasterises before the perimeter). Refreshed every frame that enqueues
        // work, in lockstep with the pending sets, so it always matches the
        // centre the visible/predicted tiles were selected for.
        public double PendingCenterX;
        public double PendingCenterY;

        // Off-screen north-up composite for the current rotated frame (null on
        // north-up frames). DrawImage is deferred, so these must outlive the frame
        // that placed them; the next composite frees them once it has flushed.
        public SKSurface? RotationSurface;
        public SKImage? RotationImage;
    }
}
