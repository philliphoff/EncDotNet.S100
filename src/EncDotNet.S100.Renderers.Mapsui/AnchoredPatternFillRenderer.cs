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
    internal static void Register()
    {
        global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(
            typeof(AnchoredPatternFillStyle), Instance);
    }

    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer,
                     IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (feature is not GeometryFeature gf ||
            style is not AnchoredPatternFillStyle patternStyle)
        {
            return false;
        }

        var drawStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return DrawCore(canvas, viewport, layer, gf, patternStyle);
        }
        finally
        {
            var elapsedMs = System.Diagnostics.Stopwatch
                .GetElapsedTime(drawStartTimestamp).TotalMilliseconds;
            Diagnostics.Telemetry.PatternFillDrawDuration.Record(elapsedMs);
        }
    }

    private bool DrawCore(SKCanvas canvas, Viewport viewport, ILayer layer,
                          GeometryFeature gf, AnchoredPatternFillStyle patternStyle)
    {
        IEnumerable<Polygon> polygons = gf.Geometry switch
        {
            Polygon p => [p],
            MultiPolygon mp => mp.Geometries.OfType<Polygon>(),
            _ => []
        };

        float opacity = (float)(layer.Opacity * patternStyle.Opacity);
        var clipRect = viewport.ToSkiaRect();

        // Build a combined path from all polygons so the pattern is drawn
        // exactly once over the union of all geometries, preventing alpha
        // accumulation where polygons overlap.
        using var path = new SKPath();
        foreach (var polygon in polygons)
        {
            using var polyPath = ToSkiaPath(polygon, viewport, clipRect);
            path.AddPath(polyPath);
        }

        if (path.IsEmpty)
            return false;

        var bounds = path.Bounds;

        // The tile is a vector SKPicture recorded in millimetre units. It is
        // stamped once per lattice cell via canvas.DrawPicture, so every copy is
        // played back through the canvas transform (including the surface's device
        // scale) and rasterized at the surface's true device resolution. This keeps
        // the pattern crisp at any zoom level and on HiDPI/Retina surfaces.
        //
        // A picture *shader* was tried first but appeared blurry on Retina: an
        // SKPictureShader caches its rasterized tile at the shader's local-matrix
        // density only (here onScreenPxPerMm = 1.5) and the canvas CTM then upsamples
        // that cached tile by the device scale (2x), softening the pattern. Stamping
        // avoids the intermediate tile cache entirely.
        const float onScreenPxPerMm = (float)SkiaSvgRasterizer.DefaultPixelsPerMm;
        var (anchorScreenX, anchorScreenY) = viewport.WorldToScreenXY(0, 0);

        float tileScreenW = patternStyle.TileRect.Width * onScreenPxPerMm;
        float tileScreenH = patternStyle.TileRect.Height * onScreenPxPerMm;
        if (tileScreenW <= 0.01f || tileScreenH <= 0.01f)
            return false;

        // Snap the repeat spacing to whole pixels. The lattice is anchored to a
        // fixed world origin and stepped many times across the polygon, so any
        // fractional component in the step causes successive tiles to land on
        // varying sub-pixel offsets, giving the pattern an inconsistent, shimmery
        // sharpness. Stepping by a whole number of pixels keeps every tile aligned
        // to the same pixel phase. The symbol is centred well within the cell, so
        // the sub-millimetre difference between the (transparent) tile edge and the
        // snapped step is invisible.
        float tileStepW = MathF.Max(1f, MathF.Round(tileScreenW));
        float tileStepH = MathF.Max(1f, MathF.Round(tileScreenH));

        // Anchor the lattice to a fixed world origin projected to screen space so
        // the pattern is shared by all polygons (S-100 areaCRS=GlobalGeometry) and
        // stays seamless across overlapping geometries during panning.
        int startCol = (int)Math.Floor((bounds.Left - anchorScreenX) / tileStepW);
        int endCol = (int)Math.Ceiling((bounds.Right - anchorScreenX) / tileStepW);
        int startRow = (int)Math.Floor((bounds.Top - anchorScreenY) / tileStepH);
        int endRow = (int)Math.Ceiling((bounds.Bottom - anchorScreenY) / tileStepH);

        var tileScale = SKMatrix.CreateScale(onScreenPxPerMm, onScreenPxPerMm);

        canvas.Save();
        canvas.ClipPath(path, antialias: true);

        bool useLayer = opacity < 0.999f;
        if (useLayer)
        {
            using var layerPaint = new SKPaint { Color = new SKColor(0, 0, 0, (byte)(opacity * 255)) };
            canvas.SaveLayer(layerPaint);
        }

        for (int row = startRow; row <= endRow; row++)
        {
            for (int col = startCol; col <= endCol; col++)
            {
                float tx = (float)anchorScreenX + col * tileStepW;
                float ty = (float)anchorScreenY + row * tileStepH;
                var tileMatrix = SKMatrix.Concat(SKMatrix.CreateTranslation(tx, ty), tileScale);
                canvas.DrawPicture(patternStyle.Tile, in tileMatrix);
            }
        }

        if (useLayer)
            canvas.Restore();
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
