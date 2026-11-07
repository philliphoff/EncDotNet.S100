using System.Diagnostics;
using System.Runtime.CompilerServices;
using EncDotNet.S100.Core.Caching;
using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;
using CoreViewport = EncDotNet.S100.Pipelines.Viewport;
using S100Diag = EncDotNet.S100.Rendering.Engine.Diagnostics;
using SceneRgbaColor = EncDotNet.S100.Pipelines.RgbaColor;

namespace EncDotNet.S100.Rendering.Engine;

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
/// <b>Jobs.</b> Each pending tile is a job on the process-wide work scheduler
/// (<see cref="WorkScheduler.Shared"/>, map rendering slice&#160;5) at its class
/// (visible, predicted or cross-band pre-warm) and its distance from the view
/// centre, so every layer's central visible tiles rasterise first and no layer
/// holds workers of its own. The pending sets are rebuilt every frame and a
/// tile that leaves them drops its job, so tiles panned out of view are
/// dropped before they render. Each tile is rendered with a <b>gutter</b>
/// (bleed beyond the tile bounds) and hard-clipped to its core on composite, so
/// lines and area fills stay continuous across seams. Finished tiles enter the
/// native-byte-bounded <see cref="TileCache"/> and trigger a single repaint via
/// the layer's per-session redraw sink.
/// </para>
/// <para>
/// <b>Scope (Phase&#160;2).</b> Tiling + LRU cache + best-available compositor.
/// No prediction/pre-warm (Phase&#160;3), no disk cache or separate label plane
/// (Phase&#160;4). North-up only; a rotated viewport draws nothing for that
/// frame.
/// </para>
/// </remarks>
public static partial class S100VectorTileRenderer
{
    /// <summary>
    /// The Mapsui <c>ILayer.CustomLayerRendererName</c> value that routes a
    /// layer through this renderer.
    /// </summary>
    public const string RendererName = "s100.vector.tile";

    /// <summary>
    /// Gutter, in DIP, rasterised beyond each tile's bounds on every edge and
    /// hard-clipped away on composite, so strokes crossing a tile seam keep
    /// their joins/caps. Sourced from
    /// <see cref="ChartSessionOptions.TileGutterDip"/> (seeded from
    /// <c>S100_VECTOR_TILE_GUTTER</c>, default 64); only newly-rasterised tiles
    /// pick up a live change.
    /// </summary>
    public static double GutterDip => ChartEngine.Options.TileGutterDip;

    /// <summary>
    /// The apron, in device pixels, a stored tile keeps beyond its core on every
    /// edge (map rendering slice&#160;4). The tile is still rasterised with the
    /// full <see cref="GutterDip"/> gutter so strokes crossing a seam keep their
    /// joins and caps, but only the core plus this apron is kept: the compositor
    /// clips each blit to the core, and bilinear sampling of a core-edge pixel
    /// reads at most one pixel past it, even when the tile is drawn up to √2
    /// smaller between bands. Storing the full 64&#160;DIP gutter held
    /// <c>(256 + 128)² / 256² = 2.25×</c> the core's pixels; the apron holds
    /// about 1.03×.
    /// </summary>
    internal const int ApronPx = 2;

    /// <summary>
    /// The edge length, in device pixels, of a tile's core (the 256&#160;DIP
    /// grid cell) rasterised at <paramref name="deviceScale"/>.
    /// </summary>
    internal static int CorePixelSize(float deviceScale) =>
        Math.Clamp(
            (int)Math.Round(TileGrid.TileSizeDip * deviceScale),
            1,
            MaxImageDimension - 2 * ApronPx);

    /// <summary>
    /// The gutter, in whole device pixels, rasterised beyond a tile's core at
    /// <paramref name="deviceScale"/>: <see cref="GutterDip"/> rounded, never
    /// less than the stored apron and never so wide that the raster surface
    /// passes <see cref="MaxImageDimension"/>. A whole number of pixels keeps
    /// the stored apron a plain crop of the raster.
    /// </summary>
    internal static int GutterPixelSize(float deviceScale)
    {
        var core = CorePixelSize(deviceScale);
        var gutter = (int)Math.Round(GutterDip * deviceScale);
        return Math.Clamp(gutter, ApronPx, Math.Max(ApronPx, (MaxImageDimension - core) / 2));
    }

    /// <summary>
    /// The edge length, in device pixels, of a stored tile (core + apron on
    /// both sides) rasterised at <paramref name="deviceScale"/>. The cached
    /// image's width is this value, so it doubles as the tile's raster-scale
    /// tag: the hot and warm caches only count a tile as a hit for a frame
    /// whose own scale maps to the same pixel size.
    /// </summary>
    internal static int TilePixelSize(float deviceScale) => CorePixelSize(deviceScale) + 2 * ApronPx;

    [ThreadStatic]
    private static int _offscreenDepth;

    /// <summary>
    /// Whether the calling thread is inside a
    /// <see cref="BeginOffscreenRender"/> scope.
    /// </summary>
    public static bool IsOffscreenRender => _offscreenDepth > 0;

    /// <summary>
    /// Marks every <see cref="Render"/> call on the calling thread, until the
    /// returned scope is disposed, as an <b>off-screen</b> render of the live
    /// layers (for example an Avalonia <c>RenderTargetBitmap</c> capture or a
    /// print preview) rather than a live frame.
    /// </summary>
    /// <remarks>
    /// An off-screen frame is read-only with respect to tile scheduling: it
    /// composites whatever tiles are already cached (scaling them to its own
    /// device scale) and the live symbol/text overlay, but it never enqueues
    /// rasterisation, never changes the pending device scale, viewport or
    /// velocity the live view's workers use, and never touches GPU residency
    /// or the visible-tile eviction pins. A capture at 1x of a 2x window
    /// therefore cannot leave 1x tiles behind for the live view to blit. Scopes
    /// nest and are per thread.
    /// </remarks>
    /// <returns>A scope that ends the off-screen render when disposed.</returns>
    public static IDisposable BeginOffscreenRender()
    {
        _offscreenDepth++;
        return new OffscreenRenderScope();
    }

