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
/// <para>Gated by the <c>S100_VECTOR_PICTURE_SNAPSHOT</c> environment variable
/// (default off) for A/B comparison, mirroring
/// <c>S100_VECTOR_PATH_CACHE</c> / <c>S100_VECTOR_SIMPLIFY_PX</c>.</para>
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
    /// True when the picture-snapshot fast path is enabled (env
    /// <c>S100_VECTOR_PICTURE_SNAPSHOT</c> is set to a truthy value). Default
    /// off until proven, so the renderer can be A/B'd against the path-cache
    /// baseline with the perf harness.
    /// </summary>
    public static bool Enabled { get; } =
        (Environment.GetEnvironmentVariable("S100_VECTOR_PICTURE_SNAPSHOT") ?? string.Empty)
            is "1" or "true" or "TRUE" or "True";

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

    private sealed class SnapshotState
    {
        public readonly object Sync = new();
        public SKImage? Image;
        public double Resolution;
        public double RecordCenterX;
        public double RecordCenterY;
        public double RecordWidth;
        public double RecordHeight;
        public int FeatureCount = -1;

        public SnapshotAnchor ToAnchor() =>
            new(RecordCenterX, RecordCenterY, RecordWidth, RecordHeight, Resolution, FeatureCount);
    }
}
