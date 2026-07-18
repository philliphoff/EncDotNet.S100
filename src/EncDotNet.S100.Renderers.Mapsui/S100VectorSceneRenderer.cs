using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using SkiaSharp;
using CoreViewport = EncDotNet.S100.Pipelines.Viewport;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics;
using SceneRgbaColor = EncDotNet.S100.Pipelines.RgbaColor;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// The <b>TiledScene ("B") render subsystem, Phase&#160;1</b>: a Mapsui
/// <i>custom layer renderer</i> that draws the chart base plane by rasterising
/// the backend-agnostic <see cref="VectorScene"/> IR directly — bypassing
/// Mapsui's feature / style / layer walk entirely — on a <b>worker thread</b>,
/// then compositing the result on the UI/render thread as a single translated
/// <see cref="SKImage"/> blit. See
/// <c>docs/design/S100-Render-Subsystem-Design.md</c> (§3, §4, Phase&#160;1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is the "B" arm.</b> The "A" arm
/// (<see cref="S100VectorSnapshotRenderer"/>) also collapses a settled layer to
/// a single blittable raster, but it <i>records what Mapsui would draw</i> — it
/// rides <c>GetFeatures</c> / <c>SortFeatures</c> and the per-feature style
/// dispatch to produce the image. This renderer cuts that tie: it consumes the
/// fully-resolved <see cref="VectorScene"/> (world coords in EPSG:3857, sizes in
/// display px, colours resolved, symbols pre-processed, patterns pre-rasterised)
/// and draws it with <see cref="SkiaDisplayListRenderer"/>, so the base plane no
/// longer touches Mapsui's feature/style model at all.
/// </para>
/// <para>
/// <b>Off the synchronous loop (the Phase&#160;1 goal).</b> The expensive
/// rasterisation never runs on the render thread. Each frame the renderer blits
/// the best available image under a pure translation (north-up, constant
/// resolution ⇒ the world→screen projection is an affine translation, identical
/// to the snapshot path's math). When the current image no longer covers the
/// viewport (zoom, or a pan past the recorded <see cref="MarginPx"/> margin) a
/// fresh whole-viewport-plus-margin raster is scheduled on a worker; the stale
/// image keeps blitting (translated) until the new one publishes, at which point
/// <see cref="RequestRedraw"/> marshals a single repaint that swaps it in. Jobs
/// are coalesced latest-wins per layer so a fast gesture never queues a backlog.
/// </para>
/// <para>
/// <b>Single surface (Phase&#160;1 scope).</b> One image per layer covering the
/// viewport + margin — no tile pyramid, no prediction, no disk cache yet (those
/// are Phases&#160;2–4). A rotated viewport (course-up) breaks the translate-only
/// blit and is out of v1 scope, so it falls back to drawing nothing for that
/// frame (north-up is the only supported orientation).
/// </para>
/// <para>
/// <b>Fidelity.</b> The scene bound to the layer via <see cref="BindScene"/> is
/// built by <see cref="MapsuiDisplayListRenderer"/> with the pattern resolver
/// set, so area pattern fills are present in the IR (unlike the Mapsui arm,
/// which lowers patterns through a separate post-IR phase). Scale-visibility
/// (S-100 Part 9 §11.1, the per-op SCAMIN carried in the IR) is honoured against
/// the live viewport's scale denominator, matching the A arm's scale-dependent
/// show/hide.
/// </para>
/// <para>
/// <b>Selection.</b> A fresh vector layer is tagged with
/// <see cref="RendererName"/> only while the <c>TiledScene</c> subsystem is the
/// active <see cref="RenderingOptimizations.RenderSubsystem"/> (the tagging is
/// done by <see cref="MapsuiDisplayListRenderer"/>). <see cref="Register"/> wires
/// the renderer into Mapsui once at startup.
/// </para>
/// </remarks>
public static class S100VectorSceneRenderer
{
    /// <summary>
    /// The <see cref="ILayer.CustomLayerRendererName"/> value that routes a layer
    /// through this renderer. Set on the vector layer by
    /// <see cref="MapsuiDisplayListRenderer"/> when the <c>TiledScene</c>
    /// subsystem is active.
    /// </summary>
    public const string RendererName = "s100.vector.scene";

    /// <summary>
    /// Margin, in screen pixels, rasterised around the viewport on every edge so
    /// a pan reveals already-rendered content out to the margin before a
    /// re-raster is needed. Read once from <c>S100_VECTOR_SCENE_MARGIN</c>
    /// (default 256), mirroring <see cref="S100VectorSnapshotRenderer.MarginPx"/>
    /// so the A/B arms record comparable over-render.
    /// </summary>
    public static double MarginPx { get; } = ReadMargin();