    private sealed class OffscreenRenderScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _offscreenDepth--;
            }
        }
    }

    /// <summary>
    /// Process-wide hot-cache native-byte budget, shared by every layer's tiles
    /// through <see cref="TileMemoryPool.Raster"/> (map rendering slice&#160;4).
    /// Sourced from <see cref="ChartSessionOptions.TileBudgetMb"/> (seeded
    /// from <c>S100_VECTOR_TILE_BUDGET_MB</c>, default by machine tier); read on
    /// every insert, so a change applies at once.
    /// </summary>
    public static long BudgetBytes => (long)(ChartEngine.Options.TileBudgetMb * 1024 * 1024);

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
    /// Padding, in DIP at the displayed resolution, around a rectangle tested
    /// against a layer's <see cref="HiddenCoverage"/> (issue #691). The coverage
    /// clip is anti-aliased, so a pixel straddling a finer coverage's edge keeps
    /// part of the coarser layer; padding by a couple of pixels keeps every such
    /// edge pixel drawn, so skipping hidden work stays pixel-identical.
    /// </summary>
    private const double HiddenMarginDip = 2.0;

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
    /// Sourced from <see cref="ChartSessionOptions.TilePredictionEnabled"/>
    /// (seeded from <c>S100_VECTOR_TILE_PREDICT</c>, default on); read every frame
    /// so a change takes effect live.
    /// </summary>
    public static bool PredictionEnabled => ChartEngine.Options.TilePredictionEnabled;

    /// <summary>
    /// Whether any layer had visible tiles waiting to be drawn within the last
    /// half second. The renderer holds its own predicted and pre-warm tiles back
    /// while this is true; hosts can hold back their own speculative work
    /// (such as loading cells ahead of the viewport) the same way.
    /// </summary>
    public static bool HasVisibleTileWork
    {
        get
        {
            var last = Volatile.Read(ref _lastVisibleTileWorkTicks);
            return last != 0
                && Stopwatch.GetElapsedTime(last).TotalSeconds <= VisibleWorkWindowSeconds;
        }
    }

    /// <summary>
    /// How long after a frame last had cold visible tiles
    /// <see cref="HasVisibleTileWork"/> still reports them: a couple of frames,
    /// enough to bridge the per-layer paints of one frame.
    /// </summary>
    private const double VisibleWorkWindowSeconds = 0.5;

    private static long _lastVisibleTileWorkTicks;

    /// <summary>
    /// The scheduler tile raster jobs run on (map rendering slice 5): the
    /// process-wide <see cref="WorkScheduler.Shared"/>, whose dedicated workers
    /// take the best job across every layer and stage. Visible tiles run
    /// before predicted ones, and those before cross-band pre-warm, with no
    /// per-layer worker floors.
    /// </summary>
    internal static IWorkScheduler Scheduler => WorkScheduler.Shared;

    private static long _lastViewChangeTicks;

    /// <summary>The <see cref="Stopwatch"/> timestamp of the last live frame at a new viewport, or 0.</summary>
    internal static long LastViewChangeTicks => Volatile.Read(ref _lastViewChangeTicks);

    /// <summary>
    /// How long ago any live frame drew a tiled layer at a new viewport (a pan,
    /// zoom, rotation, resize or a layer's first frame), or
    /// <see cref="TimeSpan.MaxValue"/> if none has. Hosts use it to keep
    /// background work such as an idle compacting collection
    /// (<see cref="IdleMemoryCompactor"/>) out of a gesture.
    /// </summary>
    public static TimeSpan SinceLastViewChange
    {
        get
        {
            var last = Volatile.Read(ref _lastViewChangeTicks);
            return last == 0 ? TimeSpan.MaxValue : Stopwatch.GetElapsedTime(last);
        }
    }

    /// <summary>
    /// Whether idle <b>cross-band pre-warm</b> (issue&#160;#428) is enabled. When
    /// on, and the tiled base plane is otherwise idle for a layer (no cold
    /// visible misses this frame and cache headroom to spare), the renderer
    /// speculatively rasterises the band&#160;±&#160;1 tiles covering the current
    /// viewport at the <em>lowest</em> worker priority (behind visible and
    /// same-band predicted work), so a subsequent zoom-in or zoom-out starts warm
    /// instead of paying full cold-tile latency at the new band. Sourced from
    /// <see cref="ChartSessionOptions.TileCrossBandPrewarmEnabled"/> (seeded
    /// from <c>S100_VECTOR_TILE_XBAND</c>, default on except off by default on the
    /// LowEnd tier — an explicit opt-in is still honoured there); read every frame
    /// so a change takes effect live. Like the same-band prediction warm set, its
    /// tiles never trigger a redraw and are cancelled (rebuilt) every frame.
    /// </summary>
    public static bool CrossBandPrewarmEnabled => ChartEngine.Options.TileCrossBandPrewarmEnabled;

    /// <summary>
    /// Whether adjacent misses may be rasterised as a 2&#215;2 metatile and
    /// sliced into tile-granular cache entries (issue&#160;#427). Sourced from
    /// <see cref="ChartSessionOptions.TileMetatileEnabled"/> and seeded by
    /// <c>S100_VECTOR_TILE_METATILE</c>; default off pending the real-data gate.
    /// </summary>
    public static bool MetatileEnabled => ChartEngine.Options.TileMetatileEnabled;

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
    /// The fraction of the process-wide hot-cache byte budget below which idle cross-band
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
    /// from <see cref="ChartSessionOptions.TileDiskCacheEnabled"/> (seeded from
    /// <c>S100_VECTOR_TILE_DISK</c>, default on); the shared cache is created once
    /// per process, so a change applies on restart. The on-disk key folds a
    /// <c>styleStateHash</c> so a tile is never served for a different
    /// mariner/palette state (design §3.4).
    /// </summary>
    public static bool DiskCacheEnabled => ChartEngine.Options.TileDiskCacheEnabled;

    /// <summary>
    /// Whether <b>GPU texture residency</b> (Phase&#160;5) is enabled. When on,
    /// and the live compositor surface is GPU-backed
    /// (<see cref="SKCanvas.Context"/> resolves to a <see cref="GRContext"/>),
    /// each warm raster tile is uploaded <i>once</i> to a GPU-resident texture
    /// (<see cref="SKImage.ToTextureImage(GRContext)"/>) and reused on every
    /// subsequent frame, instead of re-uploading the same pixels each paint —
    /// the dominant per-frame cost identified in Appendix&#160;F. Sourced from
    /// <see cref="ChartSessionOptions.TileGpuResidencyEnabled"/> (seeded from
    /// <c>S100_VECTOR_TILE_GPU</c>, default off); read every frame so a change
    /// takes effect live. On a software/CPU surface this is inert and the
    /// renderer blits the raster tile directly (universal fallback, no regression).
    /// </summary>
    public static bool GpuResidencyEnabled => ChartEngine.Options.TileGpuResidencyEnabled;

    /// <summary>
    /// Process-wide GPU-texture residency budget, in native bytes, shared by
    /// every layer's textures through <see cref="TileMemoryPool.Gpu"/> (map
    /// rendering slice&#160;4). The resident GPU texture set is bounded
    /// independently of the CPU hot cache (it holds only tiles actually blitted,
    /// so it tracks the viewport working set). Sourced from
    /// <see cref="ChartSessionOptions.TileGpuBudgetMb"/> (seeded from
    /// <c>S100_VECTOR_TILE_GPU_MB</c>, default by machine tier); read on every
    /// upload, so a change applies at once. Must comfortably exceed the visible
    /// + fallback working set of all layers or promotion thrashes (re-upload each
    /// frame).
    /// </summary>
    public static long GpuBudgetBytes => (long)(ChartEngine.Options.TileGpuBudgetMb * 1024 * 1024);

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

    /// <summary>
    /// Whether a scene whose product hints live drawing
    /// (<see cref="VectorScene.PreferLiveDrawing"/>) is drawn directly each
    /// frame instead of tiled (see <see cref="DrawsLive"/>). Seeded from
    /// <see cref="ChartSessionOptions.LiveBaseEnabled"/> (default on); off tiles every scene.
    /// </summary>
    internal static bool LiveBaseEnabled => ChartEngine.Options.LiveBaseEnabled;

    /// <summary>
    /// The most base-plane vertices a hinted scene may carry and still be drawn
    /// live: a safety valve for a dataset denser than its product's usual.
    /// Lines are clipped to the view before they are stroked, so the per-frame
    /// cost follows what is on screen, not how long the lines are.
    /// </summary>
    internal const int LiveBaseMaxVertices = 4096;

    private static long _diagLastTick;

    private static long _diagBailTick;

    /// <summary>
    /// The on-screen rotation, in degrees, to apply to the north-up tile
    /// composite so it aligns with Mapsui's rotated base map. Derived from
    /// Mapsui's own <c>WorldToScreenXY</c> projection (which applies the viewport
    /// rotation internally) rather than hardcoding a sign convention: the screen
    /// direction of world-north is measured and compared against north-up's
    /// straight-up (-90&#176;). Returns 0 for an unrotated viewport.
    /// </summary>
    private static double ScreenRotationDegrees(ViewState viewport)
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
        var last = Interlocked.Read(ref _diagBailTick);
        if (last != 0 && now - last < 1000)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _diagBailTick, now, last) != last)
        {
            return;
        }

        Console.Error.WriteLine($"[S100.DIAG] Render bailed (layer draws nothing): {reason}");
    }


    /// <summary>
    /// Process-wide graceful-shutdown gate for the background tile-rasterisation
    /// workers. Tile workers call into native Skia; if the process begins
    /// tearing down — the managed runtime running the C++ <c>__cxa_finalize</c>
    /// destructors of <c>libSkiaSharp</c> — while a worker is mid-rasterise, the
    /// worker dereferences freed Skia globals and the process dies with a native
    /// SIGSEGV (first observed on a fast headless quit; latent on any quit).
    /// The host MUST call
    /// <see cref="ShutdownAndDrain"/> before letting the process exit. See
    /// <see cref="WorkerDrainGate"/>.
    /// </summary>
    private static readonly WorkerDrainGate DrainGate = new();

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
        DrainGate.DrainAndWait(timeout);

    private static readonly ConditionalWeakTable<object, TileState> States = new();

    /// <summary>
    /// Process-wide registry of every live GPU-texture residency cache, keyed
    /// weakly by its owning owner token (Phase&#160;5). Each entry holds
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
    private static readonly List<GpuRegistryEntry> GpuRegistry = new();

    /// <summary>
    /// A single layer's pinned GPU residency: its texture cache and — mirrored
    /// from the owning <see cref="TileState"/> in lockstep — its current rotated
    /// off-screen composite. The mirror exists purely to keep those GPU objects
    /// strongly reachable (off the weakly-held <see cref="TileState"/>) so they
    /// survive to be disposed on the render thread rather than the finalizer
    /// thread. See <see cref="GpuRegistry"/>.
    /// </summary>
    internal sealed class GpuRegistryEntry
    {
        public required WeakReference<object> Layer;
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


    private static readonly object GpuRegistrySync = new();

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    // Reusable display-list renderer for the live symbol/text overlay. Render is
    // single-threaded (Mapsui's render thread), so one shared, stateless
    // instance is safe. Transparent background + scale-visibility so SCAMIN is
    // honoured against the live viewport's denominator.
    private static readonly SkiaDisplayListRenderer OverlayRenderer = new()
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
    private static readonly LabelDeclutterer OverlayLabelDeclutterer = new();

    private static readonly Lazy<TileDiskCache?> DiskCache = new(CreateSharedDiskCache);

    /// <summary>
    /// The process-wide warm disk cache, or <see langword="null"/> when disabled
    /// or its root directory could not be established. Shared across every layer
    /// and session.
    /// </summary>
    private static TileDiskCache? SharedDiskCache => DiskCache.Value;

    /// <summary>
    /// The root of the content store the engine creates for its warm tiles
    /// when the host gives it none (<see cref="ChartEngine.ContentStore"/>):
    /// the <see cref="ChartSessionOptions.TileDiskDirectory"/> override
    /// (env var or host-assigned), or an OS-temp folder when unset. Tiles go in
    /// its <c>tiles</c> kind folder.
    /// </summary>
    public static string ResolveTileDiskDirectory() =>
        string.IsNullOrEmpty(ChartEngine.Options.TileDiskDirectory)
            ? Path.Combine(Path.GetTempPath(), "encdotnet-s100")
            : ChartEngine.Options.TileDiskDirectory!;

    private static TileDiskCache? CreateSharedDiskCache()
    {
        if (!DiskCacheEnabled)
        {
            return null;
        }

        try
        {
            var store = ChartEngine.ContentStore;
            if (store is null)
            {
                var own = new ContentStore(ResolveTileDiskDirectory());
                AppDomain.CurrentDomain.ProcessExit += (_, _) => own.Dispose();
                store = own;
            }

            var kind = store.Kinds.FirstOrDefault(k => k.Name == TileDiskCache.KindName)
                ?? store.Open(
                    TileDiskCache.KindName,
                    (long)(ChartEngine.Options.TileDiskMb * 1024 * 1024),
                    TileDiskCache.KindOptions);
            return new TileDiskCache(kind);
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

    /// <summary>
    /// Binds the fully-resolved <see cref="VectorScene"/> for a layer and
    /// invalidates its tile cache (a new generation), so the next frame
    /// re-rasterises from the new scene. Equivalent to calling
    /// <see cref="BindScene(object, VectorScene, string?, string?)"/> without a
    /// disk-cache key (the warm disk cache is bypassed for this layer).
    /// </summary>
    public static void BindScene(object layer, VectorScene scene) =>
        BindScene(layer, scene, productLayerSet: null, styleStateHash: null);

    /// <summary>
    /// Binds the fully-resolved <see cref="VectorScene"/> for a layer and
    /// invalidates its tile cache (a new generation), so the next frame
    /// re-rasterises from the new scene.
    /// </summary>
    /// <param name="layer">The owner token of the layer this scene portrays (see <see cref="LayerFrame.Owner"/>).</param>
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
    public static void BindScene(object layer, VectorScene scene, string? productLayerSet, string? styleStateHash)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(scene);

        var diskNamespace =
            DiskCacheEnabled
            && !string.IsNullOrEmpty(productLayerSet)
            && !string.IsNullOrEmpty(styleStateHash)
                ? TileDiskCache.NamespaceFor(productLayerSet, styleStateHash)
                : null;

        var state = States.GetValue(layer, static l => new TileState(l));
        lock (state.Sync)
        {
            var (baseScene, overlayScene) = PartitionScene(scene);
            state.Scene = baseScene;
            state.LiveBase = DrawsLive(scene, baseScene);
            state.BaseIndex = new BaseSpatialIndex(baseScene);
            state.Overlay = new OverlaySource(overlayScene);
            state.Generation++;
            state.DiskNamespace = diskNamespace;
            state.Cache.Clear();
            state.InFlight.Clear();
            state.PendingVisible.Clear();
            state.PendingPredicted.Clear();
            state.PendingCrossBand.Clear();
            ReleaseTileJobs(state);
            state.CurrentVisible.Clear();
            state.CurrentSpeculative.Clear();
            state.CurrentViewport = null;
            state.ViewportEpoch++;
            state.PredictedInCache.Clear();
            state.PredictedCentres.Clear();
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
    /// via <see cref="BindScene(object, VectorScene, string?, string?)"/>.
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
        object layer, out VectorScene baseScene, out VectorScene overlayScene)
    {
        ArgumentNullException.ThrowIfNull(layer);

        if (States.TryGetValue(layer, out var state))
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

        return (
            new VectorScene(baseOps) { ScaleMinimumCap = scene.ScaleMinimumCap, PreferLiveDrawing = scene.PreferLiveDrawing },
            new VectorScene(overlayOps) { ScaleMinimumCap = scene.ScaleMinimumCap, PreferLiveDrawing = scene.PreferLiveDrawing });
    }

    /// <summary>
    /// The render handler Mapsui invokes for tagged layers. Composites the best
    /// available tiles for the live viewport and enqueues missing visible tiles.
    /// A layer whose <see cref="LayerFrame.Opacity"/> is below 1 (a stale surface
    /// analysis, drawn at half opacity) is drawn through one alpha layer, so
    /// its overlapping tiles, lines and labels fade together.
    /// </summary>
    public static void Render(SKCanvas canvas, ViewState viewport, LayerFrame layer)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layer.Owner);

        var opacity = layer.Opacity;
        if (opacity >= 1.0)
        {
            RenderCore(canvas, viewport, layer.Owner, layer.Extent, layer.Name);
            return;
        }

        if (opacity <= 0.0)
        {
            return;
        }

        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(opacity * 255)) };
        canvas.SaveLayer(paint);
        try
        {
            RenderCore(canvas, viewport, layer.Owner, layer.Extent, layer.Name);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static void RenderCore(SKCanvas canvas, ViewState viewport, object layer, WorldRect? layerExtent, string? layerName)
    {
        var offscreen = IsOffscreenRender;
        if (!viewport.HasSize)
        {
            if (!offscreen)
            {
                InvalidateViewport(layer);
            }

            return;
        }

        var resolution = viewport.Resolution;
        if (resolution <= 0)
        {
            if (DiagEnabled)
            {
                DiagBail($"resolution={resolution:F3}");
            }

            if (!offscreen)
            {
                InvalidateViewport(layer);
            }

            return;
        }

        var deviceScale = canvas.TotalMatrix.ScaleX;
        if (deviceScale <= 0 || float.IsNaN(deviceScale))
        {
            deviceScale = 1f;
        }

        // The device-pixel edge of a tile rasterised for this frame. A cached
        // tile of another size (rasterised at another device scale) is still
        // drawn as a placeholder but counts as a miss, so it is replaced.
        var tilePx = TilePixelSize(deviceScale);

        // Skip a cell whose data extent lies entirely outside the viewport.
        // Mapsui invokes this custom renderer for every enabled, in-resolution
        // layer each frame without extent-culling (VisibleFeatureIterator only
        // filters Enabled/Min/MaxVisible), so an exchange set of many S-101 cells
        // would otherwise run the full per-frame path — empty-tile scheduling,
        // GPU residency, composite, live overlay, and a redraw on every off-view
        // worker publish — once per off-view cell. Culling here makes off-view
        // cells cost nothing. The CullMarginPx halo keeps edge cells (whose
        // symbols/over-render reach into the view) rendering.
        //
        // The cell is drawn at every world copy that meets the view (issue
        // #773), so data kept in a 0…360 or other continuous longitude frame
        // shows wherever the viewer pans, like the basemap. Each copy is the
        // same data-space tiles blitted at a shifted centre, so a copy costs a
        // blit, not a raster.
        var state = States.GetValue(layer, static l => new TileState(l));
        var copies = LayerExtentCulling.VisibleWorldCopies(layerExtent, viewport, resolution, CullMarginPx);
        if (copies.Count == 0)
        {
            if (!offscreen && InvalidateViewport(state))
            {
                LayerRepaint.Request(layer);
            }

            RecordSettleCulled(state, viewport, resolution, offscreen);
            return;
        }

        // Tiles are rasterised no coarser than the scene's out-of-band cap (see
        // TileScaleDenominator), so the cap itself is applied here, against the
        // live display scale, exactly where the status bar reads it (#761).
        if (IsPastScaleMinimumCap(state.Scene, viewport.CenterX, viewport.CenterY, resolution))
        {
            if (!offscreen && InvalidateViewport(state))
            {
                LayerRepaint.Request(layer);
            }

            RecordSettleCulled(state, viewport, resolution, offscreen);
            return;
        }

        // Skip a coarser cell that a finer, currently-drawing cell hides across
        // the whole viewport (issue #691). Its coverage-clip difference would
        // erase every pixel it draws (tiles and live overlay alike), so treat it
        // as culled. Null (no finer coverage active) skips nothing.
        var hidden = CoverageClip.GetHiddenCoverage(layer, resolution);
        if (hidden is not null && AreAllCopiesHidden(hidden, viewport, resolution, copies))
        {
            S100Diag.Telemetry.TileLayerHiddenSkipped.Add(1);
            if (!offscreen && InvalidateViewport(state))
            {
                LayerRepaint.Request(layer);
            }

            RecordSettleCulled(state, viewport, resolution, offscreen);
            return;
        }

        // Resolve the live GPU context from the compositor canvas (null on a
        // software/CPU surface or when residency is disabled). Phase 5: warm
        // tiles are promoted to GPU-resident textures so identical pixels are
        // not re-uploaded every frame (Appendix F).
        // An off-screen frame never touches GPU residency: its surface (if GPU
        // at all) is not the live context, and a CPU surface would otherwise
        // drop the live view's resident textures. It blits the raster tiles.
        var grContext = state.Options.TileGpuResidencyEnabled && !offscreen ? (canvas.Context as GRContext) : null;

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

        // A sparse overlay its product hints for live drawing (an S-421 route)
        // is drawn directly: a collection of many such datasets would otherwise
        // rasterise, cache and blit a stack of mostly transparent tiles per
        // dataset every frame.
        if (TryRenderLive(canvas, viewport, layer, state, copies, centerX, centerY, widthDip, heightDip, coverWidth, coverHeight, resolution, rotationDeg, deviceScale))
        {
            // Drawn live, straight from the scene: nothing waits on tiles.
            RecordSettleCulled(state, viewport, resolution, offscreen);
            return;
        }

        // The copy whose data lies nearest the view centre orders the tile queue.
        var primaryOffset = copies.Count == 1 || layerExtent is not { } extent
            ? copies[0]
            : WorldCopies.Nearest(copies, extent.MinX, extent.MaxX, centerX);

        var coldExposure = 0;
        var visibleQueueDepth = 0;
        var predictionHits = 0L;
        var predictionWaste = 0L;
        var slotHits = 0L;
        var slotMisses = 0L;
        Prediction.DemandPlanner? planner = null;
        List<(string Namespace, long Generation, TileKey Key)>? predictionHitsToPersist = null;
        var compositeStart = Stopwatch.GetTimestamp();
        var compositeLockAcquired = compositeStart;
        var compositePaintStart = compositeStart;
        var compositeGpuEnd = compositeStart;
        var compositeClipEnd = compositeStart;
        var compositeBaseEnd = compositeStart;
        var compositeOverlayEnd = compositeStart;
        lock (state.Sync)
        {
            compositeLockAcquired = Stopwatch.GetTimestamp();
            var chart = state.Chart;
            if (state.Scene is null && chart is null)
            {
                return;
            }

            // A chart layer's slots are planned against the live scale at the
            // view centre, where each layer's out-of-band cap is applied (#761).
            var liveDenominator = chart is null
                ? 0.0
                : S100VectorSceneRenderer.ScaleDenominatorFor(centerX, centerY, resolution);

            // Only tiles that intersect this cell's base content can draw
            // anything; the rest would rasterise to pure transparency. Dropping
            // them keeps raster, cache, disk and per-frame blit work bounded by
            // the part of the viewport this cell covers. Tiles wholly under
            // finer, currently-drawing coverage are dropped too: the coverage
            // clip would erase them (issue #691). Each world copy contributes
            // the data-space tiles under its part of the view; scheduling works
            // on their union.
            //
            // Each tile is keyed by its SCAMIN scale class at the live scale
            // (issue #774), so an op's own scale limits hold at the live scale
            // rather than the band's. The live-to-band resolution ratio is the
            // same for every tile, and is kept for speculative tiles.
            var bandRatio = resolution / TileGrid.ResolutionForBand(band);
            var copyTiles = new CopyTiles[copies.Count];
            for (var i = 0; i < copies.Count; i++)
            {
                var grid = TileGrid.VisibleTiles(centerX - copies[i], centerY, coverWidth, coverHeight, resolution, band);
                var tiles = chart is not null
                    ? ChartTiles(state, chart, grid, bandRatio, resolution, liveDenominator)
                    : WithScaleClasses(state.Scene!, WithContent(state, grid, hidden), bandRatio);
                copyTiles[i] = new CopyTiles(
                    copies[i],
                    copies.Count > 1 && layerExtent is { } dataExtent
                        ? WithCoreInExtent(tiles, dataExtent.MinX, dataExtent.MaxX, TileGrid.ResolutionForBand(band))
                        : tiles);
            }

            var visible = UnionTiles(copyTiles);

            // An off-screen render (BeginOffscreenRender: a capture or print
            // through the live layers, typically at another device scale) only
            // composites what is cached. Scheduling would enqueue tiles at its
            // scale and overwrite the live view's pending scale, viewport and
            // velocity, leaving off-scale tiles for the live view to blit.
            if (!offscreen)
            {
                var currentViewport = new TileViewport(
                    centerX,
                    centerY,
                    coverWidth,
                    coverHeight,
                    resolution,
                    deviceScale);
                if (state.CurrentViewport != currentViewport)
                {
                    Volatile.Write(ref _lastViewChangeTicks, Stopwatch.GetTimestamp());
                }

                state.ViewportEpoch = NextViewportEpoch(
                    state.ViewportEpoch,
                    state.CurrentViewport,
                    currentViewport);
                state.CurrentViewport = currentViewport;

                UpdateVelocity(state, centerX, centerY);

                // Enqueue visible misses at high priority (replace the pending set
                // every frame so tiles panned out of view are dropped — cancellation).
                state.PendingVisible.Clear();
                var frameTicks = Stopwatch.GetTimestamp();
                var visibleSet = new HashSet<TileKey>(visible.Count);
                state.CurrentVisible.Clear();
                (state.ShownSlots, state.PreviousShownSlots) = (state.PreviousShownSlots, state.ShownSlots);
                state.ShownSlots.Clear();
                foreach (var key in visible)
                {
                    visibleSet.Add(key);
                    state.CurrentVisible.Add(key);
                    var cached = state.Cache.Contains(key, tilePx);
                    if (state.ShownSlots.Add(key.Slot) && !state.PreviousShownSlots.Contains(key.Slot))
                    {
                        // A slot that just became visible: a hit when its
                        // tile is already resident (map rendering slice 9).
                        if (cached)
                        {
                            slotHits++;
                        }
                        else
                        {
                            slotMisses++;
                        }
                    }

                    if (cached)
                    {
                        // A tile we rasterised speculatively is now actually visible:
                        // a prediction hit. Count it once.
                        if (state.PredictedInCache.Remove(key))
                        {
                            predictionHits++;
                            if (state.DiskNamespace is not null
                                && (chart is null || (state.Plans.TryGetValue(key, out var plan) && plan.Persistable)))
                            {
                                (predictionHitsToPersist ??= []).Add(
                                    (TileDiskCache.NamespaceFor(state.DiskNamespace, tilePx),
                                        state.Generation, key));
                            }
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

                if (coldExposure > 0)
                {
                    Volatile.Write(ref _lastVisibleTileWorkTicks, frameTicks);
                }

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
                state.CurrentSpeculative.Clear();
                if (state.Options.TilePredictionEnabled)
                {
                    foreach (var gridKey in SpeculativeTiles(copies, offset => TileGrid.PredictedTiles(
                        centerX - offset, centerY, coverWidth, coverHeight, resolution, band,
                        state.VelocityX, state.VelocityY)))
                    {
                        TileKey key;
                        if (chart is not null)
                        {
                            if (!TryChartTile(state, chart, gridKey, bandRatio, resolution, liveDenominator, out key))
                            {
                                continue;
                            }
                        }
                        else if (!TileHasContent(state, gridKey) || IsTileHidden(state, gridKey, hidden))
                        {
                            continue;
                        }
                        else
                        {
                            key = WithScaleClass(state.Scene!, gridKey, bandRatio);
                        }

                        state.CurrentSpeculative.Add(key);
                        if (!visibleSet.Contains(key)
                            && !state.Cache.Contains(key, tilePx)
                            && !state.InFlight.Contains(key))
                        {
                            state.PendingPredicted.Add(key);
                        }
                    }

                    // The planner's views a second ahead (map rendering
                    // slice 9): a fling's resting viewport, an animation's
                    // target, and the motion extrapolated with its zoom rate.
                    state.PredictedCentres.Clear();
                    // Not while the view still waits for cells: each one that
                    // lands changes the planned slots, so their tiles would be
                    // drawn again for every stack version.
                    if (chart is not null && state.Planner is { } tilePlanner && tilePlanner.Arrivals.Pending == 0)
                    {
                        AddPlannedTiles(state, chart, tilePlanner.Plan(frameTicks), copies, visibleSet, tilePx);
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
                if (state.Options.TileCrossBandPrewarmEnabled
                    && coldExposure == 0
                    && state.Cache.Pool.ResidentBytes < (long)(state.Cache.Pool.LimitBytes * CrossBandPrewarmHeadroomFraction))
                {
                    foreach (var gridKey in SpeculativeTiles(copies, offset => TileGrid.CrossBandPrewarmTiles(
                        centerX - offset, centerY, coverWidth, coverHeight, resolution, band,
                        CrossBandPrewarmMaxTiles)))
                    {
                        // Zooming in only adds active finer coverages, so a tile of a
                        // finer band hidden now stays hidden there. Zooming out can
                        // drop them, so coarser-band tiles are never skipped.
                        // A zoom step keeps the live-to-band ratio, so warm the
                        // class the neighbouring band would be shown at.
                        TileKey key;
                        if (chart is not null)
                        {
                            var bandResolution = TileGrid.ResolutionForBand(gridKey.Band) * bandRatio;
                            if (!TryChartTile(
                                    state, chart, gridKey, bandRatio, bandResolution,
                                    S100VectorSceneRenderer.ScaleDenominatorFor(centerX, centerY, bandResolution),
                                    out key))
                            {
                                continue;
                            }
                        }
                        else if (!TileHasContent(state, gridKey)
                            || (gridKey.Band > band && IsTileHidden(state, gridKey, hidden)))
                        {
                            continue;
                        }
                        else
                        {
                            key = WithScaleClass(state.Scene!, gridKey, bandRatio);
                        }

                        state.CurrentSpeculative.Add(key);
                        // Also exclude keys already queued in a higher tier this frame:
                        // the band ± 1 centre tiles overlap TileGrid.PredictedTiles, so
                        // without this guard the same key would sit in both PendingPredicted
                        // and PendingCrossBand and be rasterised twice (predicted tier first,
                        // then cross-band), double-counting TilePredictionRasterized and
                        // wasting CPU / disk writes (issue #428 review follow-up).
                        if (!state.Cache.Contains(key, tilePx)
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
                    state.PendingCenterX = centerX - primaryOffset;
                    state.PendingCenterY = centerY;
                    // The pending sets are this layer's interests in scheduler
                    // jobs: one per tile, at the tile's class (visible, predicted
                    // or cross-band pre-warm) and its distance from the view
                    // centre. Tiles that left the sets drop their interest, which
                    // cancels them if they haven't started.
                }

                SyncTileJobs(state, resolution);
                if (chart is not null)
                {
                    PrunePlans(state);
                }

                // Bound the prediction-hit bookkeeping: a tile predicted then
                // evicted before it was ever shown would otherwise linger. When the
                // set grows past the cache's own capacity, drop keys no longer
                // resident (those can never score a hit).
                if (state.PredictedInCache.Count > state.Cache.Count + 256
                    || frameTicks - state.LastPredictionSweep > PredictionSweepTicks)
                {
                    state.LastPredictionSweep = frameTicks;
                    predictionWaste = SweepPredicted(state, frameTicks);
                }

                planner = state.Planner;
            }

            // Phase 5 GPU residency (render thread only): first dispose any
            // GPU-texture caches whose owning layer has been torn down (under
            // the live context), then reconcile this layer's cache with the
            // current context + scene generation before compositing, so stale
            // textures are never blitted.
            //
            // Guard the whole paint block: a throw here would escape the frame
            // and drop its telemetry and repaint requests. A dropped frame is
            // always recoverable.
            compositePaintStart = Stopwatch.GetTimestamp();
            compositeGpuEnd = compositePaintStart;
            compositeClipEnd = compositePaintStart;
            compositeBaseEnd = compositePaintStart;
            compositeOverlayEnd = compositePaintStart;
            try
            {
                if (grContext is not null)
                {
                    ReconcileGpuCaches(grContext);
                }

                if (!offscreen)
                {
                    ManageGpuResidency(state, grContext, layer);
                }

                compositeGpuEnd = Stopwatch.GetTimestamp();

                // Cross-cell overlap suppression (issue #438 Phase 2): remove
                // from this coarser cell's drawable region the coverage of every
                // finer overlapping cell that is still visible at the live
                // resolution (screen-space, so tiles stay cached; zoom-aware, so
                // a finer cell that has dropped out of its band no longer leaves
                // a blank hole).
                var clipPaths = BuildCopyClipPaths(layer, viewport, resolution);
                compositeClipEnd = Stopwatch.GetTimestamp();
                var clipApplied = false;
                try
                {
                    // Apply the clip inside the try so that if ClipPath throws
                    // (e.g. a degenerate path) the finally still restores the
                    // canvas and disposes every path.
                    if (clipPaths.Count > 0)
                    {
                        canvas.Save();
                        clipApplied = true;
                        foreach (var clipPath in clipPaths)
                            canvas.ClipPath(clipPath, SKClipOperation.Difference, antialias: true);
                    }

                    var outcome = Composite(canvas, state, band, copyTiles, visible, centerX, centerY, widthDip, heightDip, coverWidth, coverHeight, resolution, rotationDeg, grContext, pinVisible: !offscreen);
                    compositeBaseEnd = Stopwatch.GetTimestamp();
                    if (!offscreen && _settleTracking)
                    {
                        RecordSettleComposite(state, viewport, resolution, visible, outcome.TargetExact, tilePx);
                    }

                    // Draw point symbols + soundings live, on top of the composited
                    // base tiles, at constant on-screen size (the base tiles are
                    // band-scaled, so symbols must not be baked into them). Each
                    // world copy draws (and declutters) its own overlay. A cell
                    // whose base plane has not appeared yet keeps its symbols
                    // back too, so they appear with its fills, not before them.
                    if (chart is not null)
                    {
                        DrawChartOverlay(
                            canvas, chart, offscreen ? null : outcome.Unrevealed, viewport, copies,
                            centerX, centerY, widthDip, heightDip, resolution, rotationDeg, deviceScale, liveDenominator);
                    }
                    else if (offscreen || outcome.ShowsBase)
                    {
                        foreach (var offset in copies)
                        {
                            DrawOverlay(canvas, state.Overlay, centerX - offset, centerY, widthDip, heightDip, resolution, rotationDeg, deviceScale);
                        }
                    }

                    compositeOverlayEnd = Stopwatch.GetTimestamp();
                }
                finally
                {
                    // Restore only if Save() actually ran, so a throw between
                    // Save() and the first ClipPath still balances the stack.
                    if (clipApplied)
                        canvas.Restore();
                    foreach (var clipPath in clipPaths)
                        clipPath.Dispose();
                }
            }
            catch (Exception ex)
            {
                S100Diag.Telemetry.RecordRenderFault(ex);
            }
        }

        var compositeEnd = Stopwatch.GetTimestamp();
        var compositeDuration = Stopwatch.GetElapsedTime(
            compositeStart,
            compositeEnd).TotalMilliseconds;
        S100Diag.Telemetry.TileCompositeDuration.Record(compositeDuration);
        if (compositeDuration >= 50)
        {
            var capturedAtUtc = DateTimeOffset.UtcNow;
            using var activity = S100Diag.Telemetry.ActivitySource.StartActivity(
                "s100.render.tile.composite.slow",
                ActivityKind.Internal,
                default(ActivityContext),
                startTime: capturedAtUtc - TimeSpan.FromMilliseconds(compositeDuration));
            activity?.SetTag("s100.render.layer", layerName);
            activity?.SetTag("s100.render.tile.composite.duration_ms", compositeDuration);
            activity?.SetTag(
                "s100.render.tile.composite.lock_wait_ms",
                Stopwatch.GetElapsedTime(
                    compositeStart,
                    compositeLockAcquired).TotalMilliseconds);
            activity?.SetTag(
                "s100.render.tile.composite.locked_ms",
                Stopwatch.GetElapsedTime(
                    compositeLockAcquired,
                    compositeEnd).TotalMilliseconds);
            activity?.SetTag(
                "s100.render.tile.composite.setup_ms",
                Stopwatch.GetElapsedTime(
                    compositeLockAcquired,
                    compositePaintStart).TotalMilliseconds);
            activity?.SetTag(
                "s100.render.tile.composite.gpu_ms",
                Stopwatch.GetElapsedTime(
                    compositePaintStart,
                    compositeGpuEnd).TotalMilliseconds);
            activity?.SetTag(
                "s100.render.tile.composite.clip_ms",
                Stopwatch.GetElapsedTime(
                    compositeGpuEnd,
                    compositeClipEnd).TotalMilliseconds);
            activity?.SetTag(
                "s100.render.tile.composite.base_ms",
                Stopwatch.GetElapsedTime(
                    compositeClipEnd,
                    compositeBaseEnd).TotalMilliseconds);
            activity?.SetTag(
                "s100.render.tile.composite.overlay_ms",
                Stopwatch.GetElapsedTime(
                    compositeBaseEnd,
                    compositeOverlayEnd).TotalMilliseconds);
            activity?.SetTag("s100.render.tile.cold_exposure", coldExposure);
            activity?.SetTag("s100.render.tile.visible_queue_depth", visibleQueueDepth);
        }
        if (predictionHitsToPersist is not null && SharedDiskCache is { } disk)
        {
            foreach (var item in predictionHitsToPersist)
            {
                disk.TryQueueDeferredSnapshot(
                    item.Namespace,
                    item.Key,
                    () => TryCreateVisibleTileSnapshot(
                        state,
                        item.Key,
                        item.Generation,
                        tilePx),
                    () => IsTileVisible(
                        state,
                        item.Key,
                        item.Generation));
            }
        }
        if (!offscreen)
        {
            S100Diag.Telemetry.TileColdExposure.Record(coldExposure);
        }

        if (visibleQueueDepth > 0)
        {
            S100Diag.Telemetry.TileVisibleQueueDepth.Record(visibleQueueDepth);
        }
        if (predictionHits > 0)
        {
            S100Diag.Telemetry.TilePredictionHits.Add(predictionHits);
        }

        if (planner is not null)
        {
            planner.Stats.RecordSlots(slotHits, slotMisses);
            if (predictionHits > 0)
            {
                planner.Stats.RecordTilesUsed(predictionHits);
            }

            if (predictionWaste > 0)
            {
                planner.Stats.RecordTilesWasted(predictionWaste);
            }
        }
    }

    /// <summary>
    /// Whether the scene bound to a layer is drawn live: live drawing is on and
    /// <see cref="IsLiveBaseScene"/> accepts its base plane. A hinted scene that
    /// is tiled anyway is counted, so how often a hinted product outgrows the
    /// budget can be measured before the hint is given to more products.
    /// </summary>
    private static bool DrawsLive(VectorScene scene, VectorScene baseScene)
    {
        if (!LiveBaseEnabled || !scene.PreferLiveDrawing)
        {
            return false;
        }

        if (IsLiveBaseScene(baseScene))
        {
            return true;
        }

        S100Diag.Telemetry.TileLiveBaseFallback.Add(1);
        return false;
    }

    /// <summary>
    /// Whether <paramref name="baseScene"/>'s product hints live drawing
    /// (<see cref="VectorScene.PreferLiveDrawing"/>) and it is sparse enough to
    /// draw every frame: no pattern fills (their clip and tile decode is the
    /// costly part of a raster) and at most <see cref="LiveBaseMaxVertices"/>
    /// vertices across its lines and areas.
    /// </summary>
    internal static bool IsLiveBaseScene(VectorScene baseScene)
    {
        if (!baseScene.PreferLiveDrawing)
        {
            return false;
        }

        var vertices = 0;
        foreach (var op in baseScene.Ops)
        {
            switch (op)
            {
                case LinePaintOp line:
                    // Live drawing strokes plain lines only; symbols along a
                    // line (S-100 Part 9 line styles) need the tile renderer.
                    if (line.Symbols.Count > 0 || !line.Stroke)
                        return false;
                    vertices += line.World.Count;
                    break;
                case AreaPaintOp area:
                    vertices += area.WorldShell.Count;
                    foreach (var hole in area.WorldHoles)
                    {
                        vertices += hole.Count;
                    }

                    break;
                default:
                    return false;
            }

            if (vertices > LiveBaseMaxVertices)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Draws a layer whose scene <see cref="IsLiveBaseScene">is drawn live</see>:
    /// the base plane straight onto the canvas at the live scale, then the
    /// overlay, at every world copy and under the same coverage clip as tiles.
    /// No tile is scheduled, cached or blitted.
    /// </summary>
    /// <returns><see langword="false"/> when the layer is tiled instead.</returns>
    private static bool TryRenderLive(
        SKCanvas canvas, ViewState viewport, object layer, TileState state, IReadOnlyList<double> copies,
        double centerX, double centerY, double widthDip, double heightDip,
        double coverWidth, double coverHeight, double resolution, double rotationDeg, float deviceScale)
    {
        lock (state.Sync)
        {
            if (!state.LiveBase || state.Scene is not { } scene)
            {
                return false;
            }

            // Like the tiled composite, a throw here costs one frame, not the layer.
            try
            {
                var clipPaths = BuildCopyClipPaths(layer, viewport, resolution);
                var clipApplied = false;
                try
                {
                    if (clipPaths.Count > 0)
                    {
                        canvas.Save();
                        clipApplied = true;
                        foreach (var clipPath in clipPaths)
                            canvas.ClipPath(clipPath, SKClipOperation.Difference, antialias: true);
                    }

                    foreach (var offset in copies)
                    {
                        DrawLiveBase(canvas, scene, centerX - offset, centerY, widthDip, heightDip, coverWidth, coverHeight, resolution, rotationDeg, deviceScale);
                    }

                    foreach (var offset in copies)
                    {
                        DrawOverlay(canvas, state.Overlay, centerX - offset, centerY, widthDip, heightDip, resolution, rotationDeg, deviceScale);
                    }
                }
                finally
                {
                    if (clipApplied)
                        canvas.Restore();
                    foreach (var clipPath in clipPaths)
                        clipPath.Dispose();
                }
            }
            catch (Exception ex)
            {
                S100Diag.Telemetry.RecordRenderFault(ex);
            }
        }

        return true;
    }

    /// <summary>
    /// Draws <paramref name="scene"/>'s base plane live for one world copy. A
    /// rotated view draws north-up over the rotated viewport's bounding box and
    /// turns the canvas about the screen centre, as the tiled composite does.
    /// </summary>
    private static void DrawLiveBase(
        SKCanvas canvas, VectorScene scene,
        double centerX, double centerY, double widthDip, double heightDip,
        double coverWidth, double coverHeight, double resolution, double rotationDeg, float deviceScale)
    {
        if (scene.Ops.Count == 0)
        {
            return;
        }

        var halfWorldW = coverWidth * resolution * 0.5;
        var halfWorldH = coverHeight * resolution * 0.5;
        // Lossless (unclamped) inverse, as in DrawOverlay, so WorldToScreen maps
        // back to these exact EPSG:3857 bounds.
        var (minLon, minLat) = WebMercator.ToLonLat(centerX - halfWorldW, centerY - halfWorldH, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(centerX + halfWorldW, centerY + halfWorldH, clampLatitude: false);

        // Tiles are rasterised in device pixels (RasterizeTile), so base-plane
        // widths and dashes are device pixels; project in device pixels too so
        // a live layer looks the same as a tiled one.
        var coverViewport = new CoreViewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = Math.Max(1, (int)Math.Round(coverWidth * deviceScale)),
            HeightPixels = Math.Max(1, (int)Math.Round(coverHeight * deviceScale)),
            ScaleDenominator = S100VectorSceneRenderer.ScaleDenominatorFor(centerX, centerY, resolution),
        };

        canvas.Save();
        if (rotationDeg != 0)
        {
            canvas.RotateDegrees((float)rotationDeg, (float)(widthDip * 0.5), (float)(heightDip * 0.5));
        }

        canvas.Translate((float)((widthDip - coverWidth) * 0.5), (float)((heightDip - coverHeight) * 0.5));
        canvas.Scale(1f / deviceScale);
        OverlayRenderer.RenderOnto(canvas, scene, coverViewport);
        canvas.Restore();
    }

    internal static long NextViewportEpoch(
        long currentEpoch,
        TileViewport? previous,
        TileViewport current) =>
        previous == current ? currentEpoch : currentEpoch + 1;

    internal static TileRelevance ClassifyTileRelevance(
        bool generationMatches,
        bool isVisible,
        bool isSpeculative) =>
        generationMatches
            ? isVisible
                ? TileRelevance.Visible
                : isSpeculative
                    ? TileRelevance.Speculative
                    : TileRelevance.Irrelevant
            : TileRelevance.Irrelevant;

    private static void InvalidateViewport(object layer)
    {
        if (States.TryGetValue(layer, out var state)
            && InvalidateViewport(state))
        {
            LayerRepaint.Request(layer);
        }
    }

    /// <summary>
    /// Routes a worker-thread publish to the owning layer's redraw sink. The
    /// layer is held weakly (it is this state's key in <see cref="States"/>, so a
    /// strong reference would pin the entry); a collected layer yields no repaint.
    /// </summary>
    private static void RequestRepaint(TileState state)
    {
        if (state.Owner.TryGetTarget(out var layer))
        {
            LayerRepaint.Request(layer);
        }
    }

    /// <summary>
    /// Whether <paramref name="key"/>'s tile (+ gutter) intersects any base op of
    /// the bound scene. A tile with no candidate ops rasterises to pure
    /// transparency, so it is never scheduled, cached or blitted. Mirrors the
    /// <see cref="RasterizeTile"/> query bounds exactly, so skipping is
    /// pixel-identical. Conservatively <see langword="true"/> before a scene (and
    /// its index) is bound. Call under <c>state.Sync</c>.
    /// </summary>
    private static bool TileHasContent(TileState state, TileKey key)
    {
        if (state.BaseIndex is not { } index)
        {
            return true;
        }

        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var gutterWorld = GutterDip * TileGrid.ResolutionForBand(key.Band);
        return index.Intersects(minX - gutterWorld, minY - gutterWorld, maxX + gutterWorld, maxY + gutterWorld);
    }

    /// <summary>
    /// Whether <paramref name="key"/>'s tile core, padded by
    /// <see cref="HiddenMarginDip"/>, lies wholly inside
    /// <paramref name="hidden"/>, so the coverage clip would erase every pixel
    /// it blits (issue #691). The padding uses the coarsest resolution the tile's
    /// band is displayed at, so the answer holds across the band and is memoised
    /// per tile until the hidden region changes. Call under <c>state.Sync</c>.
    /// </summary>
    private static bool IsTileHidden(TileState state, TileKey key, HiddenCoverage? hidden)
    {
        if (hidden is null)
        {
            return false;
        }

        if (!ReferenceEquals(state.HiddenMemoOwner, hidden)
            || state.HiddenMemo.Count >= HiddenMemoCapacity)
        {
            state.HiddenMemo.Clear();
            state.HiddenMemoOwner = hidden;
        }

        if (state.HiddenMemo.TryGetValue(key, out var memo))
        {
            return memo;
        }

        var isHidden = IsTileCoreHidden(hidden, key);
        state.HiddenMemo[key] = isHidden;
        return isHidden;
    }

    /// <summary>
    /// Whether <paramref name="key"/>'s tile core, padded by
    /// <see cref="HiddenMarginDip"/> at the coarsest resolution its band is
    /// displayed at, lies wholly inside <paramref name="hidden"/>.
    /// </summary>
    internal static bool IsTileCoreHidden(HiddenCoverage hidden, TileKey key)
    {
        // BandForResolution picks the log-nearest band, so the live resolution
        // is at most √2 coarser than the band's own.
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var margin = HiddenMarginDip * Math.Sqrt(2.0) * TileGrid.ResolutionForBand(key.Band);
        return hidden.Covers(minX - margin, minY - margin, maxX + margin, maxY + margin);
    }

    private const int HiddenMemoCapacity = 4096;

    /// <summary>
    /// Whether the whole (rotation-aware) viewport, padded by
    /// <see cref="HiddenMarginDip"/>, lies inside <paramref name="hidden"/>.
    /// </summary>
    internal static bool IsViewportHidden(HiddenCoverage hidden, ViewState viewport, double resolution)
    {
        var extent = viewport.ToExtent();
        var margin = HiddenMarginDip * resolution;
        return hidden.Covers(
            extent.MinX - margin, extent.MinY - margin,
            extent.MaxX + margin, extent.MaxY + margin);
    }

    /// <summary>
    /// Whether finer coverage hides the cell across the view at every world
    /// copy in <paramref name="copies"/> (see <see cref="IsViewportHidden"/>).
    /// The hidden region is in the data frame, so each copy tests the viewport
    /// shifted back into it.
    /// </summary>
    internal static bool AreAllCopiesHidden(
        HiddenCoverage hidden, ViewState viewport, double resolution, IReadOnlyList<double> copies)
    {
        foreach (var offset in copies)
        {
            if (!IsViewportHidden(hidden, viewport with { CenterX = viewport.CenterX - offset }, resolution))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The screen-space difference clips for every world copy of the finer
    /// coverages attached to <paramref name="layer"/>. Finer cells draw at
    /// their own copies, so a finer cell in any copy hides whatever copy of
    /// this cell lies beneath it on screen; coverages off-view are culled by
    /// <see cref="CoverageClip.BuildActiveDifferencePaths(object, ViewState, double)"/>.
    /// </summary>
    private static List<SKPath> BuildCopyClipPaths(object layer, ViewState viewport, double resolution)
    {
        var paths = new List<SKPath>();
        if (CoverageClip.Get(layer) is null)
        {
            return paths;
        }

        foreach (var offset in WorldCopies.OffsetsX)
        {
            paths.AddRange(CoverageClip.BuildActiveDifferencePaths(
                layer, viewport with { CenterX = viewport.CenterX - offset }, resolution));
        }

        return paths;
    }

    /// <summary>
    /// Whether any op one world east or west has a copy within the world box
    /// (<paramref name="minX"/>..<paramref name="maxY"/>).
    /// </summary>
    private static bool HasAdjacentWorldOps(
        BaseSpatialIndex baseIndex, double minX, double minY, double maxX, double maxY)
    {
        foreach (var offset in WorldCopies.OffsetsX)
        {
            if (offset != 0.0 && baseIndex.Intersects(minX - offset, minY, maxX - offset, maxY))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The tiles whose core (gutter excluded) overlaps the data's X span
    /// [<paramref name="minX"/>, <paramref name="maxX"/>]. With several world
    /// copies in view, a tile just past the data's frame edge has content only
    /// in its gutter, and its core lies under the adjacent copy's tile, which
    /// holds the data there; blitting both drew the seam column twice. Dropping
    /// it leaves every screen column to the one copy whose data it shows. A
    /// core that overlaps by less than <paramref name="tolerance"/> (a pixel at
    /// the band's resolution) counts as outside, so data ending exactly on the
    /// tile edge (0° and 360° are edges at every band) is not caught by
    /// rounding.
    /// </summary>
    private static IReadOnlyList<TileKey> WithCoreInExtent(
        IReadOnlyList<TileKey> keys, double minX, double maxX, double tolerance)
    {
        List<TileKey>? kept = null;
        for (var i = 0; i < keys.Count; i++)
        {
            var (tileMinX, _, tileMaxX, _) = TileGrid.TileWorldBounds(keys[i]);
            var inside = tileMaxX > minX + tolerance && tileMinX < maxX - tolerance;
            if (!inside && kept is null)
            {
                kept = new List<TileKey>(keys.Count);
                for (var j = 0; j < i; j++)
                {
                    kept.Add(keys[j]);
                }
            }
            else if (inside)
            {
                kept?.Add(keys[i]);
            }
        }

        return (IReadOnlyList<TileKey>?)kept ?? keys;
    }

    /// <summary>
    /// The distinct tiles of every world copy, in copy order: the set the frame
    /// schedules and pins. A single copy is returned as is.
    /// </summary>
    private static IReadOnlyList<TileKey> UnionTiles(CopyTiles[] copies)
    {
        if (copies.Length == 1)
        {
            return copies[0].Tiles;
        }

        var seen = new HashSet<TileKey>();
        var union = new List<TileKey>();
        foreach (var copy in copies)
        {
            foreach (var key in copy.Tiles)
            {
                if (seen.Add(key))
                {
                    union.Add(key);
                }
            }
        }

        return union;
    }

    /// <summary>
    /// The speculative (predicted or cross-band) tiles of every world copy in
    /// view, built by <paramref name="tilesAt"/> for each copy's offset.
    /// Duplicates are possible across copies; the callers' pending sets
    /// absorb them.
    /// </summary>
    private static IEnumerable<TileKey> SpeculativeTiles(
        IReadOnlyList<double> copies, Func<double, IReadOnlyList<TileKey>> tilesAt)
    {
        foreach (var offset in copies)
        {
            foreach (var key in tilesAt(offset))
            {
                yield return key;
            }
        }
    }

    /// <summary>
    /// <paramref name="keys"/> with their SCAMIN scale classes at
    /// <paramref name="ratio"/> (see <see cref="WithScaleClass"/>), returning
    /// the input list unchanged when the scene has no scale limits.
    /// </summary>
    private static IReadOnlyList<TileKey> WithScaleClasses(VectorScene scene, IReadOnlyList<TileKey> keys, double ratio)
    {
        if (TileScaleClass.Thresholds(scene).Length == 0)
        {
            return keys;
        }

        var classed = new TileKey[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            classed[i] = WithScaleClass(scene, keys[i], ratio);
        }

        return classed;
    }

    /// <summary>
    /// Filters <paramref name="keys"/> to the tiles that intersect base content
    /// (see <see cref="TileHasContent"/>) and are not wholly hidden by finer
    /// coverage (see <see cref="IsTileHidden"/>), returning the input list
    /// unchanged when every tile qualifies. Call under <c>state.Sync</c>.
    /// </summary>
    private static IReadOnlyList<TileKey> WithContent(TileState state, IReadOnlyList<TileKey> keys, HiddenCoverage? hidden)
    {
        List<TileKey>? filtered = null;
        var skippedHidden = 0;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var hasContent = TileHasContent(state, key);
            var keep = hasContent && !IsTileHidden(state, key, hidden);
            if (hasContent && !keep)
            {
                skippedHidden++;
            }

            if (keep)
            {
                filtered?.Add(key);
            }
            else if (filtered is null)
            {
                filtered = new List<TileKey>(keys.Count);
                for (var j = 0; j < i; j++)
                {
                    filtered.Add(keys[j]);
                }
            }
        }

        if (skippedHidden > 0)
        {
            S100Diag.Telemetry.TileHiddenSkipped.Add(skippedHidden);
        }

        return (IReadOnlyList<TileKey>?)filtered ?? keys;
    }

    private static bool InvalidateViewport(TileState state)
    {
        lock (state.Sync)
        {
            var hadViewportWork =
                state.CurrentViewport is not null
                || state.CurrentVisible.Count > 0
                || state.CurrentSpeculative.Count > 0;
            if (hadViewportWork)
            {
                state.ViewportEpoch++;
            }
            state.CurrentViewport = null;
            state.CurrentVisible.Clear();
            state.CurrentSpeculative.Clear();
            state.PendingVisible.Clear();
            state.PendingPredicted.Clear();
            state.PendingCrossBand.Clear();
            state.VisibleEnqueueTicks.Clear();
            ReleaseTileJobs(state);
            return false;
        }
    }

    private static TileRelevance GetTileRelevance(
        TileState state,
        TileKey key,
        long generation) =>
        ClassifyTileRelevance(
            generation == state.Generation,
            state.CurrentVisible.Contains(key),
            state.CurrentSpeculative.Contains(key));

    private static bool IsTileVisible(
        TileState state,
        TileKey key,
        long generation)
    {
        lock (state.Sync)
        {
            return GetTileRelevance(state, key, generation) == TileRelevance.Visible;
        }
    }

    private static SKImage? TryCreateVisibleTileSnapshot(
        TileState state,
        TileKey key,
        long generation,
        int pixelSize)
    {
        lock (state.Sync)
        {
            return GetTileRelevance(state, key, generation) == TileRelevance.Visible
                ? state.Cache.TryCreateSnapshot(key, generation, pixelSize)
                : null;
        }
    }

    /// <summary>
    /// Whether publishing <paramref name="image"/> for <paramref name="key"/>
    /// would replace a resident tile that already matches the live view's
    /// device scale with one that does not (a job queued before the live scale
    /// changed, landing late). Such a result is stale and is discarded. Call
    /// under <c>state.Sync</c>.
    /// </summary>
    private static bool SupersededByLiveScale(TileState state, TileKey key, SKImage image)
    {
        if (state.CurrentViewport is not { } live)
        {
            return false;
        }

        var livePx = TilePixelSize(live.DeviceScale);
        return image.Width != livePx && state.Cache.Contains(key, livePx);
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
    /// <paramref name="heightDip"/>. An off-screen frame passes
    /// <paramref name="pinVisible"/> <see langword="false"/> so it does not
    /// replace the live view's eviction pins with its own viewport's tiles.
    /// Each entry of <paramref name="copies"/> is one world copy: its tiles are
    /// blitted against the centre shifted by the copy's offset, and
    /// <paramref name="target"/> is the union of their tiles.
    /// </summary>
    private static CompositeOutcome Composite(
        SKCanvas canvas, TileState state, int band, IReadOnlyList<CopyTiles> copies, IReadOnlyList<TileKey> target,
        double centerX, double centerY, double widthDip, double heightDip,
        double coverWidth, double coverHeight, double resolution, double rotationDeg,
        GRContext? grContext, bool pinVisible = true)
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

        // The target-band visible tiles (with content and not hidden, as
        // scheduled this frame).
        //
        // Pin the visible set so neither the hot nor the GPU cache can evict a
        // tile that is on screen this frame, no matter how small the budget is:
        // a tile in active use must never be evicted by speculative/predicted
        // inserts, or it would flicker between rendered and blank.
        // A tile whose SCAMIN scale class has just changed (issue #774) shows
        // its cached sibling class until its own raster lands: same band and
        // pixels but for the ops whose limit was crossed, so no gap opens. A
        // chart tile whose slot content changed shows the slot's last tile
        // instead (stale-while-revalidate).
        var chart = state.Chart is not null;
        var copyTargets = new IReadOnlyList<TileKey>[copies.Count];
        for (var i = 0; i < copies.Count; i++)
        {
            copyTargets[i] = chart
                ? WithStandIns(state, copies[i].Tiles)
                : WithCachedScaleClasses(state, copies[i].Tiles);
        }

        if (pinVisible)
        {
            IReadOnlyCollection<TileKey> pinned = target;
            if (chart)
            {
                var withStandIns = new HashSet<TileKey>(target);
                foreach (var keys in copyTargets)
                {
                    withStandIns.UnionWith(keys);
                }

                pinned = withStandIns;
            }

            state.Cache.Protect(pinned);
            gpuCache?.Protect(pinned);
        }

        // Whether the band fully covers the viewport (every visible tile
        // cached). An empty target (no tile in view intersects this cell's
        // content) is trivially complete: there is nothing to draw and no gap
        // to backfill.
        var targetComplete = true;
        var anyTarget = false;
        var anyShown = false;
        foreach (var keys in copyTargets)
        {
            foreach (var key in keys)
            {
                anyTarget = true;
                if (!state.Cache.Contains(key))
                {
                    targetComplete = false;
                }
                else
                {
                    anyShown = true;
                }
            }
        }

        // Backdrop: only needed to fill gaps while the target band is still
        // incomplete (during a zoom transition). Draw the SINGLE nearest cached
        // band — never multiple bands at once — so symbols rasterised at
        // different scales cannot stack and ghost. Skipped once the target band
        // is complete (its opaque fills fully occlude any backdrop anyway). The
        // band is chosen across every world copy, then each copy keeps the
        // tiles that land in view at its own centre.
        var blits = new CopyBlits[copies.Count];
        for (var i = 0; i < copies.Count; i++)
        {
            blits[i] = new CopyBlits(centerX - copies[i].OffsetX, new List<TileKey>(), copyTargets[i]);
        }

        if (!targetComplete)
        {
            var nearestDist = int.MaxValue;
            foreach (var key in state.Cache.SnapshotKeys())
            {
                var dist = Math.Abs(key.Band - band);
                if (dist == 0 || dist > MaxFallbackBandDistance || dist > nearestDist)
                {
                    continue;
                }

                foreach (var blit in blits)
                {
                    var core = TileGrid.TileCoreScreenRect(key, blit.CenterX, centerY, widthDip, heightDip, resolution);
                    if (!core.IntersectsViewport(coverWidth, coverHeight))
                    {
                        continue;
                    }

                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        foreach (var other in blits)
                        {
                            other.Fallback.Clear();
                        }
                    }

                    blit.Fallback.Add(key);
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
                canvas, state, blits,
                centerY, widthDip, heightDip, coverWidth, coverHeight,
                resolution, rotationDeg, grContext, gpuCache);
        }
        else
        {
            BlitCopies(canvas, state, blits, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
        }

        if (DiagEnabled)
        {
            var fallback = new List<TileKey>();
            foreach (var blit in blits)
            {
                fallback.AddRange(blit.Fallback);
            }

            DiagComposite(state, band, centerX, centerY, coverWidth, coverHeight, resolution, fallback, offscreen: !pinVisible);
        }

        var anyBackdrop = false;
        foreach (var blit in blits)
        {
            anyBackdrop |= blit.Fallback.Count > 0;
        }

        if (pinVisible && _settleTracking && state.Owner.TryGetTarget(out var owner))
        {
            for (var i = 0; i < copies.Count; i++)
            {
                var targets = copies[i].Tiles;
                for (var j = 0; j < targets.Count; j++)
                {
                    var shown = copyTargets[i][j];
                    RevealTrace.Report(owner, targets[j].Slot, state.Cache.Contains(shown) ? shown : null);
                }
            }
        }

        HashSet<ChartLayerState>? unrevealed = null;
        if (chart)
        {
            unrevealed = UnrevealedLayers(state, copies, copyTargets, anyBackdrop);
            if (pinVisible)
            {
                RecordShown(state, copies);
            }
        }

        // The target shows exactly when every visible tile is its own cached
        // tile: none stood in for by another SCAMIN scale class or, for a chart
        // tile, by the slot's previous tile (WithCachedScaleClasses and
        // WithStandIns return their input unchanged when they replaced
        // nothing). Fallback tiles only draw when the target is incomplete, so
        // they are ruled out too.
        var targetExact = targetComplete;
        for (var i = 0; targetExact && i < copies.Count; i++)
        {
            targetExact = ReferenceEquals(copyTargets[i], copies[i].Tiles);
        }

        return new CompositeOutcome(
            ShowsBase: !anyTarget || anyShown || anyBackdrop,
            TargetExact: targetExact,
            Unrevealed: unrevealed);
    }

    /// <summary>What a composite drew.</summary>
    /// <param name="ShowsBase">
    /// Whether any base-plane pixels of the layer are on screen (or none are
    /// due): a revealed target tile or a backdrop tile drew, or no tile in
    /// view has content. The live overlay waits for it, so a cell's symbols
    /// do not float over the chart before its fills and lines appear.
    /// </param>
    /// <param name="TargetExact">
    /// Whether every target tile drew as itself: cached, and not replaced by
    /// another scale class or a previous chart tile, so no stand-in drew.
    /// </param>
    /// <param name="Unrevealed">
    /// A chart layer's stack layers whose symbols wait for their base to
    /// appear (see <see cref="UnrevealedLayers"/>), or null.
    /// </param>
    private readonly record struct CompositeOutcome(
        bool ShowsBase, bool TargetExact, HashSet<ChartLayerState>? Unrevealed = null);

    /// <summary>
    /// <paramref name="keys"/> with each uncached tile replaced by a cached
    /// tile of the same band and position in another SCAMIN scale class, the
    /// nearest class first, when there is one. Returns the input list when
    /// nothing is replaced. Call under <c>state.Sync</c>.
    /// </summary>
    private static IReadOnlyList<TileKey> WithCachedScaleClasses(TileState state, IReadOnlyList<TileKey> keys)
    {
        if (state.Scene is not { } scene || TileScaleClass.Thresholds(scene) is not { Length: > 0 } thresholds)
        {
            return keys;
        }

        TileKey[]? replaced = null;
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (state.Cache.Contains(key))
            {
                continue;
            }

            var count = TileScaleClass.Count(thresholds, key.Band, BandDenominator(key));
            for (var step = 1; step < count; step++)
            {
                var finer = key with { ScaleClass = key.ScaleClass - step };
                var coarser = key with { ScaleClass = key.ScaleClass + step };
                var sibling = finer.ScaleClass >= 0 && state.Cache.Contains(finer) ? finer
                    : coarser.ScaleClass < count && state.Cache.Contains(coarser) ? coarser
                    : (TileKey?)null;
                if (sibling is { } found)
                {
                    replaced ??= [.. keys];
                    replaced[i] = found;
                    break;
                }
            }
        }

        return (IReadOnlyList<TileKey>?)replaced ?? keys;
    }

    /// <summary>
    /// Blits each world copy's backdrop and then its exact target band (crisp
    /// where present) against the copy's shifted centre.
    /// </summary>
    private static void BlitCopies(
        SKCanvas canvas, TileState state, IReadOnlyList<CopyBlits> blits,
        double centerY, double widthDip, double heightDip, double resolution,
        GRContext? grContext, TileCache? gpuCache)
    {
        foreach (var blit in blits)
        {
            foreach (var key in blit.Fallback)
            {
                BlitTile(canvas, state, key, blit.CenterX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
            }
        }

        foreach (var blit in blits)
        {
            foreach (var key in blit.Target)
            {
                BlitTile(canvas, state, key, blit.CenterX, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
            }
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
        SKCanvas canvas, TileState state, IReadOnlyList<CopyBlits> blits,
        double centerY, double widthDip, double heightDip,
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
            BlitCopies(canvas, state, blits, centerY, widthDip, heightDip, resolution, grContext, gpuCache);
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

        BlitCopies(offCanvas, state, blits, centerY, widthDip, heightDip, resolution, grContext, gpuCache);

        var image = surface.Snapshot();

        canvas.Save();
        canvas.RotateDegrees((float)rotationDeg, (float)(widthDip * 0.5), (float)(heightDip * 0.5));
        var dest = new SKRect(
            (float)originX, (float)originY,
            (float)(originX + coverWidth), (float)(originY + coverHeight));
        canvas.DrawImage(image, dest, Sampling);
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
    /// ghosting and zoom-out blanking can be root-caused from the log. The
    /// <c>targetPx</c> histogram (resident target tiles by pixel width) and the
    /// <c>frame=live|offscreen</c> tag expose tiles blitted at the wrong device
    /// scale.
    /// </summary>
    private static void DiagComposite(
        TileState state, int band,
        double centerX, double centerY, double widthDip, double heightDip, double resolution,
        List<TileKey> fallback, bool offscreen)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _diagLastTick);
        if (last != 0 && now - last < 1000)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _diagLastTick, now, last) != last)
        {
            return;
        }

        int targetTotal = 0, targetPresent = 0;
        var present = new HashSet<TileKey>(state.Cache.SnapshotKeys());
        var targetPx = new SortedDictionary<int, int>();
        var bandHist = new SortedDictionary<int, int>();
        foreach (var key in present)
        {
            bandHist.TryGetValue(key.Band, out var c);
            bandHist[key.Band] = c + 1;
        }

        var bandRatio = resolution / TileGrid.ResolutionForBand(band);
        foreach (var gridKey in TileGrid.VisibleTiles(centerX, centerY, widthDip, heightDip, resolution, band))
        {
            var key = state.Scene is { } scene ? WithScaleClass(scene, gridKey, bandRatio) : gridKey;
            targetTotal++;
            if (present.Contains(key))
            {
                targetPresent++;
                if (state.Cache.TryGet(key) is { } image)
                {
                    targetPx.TryGetValue(image.Width, out var n);
                    targetPx[image.Width] = n + 1;
                }
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
            $"gpu={(state.GpuTextures?.Count.ToString() ?? "-")} bandHist=[{hist}] " +
            $"targetPx=[{string.Join(",", targetPx.Select(kv => $"{kv.Key}:{kv.Value}"))}] " +
            $"frame={(offscreen ? "offscreen" : "live")}");
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
    /// <item>scene/style generation advanced (a <see cref="BindScene(object, VectorScene)"/>
    /// invalidation) → discard the now-stale textures so the re-rasterised
    /// tiles are re-promoted.</item>
    /// </list>
    /// </summary>
    private static void ManageGpuResidency(TileState state, GRContext? grContext, object layer)
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

            state.GpuTextures = new TileCache(TileMemoryPool.Gpu, owner: null, deferDisposal: true);
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
    /// rotation resources into it. See <see cref="GpuRegistry"/>.
    /// </summary>
    private static GpuRegistryEntry RegisterGpuCache(object layer, TileCache cache, object context)
    {
        var entry = new GpuRegistryEntry
        {
            Layer = new WeakReference<object>(layer),
            Cache = cache,
            Context = context,
        };

        lock (GpuRegistrySync)
        {
            GpuRegistry.Add(entry);
        }

        return entry;
    }

    /// <summary>
    /// Removes a GPU-texture cache from the registry once the render thread has
    /// taken ownership of disposing it (a live layer rebuilding or dropping its
    /// cache). See <see cref="GpuRegistry"/>.
    /// </summary>
    private static void UnregisterGpuCache(TileCache cache)
    {
        lock (GpuRegistrySync)
        {
            for (int i = GpuRegistry.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(GpuRegistry[i].Cache, cache))
                {
                    GpuRegistry.RemoveAt(i);
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
    /// under the wrong one. See <see cref="GpuRegistry"/>.
    /// </summary>
    private static void ReconcileGpuCaches(object grContext)
    {
        lock (GpuRegistrySync)
        {
            for (int i = GpuRegistry.Count - 1; i >= 0; i--)
            {
                var entry = GpuRegistry[i];
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

                GpuRegistry.RemoveAt(i);
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
        object layer, TileCache cache, object context, SKSurface? rotationSurface, SKImage? rotationImage)
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

    /// <summary>
    /// Test-only seam: the pixel width of every tile resident in
    /// <paramref name="layer"/>'s hot cache, keyed by tile, so a test can assert
    /// which raster scale is being blitted. Empty when the layer has no state.
    /// </summary>
    internal static IReadOnlyDictionary<TileKey, int> CachedTilePixelSizesForTest(object layer)
    {
        var sizes = new Dictionary<TileKey, int>();
        if (States.TryGetValue(layer, out var state))
        {
            foreach (var key in state.Cache.SnapshotKeys())
            {
                if (state.Cache.TryGet(key) is { } image)
                {
                    sizes[key] = image.Width;
                }
            }
        }

        return sizes;
    }

    /// <summary>
    /// Test-only seam: whether <paramref name="layer"/> still has visible tiles
    /// queued or rasterising, so a headless settle loop can tell "no tile has
    /// published yet" apart from "nothing left to publish". Speculative
    /// (prediction / cross-band) work is ignored: it never requests a redraw.
    /// </summary>
    internal static bool HasVisibleTileWorkForTest(object layer)
    {
        if (!States.TryGetValue(layer, out var state))
        {
            return false;
        }

        lock (state.Sync)
        {
            if (state.PendingVisible.Count > 0)
            {
                return true;
            }

            foreach (var key in state.InFlight)
            {
                if (GetTileRelevance(state, key, state.Generation) == TileRelevance.Visible)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Test-only seam: whether every tile the last live frame of
    /// <paramref name="layer"/> found visible is resident at
    /// <paramref name="pixelSize"/> with no visible work queued or rasterising,
    /// so the next frame composites a settled picture. False until the layer has
    /// painted a frame with visible tiles.
    /// </summary>
    internal static bool IsVisibleSettledForTest(object layer, int pixelSize)
    {
        if (!States.TryGetValue(layer, out var state))
        {
            return false;
        }

        lock (state.Sync)
        {
            return state.CurrentVisible.Count > 0
                && state.PendingVisible.Count == 0
                && !state.CurrentVisible.Any(state.InFlight.Contains)
                && state.CurrentVisible.All(key => state.Cache.Contains(key, pixelSize));
        }
    }

    /// <summary>
    /// Test-only seam: a one-line summary of <paramref name="layer"/>'s tile
    /// scheduling state and the process-wide worker pool, for a settle loop to
    /// report when it times out.
    /// </summary>
    internal static string DescribeTileWorkForTest(object layer)
    {
        var stats = Scheduler.GetStats();
        var pool = $"scheduler busy={stats.BusyWorkers}/{stats.Workers} queued={string.Join('/', stats.QueuedByClass)}";
        if (!States.TryGetValue(layer, out var state))
        {
            return $"no tile state; {pool}";
        }

        lock (state.Sync)
        {
            var resident = state.CurrentVisible.Count(state.Cache.Contains);
            return $"visible={state.CurrentVisible.Count} resident={resident} "
                + $"pendingVisible={state.PendingVisible.Count} inFlight={state.InFlight.Count} "
                + $"pendingPredicted={state.PendingPredicted.Count} pendingCrossBand={state.PendingCrossBand.Count} "
                + $"cached={state.Cache.Count} jobs={state.Jobs.Count}; {pool}";
        }
    }

    /// <summary>Test-only seam: the current GPU-registry entry count.</summary>
    internal static int GpuRegistryEntryCountForTest
    {
        get
        {
            lock (GpuRegistrySync)
            {
                return GpuRegistry.Count;
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
        lock (GpuRegistrySync)
        {
            foreach (var entry in GpuRegistry)
            {
                entry.Cache.Dispose();
                entry.RotationImage?.Dispose();
                entry.RotationSurface?.Dispose();
            }

            GpuRegistry.Clear();
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
        var textureKey = key;
        var image = state.Cache.TryGet(key);
        if (image is null)
        {
            return;
        }

        var toDraw = image;
        if (grContext is not null && gpuCache is not null)
        {
            var gpu = gpuCache.TryGet(textureKey);

            // The raster tile can be replaced in place at another device scale;
            // a texture twin of the old size is stale, so re-upload it.
            if (gpu is not null && (gpu.Width != image.Width || gpu.Height != image.Height))
            {
                gpu = null;
            }

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
                    gpuCache.Put(textureKey, gpu);
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
        var apronWorld = ApronWorld(image.Width, maxX - minX);

        var core = TileGrid.WorldToScreenRect(minX, minY, maxX, maxY, centerX, centerY, widthDip, heightDip, resolution);
        var full = TileGrid.WorldToScreenRect(
            minX - apronWorld, minY - apronWorld, maxX + apronWorld, maxY + apronWorld,
            centerX, centerY, widthDip, heightDip, resolution);

        var coreRect = new SKRect((float)core.Left, (float)core.Top, (float)core.Right, (float)core.Bottom);
        var fullRect = new SKRect((float)full.Left, (float)full.Top, (float)full.Right, (float)full.Bottom);

        canvas.Save();
        canvas.ClipRect(coreRect, SKClipOperation.Intersect, antialias: false);
        canvas.DrawImage(toDraw, fullRect, Sampling);
        canvas.Restore();
    }

    /// <summary>
    /// The world width of a stored tile's apron: <see cref="ApronPx"/> pixels
    /// of a tile <paramref name="storedPx"/> pixels wide whose core spans
    /// <paramref name="coreWorld"/>. The stored width is the tile's raster-scale
    /// tag, so the image alone says where its core lies.
    /// </summary>
    internal static double ApronWorld(int storedPx, double coreWorld)
    {
        var corePx = storedPx - 2 * ApronPx;
        return corePx > 0 ? ApronPx * coreWorld / corePx : 0.0;
    }

    /// <summary>
    /// Brings <paramref name="state"/>'s scheduler interests in line with its
    /// pending sets: a job per pending tile at that tile's class and its
    /// on-screen distance from the view centre, so central tiles of every
    /// layer run first. Tiles that left the sets release their interest,
    /// which drops a job that hasn't started. Caller holds <c>state.Sync</c>.
    /// </summary>
    /// <param name="state">The layer's tile state.</param>
    /// <param name="resolution">The frame's resolution, metres per pixel, to put urgency in pixels.</param>
    private static void SyncTileJobs(TileState state, double resolution)
    {
        var jobs = state.Jobs;
        if (jobs.Count == 0
            && state.PendingVisible.Count == 0
            && state.PendingPredicted.Count == 0
            && state.PendingCrossBand.Count == 0)
        {
            return;
        }

        state.JobPruneScratch.Clear();
        foreach (var key in jobs.Keys)
        {
            if (!state.PendingVisible.Contains(key)
                && !state.PendingPredicted.Contains(key)
                && !state.PendingCrossBand.Contains(key))
            {
                state.JobPruneScratch.Add(key);
            }
        }

        foreach (var key in state.JobPruneScratch)
        {
            if (jobs.Remove(key, out var stale))
            {
                stale.Dispose();
            }
        }

        WantTileJobs(state, state.PendingVisible, WorkClass.Visible, resolution);
        WantTileJobs(state, state.PendingPredicted, WorkClass.Predicted, resolution);
        WantTileJobs(state, state.PendingCrossBand, WorkClass.Background, resolution);
    }

    private static void WantTileJobs(TileState state, HashSet<TileKey> pending, WorkClass workClass, double resolution)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var scheduler = Scheduler;
        foreach (var key in pending)
        {
            var priority = new WorkPriority(
                workClass,
                state.PredictedCentres.TryGetValue(key, out var planned)
                    ? TileUrgency(key, planned.X, planned.Y, planned.Resolution)
                    : TileUrgency(key, state.PendingCenterX, state.PendingCenterY, resolution));
            if (state.Jobs.TryGetValue(key, out var interest))
            {
                // A pan moves every tile's urgency a little each frame; only a
                // class change or a move of a quarter tile re-sorts the queue.
                if (interest.Priority.Class != workClass
                    || Math.Abs(interest.Priority.Urgency - priority.Urgency) >= UrgencyResortPixels)
                {
                    interest.Update(priority);
                }

                continue;
            }

            var tileKey = key;
            state.Jobs[key] = scheduler.Schedule(
                new WorkKey(state, tileKey),
                WorkStage.Raster,
                priority,
                _ => RunTileJobAsync(state, tileKey));
        }
    }

    /// <summary>
    /// How far, in pixels, a queued tile's distance from the view centre must
    /// move before its job is re-sorted: a quarter of a 256&#160;px tile.
    /// </summary>
    private const double UrgencyResortPixels = 64;

    /// <summary>Releases every scheduler interest of <paramref name="state"/>. Caller holds <c>state.Sync</c>.</summary>
    private static void ReleaseTileJobs(TileState state)
    {
        foreach (var interest in state.Jobs.Values)
        {
            interest.Dispose();
        }

        state.Jobs.Clear();
    }

    /// <summary>
    /// A scheduler job: takes <paramref name="key"/> from whichever pending
    /// set still holds it (with its metatile peers, when metatiles are on),
    /// then reads it from disk or rasterises it and publishes it. A tile that
    /// has left the pending sets since the job was queued is skipped.
    /// </summary>
    /// <returns>Whether the job did any work.</returns>
    private static async ValueTask<bool> RunTileJobAsync(TileState state, TileKey key)
    {
        if (!DrainGate.TryRegister())
        {
            return false;
        }

        try
        {
            TileJob job;
            lock (state.Sync)
            {
                if (state.Jobs.Remove(key, out var own))
                {
                    own.Dispose();
                }

                var currentScene = state.Scene;
                if (currentScene is null && state.Chart is null)
                {
                    return false;
                }

                HashSet<TileKey> pending;
                TileJobPriority priority;
                if (state.PendingVisible.Contains(key))
                {
                    pending = state.PendingVisible;
                    priority = TileJobPriority.Visible;
                }
                else if (state.PendingPredicted.Contains(key))
                {
                    pending = state.PendingPredicted;
                    priority = TileJobPriority.Predicted;
                }
                else if (state.PendingCrossBand.Contains(key))
                {
                    pending = state.PendingCrossBand;
                    priority = TileJobPriority.CrossBand;
                }
                else
                {
                    return false;
                }

                var metatiles = state.Options.TileMetatileEnabled;
                var keys = TakeMetatile(pending, key, metatiles);
                if (metatiles && keys.Count == 1)
                {
                    RecordMetatileFallback("sparse");
                }

                foreach (var taken in keys)
                {
                    state.InFlight.Add(taken);
                    if (!taken.Equals(key) && state.Jobs.Remove(taken, out var peer))
                    {
                        peer.Dispose();
                    }
                }

                double? queueWaitMs = null;
                if (priority == TileJobPriority.Visible)
                {
                    foreach (var taken in keys)
                    {
                        if (state.VisibleEnqueueTicks.TryGetValue(taken, out var enqueuedAt))
                        {
                            queueWaitMs = Math.Max(
                                queueWaitMs ?? 0,
                                Stopwatch.GetElapsedTime(enqueuedAt).TotalMilliseconds);
                        }
                    }
                }

                Dictionary<TileKey, SlotPlan>? plans = null;
                if (state.Chart is not null)
                {
                    plans = new Dictionary<TileKey, SlotPlan>(keys.Count);
                    foreach (var taken in keys)
                    {
                        if (state.Plans.TryGetValue(taken, out var plan))
                        {
                            plans[taken] = plan;
                        }
                    }
                }

                job = new TileJob(
                    keys,
                    state.PendingDeviceScale,
                    state.PendingGeneration,
                    state.ViewportEpoch,
                    currentScene,
                    state.BaseIndex,
                    plans,
                    priority,
                    state.DiskNamespace is { } diskNamespace
                        ? TileDiskCache.NamespaceFor(
                            diskNamespace,
                            TilePixelSize(state.PendingDeviceScale))
                        : null,
                    queueWaitMs);
            }

            await ProcessTileJobAsync(state, job);
            return true;
        }
        catch (Exception ex)
        {
            S100Diag.Telemetry.RecordRenderFault(ex);
            return false;
        }
        finally
        {
            DrainGate.Complete();
        }
    }

    private static async Task ProcessTileJobAsync(TileState state, TileJob job)
    {
        var disk = SharedDiskCache;
        var images = new Dictionary<TileKey, SKImage>();
        var rasterized = new HashSet<TileKey>();
        var discardedBeforeRaster = new HashSet<TileKey>();
        var activeKeys = new List<TileKey>(job.Keys.Count);
        using var activity = StartTileJobActivity(job);

        try
        {
            lock (state.Sync)
            {
                activity?.SetTag(
                    "s100.render.tile.viewport_epoch.current",
                    state.ViewportEpoch);
                foreach (var key in job.Keys)
                {
                    if (GetTileRelevance(state, key, job.Generation)
                        == TileRelevance.Irrelevant)
                    {
                        state.InFlight.Remove(key);
                        discardedBeforeRaster.Add(key);
                    }
                    else
                    {
                        activeKeys.Add(key);
                    }
                }
            }

            if (activeKeys.Count == 0)
            {
                activity?.SetTag(
                    "s100.render.tile.outcome",
                    "stale_before_disk_read");
            }
            else if (disk is not null && job.DiskNamespace is not null)
            {
                using var diskReadActivity = S100Diag.Telemetry.ActivitySource.StartActivity(
                    "s100.render.tile.stage.disk_read", ActivityKind.Internal);
                var tilePx = TilePixelSize(job.DeviceScale);

                // Every read starts before the first is awaited, and an awaited
                // read gives the worker back to the scheduler; decoding resumes
                // on a worker. The namespace is per pixel size, and a tile of
                // another size (a corrupt or foreign file) is a miss.
                var reads = new List<(TileKey Key, ValueTask<SKImage?> Read)>(activeKeys.Count);
                foreach (var key in activeKeys)
                {
                    if (IsPersistable(job, key))
                    {
                        reads.Add((key, disk.TryReadAsync(job.DiskNamespace, key, tilePx)));
                    }
                }

                foreach (var (key, read) in reads)
                {
                    if (await read is { } image)
                    {
                        images.Add(key, image);
                        S100Diag.Telemetry.TileDiskHits.Add(1);
                    }
                }
                diskReadActivity?.SetTag("s100.render.tile.disk.hits", images.Count);
                diskReadActivity?.SetTag(
                    "s100.render.tile.disk.misses", activeKeys.Count - images.Count);
            }

            lock (state.Sync)
            {
                activeKeys.RemoveAll(key =>
                {
                    if (GetTileRelevance(state, key, job.Generation)
                        != TileRelevance.Irrelevant)
                    {
                        return false;
                    }

                    state.InFlight.Remove(key);
                    discardedBeforeRaster.Add(key);
                    if (images.Remove(key, out var staleImage))
                    {
                        staleImage.Dispose();
                    }

                    return true;
                });
            }

            var misses = activeKeys.Where(key => !images.ContainsKey(key)).ToList();
            if (misses.Count > 0)
            {
                var rasterStart = Stopwatch.GetTimestamp();
                IReadOnlyDictionary<TileKey, SKImage> fresh;
                if (job.Plans is { } jobPlans)
                {
                    fresh = RasterizeChartTiles(misses, jobPlans, job.DeviceScale);
                }
                else if (misses.Count > 1)
                {
                    fresh = RasterizeMetatile(job.Scene!, job.BaseIndex, misses, job.DeviceScale);
                }
                else
                {
                    if (job.Keys.Count > 1)
                    {
                        RecordMetatileFallback("disk");
                    }

                    using var bitmap = RasterizeTile(
                        job.Scene!, job.BaseIndex, misses[0], job.DeviceScale);
                    fresh = new Dictionary<TileKey, SKImage>
                    {
                        [misses[0]] = SKImage.FromBitmap(bitmap),
                    };
                }

                var elapsedMs = Stopwatch.GetElapsedTime(rasterStart).TotalMilliseconds;
                var perTileMs = elapsedMs / fresh.Count;
                foreach (var (key, image) in fresh)
                {
                    images.Add(key, image);
                    rasterized.Add(key);
                    S100Diag.Telemetry.TileRasterizeDuration.Record(perTileMs);
                }
            }

            activity?.SetTag("s100.render.tile.disk.hits", images.Count - rasterized.Count);
            activity?.SetTag("s100.render.tile.rasterized", rasterized.Count);
            if (activeKeys.Count > 0)
            {
                activity?.SetTag(
                    "s100.render.tile.outcome",
                    (images.Count - rasterized.Count, rasterized.Count) switch
                    {
                        ( > 0, > 0) => "mixed",
                        ( > 0, 0) => "disk",
                        (0, > 0) => "raster",
                        _ => "empty",
                    });
            }
            else if (discardedBeforeRaster.Count > 0)
            {
                activity?.SetTag(
                    "s100.render.tile.outcome",
                    "stale_before_raster");
            }
        }
        catch (Exception ex)
        {
            foreach (var image in images.Values)
            {
                image.Dispose();
            }

            images.Clear();
            rasterized.Clear();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.SetTag("s100.render.tile.outcome", "fault");
            S100Diag.Telemetry.RecordRenderFault(ex);
        }

        var publishedVisible = false;
        var published = 0;
        var stale = 0;
        var persistenceQueued = 0;
        var persistenceDiscarded = 0;
        List<TileKey>? persistenceKeys = null;
        using (var publishActivity = S100Diag.Telemetry.ActivitySource.StartActivity(
                   "s100.render.tile.stage.publish", ActivityKind.Internal))
        {
            lock (state.Sync)
            {
                activity?.SetTag(
                    "s100.render.tile.viewport_epoch.current",
                    state.ViewportEpoch);
                activity?.SetTag(
                    "s100.render.tile.viewport_epoch.stale",
                    job.ViewportEpoch != state.ViewportEpoch);
                foreach (var key in job.Keys)
                {
                    state.InFlight.Remove(key);
                    var relevance = GetTileRelevance(
                        state,
                        key,
                        job.Generation);
                    if (images.Remove(key, out var image)
                        && relevance != TileRelevance.Irrelevant
                        && !SupersededByLiveScale(state, key, image))
                    {
                        state.Cache.Put(key, image);
                        published++;
                        if (relevance == TileRelevance.Visible
                            && rasterized.Contains(key)
                            && disk is not null
                            && job.DiskNamespace is not null
                            && IsPersistable(job, key))
                        {
                            (persistenceKeys ??= []).Add(key);
                        }
                        if (relevance == TileRelevance.Speculative
                            && state.PredictedInCache.TryAdd(key, Stopwatch.GetTimestamp()))
                        {
                            state.Planner?.Stats.RecordTilesPredicted(1);
                        }
                        else
                        {
                            publishedVisible = true;
                            if (state.VisibleEnqueueTicks.Remove(key, out var enqueuedAt))
                            {
                                S100Diag.Telemetry.TileColdLatency.Record(
                                    Stopwatch.GetElapsedTime(enqueuedAt).TotalMilliseconds);
                            }
                        }
                    }
                    else if (image is not null
                        && relevance == TileRelevance.Irrelevant
                        && state.Chart is not null
                        && IsWantedStandIn(state, key)
                        && !SupersededByLiveScale(state, key, image))
                    {
                        // A chart tile of a stack the slot has since moved
                        // past: still better than nothing while the slot's
                        // current tile rasterises, so it stands in for it.
                        state.Cache.Put(key, image);
                        state.ShownBySlot[key.Slot] = key;
                        published++;
                        publishedVisible = true;
                    }
                    else if (image is not null
                        || discardedBeforeRaster.Contains(key))
                    {
                        image?.Dispose();
                        stale++;
                    }
                }
            }

            publishActivity?.SetTag("s100.render.tile.published", published);
            publishActivity?.SetTag("s100.render.tile.stale", stale);
        }

        if (persistenceKeys is not null
            && disk is not null
            && job.DiskNamespace is not null)
        {
            foreach (var key in persistenceKeys)
            {
                var enqueueResult = disk.TryQueueDeferredSnapshot(
                    job.DiskNamespace,
                    key,
                    () => TryCreateVisibleTileSnapshot(
                        state,
                        key,
                        job.Generation,
                        TilePixelSize(job.DeviceScale)),
                    () => IsTileVisible(
                        state,
                        key,
                        job.Generation));
                if (enqueueResult == TileDiskCache.WriteEnqueueResult.Queued)
                {
                    persistenceQueued++;
                }
                else
                {
                    persistenceDiscarded++;
                }
            }
        }

        activity?.SetTag("s100.render.tile.published", published);
        activity?.SetTag("s100.render.tile.stale", stale);
        activity?.SetTag(
            "s100.render.tile.stale_before_raster",
            discardedBeforeRaster.Count);
        activity?.SetTag("s100.render.tile.persistence.queued", persistenceQueued);
        activity?.SetTag(
            "s100.render.tile.persistence.discarded",
            persistenceDiscarded);
        foreach (var image in images.Values)
        {
            image.Dispose();
        }

        if (job.Priority != TileJobPriority.Visible && rasterized.Count > 0)
        {
            S100Diag.Telemetry.TilePredictionRasterized.Add(rasterized.Count);
        }

        if (publishedVisible)
        {
            RequestRepaint(state);
        }
    }

    /// <summary>
    /// Whether <paramref name="key"/>'s tile may be read from and written to
    /// disk: always for a layer bound to one scene (its namespace is its style
    /// state), and for a chart tile only when every scene in its plan has a
    /// stable key, so its slice names the same content in every session.
    /// </summary>
    private static bool IsPersistable(TileJob job, TileKey key) =>
        job.Plans is not { } plans || (plans.TryGetValue(key, out var plan) && plan.Persistable);

    private static void RecordMetatileFallback(string reason) =>
        S100Diag.Telemetry.MetatileFallbacks.Add(
            1, new KeyValuePair<string, object?>("reason", reason));

    private static Activity? StartTileJobActivity(TileJob job)
    {
        var activity = S100Diag.Telemetry.ActivitySource.StartActivity(
            "s100.render.tile.job", ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("s100.render.tile.keys", FormatTileKeys(job.Keys));
        activity.SetTag("s100.render.tile.count", job.Keys.Count);
        activity.SetTag("s100.render.tile.priority", job.Priority switch
        {
            TileJobPriority.Visible => "visible",
            TileJobPriority.Predicted => "predicted",
            TileJobPriority.CrossBand => "cross_band",
            _ => "unknown",
        });
        activity.SetTag("s100.render.tile.band", job.Keys[0].Band);
        activity.SetTag("s100.render.tile.device_scale", job.DeviceScale);
        activity.SetTag("s100.render.tile.generation", job.Generation);
        activity.SetTag("s100.render.tile.viewport_epoch", job.ViewportEpoch);
        activity.SetTag("s100.render.tile.queue_wait_ms", job.QueueWaitMs);
        activity.SetTag("s100.render.tile.disk.enabled", job.DiskNamespace is not null);
        return activity;
    }

    private static string FormatTileKeys(IReadOnlyList<TileKey> keys) =>
        string.Join(';', keys.Select(static key => $"{key.Band}/{key.X}/{key.Y}"));

    private enum TileJobPriority
    {
        Visible,
        Predicted,
        CrossBand,
    }

    /// <summary>
    /// The data-space tiles one world copy contributes to a frame: those under
    /// the view shifted back by <paramref name="OffsetX"/> (EPSG:3857 metres).
    /// </summary>
    private readonly record struct CopyTiles(double OffsetX, IReadOnlyList<TileKey> Tiles);

    /// <summary>
    /// One world copy's blits for a frame: the centre its tiles are projected
    /// against (the live centre shifted back by the copy offset), its backdrop
    /// tiles and its exact target-band tiles.
    /// </summary>
    private readonly record struct CopyBlits(double CenterX, List<TileKey> Fallback, IReadOnlyList<TileKey> Target);

    internal enum TileRelevance
    {
        Irrelevant,
        Speculative,
        Visible,
    }

    internal readonly record struct TileViewport(
        double CenterX,
        double CenterY,
        double CoverWidth,
        double CoverHeight,
        double Resolution,
        float DeviceScale);

    private sealed record TileJob(
        IReadOnlyList<TileKey> Keys,
        float DeviceScale,
        long Generation,
        long ViewportEpoch,
        VectorScene? Scene,
        BaseSpatialIndex? BaseIndex,
        IReadOnlyDictionary<TileKey, SlotPlan>? Plans,
        TileJobPriority Priority,
        string? DiskNamespace,
        double? QueueWaitMs);

    /// <summary>
    /// A tile job's urgency within its class: the on-screen distance, in
    /// pixels, from the view centre (<paramref name="centerX"/>,
    /// <paramref name="centerY"/> in EPSG:3857 metres) to the tile's centre,
    /// so central tiles of every layer rasterise before the perimeter. Pixels
    /// rather than metres keep layers at different bands comparable.
    /// </summary>
    /// <param name="key">The tile.</param>
    /// <param name="centerX">View centre X in EPSG:3857 metres.</param>
    /// <param name="centerY">View centre Y in EPSG:3857 metres.</param>
    /// <param name="resolution">The live resolution, metres per pixel.</param>
    /// <returns>The urgency; lower runs first.</returns>
    internal static double TileUrgency(TileKey key, double centerX, double centerY, double resolution)
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var dx = (minX + maxX) * 0.5 - centerX;
        var dy = (minY + maxY) * 0.5 - centerY;
        return Math.Sqrt(dx * dx + dy * dy) / (resolution > 0 ? resolution : 1);
    }

    /// <summary>
    /// Removes <paramref name="seed"/> from <paramref name="pending"/> and,
    /// when enabled, claims the other pending tiles in its stable 2&#215;2
    /// grid-aligned metatile. The seed is always first; peers come only from
    /// the same pending set, so they keep the seed's class.
    /// </summary>
    /// <param name="pending">The pending set holding the seed; mutated in place.</param>
    /// <param name="seed">The tile the scheduler picked.</param>
    /// <param name="enabled">Whether metatiles are on.</param>
    /// <returns>The seed, then any peers.</returns>
    internal static IReadOnlyList<TileKey> TakeMetatile(HashSet<TileKey> pending, TileKey seed, bool enabled)
    {
        if (!pending.Remove(seed))
        {
            return [];
        }

        var result = new List<TileKey>(4) { seed };
        if (!enabled)
        {
            return result;
        }

        var xStart = seed.X & ~1;
        var yStart = seed.Y & ~1;
        for (var y = yStart; y < yStart + 2; y++)
        {
            for (var x = xStart; x < xStart + 2; x++)
            {
                var peer = seed with { X = x, Y = y };
                if (peer != seed && pending.Remove(peer))
                {
                    result.Add(peer);
                }
            }
        }

        return result;
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
        SKCanvas canvas, OverlaySource? source,
        double centerX, double centerY, double widthDip, double heightDip,
        double resolution, double rotationDeg, float deviceScale)
    {
        var overlay = source?.Scene;
        if (source is null || overlay is null || overlay.Ops.Count == 0)
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
        // the declutter pass and the overlay draw work in this frame because
        // they rotate anchors in code rather than rotating the canvas.
        var screenCull = new SKRect(
            -margin, -margin,
            (float)widthDip + margin, (float)heightDip + margin);

        // The rotated viewport footprint, larger than the axis-aligned screen
        // rect: its world preimage bounds the viewport scoping below, since the
        // declutter and draw passes rotate anchors from it onto the screen.
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
            source, overlay, rotate ? rotatedCull : screenCull,
            centerX, centerY, widthDip, heightDip, resolution);

        // Declutter labels deterministically: footprints are computed in final
        // on-screen space (anchors rotated by the same angle as the overlay), so
        // collisions are correct under rotation. Points reserve space first;
        // lower-priority labels that overlap an occupied footprint are skipped.
        var suppressed = OverlayLabelDeclutterer.Declutter(
            scene, viewport, screenCull, OverlayRenderer.HonorScaleVisibility,
            rotationDeg, cx, cy);

        // One pass for symbols and labels, in op order as north-up. Under
        // rotation each anchor is turned about the screen centre in code, so
        // labels and screen-relative symbols stay upright and only north-relative
        // (GeographicCRS) symbols turn with the chart (issue #652).
        OverlayRenderer.RenderOnto(canvas, scene, viewport, new OverlayDrawOptions
        {
            PointCullBounds = screenCull,
            SuppressedText = suppressed,
            DeviceScale = deviceScale,
            AnchorRotationDegrees = rotationDeg,
            ScreenCenterX = cx,
            ScreenCenterY = cy,
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
        OverlaySource source, VectorScene overlay, SKRect queryCull,
        double centerX, double centerY, double widthDip, double heightDip, double resolution)
    {
        var index = source.Index;

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
            source.QueryScratch, source.Candidates);

        return source.ScopedScene;
    }

    /// <summary>
    /// Rasterises a single tile from the scene at its band resolution and the
    /// frame's device scale. The scene is drawn over the core plus the full
    /// gutter, so strokes crossing a seam keep their joins and caps, but the
    /// returned bitmap holds only the core plus <see cref="ApronPx"/> on each
    /// side (<see cref="TilePixelSize"/> pixels square), cropped from the
    /// guttered raster so its pixels are the same as before the crop.
    /// </summary>
    internal static SKBitmap RasterizeTile(VectorScene scene, BaseSpatialIndex? baseIndex, TileKey key, float deviceScale)
    {
        using var activity = S100Diag.Telemetry.ActivitySource.StartActivity(
            "s100.render.tile.rasterize", ActivityKind.Internal);
        activity?.SetTag("s100.render.tile.keys", FormatTileKeys([key]));
        activity?.SetTag("s100.render.tile.count", 1);
        activity?.SetTag("s100.render.tile.band", key.Band);

        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        var corePx = CorePixelSize(deviceScale);
        var gutterPx = GutterPixelSize(deviceScale);
        var gutterWorld = gutterPx * (maxX - minX) / corePx;

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
        activity?.SetTag("s100.render.tile.candidate_operations", tileScene.Ops.Count);

        // Lossless (unclamped) inverse so WorldToScreen reproduces these exact
        // tile bounds. The top tile row's gutter pushes fullMaxY just past the
        // Web-Mercator pole limit (±π·EarthRadius); clamping there would squash
        // the tile's vertical mapping and drift the base geometry poleward. See
        // WebMercator.ToLonLat.
        var (minLon, minLat) = WebMercator.ToLonLat(fullMinX, fullMinY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(fullMaxX, fullMaxY, clampLatitude: false);

        var rasterPx = corePx + 2 * gutterPx;
        var px = TilePixelSize(deviceScale);
        activity?.SetTag("s100.render.tile.width_px", px);
        activity?.SetTag("s100.render.tile.height_px", px);

        var denom = TileScaleDenominator(scene, key);

        var viewport = new CoreViewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = rasterPx,
            HeightPixels = rasterPx,
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

        using var raster = renderer.Render(tileScene, viewport);
        if (baseIndex is not null)
        {
            using var canvas = new SKCanvas(raster);
            DrawAdjacentWorldOps(
                renderer, canvas, rasterPx, baseIndex, viewport, gutterPx,
                TileGrid.ResolutionForBand(key.Band), fullMinX, fullMinY, fullMaxX, fullMaxY);
            canvas.Flush();
        }

        // Keep the core plus the apron: a plain crop, so every stored pixel is
        // the guttered raster's own.
        return CropStored(raster, gutterPx - ApronPx, gutterPx - ApronPx, px);
    }

    /// <summary>
    /// Copies the <paramref name="size"/>-pixel square at
    /// (<paramref name="left"/>, <paramref name="top"/>) out of
    /// <paramref name="raster"/> into a bitmap of its own.
    /// </summary>
    private static SKBitmap CropStored(SKBitmap raster, int left, int top, int size)
    {
        using var subset = new SKBitmap();
        if (!raster.ExtractSubset(subset, SKRectI.Create(left, top, size, size)))
        {
            throw new InvalidOperationException(
                $"Could not crop a {size} px tile at ({left}, {top}) from a {raster.Width}×{raster.Height} raster.");
        }

        return subset.Copy();
    }

    /// <summary>
    /// Draws into a tile's gutter the ops one world east and west whose copies
    /// reach into it (issue #773). Data kept in a 0…360 frame is clipped at
    /// 360°, so the tile ending there had an empty gutter: scaled for a zoom
    /// between bands, its edge blended with that transparency and showed a
    /// faint line where the data continues in the adjacent world copy. The
    /// copies are drawn only outside the tile's core, which shows the data at
    /// its own position (the compositor blits each copy's own tiles there, so
    /// a copy in the core would draw twice), and outside the scene's own X
    /// span, where none of its ops can be. A side whose core edge lies past
    /// that span gets none either: the core is empty there, and a filled
    /// gutter would bleed into it over the copy that draws that place.
    /// </summary>
    /// <remarks>
    /// <paramref name="canvas"/> works in the guttered raster's pixels
    /// (<paramref name="rasterPx"/> square, <paramref name="gutterPx"/> of
    /// gutter); only the stored apron of that gutter reaches the surface.
    /// </remarks>
    private static void DrawAdjacentWorldOps(
        SkiaDisplayListRenderer renderer, SKCanvas canvas, int rasterPx, BaseSpatialIndex baseIndex,
        CoreViewport viewport, int gutterPx, double tolerance,
        double fullMinX, double fullMinY, double fullMaxX, double fullMaxY)
    {
        if (baseIndex.XSpan is not { } span)
        {
            return;
        }

        var gutterWorld = gutterPx * (fullMaxX - fullMinX) / rasterPx;
        var clipped = false;
        try
        {
            foreach (var offset in WorldCopies.OffsetsX)
            {
                if (offset == 0.0)
                {
                    continue;
                }

                // The ops whose copy at +offset lies in the tile sit at -offset.
                var ops = baseIndex.Query(fullMinX - offset, fullMinY, fullMaxX - offset, fullMaxY);
                if (ops.Count == 0)
                {
                    continue;
                }

                if (!clipped)
                {
                    clipped = true;
                    var pxPerMetre = rasterPx / (fullMaxX - fullMinX);
                    var left = fullMinX + gutterWorld < span.MinX - tolerance ? -1f : gutterPx;
                    var right = fullMaxX - gutterWorld > span.MaxX + tolerance ? rasterPx + 1f : rasterPx - gutterPx;
                    canvas.Save();
                    canvas.ClipRect(
                        new SKRect(left, gutterPx, right, rasterPx - gutterPx),
                        SKClipOperation.Difference,
                        antialias: false);
                    canvas.ClipRect(
                        new SKRect(
                            (float)((span.MinX - fullMinX) * pxPerMetre), -1f,
                            (float)((span.MaxX - fullMinX) * pxPerMetre), rasterPx + 1f),
                        SKClipOperation.Difference,
                        antialias: true);
                }

                var degrees = offset / WorldCopies.Circumference * 360.0;
                renderer.RenderOnto(canvas, new VectorScene(ops), viewport with
                {
                    MinLongitude = viewport.MinLongitude - degrees,
                    MaxLongitude = viewport.MaxLongitude - degrees,
                });
            }
        }
        finally
        {
            if (clipped)
            {
                canvas.Restore();
            }
        }
    }

    internal static IReadOnlyDictionary<TileKey, SKImage> RasterizeMetatile(
        VectorScene scene,
        BaseSpatialIndex? baseIndex,
        IReadOnlyList<TileKey> keys,
        float deviceScale)
    {
        var groups = PartitionMetatileForScale(scene, baseIndex, keys);
        if (groups.Count > 1)
        {
            RecordMetatileFallback("scamin");
        }

        var result = new Dictionary<TileKey, SKImage>(keys.Count);
        try
        {
            foreach (var group in groups)
            {
                if (group.Count == 1)
                {
                    using var bitmap = RasterizeTile(scene, baseIndex, group[0], deviceScale);
                    result.Add(group[0], SKImage.FromBitmap(bitmap));
                    continue;
                }

                var groupImages = RasterizeMetatileGroup(scene, baseIndex, group, deviceScale);
                foreach (var (key, image) in groupImages)
                {
                    result.Add(key, image);
                }
            }
        }
        catch
        {
            DisposeImages(result.Values);
            throw;
        }

        return result;
    }

    /// <summary>
    /// Keeps a multi-row metatile intact only when every candidate op has the
    /// same SCAMIN result at every row denominator. Otherwise it splits by row;
    /// all tiles in one row share an exact denominator.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<TileKey>> PartitionMetatileForScale(
        VectorScene scene,
        BaseSpatialIndex? baseIndex,
        IReadOnlyList<TileKey> keys)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count < 2 || keys.Select(key => (key.Y, key.ScaleClass)).Distinct().Count() == 1)
        {
            return [keys];
        }

        var scopedScene = ScopeSceneForTiles(scene, baseIndex, keys);
        var denominators = keys
            .GroupBy(key => (key.Y, key.ScaleClass))
            .Select(group => TileScaleDenominator(scene, group.First()))
            .ToArray();

        foreach (var op in scopedScene.Ops)
        {
            var visible = ScaleVisibility.IsVisibleAtScale(op, denominators[0]);
            for (var i = 1; i < denominators.Length; i++)
            {
                if (ScaleVisibility.IsVisibleAtScale(op, denominators[i]) != visible)
                {
                    return keys
                        .GroupBy(key => (key.Y, key.ScaleClass))
                        .OrderBy(group => group.Key.Y)
                        .Select(group => (IReadOnlyList<TileKey>)group.ToList())
                        .ToList();
                }
            }
        }

        return [keys];
    }

    private static IReadOnlyDictionary<TileKey, SKImage> RasterizeMetatileGroup(
        VectorScene scene,
        BaseSpatialIndex? baseIndex,
        IReadOnlyList<TileKey> keys,
        float deviceScale)
    {
        using var activity = S100Diag.Telemetry.ActivitySource.StartActivity(
            "s100.render.tile.rasterize", ActivityKind.Internal);
        activity?.SetTag("s100.render.tile.keys", FormatTileKeys(keys));
        activity?.SetTag("s100.render.tile.count", keys.Count);
        activity?.SetTag("s100.render.tile.band", keys[0].Band);

        var xMin = keys.Min(key => key.X);
        var xMax = keys.Max(key => key.X);
        var yMin = keys.Min(key => key.Y);
        var yMax = keys.Max(key => key.Y);
        var band = keys[0].Band;
        var spanX = xMax - xMin + 1;
        var spanY = yMax - yMin + 1;
        var corePx = CorePixelSize(deviceScale);
        var gutterPx = GutterPixelSize(deviceScale);
        var tilePx = TilePixelSize(deviceScale);
        var widthPx = spanX * corePx + 2 * gutterPx;
        var heightPx = spanY * corePx + 2 * gutterPx;
        if (widthPx > MaxImageDimension || heightPx > MaxImageDimension)
        {
            activity?.SetTag("s100.render.tile.rasterize.fallback", "dimension");
            RecordMetatileFallback("dimension");
            return RasterizeIndividualTiles(scene, baseIndex, keys, deviceScale);
        }

        var first = new TileKey(band, xMin, yMin);
        var last = new TileKey(band, xMax, yMax);
        var (blockMinX, _, _, blockMaxY) = TileGrid.TileWorldBounds(first);
        var (_, blockMinY, blockMaxX, _) = TileGrid.TileWorldBounds(last);
        var gutterWorld = gutterPx * (blockMaxX - blockMinX) / (spanX * corePx);
        var fullMinX = blockMinX - gutterWorld;
        var fullMinY = blockMinY - gutterWorld;
        var fullMaxX = blockMaxX + gutterWorld;
        var fullMaxY = blockMaxY + gutterWorld;
        // A block that the adjacent world copies reach into is rasterised tile
        // by tile: each tile's gutter takes the copies' ops but its core must
        // not (see DrawAdjacentWorldOps), and in one shared bitmap an inner
        // tile's gutter is its neighbour's core. Only tiles at a dataset's
        // frame edge are affected.
        if (baseIndex is not null && HasAdjacentWorldOps(baseIndex, fullMinX, fullMinY, fullMaxX, fullMaxY))
        {
            activity?.SetTag("s100.render.tile.rasterize.fallback", "world_copy");
            RecordMetatileFallback("world_copy");
            return RasterizeIndividualTiles(scene, baseIndex, keys, deviceScale);
        }

        var scopedScene = ScopeSceneForBounds(
            scene, baseIndex, fullMinX, fullMinY, fullMaxX, fullMaxY);
        activity?.SetTag("s100.render.tile.candidate_operations", scopedScene.Ops.Count);
        activity?.SetTag("s100.render.tile.width_px", widthPx);
        activity?.SetTag("s100.render.tile.height_px", heightPx);

        var (minLon, minLat) = WebMercator.ToLonLat(
            fullMinX, fullMinY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(
            fullMaxX, fullMaxY, clampLatitude: false);
        var viewport = new CoreViewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = widthPx,
            HeightPixels = heightPx,
            ScaleDenominator = TileScaleDenominator(scene, keys[0]),
        };

        var renderer = new SkiaDisplayListRenderer
        {
            Background = SceneRgbaColor.Transparent,
            HonorScaleVisibility = true,
            EnableSeamWrap = false,
        };

        var rasterStart = Stopwatch.GetTimestamp();
        using var bitmap = renderer.Render(scopedScene, viewport);

        S100Diag.Telemetry.MetatileRasterizeDuration.Record(
            Stopwatch.GetElapsedTime(rasterStart).TotalMilliseconds);

        var sliceStart = Stopwatch.GetTimestamp();
        var result = new Dictionary<TileKey, SKImage>(keys.Count);
        try
        {
            foreach (var key in keys)
            {
                // Each tile keeps its core plus the apron, cut from the block
                // at the same pixels a single-tile raster would hold.
                var left = gutterPx - ApronPx + (key.X - xMin) * corePx;
                var top = gutterPx - ApronPx + (key.Y - yMin) * corePx;
                var subset = new SKBitmap();
                if (!bitmap.ExtractSubset(
                        subset, SKRectI.Create(left, top, tilePx, tilePx)))
                {
                    subset.Dispose();
                    throw new InvalidOperationException(
                        $"Could not slice metatile output for {key}.");
                }

                using (subset)
                using (var copy = subset.Copy())
                {
                    result.Add(key, SKImage.FromBitmap(copy));
                }
            }
        }
        catch
        {
            DisposeImages(result.Values);
            throw;
        }

        S100Diag.Telemetry.MetatileSliceDuration.Record(
            Stopwatch.GetElapsedTime(sliceStart).TotalMilliseconds);
        S100Diag.Telemetry.MetatileTiles.Record(keys.Count);
        S100Diag.Telemetry.MetatileJobs.Add(1);
        return result;
    }

    private static IReadOnlyDictionary<TileKey, SKImage> RasterizeIndividualTiles(
        VectorScene scene,
        BaseSpatialIndex? baseIndex,
        IReadOnlyList<TileKey> keys,
        float deviceScale)
    {
        var result = new Dictionary<TileKey, SKImage>(keys.Count);
        try
        {
            foreach (var key in keys)
            {
                using var bitmap = RasterizeTile(scene, baseIndex, key, deviceScale);
                result.Add(key, SKImage.FromBitmap(bitmap));
            }
        }
        catch
        {
            DisposeImages(result.Values);
            throw;
        }

        return result;
    }

    private static void DisposeImages(IEnumerable<SKImage> images)
    {
        foreach (var image in images)
        {
            image.Dispose();
        }
    }

    /// <summary>
    /// The scale denominator <paramref name="key"/>'s tile is rasterised at:
    /// one inside its SCAMIN scale class (<see cref="TileScaleClass"/>, the
    /// band's own true scale when that lies inside the class), but no coarser
    /// than the scene's <see cref="VectorScene.ScaleMinimumCap"/>.
    /// </summary>
    /// <remarks>
    /// The scale class makes each op's own scale limits exact at the live scale
    /// (#774). The cap is kept as well: the live map snaps to the log-nearest
    /// band, so a band's tiles are shown at display scales up to √2 finer than
    /// the band's own. When the band's
    /// denominator lies past the cell's out-of-band cap (S-101 PS §4.6: the
    /// cell's <c>minimumDisplayScale</c>) while the live scale is still inside
    /// it, rasterising at the band's denominator culls every capped op and the
    /// cell's line work vanishes from part of its own band (#761: 1:20 000 at
    /// 46.8°N snaps to a band at about 1:23 300, past a 1:22 000 cap). Clamping
    /// to the cap keeps each op's own SCAMIN as before; the cap is applied
    /// against the live scale instead (<see cref="IsPastScaleMinimumCap"/>).
    /// </remarks>
    internal static double TileScaleDenominator(VectorScene scene, TileKey key)
    {
        var denominator = TileScaleClass.Denominator(
            TileScaleClass.Thresholds(scene), key.Band, BandDenominator(key), key.ScaleClass);
        return scene.ScaleMinimumCap is double cap ? Math.Min(denominator, cap) : denominator;
    }

    /// <summary>The true scale denominator of <paramref name="key"/>'s band at the tile's centre.</summary>
    internal static double BandDenominator(TileKey key)
    {
        var (minX, minY, maxX, maxY) = TileGrid.TileWorldBounds(key);
        return S100VectorSceneRenderer.ScaleDenominatorFor(
            (minX + maxX) * 0.5, (minY + maxY) * 0.5, TileGrid.ResolutionForBand(key.Band));
    }

    /// <summary>
    /// <paramref name="key"/> with the SCAMIN scale class it has when its band
    /// is shown at <paramref name="ratio"/> times the band's resolution (see
    /// <see cref="TileScaleClass"/>).
    /// </summary>
    internal static TileKey WithScaleClass(VectorScene scene, TileKey key, double ratio)
    {
        var thresholds = TileScaleClass.Thresholds(scene);
        if (thresholds.Length == 0)
        {
            return key with { ScaleClass = 0 };
        }

        return key with
        {
            ScaleClass = TileScaleClass.For(thresholds, key.Band, BandDenominator(key), ratio),
        };
    }

    /// <summary>
    /// Whether the live display scale at (<paramref name="centerX"/>,
    /// <paramref name="centerY"/>) and <paramref name="resolution"/> is past
    /// <paramref name="scene"/>'s <see cref="VectorScene.ScaleMinimumCap"/>, so
    /// none of its ops may draw. <see langword="false"/> without a scene or cap.
    /// </summary>
    internal static bool IsPastScaleMinimumCap(VectorScene? scene, double centerX, double centerY, double resolution) =>
        scene?.ScaleMinimumCap is double cap
        && S100VectorSceneRenderer.ScaleDenominatorFor(centerX, centerY, resolution) > cap;

    private static VectorScene ScopeSceneForTiles(
        VectorScene scene,
        BaseSpatialIndex? baseIndex,
        IReadOnlyList<TileKey> keys)
    {
        var bounds = keys.Select(TileGrid.TileWorldBounds).ToList();
        var bandResolution = TileGrid.ResolutionForBand(keys[0].Band);
        var gutterWorld = GutterDip * bandResolution;
        return ScopeSceneForBounds(
            scene,
            baseIndex,
            bounds.Min(bounds => bounds.MinX) - gutterWorld,
            bounds.Min(bounds => bounds.MinY) - gutterWorld,
            bounds.Max(bounds => bounds.MaxX) + gutterWorld,
            bounds.Max(bounds => bounds.MaxY) + gutterWorld);
    }

    private static VectorScene ScopeSceneForBounds(
        VectorScene scene,
        BaseSpatialIndex? baseIndex,
        double minX,
        double minY,
        double maxX,
        double maxY) =>
        baseIndex is null
            ? scene
            : new VectorScene(baseIndex.Query(minX, minY, maxX, maxY));

    /// <summary>Per-layer tiling state, held in a weak table keyed by layer.</summary>
    private sealed class TileState
    {
        public TileState(object layer)
        {
            Owner = new WeakReference<object>(layer);

            // Charged to the process-wide pool; once this state is collected the
            // pool releases the tiles itself (the pool, not the state, holds them).
            Cache = new TileCache(TileMemoryPool.Raster, owner: this);
        }

        /// <summary>
        /// Weak back-reference to this state's owning layer (the weak-table key),
        /// so a worker-thread publish can reach the layer's redraw sink without a
        /// strong reference that would pin the entry.
        /// </summary>
        public readonly WeakReference<object> Owner;

        public readonly object Sync = new();

        // The session options this layer's tiles follow: the process-wide
        // options for a layer bound to one scene, its session's for a chart
        // layer (see BindStack).
        public ChartSessionOptions Options = ChartEngine.Options;

        // The chart stack a chart layer draws (map rendering slice 8), or null
        // for a layer bound to one scene. Under Sync.
        public ResolvedChartStack? Chart;

        // Chart layer only, under Sync: each slot's stack layers with ops
        // (per stack), the last plan of each slot (per live resolution and
        // cap state), the plan behind every key handed out, and the last
        // tile each slot showed exactly (the stand-in while it re-rasters).
        public readonly Dictionary<(int Band, int X, int Y), SlotContent> SlotContents = new();
        public readonly Dictionary<(int Band, int X, int Y), PlanMemoEntry> PlanMemo = new();
        public readonly Dictionary<TileKey, SlotPlan> Plans = new();
        public readonly Dictionary<(int Band, int X, int Y), TileKey> ShownBySlot = new();

        public VectorScene? Scene;

        // Whether Scene is sparse enough to draw live each frame, skipping the
        // tile pipeline (see IsLiveBaseScene). Set with Scene by BindScene.
        public bool LiveBase;

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
        public VectorScene? OverlayScene => Overlay?.Scene;

        // The overlay with its spatial index and per-frame scratch (see
        // OverlaySource). Null until a scene is bound.
        public OverlaySource? Overlay;
        public long Generation;

        // Persistent disk-cache namespace for this layer's current style state
        // (folds productLayerSet + styleStateHash). Null when the disk cache is
        // disabled or no style key was supplied.
        public string? DiskNamespace;

        public readonly TileCache Cache;
        public readonly HashSet<TileKey> InFlight = new();

        // Memoised IsTileHidden answers for the layer's current hidden region
        // (issue #691); cleared when the region changes. Render thread, under Sync.
        public HiddenCoverage? HiddenMemoOwner;
        public readonly Dictionary<TileKey, bool> HiddenMemo = new();

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
        // GpuRegistryEntry / GpuRegistry.
        public GpuRegistryEntry? GpuEntry;

        // Visible misses drain before speculative (predicted) tiles.
        public readonly HashSet<TileKey> PendingVisible = new();
        public readonly HashSet<TileKey> PendingPredicted = new();

        // Idle cross-band (±1) pre-warm tiles (issue #428): the lowest-priority
        // tier, drained only after PendingVisible and PendingPredicted are empty.
        // Populated only when the layer is otherwise idle (no cold visible misses
        // this frame, cache headroom to spare); rebuilt (cancelled) every frame.
        public readonly HashSet<TileKey> PendingCrossBand = new();

        // Exact tile relevance for the latest viewport epoch. Workers consult
        // these sets before raster and publication so an old-epoch job can be
        // promoted, demoted, or discarded without cancelling still-useful work.
        public readonly HashSet<TileKey> CurrentVisible = new();
        public readonly HashSet<TileKey> CurrentSpeculative = new();
        public TileViewport? CurrentViewport;
        public long ViewportEpoch;

        // First-enqueue Stopwatch ticks for each cold visible tile still awaiting
        // publish, so the worker can record end-to-end cold latency (queue wait +
        // rasterise) when it lands. Entry added the first frame a tile is enqueued
        // visible-cold; removed on publish. Bounded by the visible tile count.
        public readonly Dictionary<TileKey, long> VisibleEnqueueTicks = new();

        // Reusable scratch for pruning VisibleEnqueueTicks without per-frame
        // allocation (cannot mutate a dictionary while enumerating its keys).
        public readonly List<TileKey> EnqueuePruneScratch = new();

        // Speculatively-rasterised tiles still resident and not yet shown,
        // with the Stopwatch timestamp each was published at; a later visible
        // frame that finds one scores a prediction hit, and one that leaves
        // the cache or expires unshown is wasted (map rendering slice 9).
        public readonly Dictionary<TileKey, long> PredictedInCache = new();
        public long LastPredictionSweep;

        // The map's demand planner, for a chart layer (map rendering slice 9):
        // its predicted views add tile demand, and it keeps the hit and waste
        // counts. Null for a layer outside a chart session.
        public Prediction.DemandPlanner? Planner;

        // Slots drawn last frame and this frame, to find the slots that just
        // became visible (map rendering slice 9's slot hit rate).
        public HashSet<(int Band, int X, int Y)> ShownSlots = new();
        public HashSet<(int Band, int X, int Y)> PreviousShownSlots = new();

        // The view each planner-predicted pending tile was planned for, so its
        // job ranks by distance from that view's centre rather than the
        // current one (a destination is far from where the view is now).
        public readonly Dictionary<TileKey, (double X, double Y, double Resolution)> PredictedCentres = new();

        // Viewport-centre velocity EMA (EPSG:3857 m/s) for the prediction fan.
        public double VelocityX;
        public double VelocityY;
        public double LastCenterX;
        public double LastCenterY;
        public long LastCenterTimestamp;
        public bool HasLastCenter;

        // The layer's interests in scheduler jobs, one per pending tile (see
        // SyncTileJobs). Under Sync.
        public readonly Dictionary<TileKey, WorkInterest<bool>> Jobs = new();
        public readonly List<TileKey> JobPruneScratch = new();

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

        // Settle tracking (map rendering slice 1): what the last live frame of
        // this layer showed, written only while SettleTrackingEnabled. Under Sync.
        public SettleStamp? Settle;
    }
}
