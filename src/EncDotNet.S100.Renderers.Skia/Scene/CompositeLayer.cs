using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// A single renderer-neutral drawable in a headless composite. Each layer knows
/// how to paint itself onto a canvas against a <em>shared</em>
/// <see cref="Viewport"/>, so that multiple layered S-100 datasets (e.g. an
/// S-101 ENC beneath an S-102 bathymetry surface) register pixel-for-pixel.
/// The compositor draws layers in list order (bottom-most first), reproducing
/// the S-98 cross-dataset draw order already resolved by the
/// <c>SubLayerStackItem</c> engine.
/// </summary>
public abstract class CompositeLayer
{
    private protected CompositeLayer()
    {
    }

    /// <summary>
    /// Paints this layer onto <paramref name="canvas"/> against the shared
    /// composite <paramref name="viewport"/>. Implementations must not clear the
    /// canvas — the compositor clears the background once before drawing the
    /// ordered layer list.
    /// </summary>
    /// <param name="canvas">The composite canvas.</param>
    /// <param name="viewport">The shared composite viewport (pixel space).</param>
    public abstract void Draw(SKCanvas canvas, Viewport viewport);

    /// <summary>
    /// Paints the part of this layer that turns with a rotated display — all of
    /// it but vector point symbols and text — against the north-up cover
    /// <paramref name="viewport"/>; the compositor then rotates the result onto
    /// the output (issue #578).
    /// </summary>
    internal virtual void DrawRotating(SKCanvas canvas, Viewport viewport) => Draw(canvas, viewport);

    /// <summary>
    /// Paints the point symbols and text placed in screen space on a rotated
    /// display: each anchor is turned by <paramref name="rotationDegrees"/>
    /// about the centre of the north-up cover <paramref name="viewport"/>, text
    /// and screen-relative symbols are drawn unrotated, and north-relative
    /// symbols turn by the display rotation (issue #652). Ops whose rotated
    /// anchor falls outside <paramref name="cullBounds"/> (the output, in cover
    /// pixels, plus a margin) are skipped. Layers without such ops paint
    /// nothing.
    /// </summary>
    internal virtual void DrawScreenPlaced(SKCanvas canvas, Viewport viewport, double rotationDegrees, SKRect cullBounds)
    {
    }
}

/// <summary>
/// A <see cref="CompositeLayer"/> that paints a lowered vector
/// <see cref="VectorScene"/> via <see cref="SkiaDisplayListRenderer"/>.
/// </summary>
public sealed class VectorCompositeLayer : CompositeLayer
{
    /// <summary>Scenes smaller than this are drawn whole; indexing them gains nothing.</summary>
    private const int MinimumIndexedOps = 64;

    private readonly VectorScene _scene;
    private readonly RgbaColor _background;
    private readonly bool _honorScaleVisibility;
    private readonly Lazy<BaseSpatialIndex?> _index;

