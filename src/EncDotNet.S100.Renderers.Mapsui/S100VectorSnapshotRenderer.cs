using System;
using System.Collections.Generic;
using System.Linq;
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
/// identical to a live frame (modulo a single linear resample during sub-pixel
/// pans). Rotated viewports fall back to live per-feature drawing (no image),
/// preserving correctness.
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
    /// set the env var <c>S100_VECTOR_PICTURE_SNAPSHOT</c> to a falsy value
    /// (<c>0</c> / <c>false</c>) to opt out so the renderer can be A/B'd against
    /// the path-cache baseline with the perf harness.
    /// </summary>
    public static bool Enabled { get; } =
        (Environment.GetEnvironmentVariable("S100_VECTOR_PICTURE_SNAPSHOT") ?? string.Empty)
            is not ("0" or "false" or "FALSE" or "False" or "off" or "OFF");

    /// <summary>
    /// True when the <b>off-thread snapshot prebuild</b> is enabled. Opt-in and
    /// <b>off by default</b>; set <c>S100_VECTOR_SNAPSHOT_PREBUILD</c> to a truthy
    /// value (<c>1</c> / <c>true</c> / <c>on</c>) to turn it on. When off, the
    /// renderer behaves exactly as the shipped single-image snapshot: one cached
    /// image, re-recorded <i>synchronously</i> on the render thread whenever the
    /// resolution or feature-set changes.
    /// </summary>
    /// <remarks>
    /// When on, the renderer keeps a small per-resolution cache of recorded
    /// images and, on a resolution change (zoom), either (a) blits an
    /// already-prebuilt image for the new resolution, or (b) blits the nearest
    /// existing image <i>scaled</i> for a frame or two while the real image for
    /// the new resolution is rasterized on a background thread — so the
    /// ~650&#160;ms on-thread record stall (<i>cost B</i>) never blocks the UI.
    /// After the view settles, the predicted next zoom bucket(s) are rasterized
    /// in the background so a subsequent zoom lands on a ready, crisp image.
    /// </remarks>
    public static bool PrebuildEnabled { get; } =
        (Environment.GetEnvironmentVariable("S100_VECTOR_SNAPSHOT_PREBUILD") ?? string.Empty)
            is "1" or "true" or "TRUE" or "True" or "on" or "ON";

    /// <summary>
    /// Optional callback invoked (on a background thread) when an off-thread
    /// prebuild publishes a freshly recorded image, so the host can request a
    /// single repaint that swaps the transient scaled-stale blit for the crisp
    /// image. When <c>null</c> the renderer falls back to
    /// <c>BaseLayer.DataHasChanged()</c> on the recorded layer. The viewer may
    /// set this to marshal a <c>RefreshGraphics()</c> onto the UI thread.
    /// </summary>
    public static Action? RequestRedraw { get; set; }

    /// <summary>
    /// Margin, in screen pixels, recorded around the viewport on every edge. A
    /// pan can move up to this many pixels in any direction before the picture
    /// must be re-recorded, so a larger margin trades memory / record cost for
    /// fewer re-records during sustained drags. Read once from
    /// <c>S100_VECTOR_SNAPSHOT_MARGIN</c> (default 256).
    /// </summary>
    public static double MarginPx { get; } = ReadMargin();

    private static readonly bool s_diag =
        (Environment.GetEnvironmentVariable("S100_VECTOR_SNAPSHOT_DIAG") ?? string.Empty)
            is "1" or "true" or "TRUE" or "True";

    private static readonly ConditionalWeakTable<ILayer, SnapshotState> s_states = new();

    private static readonly SKSamplingOptions s_sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private static IDictionary<Type, IStyleRenderer>? s_styleRenderers;
    private static long s_iteration;

    /// <summary>Maximum number of per-resolution images retained per layer (LRU).</summary>
    private const int MaxEntries = 6;

    /// <summary>Serialises background rasterisation so prebuilds never oversubscribe the CPU.</summary>
    private static readonly System.Threading.SemaphoreSlim s_backgroundGate = new(1, 1);

    /// <summary>
    /// A dedicated <see cref="RenderService"/> for off-thread records so the live
    /// render thread's service caches are never mutated concurrently.
    /// </summary>
    private static readonly Lazy<RenderService> s_backgroundRenderService = new(() => new RenderService());

    /// <summary>Monotonic counter used as the LRU recency stamp on cache entries.</summary>
    private static long s_tick;

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

    /// <summary>
    /// Registers this renderer under <see cref="RendererName"/> with Mapsui's
    /// <c>MapRenderer</c>. Idempotent and a no-op when <see cref="Enabled"/> is
    /// false. Call once at startup (after the style renderers are registered).
    /// </summary>
    public static void Register()
    {
        if (!Enabled)
        {
            return;
        }

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

        if (!viewport.HasSize())
        {
            return;
        }

        var resolution = viewport.Resolution;
        if (resolution <= 0)
        {
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

        var state = s_states.GetValue(layer, static _ => new SnapshotState());
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
            else if (s_diag)
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

            canvas.DrawImage(image, dest, s_sampling);
        }
    }

    /// <summary>
    /// The <see cref="PrebuildEnabled"/> render path: a per-resolution image cache
    /// with off-thread record of new resolutions (scaled-stale blit until ready)
    /// and speculative prebuild of the predicted next zoom bucket(s) after settle.
    /// </summary>
    private static void RenderPrebuild(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService, double resolution)
    {
        var state = s_states.GetValue(layer, static _ => new SnapshotState());
        var featureCount = TotalFeatureCount(layer);

        var scale = canvas.TotalMatrix.ScaleX;
        if (scale <= 0 || float.IsNaN(scale))
        {
            scale = 1f;
        }

        SKImage? toBlit;
        SKRect dest;

        lock (state.Sync)
        {
            state.LastDeviceScale = scale;
            UpdateResolutionHistory(state, resolution);

            var exact = FindUsableEntry(state, viewport, resolution, featureCount);
            if (exact is not null)
            {
                exact.LastUsedTick = System.Threading.Interlocked.Increment(ref s_tick);
                (toBlit, dest) = BlitOf(exact, viewport, resolution);

                if (s_diag)
                {
                    var (dx, dy) = PanOffsetPixels(exact.ToAnchor(), viewport.CenterX, viewport.CenterY, resolution);
                    Console.Error.WriteLine($"[VecSnapshot] replay res={resolution:G6} dxPx={Math.Abs(dx):F0} dyPx={Math.Abs(dy):F0} feats={featureCount} entries={state.Entries.Count}");
                }

                SchedulePrebuilds(state, layer, viewport, resolution, featureCount, scale);
            }
            else
            {
                var staleIndex = SelectStaleAnchor(state.AnchorsSnapshot(), viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution);
                if (staleIndex >= 0)
                {
                    var stale = state.Entries[staleIndex];
                    stale.LastUsedTick = System.Threading.Interlocked.Increment(ref s_tick);
                    (toBlit, dest) = BlitOf(stale, viewport, resolution);

                    if (s_diag)
                    {
                        Console.Error.WriteLine($"[VecSnapshot] STALE blit res={resolution:G6} from={stale.Resolution:G6} feats={featureCount} entries={state.Entries.Count}");
                    }

                    EnsureAsyncRecord(state, layer, viewport, resolution, featureCount, scale);
                }
                else
                {
                    // Cold: no usable image at all. Record synchronously on the
                    // render thread (the unavoidable first-ever record) so the
                    // first frame is correct rather than blank.
                    var entry = BuildSnapshotEntry(viewport, layer, scale, renderService, featureCount);
                    AddEntry(state, entry);
                    entry.LastUsedTick = System.Threading.Interlocked.Increment(ref s_tick);
                    (toBlit, dest) = BlitOf(entry, viewport, resolution);

                    if (s_diag)
                    {
                        Console.Error.WriteLine($"[VecSnapshot] COLD record res={resolution:G6} feats={featureCount}");
                    }

                    SchedulePrebuilds(state, layer, viewport, resolution, featureCount, scale);
                }
            }
        }

        if (toBlit is not null)
        {
            canvas.DrawImage(toBlit, dest, s_sampling);
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

        if (s_diag)
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
    {
        var recordWidth = viewport.Width + 2.0 * MarginPx;
        var recordHeight = viewport.Height + 2.0 * MarginPx;
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

    /// <summary>Computes the destination rectangle for blitting <paramref name="entry"/>.</summary>
    private static (SKImage image, SKRect dest) BlitOf(SnapshotEntry entry, Viewport viewport, double resolution)
    {
        var (tx, ty, dw, dh) = ComputeBlit(entry.ToAnchor(), viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution);
        var dest = new SKRect((float)tx, (float)ty, (float)(tx + dw), (float)(ty + dh));
        return (entry.Image, dest);
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
        entry.LastUsedTick = System.Threading.Interlocked.Increment(ref s_tick);

        for (var i = state.Entries.Count - 1; i >= 0; i--)
        {
            if (ResolutionsMatch(state.Entries[i].Resolution, entry.Resolution))
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
                s_backgroundGate.Wait();
                try
                {
                    built = BuildSnapshotEntry(captured, layer, scale, s_backgroundRenderService.Value, featureCount);
                }
                finally
                {
                    s_backgroundGate.Release();
                }

                lock (state.Sync)
                {
                    AddEntry(state, built);
                    built = null;
                    state.InFlight.Remove(resolution);
                }

                if (s_diag)
                {
                    Console.Error.WriteLine($"[VecSnapshot] PUBLISH res={resolution:G6} feats={featureCount}");
                }

                RequestRepaint(layer);
            }
            catch (Exception ex)
            {
                built?.Dispose();
                lock (state.Sync)
                {
                    state.InFlight.Remove(resolution);
                }

                if (s_diag)
                {
                    Console.Error.WriteLine($"[VecSnapshot] PREBUILD FAILED res={resolution:G6}: {ex.Message}");
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
    /// Requests a single repaint after a background publish. Uses the host-supplied
    /// <see cref="RequestRedraw"/> when set (so the viewer can marshal onto the UI
    /// thread), otherwise falls back to <c>BaseLayer.DataHasChanged()</c>.
    /// </summary>
    private static void RequestRepaint(ILayer layer)
    {
        var redraw = RequestRedraw;
        if (redraw is not null)
        {
            redraw();
            return;
        }

        (layer as BaseLayer)?.DataHasChanged();
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
        var iteration = System.Threading.Interlocked.Increment(ref s_iteration);
        var features = layer.SortFeatures(layer.GetFeatures(queryExtent, resolution)).ToList();

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
            var cached = s_styleRenderers;
            if (cached is not null)
            {
                return cached;
            }

            var rendererField = typeof(MapRenderer).GetField("_styleRenderers", BindingFlags.NonPublic | BindingFlags.Static);
            if (rendererField?.GetValue(null) is IDictionary<Type, IStyleRenderer> dict)
            {
                s_styleRenderers = dict;
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
