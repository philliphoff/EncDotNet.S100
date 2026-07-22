using EncDotNet.S100.Renderers.Skia;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia.Extensions;
using Mapsui.Rendering.Skia.Functions;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders <see cref="OverscaleCurtainStyle"/> by drawing evenly spaced vertical
/// lines, anchored to a fixed world origin, clipped to the styled polygon — the
/// S-52 / S-101 overscale "curtain" (issue #441, <c>AP(OVERSC01)</c> Form A).
/// </summary>
/// <remarks>
/// <para>
/// The lines are drawn directly as stroked segments rather than by stamping a
/// repeating <see cref="SKPicture"/> tile. Direct strokes keep the pattern crisp
/// at any zoom and on HiDPI surfaces, cost only a few hundred draw operations per
/// frame (one per visible line, not one per lattice cell), and — crucially — do
/// not nest a picture inside the offscreen picture-recording canvas used when the
/// host captures a full-window screenshot, which a per-tile
/// <c>DrawPicture</c> loop can drive into a deep, crashing recursion in Skia.
/// </para>
/// <para>
/// The line lattice is anchored to world origin (0,0) projected to screen space
/// so the curtain moves seamlessly with the chart during panning and stays
/// phase-consistent across adjacent overscaled cells.
/// </para>
/// </remarks>
public sealed class OverscaleCurtainRenderer : ISkiaStyleRenderer
{
    /// <summary>Singleton instance for registration.</summary>
    public static OverscaleCurtainRenderer Instance { get; } = new();

    /// <summary>
    /// Ensures the renderer is registered with
    /// <see cref="Mapsui.Rendering.Skia.MapRenderer"/>. Safe to call repeatedly.
    /// </summary>
    public static void Register()
    {
        global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(
            typeof(OverscaleCurtainStyle), Instance);
    }

    /// <inheritdoc/>
    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer,
                     IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (feature is not GeometryFeature gf ||
            style is not OverscaleCurtainStyle curtainStyle)
        {
            return false;
        }

        IEnumerable<Polygon> polygons = gf.Geometry switch
        {
            Polygon p => [p],
            MultiPolygon mp => mp.Geometries.OfType<Polygon>(),
            _ => []
        };

        var clipRect = viewport.ToSkiaRect();

        using var path = new SKPath();
        foreach (var polygon in polygons)
        {
            using var polyPath = ToSkiaPath(polygon, viewport, clipRect);
            path.AddPath(polyPath);
        }

        if (path.IsEmpty)
            return false;

        const float onScreenPxPerMm = (float)SkiaSvgRasterizer.DefaultPixelsPerMm;

        // Snap the line spacing to whole pixels so the world-anchored lattice
        // lands on a consistent pixel phase at every step (avoids shimmer).
        float stepPx = MathF.Max(1f, MathF.Round((float)curtainStyle.LineSpacingMm * onScreenPxPerMm));
        float strokePx = MathF.Max(0.5f, (float)curtainStyle.LineWidthMm * onScreenPxPerMm);

        float opacity = (float)(layer.Opacity * curtainStyle.Opacity);
        var color = curtainStyle.LineColor
            .WithAlpha((byte)(curtainStyle.LineColor.Alpha * Math.Clamp(opacity, 0f, 1f)));

        var bounds = path.Bounds;
        var (anchorScreenX, _) = viewport.WorldToScreenXY(0, 0);

        int startCol = (int)Math.Floor((bounds.Left - anchorScreenX) / stepPx);
        int endCol = (int)Math.Ceiling((bounds.Right - anchorScreenX) / stepPx);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokePx,
            StrokeCap = SKStrokeCap.Butt,
            Color = color,
        };

        canvas.Save();
        canvas.ClipPath(path, antialias: true);

        for (int col = startCol; col <= endCol; col++)
        {
            float x = (float)anchorScreenX + col * stepPx;
            canvas.DrawLine(x, bounds.Top, x, bounds.Bottom, paint);
        }

        canvas.Restore();
        return true;
    }

    /// <summary>
    /// Converts a NTS <see cref="Polygon"/> (world coordinates) to an
    /// <see cref="SKPath"/> in screen coordinates, including interior rings so the
    /// curtain does not paint over holes (e.g. footprints of finer cells).
    /// </summary>
    private static SKPath ToSkiaPath(Polygon polygon, Viewport viewport, SKRect clipRect)
    {
        var path = new SKPath();
        AddRing(path, polygon.ExteriorRing, viewport, clipRect);
        foreach (var hole in polygon.InteriorRings)
            AddRing(path, hole, viewport, clipRect);
        return path;
    }

    private static void AddRing(SKPath path, LineString ring, Viewport viewport, SKRect clipRect)
    {
        var screenPoints = ClippingFunctions.ReducePointsToClipRect(
            ring.Coordinates, viewport, clipRect);

        if (screenPoints.Count < 3)
            return;

        path.MoveTo(screenPoints[0]);
        for (int i = 1; i < screenPoints.Count; i++)
            path.LineTo(screenPoints[i]);
        path.Close();
    }
}