    /// <summary>
    /// Hard cap on either dimension of a worker-rasterised image, in device
    /// pixels, to bound native memory for a single surface (no tiling yet in
    /// Phase&#160;1). A viewport whose margin-enlarged device size would exceed
    /// this is rendered at the cap and scaled to fit on composite (transient
    /// blur) rather than allocating an unbounded bitmap.
    /// </summary>
    private const int MaxImageDimension = 8192;

    /// <summary>
    /// Optional callback invoked (on a worker thread) when a freshly rasterised
    /// image publishes, so the host can request a single repaint that swaps the
    /// transient stale blit for the new image. When <see langword="null"/> the
    /// new image is simply used on the next natural repaint. The viewer sets this
    /// to marshal a <c>RefreshGraphics()</c> onto the UI thread.
    /// </summary>
    public static Action? RequestRedraw { get; set; }

    private static readonly ConditionalWeakTable<ILayer, SceneState> States = new();

    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private static readonly bool Diag =
        (Environment.GetEnvironmentVariable("S100_VECTOR_SCENE_DIAG") ?? string.Empty)
            is "1" or "true" or "TRUE" or "True";

    private static double ReadMargin()
    {
        var raw = Environment.GetEnvironmentVariable("S100_VECTOR_SCENE_MARGIN");
        if (!string.IsNullOrEmpty(raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            && v >= 0)
        {
            return v;
        }

        return 256.0;
    }

    /// <summary>
    /// Registers this renderer under <see cref="RendererName"/> with Mapsui's
    /// <c>MapRenderer</c>. Idempotent; call once at startup (after the style
    /// renderers are registered).
    /// </summary>
    public static void Register()
    {
        MapRenderer.RegisterLayerRenderer(RendererName, Render);
    }

    /// <summary>
    /// Binds the fully-resolved <see cref="VectorScene"/> that this renderer
    /// rasterises for <paramref name="layer"/>. Called by
    /// <see cref="MapsuiDisplayListRenderer"/> when it produces a layer for the
    /// active <c>TiledScene</c> subsystem. Binding a new scene invalidates any
    /// cached image so the next frame re-rasterises.
    /// </summary>
    /// <param name="layer">The Mapsui layer the scene belongs to.</param>
    /// <param name="scene">The resolved scene IR (with pattern fills).</param>
    public static void BindScene(ILayer layer, VectorScene scene)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(scene);

        var state = States.GetValue(layer, static _ => new SceneState());
        lock (state.Sync)
        {
            state.Scene = scene;
            state.Generation++;
            state.Image?.Dispose();
            state.Image = null;
            state.HasImage = false;
        }
    }

    /// <summary>
    /// The render handler Mapsui invokes for layers tagged with
    /// <see cref="RendererName"/>. Blits the best available image under a
    /// translation and schedules an off-thread re-raster when the image no longer
    /// covers the viewport.
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

        // North-up only (v1): a rotated viewport breaks the translate-only blit.
        if (viewport.Rotation != 0)
        {
            return;
        }

        // Skip a cell whose data extent lies entirely outside the viewport.
        // Mapsui invokes this custom renderer for every enabled, in-resolution
        // layer each frame without extent-culling, so an exchange set of many
        // S-101 cells would otherwise schedule an off-thread re-raster and hold a
        // whole-viewport image for every off-view cell. Culling here makes
        // off-view cells cost nothing; the MarginPx halo (the same over-render
        // recorded around the viewport) keeps edge cells rendering.
        if (!LayerExtentCulling.ShouldRender(layer, viewport, resolution, MarginPx))
        {
            return;
        }

        var state = States.GetValue(layer, static _ => new SceneState());

        var deviceScale = canvas.TotalMatrix.ScaleX;
        if (deviceScale <= 0 || float.IsNaN(deviceScale))
        {
            deviceScale = 1f;
        }

        SKImage? toBlit = null;
        SKRect dest = default;
        var needRaster = false;
        RasterRequest request = default;

        lock (state.Sync)
        {
            if (state.Scene is null)
            {
                return;
            }

            var anchor = state.ToAnchor();
            var valid = state.HasImage && IsValid(anchor, viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution);

            if (state.HasImage)
            {
                var (tx, ty) = ComputeTranslate(anchor, viewport.CenterX, viewport.CenterY, viewport.Width, viewport.Height, resolution);
                dest = new SKRect(
                    (float)tx,
                    (float)ty,
                    (float)(tx + state.RecordWidthDip),
                    (float)(ty + state.RecordHeightDip));
                toBlit = state.Image;
            }

            if (!valid)
            {
                needRaster = true;
                request = new RasterRequest(
                    viewport.CenterX,
                    viewport.CenterY,
                    resolution,
                    ScaleDenominatorFor(viewport.CenterX, viewport.CenterY, resolution),
                    viewport.Width,
                    viewport.Height,
                    deviceScale,
                    state.Generation);
            }
        }

