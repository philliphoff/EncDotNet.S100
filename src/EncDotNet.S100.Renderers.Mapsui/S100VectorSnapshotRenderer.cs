using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A Mapsui <i>custom layer renderer</i> that rasterizes a settled vector layer's
/// entire drawing into a single device-resolution <see cref="SKImage"/> once per
/// (resolution, feature-set) and, on subsequent pans at the same resolution,
/// blits it under a translation instead of re-iterating and re-drawing every
/// feature.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The translation-invariant path cache
/// (<see cref="CachedVectorStyleRenderer"/>) removes per-pan path
/// <i>building</i>, but Mapsui still walks every visible feature each frame
/// (<c>GetFeatures</c> + the per-feature <c>Save</c>/<c>Draw</c>/<c>Restore</c>
/// dispatch loop) and re-issues every fill/stroke. Profiling the AU IC-ENC
/// harbour cell <c>101AU005PDB01</c> showed pure pans paying ~500&#160;ms of
/// <see cref="VectorStyle"/> draw over ~1,600 features even with the path cache
/// on — a per-frame floor dominated by polygon fills (which line simplification
/// cannot touch). Recording the settled layer to a raster image collapses those
/// ~1,600 per-feature draws into one textured blit, so a pan becomes a single
/// translated <see cref="SKCanvas.DrawImage(SKImage,SKRect,SKSamplingOptions)"/>
/// independent of feature count. (An earlier prototype recorded into an
/// <c>SKPicture</c> instead, but replaying a picture still re-rasterizes every
/// recorded path each frame, so frame time was unchanged — the cost merely moved
/// out of the per-style timer into un-attributed compositor overhead. A raster
/// snapshot is O(pixels), not O(features), which is the actual win.)
/// </para>
/// <para>
/// <b>Coordinate frame.</b> The image is recorded against a <i>record
/// viewport</i> centred on the current view but enlarged by
/// <see cref="MarginPx"/> on every edge, so a pan reveals already-recorded
/// content out to the margin before a re-record is needed. Because the
/// world→screen projection is a pure affine translation at constant resolution
/// and zero rotation, blitting the image into the destination rectangle whose
/// top-left is
/// <c>Tx = (recordCenterX − centerX)/res + (Width − recordWidth)/2</c>,
/// <c>Ty = (centerY − recordCenterY)/res + (Height − recordHeight)/2</c>
/// reproduces Mapsui's transform exactly. The image is captured at the on-screen
/// device scale (<c>canvas.TotalMatrix.ScaleX</c>) and drawn back into a
/// DIP-space rectangle, so the on-screen canvas matrix keeps it crisp on HiDPI
/// surfaces. A zoom changes the resolution and forces a re-record (crisp, and
/// far rarer than pans).
/// </para>
/// <para>
/// <b>Fidelity.</b> Recording dispatches each (feature, style) to the same
/// style renderers Mapsui's <c>MapRenderer</c> would — resolved by reflecting
/// its registered <c>_styleRenderers</c> dictionary so the cached vector
/// renderer, the anchored pattern-fill renderer and any active instrumentation
/// wrappers are all honoured — in the same draw order
/// (<see cref="ILayer.SortFeatures"/> over <see cref="ILayer.GetFeatures"/>,
/// layer style first then per-feature styles). The replayed pixels are therefore
/// identical to a live frame at the <i>same</i> resolution (modulo a single
/// linear resample during sub-pixel pans). A raster recorded at one resolution
/// is only reused (scaled) at another when no scale-visibility boundary
/// (<c>MinVisible</c>/<c>MaxVisible</c>, e.g. the S-101 out-of-band cap) lies
/// between the two resolutions; otherwise the layer is drawn live for that frame
/// so a feature that is shown at one zoom but hidden at the other never blits
/// from the wrong-resolution image. Rotated viewports likewise fall back to live
/// per-feature drawing (no image), preserving correctness.
/// </para>
/// <para>
/// <b>Lifetime.</b> Snapshot state is held per <see cref="ILayer"/> instance in
/// a <see cref="ConditionalWeakTable{TKey,TValue}"/>, so rebuilding the layer
/// (palette / display-category / dataset change, which the viewer does by
/// producing a fresh layer) discards the snapshot automatically. A change in the
/// layer's visible feature count also forces a re-record. Record and replay are
/// serialised per layer so the off-screen <c>render_to_image</c> path can share
/// the renderer with the on-screen compositor safely.
/// </para>
/// <para>
/// <b>Lifetime.</b> Snapshot state is held per <see cref="ILayer"/> instance in
/// a <see cref="ConditionalWeakTable{TKey,TValue}"/>, so rebuilding the layer
/// (palette / display-category / dataset change, which the viewer does by
/// producing a fresh layer) discards the snapshot automatically. A change in the
/// layer's visible feature count also forces a re-record. Record and replay are
/// serialised per layer so the off-screen <c>render_to_image</c> path can share
/// the renderer with the on-screen compositor safely.
/// </para>
/// <para>Enabled by default; set <c>S100_VECTOR_PICTURE_SNAPSHOT=0</c> (or
/// <c>false</c>) to opt out for A/B comparison, mirroring
/// <c>S100_VECTOR_PATH_CACHE</c> / <c>S100_VECTOR_SIMPLIFY_PX</c>.</para>
/// <para>
/// <b>Off-thread prebuild.</b> The record frame (first frame at a new
/// resolution) re-rasterises the whole layer at device scale and costs
/// <i>more</i> than a single live frame (~650&#160;ms on PDB01) — a one-time
/// stall per zoom level. Setting <c>S100_VECTOR_SNAPSHOT_PREBUILD</c> (opt-in)
/// hides it: the renderer keeps a small per-resolution image cache, blits the
/// nearest existing image <i>scaled</i> for a frame or two on a zoom while the
/// exact-resolution image is rasterised on a background thread (dedicated
/// <see cref="RenderService"/>), and speculatively prebuilds the predicted
/// next zoom bucket(s) after the view settles. See <see cref="PrebuildEnabled"/>.
/// </para>
/// <para>
/// <b>Sustained-pan look-ahead.</b> Under <see cref="PrebuildEnabled"/>, a pan
/// that approaches the recorded margin no longer re-records synchronously on
/// the render thread. Once the pan crosses <see cref="PanRefreshFraction"/> of
/// the active snapshot's margin (while it still fully covers the view), the
/// renderer records a recentred-ahead, <see cref="PanMarginPx"/>-margin image
/// at the same resolution on a background thread and blits the existing
/// (translated) image until it publishes, then swaps in the crisp one. A pan
/// that briefly outruns the look-ahead blits the nearest same-resolution image
/// translated (a transient uncovered leading strip) rather than stalling. See
/// <see cref="PanMarginPx"/> and <see cref="PanRefreshFraction"/>.
/// </para>
/// </remarks>
public static class S100VectorSnapshotRenderer
{
    /// <summary>
    /// The <see cref="ILayer.CustomLayerRendererName"/> value that routes a
    /// layer through this renderer. Set on the S-101 vector layer when the
    /// snapshot is enabled.
    /// </summary>
    public const string RendererName = "s100.vector.snapshot";

    /// <summary>
    /// True when the raster-snapshot fast path is enabled. Enabled by default;
    /// driven by <see cref="RenderingOptimizations.VectorSnapshotEnabled"/> (which
    /// the viewer's <c>Settings → Map</c> section binds, seeded from the legacy
    /// <c>S100_VECTOR_PICTURE_SNAPSHOT</c> env var). Set the env var to a falsy
    /// value (<c>0</c> / <c>false</c>) to pin it off for an A/B run against the
    /// path-cache baseline.
    /// </summary>
    public static bool Enabled => RenderingOptimizations.VectorSnapshotEnabled;

