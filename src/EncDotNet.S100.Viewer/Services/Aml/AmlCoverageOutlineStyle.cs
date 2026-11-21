using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Services.Aml;

/// <summary>
/// The coverage outline of an AML cell (#1371, decision 9): a 1.4 px ink frame
/// with inward ticks every 9 px, and a label card in the top-left corner.
/// Drawn by <see cref="AmlCoverageOutlineRenderer"/>.
/// </summary>
/// <remarks>
/// No hatch (that means protected data) and no tint (that means Library
/// coverage): the frame and its ticks are the only marks, so it can't be read
/// as either.
/// </remarks>
internal sealed class AmlCoverageOutlineStyle : BaseStyle
{
    /// <summary>The frame and text colour.</summary>
    public required SKColor Ink { get; init; }

    /// <summary>The label card's fill: the chart background of the palette.</summary>
    public required SKColor Card { get; init; }

    /// <summary>The card's first line: "AML · CLB  NOC6RA3B".</summary>
    public required string Title { get; init; }

    /// <summary>The card's second line: "Not drawn · RESTRICTED".</summary>
    public required string Subtitle { get; init; }
}

/// <summary>
/// Draws <see cref="AmlCoverageOutlineStyle"/>: the frame, its inward ticks
/// and the label card, in screen pixels so they keep their size at every zoom.
/// </summary>
internal sealed class AmlCoverageOutlineRenderer : ISkiaStyleRenderer
{
    /// <summary>The frame's width, in pixels.</summary>
    internal const float FrameWidth = 1.4f;

    /// <summary>The distance between ticks along the frame, in pixels.</summary>
    internal const float TickSpacing = 9f;

    /// <summary>How far a tick reaches inward, in pixels.</summary>
    internal const float TickLength = 4.5f;

    private const float CardInset = 8f;
    private const float CardPaddingX = 7f;
    private const float CardPaddingY = 4f;
    private const float LineGap = 2f;

    private static readonly object RegisterGate = new();
    private static bool _registered;

    /// <summary>The shared instance.</summary>
    public static AmlCoverageOutlineRenderer Instance { get; } = new();

    /// <summary>Registers the renderer with Mapsui once.</summary>
    internal static void Register()
    {
        lock (RegisterGate)
        {
            if (_registered)
                return;
            global::Mapsui.Rendering.Skia.MapRenderer.RegisterStyleRenderer(typeof(AmlCoverageOutlineStyle), Instance);
            _registered = true;
        }
    }