        if (toBlit is not null)
        {
            var compositeStart = Stopwatch.GetTimestamp();
            canvas.DrawImage(toBlit, dest, Sampling);
            S100Diag.Telemetry.SceneCompositeDuration.Record(
                Stopwatch.GetElapsedTime(compositeStart).TotalMilliseconds);
        }

        if (needRaster)
        {
            Schedule(state, request);
        }
    }

    /// <summary>
    /// Derives the S-100 true-scale denominator for the live viewport from its
    /// EPSG:3857 resolution and centre latitude — the inverse of
    /// <see cref="MapsuiDisplayListRenderer.DenominatorToResolution"/>. The
    /// denominator drives the IR's per-op scale-visibility (SCAMIN) culling, so
    /// the B arm shows/hides the same detail as the live Mapsui frame.
    /// </summary>
    internal static double ScaleDenominatorFor(double centerX, double centerY, double resolution)
    {
        var (_, latitude) = WebMercator.ToLonLat(centerX, centerY);
        var cos = Math.Cos(latitude * Math.PI / 180.0);
        if (cos < 1e-6)
        {
            cos = 1e-6;
        }

        return resolution * cos / MapsuiDisplayListRenderer.DenomToResolutionMetres;
    }

    private static void Schedule(SceneState state, RasterRequest request)
    {
        var start = false;
        lock (state.Sync)
        {
            state.Pending = request;
            state.PendingToken++;
            if (!state.Rendering)
            {
                state.Rendering = true;
                start = true;
            }
        }

        if (start)
        {
            _ = Task.Run(() => Worker(state));
        }
    }

    private static void Worker(SceneState state)
    {
        while (true)
        {
            RasterRequest request;
            VectorScene scene;
            long takenToken;

            lock (state.Sync)
            {
                if (state.Pending is not { } pending || state.Scene is null)
                {
                    state.Rendering = false;
                    return;
                }

                request = pending;
                scene = state.Scene;
                takenToken = state.PendingToken;
            }

            SKImage? image = null;
            try
            {
                var coreViewport = BuildViewport(request);
                var renderer = new SkiaDisplayListRenderer
                {
                    Background = SceneRgbaColor.Transparent,
                    HonorScaleVisibility = true,
                    // Draw the scene once at its true continuous EPSG:3857
                    // position (matching the tiled path). Seam-wrap would
                    // left-edge-wrap antimeridian data whose viewport spans past
                    // ±180°, smearing it; keep it off so the data stays put.
                    EnableSeamWrap = false,
                };

                var rasterStart = Stopwatch.GetTimestamp();
                using var bitmap = renderer.Render(scene, coreViewport);
                image = SKImage.FromBitmap(bitmap);
                S100Diag.Telemetry.SceneRasterizeDuration.Record(
                    Stopwatch.GetElapsedTime(rasterStart).TotalMilliseconds);
            }
            catch
            {
                image?.Dispose();
                image = null;
            }

            var published = false;
            var done = false;
            lock (state.Sync)
            {
                var superseded = state.PendingToken != takenToken;
                var staleGeneration = request.Generation != state.Generation;

                if (image is not null && !superseded && !staleGeneration)
                {
                    state.Image?.Dispose();
                    state.Image = image;
                    state.HasImage = true;
                    state.RecordCenterX = request.CenterX;
                    state.RecordCenterY = request.CenterY;
                    state.RecordResolution = request.Resolution;
                    state.RecordWidthDip = request.WidthDip + 2 * MarginPx;
                    state.RecordHeightDip = request.HeightDip + 2 * MarginPx;
                    state.Pending = null;
                    published = true;

                    if (Diag)
                    {
                        Console.Error.WriteLine(
                            $"[VecScene] published res={request.Resolution:G6} img={image.Width}x{image.Height}");
                    }
                }
                else
                {
                    image?.Dispose();
                    if (!superseded)
                    {
                        // Same request key but the scene was replaced underneath
                        // us — drop it; a fresh request will have been scheduled.
                        state.Pending = null;
                    }
                }

                if (state.Pending is null)
                {
                    state.Rendering = false;
                    done = true;
                }
            }

            if (published)
            {
                RequestRedraw?.Invoke();
            }

            if (done)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Builds the geographic <see cref="CoreViewport"/> the
    /// <see cref="SkiaDisplayListRenderer"/> rasterises: the live viewport
    /// enlarged by <see cref="MarginPx"/> on every edge, in EPSG:3857, at device
    /// resolution. Exposed <see langword="internal"/> for unit testing the
    /// margin / device-scale / projection math.
    /// </summary>
    internal static CoreViewport BuildViewport(RasterRequest request)
    {
        var recordWidthDip = request.WidthDip + 2 * MarginPx;
        var recordHeightDip = request.HeightDip + 2 * MarginPx;

        var halfWidthMetres = recordWidthDip * 0.5 * request.Resolution;
        var halfHeightMetres = recordHeightDip * 0.5 * request.Resolution;

        var minX = request.CenterX - halfWidthMetres;
        var maxX = request.CenterX + halfWidthMetres;
        var minY = request.CenterY - halfHeightMetres;
        var maxY = request.CenterY + halfHeightMetres;

        // Lossless (unclamped) inverse so WorldToScreen reproduces these exact
        // world bounds — clamping a pole-overhanging edge back to ±85° would
        // drift geometry poleward when a high-latitude cell is zoomed out. See
        // WebMercator.ToLonLat.
        var (minLon, minLat) = WebMercator.ToLonLat(minX, minY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX, maxY, clampLatitude: false);

        var widthPx = (int)Math.Round(recordWidthDip * request.DeviceScale);
        var heightPx = (int)Math.Round(recordHeightDip * request.DeviceScale);
        widthPx = Math.Clamp(widthPx, 1, MaxImageDimension);
        heightPx = Math.Clamp(heightPx, 1, MaxImageDimension);

        return new CoreViewport
        {
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            WidthPixels = widthPx,
            HeightPixels = heightPx,
            ScaleDenominator = request.ScaleDenominator,
        };
    }

    /// <summary>
    /// The parameters of one whole-viewport raster request, captured on the
    /// render thread and handed to the worker. Stable so the worker never sees
    /// a torn read.
    /// </summary>
    internal readonly record struct RasterRequest(
        double CenterX,
        double CenterY,
        double Resolution,
        double ScaleDenominator,
        double WidthDip,
        double HeightDip,
        double DeviceScale,
        long Generation);

    /// <summary>
    /// The record anchor of a rasterised image: the world centre it is anchored
    /// at (EPSG:3857) and its DIP-space size and resolution. Pure value used by
    /// <see cref="IsValid"/> / <see cref="ComputeTranslate"/>.
    /// </summary>
    internal readonly record struct RecordAnchor(
        double CenterX,
        double CenterY,
        double WidthDip,
        double HeightDip,
        double Resolution);

    /// <summary>
    /// True when an image recorded at <paramref name="anchor"/> can be blitted to
    /// cover <paramref name="width"/> × <paramref name="height"/> centred at
    /// (<paramref name="centerX"/>, <paramref name="centerY"/>) at
    /// <paramref name="resolution"/>: same resolution and the pan is within the
    /// recorded margin on both axes. Mirrors
    /// <see cref="S100VectorSnapshotRenderer.IsSnapshotValid"/> (minus the
    /// feature-count check — the bound scene is immutable per generation).
    /// </summary>
    internal static bool IsValid(RecordAnchor anchor, double centerX, double centerY, double width, double height, double resolution)
    {
        if (anchor.Resolution != resolution)
        {
            return false;
        }

        var marginX = (anchor.WidthDip - width) / 2.0;
        var marginY = (anchor.HeightDip - height) / 2.0;
        if (marginX < 0 || marginY < 0)
        {
            return false;
        }

        var dx = (centerX - anchor.CenterX) / resolution;
        var dy = (centerY - anchor.CenterY) / resolution;
        return Math.Abs(dx) <= marginX && Math.Abs(dy) <= marginY;
    }

    /// <summary>
    /// The DIP-space top-left at which an image recorded at
    /// <paramref name="anchor"/> is blitted so its anchored world centre lands at
    /// the correct screen position for the current (translated) viewport. Pure;
    /// identical math to <see cref="S100VectorSnapshotRenderer.ComputeTranslate"/>.
    /// </summary>
    internal static (double tx, double ty) ComputeTranslate(RecordAnchor anchor, double centerX, double centerY, double width, double height, double resolution)
    {
        var tx = (anchor.CenterX - centerX) / resolution + (width - anchor.WidthDip) / 2.0;
        var ty = (centerY - anchor.CenterY) / resolution + (height - anchor.HeightDip) / 2.0;
        return (tx, ty);
    }

    /// <summary>
    /// Per-layer render state: the bound scene, the current rasterised image and
    /// its record anchor, and the worker coalescing fields. Held in a
    /// <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed by layer so a
    /// rebuilt layer (palette / category / dataset change) discards its state
    /// automatically.
    /// </summary>
    private sealed class SceneState
    {
        public readonly object Sync = new();

        public VectorScene? Scene;
        public long Generation;

        public SKImage? Image;
        public bool HasImage;
        public double RecordCenterX;
        public double RecordCenterY;
        public double RecordWidthDip;
        public double RecordHeightDip;
        public double RecordResolution;

        public bool Rendering;
        public RasterRequest? Pending;
        public long PendingToken;

        public RecordAnchor ToAnchor() =>
            new(RecordCenterX, RecordCenterY, RecordWidthDip, RecordHeightDip, RecordResolution);
    }
}