    /// <summary>
    /// Creates a vector composite layer from a resolved scene.
    /// </summary>
    /// <param name="scene">The lowered vector scene (from <c>VectorSceneBuilder</c>).</param>
    /// <param name="honorScaleVisibility">
    /// Whether to apply S-100 Part 9 §11.1 scale-visibility culling using the
    /// shared viewport's scale denominator. Defaults to <see langword="true"/>.
    /// </param>
    public VectorCompositeLayer(VectorScene scene, bool honorScaleVisibility = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
        // The composite background is cleared once by the compositor, so each
        // vector layer paints transparently over what is already there.
        _background = RgbaColor.Transparent;
        _honorScaleVisibility = honorScaleVisibility;
        _index = new Lazy<BaseSpatialIndex?>(
            () => scene.Ops.Count >= MinimumIndexedOps ? new BaseSpatialIndex(scene, indexAnchors: true) : null,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// The coarsest scale denominator the whole layer is drawn at, such as a
    /// cell's <c>DataCoverage.minimumDisplayScale</c>, or <see langword="null"/>
    /// for no limit. Applied only with scale-visibility culling on: past it,
    /// <see cref="Draw"/> paints nothing, area fills included.
    /// </summary>
    public double? MinimumDisplayScale { get; init; }

    /// <summary>
    /// The coverages of finer cells that hide this layer where they overlap it
    /// and are themselves drawing (see <see cref="FinerCoverage"/>). Empty by
    /// default, so the layer paints in full.
    /// </summary>
    public IReadOnlyList<FinerCoverage> FinerCoverages { get; init; } = [];

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);

        if (IsPastMinimumDisplayScale(viewport))
            return;

        int save = ClipOutFinerCoverages(canvas, viewport, rotationDegrees: 0);
        try
        {
            CreateRenderer().RenderOnto(canvas, ScopeTo(viewport), viewport);
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    /// <summary>
    /// The part of the scene that can draw in <paramref name="viewport"/>: the
    /// ops whose bounds (an anchor, for points and text) lie within
    /// <see cref="SkiaDisplayListRenderer.PointCullMarginPx"/> pixels of it, in
    /// draw order. The renderer culls anchors at that same margin. Drawing a small part of a large composite, as each tile does, then
    /// walks only the ops near it. The renderer still culls and clips exactly,
    /// so the picture is unchanged.
    /// </summary>
    private VectorScene ScopeTo(Viewport viewport)
    {
        // A wrapping viewport folds geometry from beyond the seam into view, so
        // a bounds query in raw world coordinates would miss it.
        bool wraps = EnableSeamWrap && (viewport.MaxLongitude > 180.0 || viewport.MinLongitude < -180.0);
        if (wraps || _index.Value is not { } index)
            return _scene;

        var (minX, minY) = WebMercator.FromLonLat(viewport.MinLongitude, viewport.MinLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(viewport.MaxLongitude, viewport.MaxLatitude);
        double marginX = (maxX - minX) / viewport.WidthPixels * SkiaDisplayListRenderer.PointCullMarginPx;
        double marginY = (maxY - minY) / viewport.HeightPixels * SkiaDisplayListRenderer.PointCullMarginPx;
        var ops = index.Query(minX - marginX, minY - marginY, maxX + marginX, maxY + marginY);
        return ops.Count == _scene.Ops.Count
            ? _scene
            : new VectorScene(ops) { ScaleMinimumCap = _scene.ScaleMinimumCap, PreferLiveDrawing = _scene.PreferLiveDrawing };
    }

    private bool IsPastMinimumDisplayScale(Viewport viewport) =>
        _honorScaleVisibility
        && MinimumDisplayScale is double limit
        && viewport.ScaleDenominator > limit;

    /// <summary>
    /// Saves the canvas and removes the active finer coverages from its clip,
    /// turned by <paramref name="rotationDegrees"/> about the viewport centre for
    /// symbols and text placed on a rotated display. Returns the save count to
    /// restore to.
    /// </summary>
    private int ClipOutFinerCoverages(SKCanvas canvas, Viewport viewport, double rotationDegrees)
    {
        int save = canvas.Save();
        if (FinerCoverages.Count == 0)
            return save;

        float centerX = viewport.WidthPixels / 2f;
        float centerY = viewport.HeightPixels / 2f;
        if (rotationDegrees != 0)
            canvas.RotateDegrees((float)rotationDegrees, centerX, centerY);

        // The clip is recorded in device space, so undoing the rotation leaves
        // it turned while the layer draws in its own (unrotated) space.
        FinerCoverage.ClipOut(
            canvas,
            FinerCoverages,
            WorldToScreen.Create(viewport, EnableSeamWrap),
            canvas.LocalClipBounds,
            viewport.ScaleDenominator,
            _honorScaleVisibility);

        if (rotationDegrees != 0)
            canvas.RotateDegrees((float)-rotationDegrees, centerX, centerY);
        return save;
    }

    internal override void DrawRotating(SKCanvas canvas, Viewport viewport)
    {
        if (IsPastMinimumDisplayScale(viewport))
            return;

        int save = ClipOutFinerCoverages(canvas, viewport, rotationDegrees: 0);
        try
        {
            CreateRenderer().RenderOnto(canvas, ScopeTo(viewport), viewport, new OverlayDrawOptions
            {
                DrawPoints = false,
                DrawText = false,
            });
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    internal override void DrawScreenPlaced(SKCanvas canvas, Viewport viewport, double rotationDegrees, SKRect cullBounds)
    {
        if (IsPastMinimumDisplayScale(viewport))
            return;

        int save = ClipOutFinerCoverages(canvas, viewport, rotationDegrees);
        try
        {
            CreateRenderer().RenderOnto(canvas, ScopeTo(viewport), viewport, new OverlayDrawOptions
            {
                PointCullBounds = cullBounds,
                DrawAreasAndLines = false,
                AnchorRotationDegrees = rotationDegrees,
                ScreenCenterX = viewport.WidthPixels / 2f,
                ScreenCenterY = viewport.HeightPixels / 2f,
            });
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    /// <summary>
    /// Whether geometry is wrapped into a viewport that crosses the ±180°
    /// antimeridian (see <see cref="SkiaDisplayListRenderer.EnableSeamWrap"/>).
    /// Defaults to <see langword="true"/>. A tiled render turns it off and draws
    /// the world copies itself, because wrapping under a narrow tile viewport
    /// smears polygons that extend beyond the tile.
    /// </summary>
    public bool EnableSeamWrap { get; init; } = true;

    /// <summary>
    /// Whether area-placed symbols and text move into the part of their
    /// surface in the viewport (see <see cref="SkiaDisplayListRenderer.KeepAreaSymbolsInView"/>).
    /// Defaults to <see langword="true"/>. A tiled render turns it off.
    /// </summary>
    public bool KeepAreaSymbolsInView { get; init; } = true;

    private SkiaDisplayListRenderer CreateRenderer() => new()
    {
        Background = _background,
        HonorScaleVisibility = _honorScaleVisibility,
        EnableSeamWrap = EnableSeamWrap,
        KeepAreaSymbolsInView = KeepAreaSymbolsInView,
    };
}

/// <summary>
/// A <see cref="CompositeLayer"/> that paints a styled coverage surface
/// (colour-band fill and/or oriented arrows) via
/// <see cref="CoverageHeadlessRenderer.DrawOnto"/>, projecting the coverage's
/// geographic extent into the shared viewport's pixel space.
/// </summary>
public sealed class CoverageCompositeLayer : CompositeLayer
{
    private readonly StyledCoverageLayer _layer;
    private readonly double _west;
    private readonly double _east;
    private readonly double _south;
    private readonly double _north;
    private readonly CoverageHeadlessRenderer _renderer;

    // The arrow renderer caches parsed symbols without synchronisation, so draws
    // are serialised to let concurrent tile renders share one layer.
    private readonly object _drawLock = new();

    /// <summary>
    /// Creates a coverage composite layer.
    /// </summary>
    /// <param name="layer">The styled coverage layer (colour and/or symbol scheme).</param>
    /// <param name="westLongitude">Western extent edge in WGS84 degrees.</param>
    /// <param name="eastLongitude">Eastern extent edge in WGS84 degrees.</param>
    /// <param name="southLatitude">Southern extent edge in WGS84 degrees.</param>
    /// <param name="northLatitude">Northern extent edge in WGS84 degrees.</param>
    /// <param name="arrowRenderer">Optional arrow renderer for symbol schemes.</param>
    /// <param name="nativeToWgs84">Transform from the grid's native CRS to WGS84; defaults to identity.</param>
    /// <param name="landAreas">
    /// Optional S-101 <c>LandArea</c> geometries (WGS84) used to clip the surface
    /// to water at output-pixel resolution (S-98 R-101-104-B; issue #483).
    /// </param>
    public CoverageCompositeLayer(
        StyledCoverageLayer layer,
        double westLongitude,
        double eastLongitude,
        double southLatitude,
        double northLatitude,
        SkiaCoverageArrowRenderer? arrowRenderer = null,
        ICrsTransform? nativeToWgs84 = null,
        IReadOnlyList<FeatureGeometry>? landAreas = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _layer = layer;
        _west = westLongitude;
        _east = eastLongitude;
        _south = southLatitude;
        _north = northLatitude;
        _renderer = new CoverageHeadlessRenderer
        {
            // Coverage layers paint over what is already composited, so no-data
            // and out-of-coverage areas must stay transparent.
            Background = RgbaColor.Transparent,
            NoDataColor = RgbaColor.Transparent,
            ArrowRenderer = arrowRenderer,
            NativeToWgs84 = nativeToWgs84 ?? IdentityCrsTransform.Instance,
            LandAreas = landAreas,
        };
    }

    /// <summary>
    /// The opacity the surface is composited at, 0 (invisible) to 1 (opaque).
    /// Defaults to <c>1.0</c>.
    /// </summary>
    public double Opacity { get; init; } = 1.0;

    /// <inheritdoc/>
    public override void Draw(SKCanvas canvas, Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);

        lock (_drawLock)
        {
            DrawCore(canvas, viewport);
        }
    }

    private void DrawCore(SKCanvas canvas, Viewport viewport)
    {
        if (Opacity >= 1.0)
        {
            _renderer.DrawOnto(canvas, viewport, _layer, _west, _east, _south, _north);
            return;
        }

        if (Opacity <= 0.0)
            return;

        // Composite the whole surface through one alpha layer so overlapping
        // draws inside it don't stack their transparency.
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Opacity * 255)) };
        canvas.SaveLayer(paint);
        try
        {
            _renderer.DrawOnto(canvas, viewport, _layer, _west, _east, _south, _north);
        }
        finally
        {
            canvas.Restore();
        }
    }
}
