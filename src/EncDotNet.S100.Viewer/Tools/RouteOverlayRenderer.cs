using System.Globalization;
using Avalonia;
using EncDotNet.S100.DataModel;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// The Mapsui style that carries a <see cref="RouteOverlayScene"/>; drawn by
/// <see cref="RouteOverlayRenderer"/>.
/// </summary>
internal sealed class RouteOverlayStyle : BaseStyle
{
    /// <summary>The scene to draw. Replaced (not mutated) on every change.</summary>
    public RouteOverlayScene Scene { get; set; } = RouteOverlayScene.Empty;
}

/// <summary>
/// Draws the route overlay in screen space every frame, so leg labels and
/// insert handles follow the zoom: labels turn along their legs and drop out
/// on short legs, and handles show only on legs long enough to grab.
/// </summary>
/// <remarks>
/// <para>
/// Not editing: the active route is a 4 px accent line with numbered waypoint
/// discs; other routes are drawn fainter.
/// </para>
/// <para>
/// Editing (handoff A6): the active route gets a 9 px casing (white, black on
/// Dusk and Night) under a 3.5 px accent line, a 12 px accent halo at 0.25 on
/// the selected leg, an insert handle at each long-enough leg's midpoint, and
/// r 10.5 numbered discs. The other routes are dimmed to the inactive colour at
/// 0.6 opacity.
/// </para>
/// <para>
/// Layout comes from <see cref="RouteOverlayLayout"/>, which the edit tool also
/// uses to hit-test the handles.
/// </para>
/// </remarks>
internal sealed class RouteOverlayRenderer : ISkiaStyleRenderer
{
    /// <summary>Waypoint disc radius while editing.</summary>
    public const float EditDiscRadius = 10.5f;

    private const float ActiveDiscRadius = 8f;
    private const float InactiveDiscRadius = 5f;
    private const float LabelFontSize = 10.5f;

    private static readonly SKTypeface LabelTypeface = Typeface("Inter", "Helvetica Neue", "Segoe UI", SKFontStyle.Normal);
    private static readonly SKTypeface LabelBoldTypeface = Typeface("Inter", "Helvetica Neue", "Segoe UI", SKFontStyle.Bold);
    private static readonly SKTypeface NumberTypeface = Typeface("JetBrains Mono", "Menlo", "Consolas", SKFontStyle.Bold);

    private static int _registered;

    /// <summary>The renderer instance.</summary>
    public static RouteOverlayRenderer Instance { get; } = new();

