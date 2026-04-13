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
/// Renders <see cref="AnchoredPatternFillStyle"/> by filling the polygon with
/// a repeating tile bitmap whose origin is anchored to the geometry's
/// screen-space bounding box, so the pattern moves with the polygon during panning.
/// </summary>
public sealed class AnchoredPatternFillRenderer : ISkiaStyleRenderer
{
    /// <summary>Singleton instance for registration.</summary>
    public static AnchoredPatternFillRenderer Instance { get; } = new();

    /// <summary>
    /// Ensures the renderer is registered with <see cref="Mapsui.Rendering.Skia.MapRenderer"/>.
    /// Safe to call multiple times.
    /// </summary>
    public static void Register()
    {
        global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(
            typeof(AnchoredPatternFillStyle), Instance);
    }

    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer,
                     IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (feature is not GeometryFeature gf ||
            gf.Geometry is not Polygon polygon ||
            style is not AnchoredPatternFillStyle patternStyle)
        {
            return false;
        }

        float opacity = (float)(layer.Opacity * style.Opacity);
        var clipRect = viewport.ToSkiaRect();

        using var path = ToSkiaPath(polygon, viewport, clipRect);
        if (path.IsEmpty)
            return false;

        var bounds = path.Bounds;

        // Decode the tile PNG into an SKImage
        using var tileImage = SKImage.FromEncodedData(patternStyle.TilePng);
        if (tileImage is null)
            return false;

        // Anchor the shader to the polygon's *unclipped* world-coordinate envelope
        // projected to screen space. Using the clipped path bounds would cause the
        // pattern to shift when part of the polygon is off-screen (the clipped
        // bounds get clamped to the viewport edge).
        var env = polygon.EnvelopeInternal;
        var (anchorScreenX, anchorScreenY) = viewport.WorldToScreenXY(env.MinX, env.MaxY);
        var anchorMatrix = SKMatrix.CreateTranslation((float)anchorScreenX, (float)anchorScreenY);
        using var shader = tileImage.ToShader(
            SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, anchorMatrix);

        using var fillPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = shader,
            Color = new SKColor(255, 255, 255, (byte)(opacity * 255)),
        };

        canvas.Save();
        canvas.ClipPath(path);
        canvas.DrawRect(bounds, fillPaint);
        canvas.Restore();

        // Draw outline
        if (patternStyle.OutlineWidth > 0)
        {
            using var outlinePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)patternStyle.OutlineWidth,
                Color = new SKColor(
                    (byte)patternStyle.OutlineColor.R,
                    (byte)patternStyle.OutlineColor.G,
                    (byte)patternStyle.OutlineColor.B,
                    (byte)(patternStyle.OutlineColor.A * opacity)),
            };
            canvas.DrawPath(path, outlinePaint);
        }

        return true;
    }

    /// <summary>
    /// Converts a NTS Polygon (in world coordinates) to an SKPath in screen coordinates.
    /// Handles the exterior ring and interior rings (holes).
    /// </summary>
    private static SKPath ToSkiaPath(Polygon polygon, Viewport viewport, SKRect clipRect)
    {
        var path = new SKPath();

        // Exterior ring
        AddRing(path, polygon.ExteriorRing, viewport, clipRect);

        // Interior rings (holes)
        foreach (var hole in polygon.InteriorRings)
        {
            AddRing(path, hole, viewport, clipRect);
        }

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
        {
            path.LineTo(screenPoints[i]);
        }
        path.Close();
    }
}