    /// <summary>
    /// True when the <b>off-thread snapshot prebuild</b> is enabled. <b>Enabled by
    /// default</b>; driven by
    /// <see cref="RenderingOptimizations.VectorSnapshotPrebuildEnabled"/> (which
    /// the viewer's <c>Settings → Map</c> section binds, seeded from the legacy
    /// <c>S100_VECTOR_SNAPSHOT_PREBUILD</c> env var). When off the renderer falls
    /// back to the single-image snapshot (one cached image, re-recorded
    /// <i>synchronously</i> on the render thread whenever the resolution or
    /// feature-set changes, and on a pan past the recorded margin).
    /// </summary>
    /// <remarks>
    /// When on, the renderer keeps a small per-resolution cache of recorded
    /// images and hides both the zoom record stall and the sustained-pan record
    /// stall off-thread: on a resolution change (zoom) it either (a) blits an
    /// already-prebuilt image for the new resolution, or (b) blits the nearest
    /// existing image <i>scaled</i> for a frame or two while the real image for
    /// the new resolution is rasterized on a background thread; on a pan it
    /// records a recentred-ahead image at the same resolution off-thread before
    /// the pan reaches the margin edge (see <see cref="PanMarginPx"/> /
    /// <see cref="PanRefreshFraction"/>) — so the ~650&#160;ms on-thread record
    /// stall never blocks the UI. After the view settles, the predicted next zoom
    /// bucket(s) are rasterized in the background so a subsequent zoom lands on a
    /// ready, crisp image.
    /// </remarks>
    public static bool PrebuildEnabled => RenderingOptimizations.VectorSnapshotPrebuildEnabled;

    /// <summary>
    /// Margin, in screen pixels, recorded around the viewport on every edge. A
    /// pan can move up to this many pixels in any direction before the picture
    /// must be re-recorded, so a larger margin trades memory / record cost for
    /// fewer re-records during sustained drags. Read once from
    /// <c>S100_VECTOR_SNAPSHOT_MARGIN</c> (default 256).
    /// </summary>
    public static double MarginPx { get; } = ReadMargin();

    /// <summary>
    /// Margin, in screen pixels, used for <b>off-thread pan re-records</b> (the
    /// prebuild path's sustained-pan look-ahead). Larger than <see cref="MarginPx"/>
    /// so a single recentred-ahead background record covers roughly a full
    /// viewport of travel before the next re-record is needed, trading memory /
    /// record cost for fewer re-records during a sustained drag. Read once from
    /// <c>S100_VECTOR_SNAPSHOT_PAN_MARGIN</c> (default 512). Only consulted when
    /// <see cref="PrebuildEnabled"/> is true; the cold/settled record still uses
    /// <see cref="MarginPx"/>.
    /// </summary>
    public static double PanMarginPx { get; } = ReadPanMargin();

    /// <summary>
    /// Fraction (0..1) of the active snapshot's recorded margin at which an
    /// off-thread pan re-record is triggered while the entry still fully covers
    /// the viewport. A smaller fraction starts the background re-record earlier
    /// (more lead time, more frequent records); a larger fraction defers it
    /// (risking that a fast pan reaches the margin edge before the recentred
    /// image is ready). Read once from <c>S100_VECTOR_SNAPSHOT_PAN_REFRESH</c>
    /// (default 0.5). Only consulted when <see cref="PrebuildEnabled"/> is true.
    /// </summary>
    public static double PanRefreshFraction { get; } = ReadPanRefresh();

    private static readonly bool Diag =
        (Environment.GetEnvironmentVariable("S100_VECTOR_SNAPSHOT_DIAG") ?? string.Empty)
            is "1" or "true" or "TRUE" or "True";

    private static readonly ConditionalWeakTable<ILayer, SnapshotState> States = new();

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private static IDictionary<Type, IStyleRenderer>? _styleRenderers;
    private static long _iteration;

    /// <summary>Maximum number of per-resolution images retained per layer (LRU).</summary>
    private const int MaxEntries = 6;

    /// <summary>Serialises background rasterisation so prebuilds never oversubscribe the CPU.</summary>
    private static readonly System.Threading.SemaphoreSlim BackgroundGate = new(1, 1);

    /// <summary>
    /// A dedicated <see cref="RenderService"/> for off-thread records so the live
    /// render thread's service caches are never mutated concurrently.
    /// </summary>
    private static readonly Lazy<RenderService> BackgroundRenderService = new(() => new RenderService());

    /// <summary>Monotonic counter used as the LRU recency stamp on cache entries.</summary>
    private static long _tick;

    private static double ReadMargin()
    {
        var raw = Environment.GetEnvironmentVariable("S100_VECTOR_SNAPSHOT_MARGIN");
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            && v >= 0)
        {
            return v;
        }

