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

        // C1: a 30 % wash over the chart, under the route, so the coloured
        // route reads over busy layers such as S-111 arrows.
        if (scene.Check is { } washed && scene.ActiveRoute is not null)
        {
            using var wash = Fill(washed.Colors.Wash.WithAlpha(Alpha(washed.Colors.Wash.Alpha / 255f * opacity)));
            canvas.DrawRect(0, 0, (float)viewport.Width, (float)viewport.Height, wash);
        }

        foreach (var route in scene.Routes)
        {
            if (!route.IsActive)
                DrawInactive(canvas, route, scene, ToScreen, opacity);
        }
        if (scene.ActiveRoute is { } active)
        {
            DrawActive(canvas, active, scene, ToScreen, opacity);
            if (scene.Check is { } check)
            {
                DrawFindingMarkers(canvas, check, ToScreen, opacity);
                DrawCheckMarkers(canvas, check, ToScreen, (float)viewport.Rotation, opacity);
                var legendTop = check.HasClearance
                    ? DrawLegend(canvas, check, (float)viewport.Height, opacity)
                    : (float)viewport.Height - LegendBottomMargin;
                if (check.Finding is { } finding)
                {
                    DrawFindingOutline(canvas, finding, check.Colors, ToScreen, opacity);
                    var scale = MapScaleFormatter.Format(viewport.Resolution, viewport.CenterY);
                    DrawFindingChip(canvas, finding, check.Colors, scale, legendTop, opacity);
                }
            }
        }
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

        if (scene.Check is { } check)
        {
            // C6: state colours at ETA on a 10 px casing.
            using var casing = Stroke(check.Colors.Casing.WithAlpha(Alpha(opacity)), 10f);
            foreach (var leg in layout)
                DrawParts(canvas, leg.Parts, casing);
        }
        else if (editing)
        {
            using var casing = Stroke(colors.Casing.WithAlpha(Alpha(opacity)), 9f);
            foreach (var leg in layout)
                DrawParts(canvas, leg.Parts, casing);
        }

        if (editing)
        {
            if (scene.SelectedLegIndex is { } selected && selected < layout.Count)
            {
                using var halo = Stroke(colors.Edit.WithAlpha(Alpha(0.25f * opacity)), 12f);
                DrawParts(canvas, layout[selected].Parts, halo);
            }
        }

        if (scene.Check is { NoDraft: false } coloured)
        {
            DrawCheckRoute(canvas, coloured, layout, toScreen, opacity);
        }
        else
        {
            using var line = Stroke(colors.Edit.WithAlpha(Alpha((editing ? 1f : 0.85f) * opacity)), editing ? 3.5f : 4f);
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

    /// <summary>Width of a state's line: below the setting is wider, so it doesn't rely on colour alone (C6).</summary>
    internal static float CheckStrokeWidth(Navigation.UkcState state) => state == Navigation.UkcState.BelowSetting ? 7f : 5f;

    /// <summary>
    /// Whether a run's line is dashed 6/4: no data, and lower confidence in
    /// any band (C6, with confidence drawn over the band colour per #939).
    /// </summary>
    internal static bool IsCheckDashed(Navigation.UkcState state, bool isLowConfidence = false) =>
        isLowConfidence || state == Navigation.UkcState.NoData;

    private static void DrawCheckRoute(SKCanvas canvas, RouteCheckOverlay check, IReadOnlyList<LegLayout> layout, Func<GeoPosition, Point?> toScreen, float opacity)
    {
        if (check.IsStale || check.Segments.Count == 0)
        {
            // C10: grey and dashed while a result is computing.
            using var stale = Stroke(check.Colors.Stale.WithAlpha(Alpha(opacity)), 5f);
            stale.StrokeCap = SKStrokeCap.Butt;
            stale.PathEffect = SKPathEffect.CreateDash([6f, 4f], 0);
            foreach (var leg in layout)
                DrawParts(canvas, leg.Parts, stale);
            return;
        }

        foreach (var segment in check.Segments)
        {
            using var paint = Stroke(check.Colors.Of(segment.State).WithAlpha(Alpha(opacity)), CheckStrokeWidth(segment.State));
            if (IsCheckDashed(segment.State, segment.IsLowConfidence))
            {
                paint.StrokeCap = SKStrokeCap.Butt;
                paint.PathEffect = SKPathEffect.CreateDash([6f, 4f], 0);
            }
            foreach (var part in Geodesy.MarineGeodesy.SplitAtAntimeridian(segment.Path))
                DrawPath(canvas, Project(part, toScreen), paint);
        }
    }

    /// <summary>Radius of a finding's marker dot, in pixels (#953).</summary>
    internal const float FindingMarkerRadius = 4.5f;

    private static void DrawFindingMarkers(SKCanvas canvas, RouteCheckOverlay check, Func<GeoPosition, Point?> toScreen, float opacity)
    {
        // #953: a dot per finding in its group's tone, on a casing ring. The
        // selected one is also outlined (F3), so it's drawn a little larger.
        using var casing = Stroke(check.Colors.Casing.WithAlpha(Alpha(opacity)), 2f);
        foreach (var marker in check.Markers)
        {
            if (toScreen(marker.Position) is not { } p)
                continue;
            var r = marker.IsSelected ? FindingMarkerRadius + 1.5f : FindingMarkerRadius;
            using var fill = Fill(marker.Tone.WithAlpha(Alpha(opacity)));
            canvas.DrawCircle((float)p.X, (float)p.Y, r, fill);
            canvas.DrawCircle((float)p.X, (float)p.Y, r, casing);
        }
    }

    private static void DrawCheckMarkers(SKCanvas canvas, RouteCheckOverlay check, Func<GeoPosition, Point?> toScreen, float rotation, float opacity)
    {
        var colors = check.Colors;
        if (!check.IsStale && check.Minimum is { } min && toScreen(min.Position) is { } m)
        {
            // C8: a ring in the tone colour and the pill "Min UKC 0,4 m · 07:52".
            var (x, y) = ((float)m.X, (float)m.Y);
            var tone = colors.Of(min.State);
            using var halo = Stroke(colors.Casing.WithAlpha(Alpha(opacity)), 5f);
            using var ring = Stroke(tone.WithAlpha(Alpha(opacity)), 2.5f);
            canvas.DrawCircle(x, y, 9f, halo);
            canvas.DrawCircle(x, y, 9f, ring);

            using var font = new SKFont(LabelBoldTypeface, LabelFontSize);
            var width = font.MeasureText(min.Label) + 14f;
            var rect = SKRect.Create(x + 12f, y - 22f, width, 18f);
            using var pill = Fill(colors.PillFill.WithAlpha(Alpha(opacity)));
            using var edge = Stroke(tone.WithAlpha(Alpha(opacity)), 1.2f);
            canvas.DrawRoundRect(rect, 9f, 9f, pill);
            canvas.DrawRoundRect(rect, 9f, 9f, edge);
            using var ink = Fill(colors.Ink.WithAlpha(Alpha(opacity)));
            var baseline = rect.MidY - ((font.Metrics.Ascent + font.Metrics.Descent) / 2f);
            canvas.DrawText(min.Label, rect.Left + 7f, baseline, SKTextAlign.Left, font, ink);
        }

        if (check.Ghost is { } ghost && toScreen(ghost.Position) is { } g)
        {
            // C9: an arrow at the cursor, pointing along the leg.
            canvas.Save();
            canvas.Translate((float)g.X, (float)g.Y);
            canvas.RotateDegrees((float)ghost.BearingDegrees + rotation);
            using var arrow = new SKPath();
            arrow.MoveTo(0, -11);
            arrow.LineTo(7, 8);
            arrow.LineTo(0, 4);
            arrow.LineTo(-7, 8);
            arrow.Close();
            using var casing = Stroke(colors.Casing.WithAlpha(Alpha(opacity)), 3f);
            using var body = Fill(colors.Ink.WithAlpha(Alpha(0.85f * opacity)));
            canvas.DrawPath(arrow, casing);
            canvas.DrawPath(arrow, body);
            canvas.Restore();
        }
    }

    /// <summary>Space below the legend, clear of the Time HUD (36 px tall, 12 px up).</summary>
    private const float LegendBottomMargin = 60f;

    /// <returns>The legend's top edge, so the finding chip can sit above it.</returns>
    private static float DrawLegend(SKCanvas canvas, RouteCheckOverlay check, float height, float opacity)
    {
        // C7: "UKC AT ETA" at the map's bottom left: the three bands, then
        // the dashed lower-confidence key (in the Meets colour, standing for
        // any band) and grey dashed no data (#939).
        var colors = check.Colors;
        var items = new (string Label, Navigation.UkcState State, bool IsLowConfidence)[]
        {
            (Resources.Strings.Check_LegendMeets, Navigation.UkcState.MeetsSettings, false),
            (Resources.Strings.Check_LegendTight, Navigation.UkcState.Tight, false),
            (Resources.Strings.Check_LegendBelow, Navigation.UkcState.BelowSetting, false),
            (Resources.Strings.Check_LegendLowConfidence, Navigation.UkcState.MeetsSettings, true),
            (Resources.Strings.Check_LegendNoData, Navigation.UkcState.NoData, false),
        };
        using var font = new SKFont(LabelTypeface, LabelFontSize);
        using var titleFont = new SKFont(LabelBoldTypeface, 9.5f);
        var width = 24f + items.Max(i => font.MeasureText(i.Label)) + 20f;
        var rowHeight = 16f;
        var boxHeight = 22f + (items.Length * rowHeight);
        // Clear of the Time HUD at the bottom centre and the scale readout.
        var rect = SKRect.Create(12f, height - boxHeight - LegendBottomMargin, Math.Max(width, titleFont.MeasureText(Resources.Strings.Check_LegendTitle) + 20f), boxHeight);

        using var fill = Fill(colors.PillFill.WithAlpha(Alpha(0.92f * opacity)));
        using var edge = Stroke(colors.NoData.WithAlpha(Alpha(0.5f * opacity)), 1f);
        canvas.DrawRoundRect(rect, 6f, 6f, fill);
        canvas.DrawRoundRect(rect, 6f, 6f, edge);
        using var ink = Fill(colors.Ink.WithAlpha(Alpha(opacity)));
        canvas.DrawText(Resources.Strings.Check_LegendTitle, rect.Left + 10f, rect.Top + 15f, SKTextAlign.Left, titleFont, ink);

        for (var i = 0; i < items.Length; i++)
        {
            var y = rect.Top + 28f + (i * rowHeight);
            using var swatch = Stroke(colors.Of(items[i].State).WithAlpha(Alpha(opacity)), CheckStrokeWidth(items[i].State) - 1f);
            swatch.StrokeCap = SKStrokeCap.Butt;
            if (IsCheckDashed(items[i].State, items[i].IsLowConfidence))
                swatch.PathEffect = SKPathEffect.CreateDash([6f, 4f], 0);
            canvas.DrawLine(rect.Left + 10f, y, rect.Left + 28f, y, swatch);
            canvas.DrawText(items[i].Label, rect.Left + 34f, y + 3.5f, SKTextAlign.Left, font, ink);
        }

        return rect.Top;
    }

    /// <summary>Half the width of the corridor outlined around a selected finding's span, in pixels.</summary>
    internal const float FindingOutlineHalfWidth = 14f;

    /// <summary>Radius of the circle outlined around a selected point finding, in pixels.</summary>
    internal const float FindingOutlineRadius = 22f;

    private static void DrawFindingOutline(SKCanvas canvas, RouteCheckOverlay.FindingFocus finding, RouteCheckColors colors, Func<GeoPosition, Point?> toScreen, float opacity)
    {
        // F3: the finding's area outlined dashed in its tone, over a casing.
        using var casing = Stroke(colors.Casing.WithAlpha(Alpha(0.8f * opacity)), 4f);
        using var dash = Stroke(finding.Tone.WithAlpha(Alpha(opacity)), 1.6f);
        dash.PathEffect = SKPathEffect.CreateDash([5f, 3f], 0);

        var points = Project(finding.Path, toScreen);
        if (points.Count == 0)
            return;
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        if (points.Count == 1 || (maxX - minX < FindingOutlineRadius && maxY - minY < FindingOutlineRadius))
        {
            var (cx, cy) = ((float)((minX + maxX) / 2), (float)((minY + maxY) / 2));
            canvas.DrawCircle(cx, cy, FindingOutlineRadius, casing);
            canvas.DrawCircle(cx, cy, FindingOutlineRadius, dash);
            return;
        }

        // A span: the outline of a corridor along the route.
        using var line = new SKPath();
        line.MoveTo((float)points[0].X, (float)points[0].Y);
        for (var i = 1; i < points.Count; i++)
            line.LineTo((float)points[i].X, (float)points[i].Y);
        using var widen = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = FindingOutlineHalfWidth * 2,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var outline = new SKPath();
        widen.GetFillPath(line, outline);
        using var simple = outline.Simplify() ?? outline;
        canvas.DrawPath(simple, casing);
        canvas.DrawPath(simple, dash);
    }

    private static void DrawFindingChip(SKCanvas canvas, RouteCheckOverlay.FindingFocus finding, RouteCheckColors colors, string scale, float legendTop, float opacity)
    {
        // F3: "Zoomed to finding · leg 6 → 7 · 1:31 300", above the legend.
        var text = string.IsNullOrEmpty(scale) ? finding.ChipText : finding.ChipText + " · " + scale;
        using var font = new SKFont(LabelTypeface, LabelFontSize);
        var runs = FallbackRuns(text, font);
        var rect = SKRect.Create(12f, legendTop - 8f - 26f, runs.Sum(r => r.Width) + 20f, 26f);
        using var fill = Fill(colors.PillFill.WithAlpha(Alpha(0.92f * opacity)));
        using var edge = Stroke(colors.NoData.WithAlpha(Alpha(0.5f * opacity)), 1f);
        canvas.DrawRoundRect(rect, 8f, 8f, fill);
        canvas.DrawRoundRect(rect, 8f, 8f, edge);
        using var ink = Fill(colors.Ink.WithAlpha(Alpha(0.8f * opacity)));
        var baseline = rect.MidY - ((font.Metrics.Ascent + font.Metrics.Descent) / 2f);
        var x = rect.Left + 10f;
        foreach (var run in runs)
        {
            canvas.DrawText(run.Text, x, baseline, SKTextAlign.Left, run.Font, ink);
            x += run.Width;
            if (!ReferenceEquals(run.Font, font))
                run.Font.Dispose();
        }
    }

    /// <summary>
    /// Splits <paramref name="text"/> into runs the label face can draw and
    /// runs in a platform fallback face (the label faces have no "→").
    /// </summary>
    private static List<(string Text, SKFont Font, float Width)> FallbackRuns(string text, SKFont font)
    {
        var runs = new List<(string, SKFont, float)>();
        var start = 0;
        SKTypeface? current = null;
        void Flush(int end)
        {
            if (end <= start)
                return;
            var part = text[start..end];
            var runFont = current is null ? font : new SKFont(current, font.Size);
            runs.Add((part, runFont, runFont.MeasureText(part)));
        }

        for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
        {
            var cp = char.ConvertToUtf32(text, i);
            var face = font.Typeface.ContainsGlyph(cp) ? null : SKFontManager.Default.MatchCharacter(cp);
            if (!ReferenceEquals(face, current) && (face is null || current is null || face.FamilyName != current.FamilyName))
            {
                Flush(i);
                start = i;
                current = face;
            }
        }

        Flush(text.Length);
        return runs;
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
