using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Controls for the tiled subsystem's live label/symbol overlay pass, passed to
/// <see cref="SkiaDisplayListRenderer.RenderOnto(SKCanvas, VectorScene, EncDotNet.S100.Pipelines.Viewport, OverlayDrawOptions)"/>.
/// The defaults reproduce the plain overlay behaviour: draw every point and
/// label, with no declutter suppression and no anchor rotation.
/// </summary>
public sealed class OverlayDrawOptions
{
    /// <summary>
    /// Pixel-space rectangle outside which point/text ops are skipped, or
    /// <see langword="null"/> to derive it from the viewport plus
    /// <see cref="SkiaDisplayListRenderer.PointCullMarginPx"/>. The test is on
    /// the anchor after <see cref="AnchorRotationDegrees"/> is applied, so a
    /// pass that rotates anchors culls against the output as drawn; a pass that
    /// instead rotates the canvas must pass the rotated viewport's bounding box.
    /// </summary>
    public SKRect? PointCullBounds { get; init; }

    /// <summary>
    /// Text ops to suppress this frame (the loser side of a declutter pass; see
    /// <see cref="LabelDeclutterer"/>), or <see langword="null"/> to draw all
    /// text. Suppression is by reference identity against the scene's ops.
    /// </summary>
    public IReadOnlySet<TextPaintOp>? SuppressedText { get; init; }

    /// <summary>
    /// Clockwise display rotation, in degrees: each point and text
    /// <i>anchor</i> is turned by it about
    /// (<see cref="ScreenCenterX"/>, <see cref="ScreenCenterY"/>), matching the
    /// rotated base plane, while labels and screen-relative symbols are drawn
    /// unrotated (upright) and north-relative symbols
    /// (<see cref="SymbolRotationCrs.Geographic"/>) turn by it too. Zero (the
    /// default, north-up) leaves everything unrotated.
    /// </summary>
    public double AnchorRotationDegrees { get; init; }

    /// <summary>Screen-space X of the rotation centre for <see cref="AnchorRotationDegrees"/>.</summary>
    public float ScreenCenterX { get; init; }

    /// <summary>Screen-space Y of the rotation centre for <see cref="AnchorRotationDegrees"/>.</summary>
    public float ScreenCenterY { get; init; }

    /// <summary>
    /// Whether to draw area fills, pattern fills and lines this pass. Defaults
    /// to <see langword="true"/>; a text-only pass (the upright labels of a
    /// rotated headless composite) turns it off.
    /// </summary>
    public bool DrawAreasAndLines { get; init; } = true;

    /// <summary>Whether to draw point symbols this pass. Defaults to <see langword="true"/>.</summary>
    public bool DrawPoints { get; init; } = true;

    /// <summary>Whether to draw text/labels this pass. Defaults to <see langword="true"/>.</summary>
    public bool DrawText { get; init; } = true;

    /// <summary>
    /// Device-pixel scale of the target canvas (its HiDPI back-buffer matrix
    /// scale), used to rasterise cached symbol sprites at the correct resolution
    /// for the symbol atlas (#332 lever c2). Defaults to <c>1</c>.
    /// </summary>
    public float DeviceScale { get; init; } = 1f;

    /// <summary>
    /// Whether point symbols may be drawn from the render-thread symbol-sprite
    /// atlas (a once-rasterised <see cref="SkiaSharp.SKImage"/> blit) instead of
    /// replaying their vector <see cref="SkiaSharp.SKPicture"/> every frame
    /// (#332 lever c2). Defaults to <see langword="true"/>; set
    /// <see langword="false"/> to force the vector path (used for pixel-parity
    /// tests and as a safety fallback). Per-op-rotated symbols always use the
    /// vector path regardless.
    /// </summary>
    public bool UseSymbolAtlas { get; init; } = true;
}