    /// <summary>Registers the renderer for <see cref="RouteOverlayStyle"/>. Safe to call repeatedly.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(typeof(RouteOverlayStyle), Instance);
    }

    /// <summary>The on-screen width of a leg label.</summary>
    public static double MeasureLabel(string text, bool bold = false)
    {
        using var font = new SKFont(bold ? LabelBoldTypeface : LabelTypeface, LabelFontSize);
        return font.MeasureText(text);
    }

    /// <inheritdoc />
    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (style is not RouteOverlayStyle { Scene: var scene })
            return false;

        Point? ToScreen(GeoPosition p)
        {
            var (x, y) = SphericalMercator.FromLonLat(p.Longitude, p.Latitude);
            var (sx, sy) = viewport.WorldToScreenXY(x, y);
            return double.IsFinite(sx) && double.IsFinite(sy) ? new Point(sx, sy) : null;
        }

        var opacity = (float)Math.Clamp(layer.Opacity, 0, 1);
        foreach (var route in scene.Routes)
        {
            if (!route.IsActive)
                DrawInactive(canvas, route, scene, ToScreen, opacity);
        }
        if (scene.ActiveRoute is { } active)
            DrawActive(canvas, active, scene, ToScreen, opacity);
        return true;
    }

    private static void DrawInactive(SKCanvas canvas, RouteOverlayScene.SceneRoute route, RouteOverlayScene scene, Func<GeoPosition, Point?> toScreen, float opacity)
    {
        var colors = scene.Colors;
        var (color, width, alpha) = scene.IsEditing
            ? (colors.Inactive, 2f, 0.6f)
            : (colors.ActiveFill, 3f, 0.5f);
        using var line = Stroke(color.WithAlpha(Alpha(alpha * opacity)), width);
        foreach (var leg in route.Legs)
        {
            foreach (var part in Geodesy.MarineGeodesy.SplitAtAntimeridian(leg.Path))
                DrawPath(canvas, Project(part, toScreen), line);
        }

        using var fill = Fill(colors.WaypointFill.WithAlpha(Alpha(0.7f * opacity)));
        using var ring = Stroke(color.WithAlpha(Alpha(0.7f * opacity)), 1.4f);
        foreach (var waypoint in route.Waypoints)
        {
            if (toScreen(waypoint) is not { } p)
                continue;
            canvas.DrawCircle((float)p.X, (float)p.Y, InactiveDiscRadius, fill);
            canvas.DrawCircle((float)p.X, (float)p.Y, InactiveDiscRadius, ring);
        }
    }

    private static void DrawActive(SKCanvas canvas, RouteOverlayScene.SceneRoute route, RouteOverlayScene scene, Func<GeoPosition, Point?> toScreen, float opacity)
    {
        var colors = scene.Colors;
        var editing = scene.IsEditing;
        var layout = RouteOverlayLayout.Layout(
            route, toScreen, text => MeasureLabel(text), scene.SelectedLegIndex, editing);

        if (editing)
        {
            using var casing = Stroke(colors.Casing.WithAlpha(Alpha(opacity)), 9f);
            foreach (var leg in layout)
                DrawParts(canvas, leg.Parts, casing);

            if (scene.SelectedLegIndex is { } selected && selected < layout.Count)
            {
                using var halo = Stroke(colors.Edit.WithAlpha(Alpha(0.25f * opacity)), 12f);
                DrawParts(canvas, layout[selected].Parts, halo);
            }
        }

        using (var line = Stroke(colors.Edit.WithAlpha(Alpha((editing ? 1f : 0.85f) * opacity)), editing ? 3.5f : 4f))
        {
            foreach (var leg in layout)
                DrawParts(canvas, leg.Parts, line);
        }

        if (editing)
        {
            using var handleFill = Fill(colors.WaypointFill.WithAlpha(Alpha(opacity)));
            using var handleRing = Stroke(colors.Edit.WithAlpha(Alpha(opacity)), 1.5f);
            using var plus = Stroke(colors.Edit.WithAlpha(Alpha(opacity)), 1.3f);
            plus.StrokeCap = SKStrokeCap.Butt;
            foreach (var leg in layout)
            {
                if (!leg.ShowInsertHandle || leg.Midpoint is not { } m)
                    continue;
                var (x, y) = ((float)m.X, (float)m.Y);
                var r = (float)RouteOverlayLayout.InsertHandleRadiusPx;
                canvas.DrawCircle(x, y, r, handleFill);
                canvas.DrawCircle(x, y, r, handleRing);
                canvas.DrawLine(x - 2.5f, y, x + 2.5f, y, plus);
                canvas.DrawLine(x, y - 2.5f, x, y + 2.5f, plus);
            }
        }

        DrawLabels(canvas, route, layout, scene, opacity);
        DrawWaypoints(canvas, route, scene, toScreen, opacity);
    }

    private static void DrawLabels(SKCanvas canvas, RouteOverlayScene.SceneRoute route, IReadOnlyList<LegLayout> layout, RouteOverlayScene scene, float opacity)
    {
        var colors = scene.Colors;
        foreach (var leg in layout)
        {
            if (leg.Label is not { } placement)
                continue;
            var selected = scene.SelectedLegIndex == leg.Index;
            var text = route.Legs[leg.Index].Label;
            using var font = new SKFont(selected ? LabelBoldTypeface : LabelTypeface, LabelFontSize);
            using var halo = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f,
                StrokeJoin = SKStrokeJoin.Round,
                Color = colors.TextHalo.WithAlpha(Alpha(opacity)),
            };
            using var ink = Fill((selected ? colors.Edit : colors.Text).WithAlpha(Alpha(opacity)));

            canvas.Save();
            canvas.Translate((float)placement.Center.X, (float)placement.Center.Y);
            canvas.RotateDegrees((float)placement.AngleDegrees);
            // Vertically centre the text on the placement point.
            var baseline = -(font.Metrics.Ascent + font.Metrics.Descent) / 2f;
            canvas.DrawText(text, 0, baseline, SKTextAlign.Center, font, halo);
            canvas.DrawText(text, 0, baseline, SKTextAlign.Center, font, ink);
            canvas.Restore();
        }
    }

    private static void DrawWaypoints(SKCanvas canvas, RouteOverlayScene.SceneRoute route, RouteOverlayScene scene, Func<GeoPosition, Point?> toScreen, float opacity)
    {
        var colors = scene.Colors;
        var editing = scene.IsEditing;
        var radius = editing ? EditDiscRadius : ActiveDiscRadius;
        using var font = new SKFont(NumberTypeface, editing ? 10.5f : 9f);
        var baseline = -(font.Metrics.Ascent + font.Metrics.Descent) / 2f;

        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            if (toScreen(route.Waypoints[i]) is not { } p)
                continue;
            var selected = scene.SelectedWaypointIndex == i;
            var discFill = selected ? colors.Edit : editing ? colors.WaypointFill : colors.ActiveFill;
            var numberColor = selected ? SKColors.White : colors.Edit;
            var (x, y) = ((float)p.X, (float)p.Y);
            var r = selected && !editing ? radius + 2f : radius;

            using var fill = Fill(discFill.WithAlpha(Alpha(opacity)));
            using var ring = Stroke(colors.Edit.WithAlpha(Alpha(opacity)), editing ? 2f : 1.5f);
            canvas.DrawCircle(x, y, r, fill);
            canvas.DrawCircle(x, y, r, ring);

            using var ink = Fill(numberColor.WithAlpha(Alpha(opacity)));
            canvas.DrawText((i + 1).ToString(CultureInfo.InvariantCulture), x, y + baseline, SKTextAlign.Center, font, ink);
        }
    }

    private static List<Point> Project(IReadOnlyList<GeoPosition> path, Func<GeoPosition, Point?> toScreen)
    {
        var points = new List<Point>(path.Count);
        foreach (var p in path)
        {
            if (toScreen(p) is { } s)
                points.Add(s);
        }
        return points;
    }

    private static void DrawParts(SKCanvas canvas, IReadOnlyList<IReadOnlyList<Point>> parts, SKPaint paint)
    {
        foreach (var part in parts)
            DrawPath(canvas, part, paint);
    }

    private static void DrawPath(SKCanvas canvas, IReadOnlyList<Point> points, SKPaint paint)
    {
        if (points.Count < 2)
            return;
        using var path = new SKPath();
        path.MoveTo((float)points[0].X, (float)points[0].Y);
        for (var i = 1; i < points.Count; i++)
            path.LineTo((float)points[i].X, (float)points[i].Y);
        canvas.DrawPath(path, paint);
    }

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        StrokeJoin = SKStrokeJoin.Round,
        StrokeCap = SKStrokeCap.Round,
        Color = color,
    };

    private static SKPaint Fill(SKColor color) => new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
        Color = color,
    };

    private static byte Alpha(float opacity) => (byte)Math.Round(255 * Math.Clamp(opacity, 0f, 1f));

    private static SKTypeface Typeface(string first, string second, string third, SKFontStyle style)
    {
        foreach (var family in new[] { first, second, third })
        {
            var face = SKTypeface.FromFamilyName(family, style);
            if (face is not null && string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                return face;
        }
        return SKTypeface.FromFamilyName(null, style) ?? SKTypeface.Default;
    }
}
