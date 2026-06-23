using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Controls for the tiled subsystem's live label/symbol overlay pass, passed to
/// <see cref="SkiaDisplayListRenderer.RenderOnto(SKCanvas, VectorScene, Viewport, OverlayDrawOptions)"/>.
/// The defaults reproduce the plain overlay behaviour: draw every point and
/// label, with no declutter suppression and no anchor rotation.
/// </summary>
public sealed class OverlayDrawOptions
{
    /// <summary>
    /// Pixel-space rectangle outside which point/text ops are skipped, or
    /// <see langword="null"/> to derive it from the viewport plus
    /// <see cref="SkiaDisplayListRenderer.PointCullMarginPx"/>. A point pass that
    /// rotates the canvas must pass the rotated viewport's bounding box; a text
    /// pass that instead rotates anchors (keeping glyphs upright) culls against
    /// the unrotated viewport.
    /// </summary>
    public SKRect? PointCullBounds { get; init; }

    /// <summary>
    /// Text ops to suppress this frame (the loser side of a declutter pass; see
    /// <see cref="LabelDeclutterer"/>), or <see langword="null"/> to draw all
    /// text. Suppression is by reference identity against the scene's ops.
    /// </summary>
    public IReadOnlySet<TextPaintOp>? SuppressedText { get; init; }

    /// <summary>
    /// Degrees to rotate each text <i>anchor</i> about
    /// (<see cref="ScreenCenterX"/>, <see cref="ScreenCenterY"/>), matching how
    /// the base/point passes rotate under a rotated viewport, while glyphs are
    /// still drawn axis-aligned (upright). Zero (the default, and the north-up
    /// v1 case) leaves anchors unrotated.
    /// </summary>
    public double TextAnchorRotationDegrees { get; init; }

    /// <summary>Screen-space X of the rotation centre for <see cref="TextAnchorRotationDegrees"/>.</summary>
    public float ScreenCenterX { get; init; }

    /// <summary>Screen-space Y of the rotation centre for <see cref="TextAnchorRotationDegrees"/>.</summary>
    public float ScreenCenterY { get; init; }

    /// <summary>Whether to draw point symbols this pass. Defaults to <see langword="true"/>.</summary>
    public bool DrawPoints { get; init; } = true;

    /// <summary>Whether to draw text/labels this pass. Defaults to <see langword="true"/>.</summary>
    public bool DrawText { get; init; } = true;
}