    /// <inheritdoc />
    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature, IStyle style, RenderService renderService, long iteration)
    {
        if (feature is not GeometryFeature { Geometry: { } geometry } || style is not AmlCoverageOutlineStyle outline)
            return false;

        var polygons = geometry switch
        {
            Polygon p => [p],
            MultiPolygon mp => mp.Geometries.OfType<Polygon>().ToArray(),
            _ => Array.Empty<Polygon>(),
        };
        if (polygons.Length == 0)
            return false;

        var opacity = (float)Math.Clamp(layer.Opacity * outline.Opacity, 0, 1);
        var ink = outline.Ink.WithAlpha((byte)(outline.Ink.Alpha * opacity));
        var view = new SKRect(0, 0, (float)viewport.Width, (float)viewport.Height);
        var tickClip = SKRect.Inflate(view, TickLength * 2, TickLength * 2);

        using var frame = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = FrameWidth,
            StrokeJoin = SKStrokeJoin.Miter,
            Color = ink,
        };
        using var tick = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.1f,
            StrokeCap = SKStrokeCap.Butt,
            Color = ink,
        };

        SKRect? labelBounds = null;
        foreach (var polygon in polygons)
        {
            var exterior = ToScreen(polygon.ExteriorRing, viewport);
            DrawRing(canvas, exterior, hole: false, frame, tick, tickClip);
            foreach (var hole in polygon.InteriorRings)
                DrawRing(canvas, ToScreen(hole, viewport), hole: true, frame, tick, tickClip);

            var bounds = BoundsOf(exterior);
            if (labelBounds is not { } best || bounds.Width * bounds.Height > best.Width * best.Height)
                labelBounds = bounds;
        }

        if (labelBounds is { } box)
            DrawCard(canvas, outline, box, view, ink, opacity);
        return true;
    }

    /// <summary>
    /// Where the ticks of a ring go: every <see cref="TickSpacing"/> pixels
    /// along it, each pointing into the covered area. Ticks outside
    /// <paramref name="clip"/> are left out. Exposed for tests.
    /// </summary>
    /// <param name="ring">The ring in screen pixels, closed or not.</param>
    /// <param name="hole">Whether the ring is a hole, whose ticks point out of it.</param>
    /// <param name="clip">The area ticks are kept in.</param>
    /// <returns>Each tick's start (on the ring) and end (inside the area).</returns>
    internal static List<(SKPoint From, SKPoint To)> Ticks(IReadOnlyList<SKPoint> ring, bool hole, SKRect clip)
    {
        var ticks = new List<(SKPoint, SKPoint)>();
        if (ring.Count < 3)
            return ticks;

        // In screen space (y down), a positive shoelace sum is a clockwise ring
        // on screen, whose inside is to the right of the direction of travel.
        double area = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            area += ((double)a.X * b.Y) - ((double)b.X * a.Y);
        }

        var insideIsRight = area > 0 != hole;
        double travelled = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 1e-6)
                continue;

            if (ClipSegment(a, b, clip, out var u0, out var u1))
            {
                var ux = dx / length;
                var uy = dy / length;
                var (nx, ny) = insideIsRight ? (-uy, ux) : (uy, -ux);
                var from = travelled + (u0 * length);
                var to = travelled + (u1 * length);
                // Half a step off the ring's start, so no tick lands on a
                // corner of a rectangular frame.
                for (var s = (Math.Ceiling((from / TickSpacing) - 0.5) + 0.5) * TickSpacing; s < to; s += TickSpacing)
                {
                    var t = s - travelled;
                    var px = (float)(a.X + (ux * t));
                    var py = (float)(a.Y + (uy * t));
                    ticks.Add((new SKPoint(px, py), new SKPoint(px + (float)(nx * TickLength), py + (float)(ny * TickLength))));
                }
            }

            travelled += length;
        }

        return ticks;
    }

    private static void DrawRing(SKCanvas canvas, SKPoint[] ring, bool hole, SKPaint frame, SKPaint tick, SKRect tickClip)
    {
        if (ring.Length < 3)
            return;

        using var path = new SKPath();
        path.AddPoly(ring, close: true);
        canvas.DrawPath(path, frame);

        foreach (var (from, to) in Ticks(ring, hole, tickClip))
            canvas.DrawLine(from, to, tick);
    }

    private static void DrawCard(SKCanvas canvas, AmlCoverageOutlineStyle outline, SKRect box, SKRect view, SKColor ink, float opacity)
    {
        using var titleFont = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 11.5f);
        using var subtitleFont = new SKFont(SKTypeface.Default, 10.5f);
        titleFont.Subpixel = subtitleFont.Subpixel = true;

        var titleWidth = titleFont.MeasureText(outline.Title);
        var subtitleWidth = subtitleFont.MeasureText(outline.Subtitle);
        var width = Math.Max(titleWidth, subtitleWidth) + (CardPaddingX * 2);
        var titleHeight = titleFont.Size;
        var height = titleHeight + LineGap + subtitleFont.Size + (CardPaddingY * 2) + 2;

        // Top-left inside the frame; while the corner is off screen, the card
        // slides along so it stays in view as long as the frame does.
        var x = Math.Max(box.Left, view.Left) + CardInset;
        var y = Math.Max(box.Top, view.Top) + CardInset;
        if (x + width > Math.Min(box.Right, view.Right) - 2 || y + height > Math.Min(box.Bottom, view.Bottom) - 2)
            return;

        var card = new SKRect(x, y, x + width, y + height);
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = outline.Card.WithAlpha((byte)(242 * opacity)),
        };
        using var border = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color = ink,
        };
        canvas.DrawRoundRect(card, 3, 3, fill);
        canvas.DrawRoundRect(card, 3, 3, border);

        using var text = new SKPaint { IsAntialias = true, Color = ink };
        var baseline = y + CardPaddingY + titleHeight - 1;
        canvas.DrawText(outline.Title, x + CardPaddingX, baseline, SKTextAlign.Left, titleFont, text);
        text.Color = ink.WithAlpha((byte)(ink.Alpha * 0.8f));
        canvas.DrawText(outline.Subtitle, x + CardPaddingX, baseline + LineGap + subtitleFont.Size, SKTextAlign.Left, subtitleFont, text);
    }

    private static SKPoint[] ToScreen(LineString ring, Viewport viewport)
    {
        var coordinates = ring.Coordinates;
        var count = coordinates.Length;
        // A closed ring repeats its first point; the path closes itself.
        if (count > 1 && coordinates[0].Equals2D(coordinates[count - 1]))
            count--;

        var points = new SKPoint[count];
        for (var i = 0; i < count; i++)
        {
            var (sx, sy) = viewport.WorldToScreenXY(coordinates[i].X, coordinates[i].Y);
            points[i] = new SKPoint((float)sx, (float)sy);
        }

        return points;
    }

    private static SKRect BoundsOf(SKPoint[] points)
    {
        if (points.Length == 0)
            return SKRect.Empty;
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        foreach (var p in points)
        {
            left = Math.Min(left, p.X);
            top = Math.Min(top, p.Y);
            right = Math.Max(right, p.X);
            bottom = Math.Max(bottom, p.Y);
        }

        return new SKRect(left, top, right, bottom);
    }

    // Liang–Barsky: the part of a→b inside clip, as fractions of the segment.
    private static bool ClipSegment(SKPoint a, SKPoint b, SKRect clip, out double u0, out double u1)
    {
        u0 = 0;
        u1 = 1;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double[] p = [-dx, dx, -dy, dy];
        double[] q = [a.X - clip.Left, clip.Right - a.X, a.Y - clip.Top, clip.Bottom - a.Y];
        for (var i = 0; i < 4; i++)
        {
            if (p[i] == 0)
            {
                if (q[i] < 0)
                    return false;
                continue;
            }

            var r = q[i] / p[i];
            if (p[i] < 0)
            {
                if (r > u1)
                    return false;
                u0 = Math.Max(u0, r);
            }
            else
            {
                if (r < u0)
                    return false;
                u1 = Math.Min(u1, r);
            }
        }

        return u0 <= u1;
    }
}