        return 256.0;
    }

    private static double ReadPanMargin()
    {
        var raw = Environment.GetEnvironmentVariable("S100_VECTOR_SNAPSHOT_PAN_MARGIN");
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            && v >= 0)
        {
            return v;
        }

        return 512.0;
    }

    private static double ReadPanRefresh()
    {
        var raw = Environment.GetEnvironmentVariable("S100_VECTOR_SNAPSHOT_PAN_REFRESH");
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            && v > 0 && v <= 1.0)
        {
            return v;
        }

        return 0.5;
    }

    /// <summary>
    /// Registers this renderer under <see cref="RendererName"/> with Mapsui's
    /// <c>MapRenderer</c>. Idempotent. Registration is unconditional so the
    /// renderer can be toggled live via <see cref="Enabled"/> (a fresh layer is
    /// tagged with <see cref="RendererName"/> only while <see cref="Enabled"/> is
    /// true, and a tagged layer falls back to live per-feature drawing when the
    /// flag is turned off — see <see cref="Render"/>). Call once at startup
    /// (after the style renderers are registered).
    /// </summary>
    internal static void Register()
    {
        MapRenderer.RegisterLayerRenderer(RendererName, Render);
    }

    /// <summary>
    /// The <see cref="CustomLayerRenderer.RenderHandler"/> invoked by Mapsui for
    /// layers tagged with <see cref="RendererName"/>. Records or replays the
    /// layer's picture as appropriate.
    /// </summary>
    public static void Render(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layer);

        // Cross-cell overlap suppression (issue #438 Phase 2): remove from this
        // coarser cell's output the coverage of every finer overlapping cell that
        // is still visible at the live resolution (screen-space; see
        // CoverageClip). Applied around every draw path; zoom-aware so a finer
        // cell that has dropped out of its band leaves no blank hole.
        var clipPaths = CoverageClip.BuildActiveDifferencePaths(layer, viewport, viewport.Resolution);
        if (clipPaths.Count == 0)
        {
            RenderCore(canvas, viewport, layer, renderService);
            return;
        }

        var clipApplied = false;
        try
        {
            // Apply the clip inside the try so that if ClipPath throws (e.g. a
            // degenerate path) the finally still restores the canvas and disposes
            // every path.
            canvas.Save();
            clipApplied = true;
            foreach (var clipPath in clipPaths)
                canvas.ClipPath(clipPath, SKClipOperation.Difference, antialias: true);

            RenderCore(canvas, viewport, layer, renderService);
        }
        finally
        {
            // Restore only if Save() actually ran, so a throw between Save() and
            // the first ClipPath still balances the stack.
            if (clipApplied)
                canvas.Restore();
            foreach (var clipPath in clipPaths)
                clipPath.Dispose();
        }
    }

    private static void RenderCore(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService)
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
            return;
        }

        // The snapshot fast path can be turned off live (Settings → Map). A
        // layer tagged while it was on may still reach here after a runtime
        // disable; draw it live so the toggle takes effect without a reload.
        if (!Enabled)
        {
            DrawLayerLive(canvas, viewport, layer, viewport.ToExtent(), renderService);
            return;
        }

        // Rotated viewports break the translate-only replay; draw live.
        if (viewport.Rotation != 0)
        {
            var queryExtent = viewport.ToExtent();
            DrawLayerLive(canvas, viewport, layer, queryExtent, renderService);
            return;
        }

        if (PrebuildEnabled)
        {
            RenderPrebuild(canvas, viewport, layer, renderService, resolution);
            return;
        }

        var state = States.GetValue(layer, static _ => new SnapshotState());
        lock (state.Sync)
        {
            var featureCount = TotalFeatureCount(layer);
            var snap = state.ToAnchor();
            var valid = state.Image is not null
                && IsSnapshotValid(snap, viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution, featureCount);

            if (!valid)
            {
                Record(canvas, viewport, layer, renderService, state, featureCount);
            }
            else if (Diag)
            {
                var (dx, dy) = PanOffsetPixels(snap, viewport.CenterX, viewport.CenterY, resolution);
                Console.Error.WriteLine($"[VecSnapshot] replay res={resolution:G6} dxPx={Math.Abs(dx):F0} dyPx={Math.Abs(dy):F0} feats={featureCount}");
            }

            var image = state.Image;
            if (image is null)
            {
                return;
            }

            // Translate-only blit: the recorded raster is anchored at the record
            // center; compute the top-left of the record rectangle in the current
            // viewport's pixel space and draw the (device-resolution) image scaled
            // back into that DIP-space rectangle so the on-screen canvas matrix
            // keeps it crisp on HiDPI surfaces.
            var (tx, ty) = ComputeTranslate(state.ToAnchor(), viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution);

            var dest = new SKRect(
                (float)tx,
                (float)ty,
                (float)(tx + state.RecordWidth),
                (float)(ty + state.RecordHeight));

            canvas.DrawImage(image, dest, Sampling);
        }
    }

    /// <summary>
    /// The <see cref="PrebuildEnabled"/> render path: a per-resolution image cache
    /// with off-thread record of new resolutions (scaled-stale blit until ready)
    /// and speculative prebuild of the predicted next zoom bucket(s) after settle.
    /// </summary>
    private static void RenderPrebuild(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService, double resolution)
    {
        var state = States.GetValue(layer, static _ => new SnapshotState());
        var featureCount = TotalFeatureCount(layer);

        var scale = canvas.TotalMatrix.ScaleX;
        if (scale <= 0 || float.IsNaN(scale))
        {
            scale = 1f;
        }

        SKImage? toBlit;
        SKRect dest;
        var drawLive = false;

        lock (state.Sync)
        {
            state.LastDeviceScale = scale;
            UpdateResolutionHistory(state, resolution);

            // Gate the pan look-ahead on actual view motion: a settled view (and
            // the repaint a background publish requests) must not trigger a pan
            // re-record, or the look-ahead could loop while idle.
            var moved = state.HasLastView
                && (Math.Abs(viewport.CenterX - state.LastViewCenterX) + Math.Abs(viewport.CenterY - state.LastViewCenterY)) > resolution * 0.5;
            state.LastViewCenterX = viewport.CenterX;
            state.LastViewCenterY = viewport.CenterY;
            state.HasLastView = true;

            var exact = FindUsableEntry(state, viewport, resolution, featureCount);
            if (exact is not null)
            {
                exact.LastUsedTick = System.Threading.Interlocked.Increment(ref _tick);
                (toBlit, dest) = BlitOf(exact, viewport, resolution);

                if (Diag)
                {
                    var (dx, dy) = PanOffsetPixels(exact.ToAnchor(), viewport.CenterX, viewport.CenterY, resolution);
                    Console.Error.WriteLine($"[VecSnapshot] replay res={resolution:G6} dxPx={Math.Abs(dx):F0} dyPx={Math.Abs(dy):F0} feats={featureCount} entries={state.Entries.Count}");
                }

                SchedulePrebuilds(state, layer, viewport, resolution, featureCount, scale);
                if (moved)
                {
                    MaybeSchedulePanRecord(state, layer, viewport, resolution, featureCount, scale, exact.ToAnchor());
                }
            }
            else
            {
                var staleIndex = SelectStaleAnchor(state.AnchorsSnapshot(), viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution);
                if (staleIndex >= 0)
                {
                    var stale = state.Entries[staleIndex];

                    // A scaled-stale blit reuses a raster recorded at a different
                    // resolution. That is only sound when the set of styles passing
                    // scale visibility cannot differ between the two resolutions;
                    // across the S-101 out-of-band cap (or any MinVisible/MaxVisible
                    // boundary) the recorded image would show the wrong feature set
                    // (e.g. buoys present at one zoom but capped-hidden at the
                    // other). When membership may differ, draw the layer live for
                    // this frame (feature-correct, like the rotated-viewport path)
                    // while the exact-resolution image records off-thread.
                    if (VisibleSetMayDiffer(GetVisibilityThresholds(state, layer, featureCount), stale.Resolution, resolution))
                    {
                        toBlit = null;
                        dest = default;
                        drawLive = true;

                        if (Diag)
                        {
                            Console.Error.WriteLine($"[VecSnapshot] LIVE (scale-band) res={resolution:G6} from={stale.Resolution:G6} feats={featureCount} entries={state.Entries.Count}");
                        }

                        EnsureAsyncRecord(state, layer, viewport, resolution, featureCount, scale);
                    }
                    else
                    {
                        stale.LastUsedTick = System.Threading.Interlocked.Increment(ref _tick);
                        (toBlit, dest) = BlitOf(stale, viewport, resolution);

                        if (Diag)
                        {
                            Console.Error.WriteLine($"[VecSnapshot] STALE blit res={resolution:G6} from={stale.Resolution:G6} feats={featureCount} entries={state.Entries.Count}");
                        }

                        EnsureAsyncRecord(state, layer, viewport, resolution, featureCount, scale);
                    }
                }
                else if (FindNearestSameResolutionEntry(state, viewport, resolution, featureCount) is { } nearest)
                {
                    // A pan outran the look-ahead before the recentred image was
                    // ready. Blit the nearest same-resolution entry translated —
                    // it may leave a transient uncovered leading strip over the
                    // basemap — and ensure a recentred pan record is in flight,
                    // rather than freezing the render thread on a synchronous
                    // re-record (the old jitter source).
                    nearest.LastUsedTick = System.Threading.Interlocked.Increment(ref _tick);
                    (toBlit, dest) = BlitOf(nearest, viewport, resolution);

                    if (Diag)
                    {
                        var (dx, dy) = PanOffsetPixels(nearest.ToAnchor(), viewport.CenterX, viewport.CenterY, resolution);
                        Console.Error.WriteLine($"[VecSnapshot] PAN-UNCOVERED blit res={resolution:G6} dxPx={Math.Abs(dx):F0} dyPx={Math.Abs(dy):F0} feats={featureCount} entries={state.Entries.Count}");
                    }

                    EnsurePanRecord(state, layer, viewport, resolution, featureCount, scale);
                }
                else
                {
                    // Cold: no usable image at all. Record synchronously on the
                    // render thread (the unavoidable first-ever record) so the
                    // first frame is correct rather than blank.
                    var entry = BuildSnapshotEntry(viewport, layer, scale, renderService, featureCount);
                    AddEntry(state, entry);
                    entry.LastUsedTick = System.Threading.Interlocked.Increment(ref _tick);
                    (toBlit, dest) = BlitOf(entry, viewport, resolution);

                    if (Diag)
                    {
                        Console.Error.WriteLine($"[VecSnapshot] COLD record res={resolution:G6} feats={featureCount}");
                    }

                    SchedulePrebuilds(state, layer, viewport, resolution, featureCount, scale);
                }
            }
        }

        if (drawLive)
        {
            DrawLayerLive(canvas, viewport, layer, viewport.ToExtent(), renderService);
        }
        else if (toBlit is not null)
        {
            canvas.DrawImage(toBlit, dest, Sampling);
        }
    }

    /// <summary>
    /// The geometric parameters of a recorded snapshot, decoupled from SkiaSharp
    /// so the anchoring/validity/translate math can be unit-tested in isolation.
    /// </summary>
    internal readonly record struct SnapshotAnchor(
        double RecordCenterX,
        double RecordCenterY,
        double RecordWidth,
        double RecordHeight,
        double Resolution,
        int FeatureCount);

    /// <summary>
    /// Computes the pan offset, in screen pixels, of the current view centre from
    /// the recorded anchor centre at the recorded resolution.
    /// </summary>
    internal static (double dx, double dy) PanOffsetPixels(SnapshotAnchor anchor, double centerX, double centerY, double resolution) =>
        ((centerX - anchor.RecordCenterX) / resolution, (centerY - anchor.RecordCenterY) / resolution);

    /// <summary>
    /// Returns <c>true</c> when a recorded snapshot can be reused (blitted) for the
    /// supplied viewport: same resolution, same visible feature count, and the view
    /// centre has not panned past the recorded margin on either axis.
    /// </summary>
    internal static bool IsSnapshotValid(SnapshotAnchor anchor, double centerX, double centerY, double width, double height, double resolution, int featureCount)
    {
        if (anchor.Resolution != resolution || anchor.FeatureCount != featureCount)
        {
            return false;
        }

        var marginX = (anchor.RecordWidth - width) / 2.0;
        var marginY = (anchor.RecordHeight - height) / 2.0;
        if (marginX < 0 || marginY < 0)
        {
            return false;
        }

        var (dx, dy) = PanOffsetPixels(anchor, centerX, centerY, resolution);
        return Math.Abs(dx) <= marginX && Math.Abs(dy) <= marginY;
    }

    /// <summary>
    /// Computes the DIP-space top-left at which the recorded image must be blitted
    /// so its anchored world origin lands at the correct screen position for the
    /// current (translated) viewport.
    /// </summary>
    internal static (double tx, double ty) ComputeTranslate(SnapshotAnchor anchor, double centerX, double centerY, double width, double height, double resolution)
    {
        var tx = (anchor.RecordCenterX - centerX) / resolution + (width - anchor.RecordWidth) / 2.0;
        var ty = (centerY - anchor.RecordCenterY) / resolution + (height - anchor.RecordHeight) / 2.0;
        return (tx, ty);
    }

    /// <summary>
    /// <c>true</c> when the set of styles that pass scale visibility
    /// (<c>MinVisible &lt;= resolution &lt;= MaxVisible</c>) <i>could</i> differ
    /// between <paramref name="resolutionA"/> and <paramref name="resolutionB"/>,
    /// i.e. a recorded raster at one resolution must not be reused (even scaled)
    /// at the other because a feature/style that is hidden at one is shown at the
    /// other. This is the case exactly when one of <paramref name="thresholds"/>
    /// (the distinct <c>MinVisible</c>/<c>MaxVisible</c> boundaries present on the
    /// layer) lies within the closed interval spanned by the two resolutions.
    /// Conservative: boundary-touching thresholds count as "may differ" so a
    /// scaled-stale blit is never used across a scale-visibility transition (the
    /// S-101 out-of-band cap being the dominant case). Pure and testable.
    /// </summary>
    internal static bool VisibleSetMayDiffer(IReadOnlyList<double> thresholds, double resolutionA, double resolutionB)
    {
        if (thresholds is null || thresholds.Count == 0 || resolutionA == resolutionB)
        {
            return false;
        }

        var lo = Math.Min(resolutionA, resolutionB);
        var hi = Math.Max(resolutionA, resolutionB);
        for (var i = 0; i < thresholds.Count; i++)
        {
            var t = thresholds[i];
            if (t >= lo && t <= hi)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Collects the distinct, finite scale-visibility boundaries
    /// (<c>MinVisible</c> and <c>MaxVisible</c>) present across the layer's style
    /// and its features' styles. Default-unbounded limits (<c>MinVisible == 0</c>,
    /// <c>MaxVisible == double.MaxValue</c>) are excluded because they never flip
    /// membership. Used by <see cref="VisibleSetMayDiffer"/> to decide whether a
    /// cross-resolution stale blit is membership-safe. Only the in-memory feature
    /// collection is consulted (the S-101 vector layers are <see cref="MemoryLayer"/>);
    /// for any other layer kind an empty set is returned, preserving the prior
    /// scaled-stale-blit behaviour.
    /// </summary>
    private static double[] CollectVisibilityThresholds(ILayer layer)
    {
        var set = new SortedSet<double>();

        void Collect(IStyle? style)
        {
            if (style is null)
            {
                return;
            }

            var min = style.MinVisible;
            if (min > 0 && !double.IsInfinity(min) && min < double.MaxValue)
            {
                set.Add(min);
            }

            var max = style.MaxVisible;
            if (max > 0 && !double.IsInfinity(max) && max < double.MaxValue)
            {
                set.Add(max);
            }

            if (style is StyleCollection collection)
            {
                foreach (var child in collection.Styles)
                {
                    Collect(child);
                }
            }
        }

        Collect(layer.Style);

        if (layer is MemoryLayer memoryLayer && memoryLayer.Features is { } features)
        {
            foreach (var feature in features)
            {
                if (feature.Styles is null)
                {
                    continue;
                }

                foreach (var style in feature.Styles)
                {
                    Collect(style);
                }
            }
        }

        var array = new double[set.Count];
        set.CopyTo(array);
        return array;
    }

    /// <summary>
    /// Returns the layer's cached scale-visibility thresholds, recomputing them
    /// when absent or when the visible feature count has changed (the same guard
    /// that forces a re-record). Caller must hold <c>state.Sync</c>.
    /// </summary>
    private static IReadOnlyList<double> GetVisibilityThresholds(SnapshotState state, ILayer layer, int featureCount)
    {
        if (state.VisibilityThresholds is null || state.ThresholdsFeatureCount != featureCount)
        {
            state.VisibilityThresholds = CollectVisibilityThresholds(layer);
            state.ThresholdsFeatureCount = featureCount;
        }

        return state.VisibilityThresholds;
    }

    /// <summary>
    /// Relative-tolerance equality for two resolutions, so floating jitter in an
    /// otherwise-identical zoom level (and a predicted resolution computed as
    /// <c>current*ratio</c>) map to the same cache bucket.
    /// </summary>
    internal static bool ResolutionsMatch(double a, double b)
    {
        if (a == b)
        {
            return true;
        }

        var scale = Math.Max(Math.Abs(a), Math.Abs(b));
        return scale > 0 && Math.Abs(a - b) <= scale * 1e-9;
    }

    /// <summary>
    /// Generalises <see cref="ComputeTranslate"/> to the case where the recorded
    /// image's resolution differs from the current viewport resolution: the image
    /// is blitted scaled by <c>anchor.Resolution / resolution</c> so a recorded
    /// zoom bucket can be shown (slightly resampled) at a neighbouring zoom while
    /// the crisp image for the current resolution is rasterised. At equal
    /// resolution the scale is 1 and the result matches <see cref="ComputeTranslate"/>.
    /// </summary>
    internal static (double tx, double ty, double destWidth, double destHeight) ComputeBlit(
        SnapshotAnchor anchor, double centerX, double centerY, double width, double height, double resolution)
    {
        var scale = anchor.Resolution / resolution;
        var destWidth = anchor.RecordWidth * scale;
        var destHeight = anchor.RecordHeight * scale;
        var tx = (anchor.RecordCenterX - centerX) / resolution + width / 2.0 - destWidth / 2.0;
        var ty = (centerY - anchor.RecordCenterY) / resolution + height / 2.0 - destHeight / 2.0;
        return (tx, ty, destWidth, destHeight);
    }

    /// <summary>
    /// <c>true</c> when an entry recorded at <paramref name="anchor"/> may be
    /// blitted directly (no resample) for the current viewport: matching feature
    /// count, resolution within <see cref="ResolutionsMatch"/> tolerance, and the
    /// view centre still inside the recorded margin.
    /// </summary>
    internal static bool IsEntryUsable(SnapshotAnchor anchor, double centerX, double centerY, double width, double height, double resolution, int featureCount)
    {
        if (anchor.FeatureCount != featureCount || !ResolutionsMatch(anchor.Resolution, resolution))
        {
            return false;
        }

        var marginX = (anchor.RecordWidth - width) / 2.0;
        var marginY = (anchor.RecordHeight - height) / 2.0;
        if (marginX < 0 || marginY < 0)
        {
            return false;
        }

        var (dx, dy) = PanOffsetPixels(anchor, centerX, centerY, resolution);
        return Math.Abs(dx) <= marginX && Math.Abs(dy) <= marginY;
    }

    /// <summary>
    /// <c>true</c> when the (possibly scaled) blit of <paramref name="anchor"/>
    /// fully covers the current viewport rectangle, i.e. the recorded content
    /// fills the screen with no uncovered margin — the precondition for using it
    /// as a scaled-stale source while the exact-resolution image is built.
    /// </summary>
    internal static bool SnapshotCoversViewport(SnapshotAnchor anchor, double centerX, double centerY, double width, double height, double resolution)
    {
        var (tx, ty, dw, dh) = ComputeBlit(anchor, centerX, centerY, width, height, resolution);
        const double eps = 0.5;
        return tx <= eps && ty <= eps && tx + dw >= width - eps && ty + dh >= height - eps;
    }

    /// <summary>
    /// Selects the index of the best scaled-stale source among
    /// <paramref name="anchors"/>: the covering entry whose resolution is closest
    /// (by ratio) to the current resolution, or <c>-1</c> when none covers the
    /// viewport.
    /// </summary>
    internal static int SelectStaleAnchor(IReadOnlyList<SnapshotAnchor> anchors, double centerX, double centerY, double width, double height, double resolution)
    {
        var best = -1;
        var bestRatio = double.PositiveInfinity;
        for (var i = 0; i < anchors.Count; i++)
        {
            var a = anchors[i];
            if (a.Resolution <= 0 || !SnapshotCoversViewport(a, centerX, centerY, width, height, resolution))
            {
                continue;
            }

            var ratio = a.Resolution >= resolution ? a.Resolution / resolution : resolution / a.Resolution;
            if (ratio < bestRatio)
            {
                bestRatio = ratio;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// Predicts the resolution(s) of the next likely zoom bucket(s) from the last
    /// two distinct resolutions observed. Returns the resolution one step further
    /// in the observed direction <i>and</i> one step back, so a settle can
    /// prebuild both the continue-zooming and reverse-zoom targets. Empty when no
    /// zoom direction has been observed yet.
    /// </summary>
    internal static IReadOnlyList<double> PredictNeighborResolutions(double current, double previous)
    {
        if (current <= 0 || previous <= 0 || ResolutionsMatch(current, previous))
        {
            return Array.Empty<double>();
        }

        var ratio = current / previous;
        var forward = current * ratio;
        var backward = current / ratio;
        if (ResolutionsMatch(forward, backward))
        {
            return new[] { forward };
        }

        return new[] { forward, backward };
    }

    /// <summary>
    /// Returns the unit pan direction implied by a pixel pan offset
    /// <paramref name="dxPx"/>/<paramref name="dyPx"/> (typically the offset of
    /// the current view centre from the active snapshot's record centre, from
    /// <see cref="PanOffsetPixels"/>), or <c>null</c> when the offset is too small
    /// to imply a direction. Pure and side-effect-free for unit testing.
    /// </summary>
    internal static (double ux, double uy)? PredictPanDirection(double dxPx, double dyPx)
    {
        var mag = Math.Sqrt(dxPx * dxPx + dyPx * dyPx);
        if (mag < 1e-6)
        {
            return null;
        }

        return (dxPx / mag, dyPx / mag);
    }

    /// <summary>
    /// Computes the world-space centre for an off-thread pan re-record placed
    /// <paramref name="leadPx"/> screen pixels <i>ahead</i> of the current view
    /// centre in the pan direction
    /// (<paramref name="dirX"/>/<paramref name="dirY"/>, a unit vector in screen
    /// space). Leading the record into the direction of travel means the new
    /// snapshot's margin extends maximally where the pan is heading, so it stays
    /// valid for the longest possible continued drag. Pure and testable.
    /// </summary>
    internal static (double centerX, double centerY) ComputeRecenterAhead(
        double centerX, double centerY, double dirX, double dirY, double leadPx, double resolution)
    {
        var leadWorld = leadPx * resolution;
        return (centerX + dirX * leadWorld, centerY + dirY * leadWorld);
    }

    /// <summary>
    /// <c>true</c> when the active snapshot still fully covers the viewport
    /// (the view centre is within the recorded margin) <i>but</i> the pan has
    /// progressed past <paramref name="refreshFraction"/> of that margin on
    /// either axis — the hysteresis band in which an off-thread pan re-record
    /// should be started so a recentred, crisp image is ready before the pan
    /// reaches the margin edge. Returns <c>false</c> once the view has already
    /// left the margin (too late to pre-empt) or while still well inside it.
    /// Pure and testable.
    /// </summary>
    internal static bool ShouldRefreshForPan(
        SnapshotAnchor anchor, double centerX, double centerY, double width, double height, double resolution, double refreshFraction)
    {
        var marginX = (anchor.RecordWidth - width) / 2.0;
        var marginY = (anchor.RecordHeight - height) / 2.0;
        if (marginX <= 0 || marginY <= 0)
        {
            return false;
        }

        var (dx, dy) = PanOffsetPixels(anchor, centerX, centerY, resolution);
        var ax = Math.Abs(dx);
        var ay = Math.Abs(dy);

        // Already outside the margin: the entry no longer fully covers, so a
        // pre-emptive refresh is moot (handled by the uncovered-fallback path).
        if (ax > marginX || ay > marginY)
        {
            return false;
        }

        return ax > refreshFraction * marginX || ay > refreshFraction * marginY;
    }

    private static void Record(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService, SnapshotState state, int featureCount)
    {
        var recordWidth = viewport.Width + 2.0 * MarginPx;
        var recordHeight = viewport.Height + 2.0 * MarginPx;
        var recordViewport = viewport with { Width = recordWidth, Height = recordHeight };

        // Rasterize at the on-screen device scale so the blit stays crisp on
        // HiDPI surfaces. The style renderers project world->screen in DIP-space
        // pixel units, so we pre-scale the record canvas by the device factor.
        var scale = canvas.TotalMatrix.ScaleX;
        if (scale <= 0 || float.IsNaN(scale))
        {
            scale = 1f;
        }

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(recordWidth * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(recordHeight * scale));

        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var recordCanvas = surface.Canvas;
        recordCanvas.Clear(SKColors.Transparent);
        recordCanvas.Scale(scale);

        var queryExtent = recordViewport.ToExtent();
        DrawLayerLive(recordCanvas, recordViewport, layer, queryExtent, renderService);

        var image = surface.Snapshot();

        state.Image?.Dispose();
        state.Image = image;
        state.Resolution = viewport.Resolution;
        state.RecordCenterX = viewport.CenterX;
        state.RecordCenterY = viewport.CenterY;
        state.RecordWidth = recordWidth;
        state.RecordHeight = recordHeight;
        state.FeatureCount = featureCount;

        if (Diag)
        {
            Console.Error.WriteLine($"[VecSnapshot] RECORD res={viewport.Resolution:G6} feats={featureCount} rW={recordWidth:F0} rH={recordHeight:F0} scale={scale:F2} px={pixelWidth}x{pixelHeight}");
        }
    }

    /// <summary>
    /// Rasterises the layer's settled drawing for <paramref name="viewport"/> into
    /// a margin-enlarged, device-scaled <see cref="SKImage"/> and returns it as a
    /// self-contained <see cref="SnapshotEntry"/>. Holds no lock and touches no
    /// shared state, so it is safe to call on a background thread (with a
    /// dedicated <paramref name="renderService"/>) as well as synchronously on the
    /// render thread. The produced raster <see cref="SKImage"/> is CPU-backed and
    /// may be blitted on any thread.
    /// </summary>
    private static SnapshotEntry BuildSnapshotEntry(Viewport viewport, ILayer layer, float scale, RenderService renderService, int featureCount)
        => BuildSnapshotEntry(viewport, layer, scale, renderService, featureCount, MarginPx);

    /// <summary>
    /// Overload of <see cref="BuildSnapshotEntry(Viewport,ILayer,float,RenderService,int)"/>
    /// that records with an explicit <paramref name="marginPx"/> (e.g.
    /// <see cref="PanMarginPx"/> for a sustained-pan look-ahead record) instead of
    /// the default <see cref="MarginPx"/>. The supplied <paramref name="viewport"/>
    /// centre is honoured as-is, so callers may pass a recentred-ahead viewport.
    /// </summary>
    private static SnapshotEntry BuildSnapshotEntry(Viewport viewport, ILayer layer, float scale, RenderService renderService, int featureCount, double marginPx)
    {
        var recordWidth = viewport.Width + 2.0 * marginPx;
        var recordHeight = viewport.Height + 2.0 * marginPx;
        var recordViewport = viewport with { Width = recordWidth, Height = recordHeight };

        if (scale <= 0 || float.IsNaN(scale))
        {
            scale = 1f;
        }

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(recordWidth * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(recordHeight * scale));

        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var recordCanvas = surface.Canvas;
        recordCanvas.Clear(SKColors.Transparent);
        recordCanvas.Scale(scale);

        var queryExtent = recordViewport.ToExtent();
        DrawLayerLive(recordCanvas, recordViewport, layer, queryExtent, renderService);

        return new SnapshotEntry
        {
            Image = surface.Snapshot(),
            Resolution = viewport.Resolution,
            RecordCenterX = viewport.CenterX,
            RecordCenterY = viewport.CenterY,
            RecordWidth = recordWidth,
            RecordHeight = recordHeight,
            FeatureCount = featureCount,
            DeviceScale = scale,
        };
    }

    /// <summary>Records the two most recent <i>distinct</i> resolutions for prediction.</summary>
    private static void UpdateResolutionHistory(SnapshotState state, double resolution)
    {
        if (resolution <= 0 || ResolutionsMatch(state.LastResolution, resolution))
        {
            return;
        }

        state.PrevResolution = state.LastResolution;
        state.LastResolution = resolution;
    }

    /// <summary>Finds a cache entry that may be blitted directly for the current viewport.</summary>
    private static SnapshotEntry? FindUsableEntry(SnapshotState state, Viewport viewport, double resolution, int featureCount)
    {
        foreach (var entry in state.Entries)
        {
            if (IsEntryUsable(entry.ToAnchor(), viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution, featureCount))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the same-resolution (and same-feature-count) entry whose record
    /// centre is closest to the current view centre, regardless of whether it
    /// still fully covers the viewport. Used as the uncovered-pan fallback so a
    /// pan that briefly outran the off-thread look-ahead blits existing content
    /// (translated, possibly with an uncovered leading strip) instead of forcing
    /// a synchronous re-record.
    /// </summary>
    private static SnapshotEntry? FindNearestSameResolutionEntry(SnapshotState state, Viewport viewport, double resolution, int featureCount)
    {
        SnapshotEntry? best = null;
        var bestDistSq = double.PositiveInfinity;
        foreach (var entry in state.Entries)
        {
            if (entry.FeatureCount != featureCount || !ResolutionsMatch(entry.Resolution, resolution))
            {
                continue;
            }

            var dx = entry.RecordCenterX - viewport.CenterX;
            var dy = entry.RecordCenterY - viewport.CenterY;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = entry;
            }
        }

        return best;
    }

    /// <summary>Computes the destination rectangle for blitting <paramref name="entry"/>.</summary>
    private static (SKImage image, SKRect dest) BlitOf(SnapshotEntry entry, Viewport viewport, double resolution)
    {
        var (tx, ty, dw, dh) = ComputeBlit(entry.ToAnchor(), viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution);
        var dest = new SKRect((float)tx, (float)ty, (float)(tx + dw), (float)(ty + dh));
        return (entry.Image, dest);
    }

    private static bool CentersCoincident(SnapshotEntry a, SnapshotEntry b)
    {
        var res = b.Resolution > 0 ? b.Resolution : a.Resolution;
        if (res <= 0)
        {
            return true;
        }

        var dxPx = (a.RecordCenterX - b.RecordCenterX) / res;
        var dyPx = (a.RecordCenterY - b.RecordCenterY) / res;
        var threshold = 0.25 * MarginPx;
        return Math.Abs(dxPx) <= threshold && Math.Abs(dyPx) <= threshold;
    }

    /// <summary>
    /// Inserts <paramref name="entry"/> into the per-layer cache, replacing any
    /// existing entry at the same resolution and evicting the least-recently-used
    /// entry (disposing its image) when over <see cref="MaxEntries"/>. Caller must
    /// hold <c>state.Sync</c>.
    /// </summary>
    private static void AddEntry(SnapshotState state, SnapshotEntry entry)
    {
        // Stamp the inserted entry as most-recently-touched so a freshly
        // (pre)built image is never the immediate LRU eviction victim — otherwise
        // a just-prebuilt entry could be evicted before the next frame sees it,
        // causing SchedulePrebuilds to re-request it endlessly (a repaint loop
        // that never reaches idle).
        entry.LastUsedTick = System.Threading.Interlocked.Increment(ref _tick);

        for (var i = state.Entries.Count - 1; i >= 0; i--)
        {
            // Replace an existing entry only when it is at the same resolution
            // AND its record centre is (near-)coincident with the new one — i.e.
            // a re-record of the same view (zoom bucket, or a pan re-record that
            // barely moved). Spatially distinct same-resolution entries (the
            // recentred-ahead pan look-ahead records) are kept so a pan reversal
            // still finds a covering entry; the LRU below bounds the total.
            if (ResolutionsMatch(state.Entries[i].Resolution, entry.Resolution)
                && CentersCoincident(state.Entries[i], entry))
            {
                state.Entries[i].Dispose();
                state.Entries.RemoveAt(i);
            }
        }

        state.Entries.Add(entry);

        while (state.Entries.Count > MaxEntries)
        {
            var lru = 0;
            for (var i = 1; i < state.Entries.Count; i++)
            {
                if (state.Entries[i].LastUsedTick < state.Entries[lru].LastUsedTick)
                {
                    lru = i;
                }
            }

            state.Entries[lru].Dispose();
            state.Entries.RemoveAt(lru);
        }
    }

    /// <summary>
    /// Kicks off (if not already present or in flight) a background rasterisation
    /// of <paramref name="layer"/> at <paramref name="viewport"/>'s resolution,
    /// publishing the result into the cache and requesting a repaint. Caller must
    /// hold <c>state.Sync</c>.
    /// </summary>
    private static void EnsureAsyncRecord(SnapshotState state, ILayer layer, Viewport viewport, double resolution, int featureCount, float scale)
    {
        if (state.InFlight.Contains(resolution))
        {
            return;
        }

        foreach (var entry in state.Entries)
        {
            if (entry.FeatureCount == featureCount && ResolutionsMatch(entry.Resolution, resolution))
            {
                return;
            }
        }

        state.InFlight.Add(resolution);
        var captured = viewport;

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            SnapshotEntry? built = null;
            try
            {
                BackgroundGate.Wait();
                try
                {
                    built = BuildSnapshotEntry(captured, layer, scale, BackgroundRenderService.Value, featureCount);
                }
                finally
                {
                    BackgroundGate.Release();
                }

                lock (state.Sync)
                {
                    AddEntry(state, built);
                    built = null;
                    state.InFlight.Remove(resolution);
                }

                if (Diag)
                {
                    Console.Error.WriteLine($"[VecSnapshot] PUBLISH res={resolution:G6} feats={featureCount}");
                }

                VectorLayerRepaint.Request(layer);
            }
            catch (Exception ex)
            {
                built?.Dispose();
                lock (state.Sync)
                {
                    state.InFlight.Remove(resolution);
                }

                if (Diag)
                {
                    Console.Error.WriteLine($"[VecSnapshot] PREBUILD FAILED res={resolution:G6}: {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// Sustained-pan look-ahead: while the active entry still covers the viewport
    /// but the pan has crossed <see cref="PanRefreshFraction"/> of its margin,
    /// records a recentred-ahead, <see cref="PanMarginPx"/>-margin snapshot at the
    /// <i>same</i> resolution on a background thread so a crisp image is ready
    /// before the pan reaches the margin edge — turning the previously
    /// synchronous on-thread re-record into an off-thread one. At most one pan
    /// record is in flight at a time. Caller must hold <c>state.Sync</c>.
    /// </summary>
    private static void MaybeSchedulePanRecord(
        SnapshotState state, ILayer layer, Viewport viewport, double resolution, int featureCount, float scale, SnapshotAnchor active)
    {
        if (!ShouldRefreshForPan(active, viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution, PanRefreshFraction))
        {
            return;
        }

        var (dx, dy) = PanOffsetPixels(active, viewport.CenterX, viewport.CenterY, resolution);
        var dir = PredictPanDirection(dx, dy);
        if (dir is not { } d)
        {
            return;
        }

        // Lead the new record into the direction of travel by (about) one of the
        // active entry's margins, so the recentred snapshot still covers the
        // current view (its trailing margin reaches back over it) while extending
        // farthest where the pan is heading.
        var leadPx = Math.Min((active.RecordWidth - viewport.Width) / 2.0, (active.RecordHeight - viewport.Height) / 2.0);
        var (cx, cy) = ComputeRecenterAhead(viewport.CenterX, viewport.CenterY, d.ux, d.uy, leadPx, resolution);

        EnsurePanRecord(state, layer, viewport with { CenterX = cx, CenterY = cy }, resolution, featureCount, scale);
    }

    /// <summary>
    /// Kicks off (unless one is already in flight) a background rasterisation of
    /// <paramref name="layer"/> at the recentred <paramref name="viewport"/>'s
    /// centre and resolution, using <see cref="PanMarginPx"/>, publishing the
    /// result into the cache and requesting a single repaint. Caller must hold
    /// <c>state.Sync</c>.
    /// </summary>
    private static void EnsurePanRecord(SnapshotState state, ILayer layer, Viewport viewport, double resolution, int featureCount, float scale)
    {
        if (state.PanRecordInFlight)
        {
            return;
        }

        state.PanRecordInFlight = true;
        var captured = viewport;

        if (Diag)
        {
            Console.Error.WriteLine($"[VecSnapshot] PAN-REFRESH res={resolution:G6} cx={captured.CenterX:F1} cy={captured.CenterY:F1} margin={PanMarginPx:F0} feats={featureCount}");
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            SnapshotEntry? built = null;
            try
            {
                BackgroundGate.Wait();
                try
                {
                    built = BuildSnapshotEntry(captured, layer, scale, BackgroundRenderService.Value, featureCount, PanMarginPx);
                }
                finally
                {
                    BackgroundGate.Release();
                }

                lock (state.Sync)
                {
                    AddEntry(state, built);
                    built = null;
                    state.PanRecordInFlight = false;
                }

                if (Diag)
                {
                    Console.Error.WriteLine($"[VecSnapshot] PAN-PUBLISH res={resolution:G6} feats={featureCount}");
                }

                VectorLayerRepaint.Request(layer);
            }
            catch (Exception ex)
            {
                built?.Dispose();
                lock (state.Sync)
                {
                    state.PanRecordInFlight = false;
                }

                if (Diag)
                {
                    Console.Error.WriteLine($"[VecSnapshot] PAN-RECORD FAILED res={resolution:G6}: {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// After a settled (exact) frame, speculatively prebuilds the predicted next
    /// zoom bucket(s) so a subsequent zoom lands on a ready, crisp image. Caller
    /// must hold <c>state.Sync</c>.
    /// </summary>
    private static void SchedulePrebuilds(SnapshotState state, ILayer layer, Viewport viewport, double resolution, int featureCount, float scale)
    {
        var predicted = PredictNeighborResolutions(state.LastResolution, state.PrevResolution);
        foreach (var nextResolution in predicted)
        {
            if (nextResolution <= 0 || double.IsInfinity(nextResolution) || double.IsNaN(nextResolution))
            {
                continue;
            }

            EnsureAsyncRecord(state, layer, viewport with { Resolution = nextResolution }, nextResolution, featureCount, scale);
        }
    }


    /// <summary>
    /// Iterates the layer's visible features and dispatches each (feature, style)
    /// to its Skia style renderer, reproducing <c>MapRenderer</c>'s per-feature
    /// draw loop (layer style first, then per-feature styles) onto
    /// <paramref name="target"/>. Used both to fill the recording canvas and as
    /// the rotated-viewport live fallback.
    /// </summary>
    private static void DrawLayerLive(SKCanvas target, Viewport viewport, ILayer layer, MRect queryExtent, RenderService renderService)
    {
        var renderers = StyleRenderers;
        if (renderers is null)
        {
            return;
        }

        var resolution = viewport.Resolution;
        var iteration = System.Threading.Interlocked.Increment(ref _iteration);
        var features = layer.SortFeatures(layer.GetFeatures(queryExtent, resolution)).ToList();

        // The one-shot snapshot record races Mapsui's asynchronous image-source
        // fetch loop. ImageStyleRenderer draws nothing when the (SVG) image bytes
        // are not yet present in the RenderService image cache, so a record taken
        // before that fetch completes would bake out the point symbols (buoys,
        // beacons, lights) permanently — the snapshot stays "valid" and is never
        // re-recorded. Mirror Mapsui's own offscreen rasteriser
        // (RasterizingTileSource, which awaits FetchAllImageDataAsync before
        // RenderToBitmapStream) by registering this layer's image sources
        // synchronously before drawing. svg-content:// / base64-content:// sources
        // resolve in-process, so the fetch completes without blocking on I/O.
        EnsureImageSourcesRegistered(features, renderService);

        if (layer.Style is { } layerStyle)
        {
            foreach (var style in layerStyle.GetStylesToApply(resolution))
            {
                foreach (var feature in features)
                {
                    DrawOne(target, viewport, layer, feature, style, renderService, iteration, renderers);
                }
            }
        }

        foreach (var feature in features)
        {
            var styles = feature.Styles;
            if (styles is null)
            {
                continue;
            }

            foreach (var style in styles)
            {
                if (style is null || !style.ShouldBeApplied(resolution))
                {
                    continue;
                }

                DrawOne(target, viewport, layer, feature, style, renderService, iteration, renderers);
            }
        }
    }

    private static void DrawOne(SKCanvas target, Viewport viewport, ILayer layer, IFeature feature,
        IStyle style, RenderService renderService, long iteration, IDictionary<Type, IStyleRenderer> renderers)
    {
        if (!renderers.TryGetValue(style.GetType(), out var renderer)
            || renderer is not ISkiaStyleRenderer skiaRenderer)
        {
            return;
        }

        var restore = target.Save();
        try
        {
            skiaRenderer.Draw(target, viewport, layer, feature, style, renderService, iteration);
        }
        finally
        {
            target.RestoreToCount(restore);
        }
    }

    internal static void EnsureImageSourcesRegistered(IReadOnlyList<IFeature> features, RenderService renderService)
    {
        var cache = renderService.ImageSourceCache;
        ConcurrentDictionary<string, string>? pending = null;

        foreach (var feature in features)
        {
            var styles = feature.Styles;
            if (styles is null)
            {
                continue;
            }

            foreach (var style in styles)
            {
                if (style is ImageStyle { Image: { } image } && cache.Get(image) is null)
                {
                    pending ??= new ConcurrentDictionary<string, string>();
                    pending[image.Source] = image.SourceId;
                }
            }
        }

        if (pending is null || pending.IsEmpty)
        {
            return;
        }

        try
        {
            cache.FetchAllImageDataAsync(pending).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            if (Diag)
            {
                Console.Error.WriteLine($"[VecSnapshot] image source fetch failed: {ex.Message}");
            }
        }
    }

    private static int TotalFeatureCount(ILayer layer)
    {
        // O(1) total-feature-count guard: a fresh layer instance gets fresh
        // state (re-record) via the weak table, so this only needs to catch
        // in-place feature mutation (e.g. sequential updates) on the same layer.
        if (layer is MemoryLayer ml && ml.Features is ICollection<IFeature> collection)
        {
            return collection.Count;
        }

        return -1;
    }

    /// <summary>
    /// Mapsui's live style-renderer dictionary, resolved once by reflection so
    /// recording honours the same renderers (including the cached vector
    /// renderer, the pattern-fill renderer, and any instrumentation wrappers
    /// installed after this renderer is registered).
    /// </summary>
    private static IDictionary<Type, IStyleRenderer>? StyleRenderers
    {
        get
        {
            var cached = _styleRenderers;
            if (cached is not null)
            {
                return cached;
            }

            var rendererField = typeof(MapRenderer).GetField("_styleRenderers", BindingFlags.NonPublic | BindingFlags.Static);
            if (rendererField?.GetValue(null) is IDictionary<Type, IStyleRenderer> dict)
            {
                _styleRenderers = dict;
                return dict;
            }

            return null;
        }
    }

    /// <summary>
    /// A single recorded raster image plus the geometry needed to blit it. Held in
    /// the per-layer <see cref="SnapshotState.Entries"/> cache.
    /// </summary>
    private sealed class SnapshotEntry : IDisposable
    {
        public required SKImage Image { get; init; }
        public double Resolution { get; init; }
        public double RecordCenterX { get; init; }
        public double RecordCenterY { get; init; }
        public double RecordWidth { get; init; }
        public double RecordHeight { get; init; }
        public int FeatureCount { get; init; }
        public float DeviceScale { get; init; }
        public long LastUsedTick;

        public SnapshotAnchor ToAnchor() =>
            new(RecordCenterX, RecordCenterY, RecordWidth, RecordHeight, Resolution, FeatureCount);

        public void Dispose() => Image.Dispose();
    }

    private sealed class SnapshotState
    {
        public readonly object Sync = new();

        // Legacy single-image path (used when PrebuildEnabled is false). Behaviour
        // is byte-for-byte identical to the shipped renderer.
        public SKImage? Image;
        public double Resolution;
        public double RecordCenterX;
        public double RecordCenterY;
        public double RecordWidth;
        public double RecordHeight;
        public int FeatureCount = -1;

        // Prebuild path: a small per-resolution LRU plus zoom-prediction history
        // and the set of resolutions currently being rasterised off-thread.
        public readonly List<SnapshotEntry> Entries = new();
        public float LastDeviceScale = 1f;
        public double PrevResolution;
        public double LastResolution;
        public readonly HashSet<double> InFlight = new();

        // Cached scale-visibility boundaries (distinct MinVisible/MaxVisible) for
        // the current feature set, used to decide whether a cross-resolution stale
        // blit could paint a feature set inconsistent with the current resolution.
        // Recomputed when the feature count changes (same guard as a re-record).
        public double[]? VisibilityThresholds;
        public int ThresholdsFeatureCount = int.MinValue;

        // Sustained-pan look-ahead: at most one off-thread same-resolution
        // recentred re-record is in flight at a time.
        public bool PanRecordInFlight;

        // Previous frame's view centre, used to gate the pan look-ahead on actual
        // motion so a settled view (including the repaint that a background
        // publish requests) never re-triggers a pan record — which would loop.
        public double LastViewCenterX;
        public double LastViewCenterY;
        public bool HasLastView;

        public SnapshotAnchor ToAnchor() =>
            new(RecordCenterX, RecordCenterY, RecordWidth, RecordHeight, Resolution, FeatureCount);

        public IReadOnlyList<SnapshotAnchor> AnchorsSnapshot()
        {
            var anchors = new SnapshotAnchor[Entries.Count];
            for (var i = 0; i < Entries.Count; i++)
            {
                anchors[i] = Entries[i].ToAnchor();
            }

            return anchors;
        }
    }
}
