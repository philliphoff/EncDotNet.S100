using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Map compass-rose overlay. Draws a circular ring of tick marks with the four
/// cardinal ticks emphasized and the north tick rendered in the accent color.
/// The whole tick ring rotates with the map so the north tick always points to
/// true north; the center letter shows whichever cardinal direction is most
/// closely aligned with screen-up.
/// </summary>
internal sealed class CompassRoseView : Control
{
    /// <summary>
    /// Map rotation in degrees clockwise (matches <c>Mapsui.Viewport.Rotation</c>).
    /// </summary>
    public static readonly StyledProperty<double> MapRotationProperty =
        AvaloniaProperty.Register<CompassRoseView, double>(nameof(MapRotation));

    static CompassRoseView()
    {
        AffectsRender<CompassRoseView>(MapRotationProperty);
    }

    public double MapRotation
    {
        get => GetValue(MapRotationProperty);
        set => SetValue(MapRotationProperty, value);
    }

    /// <summary>
    /// Updates the compass for the current map viewport rotation (degrees clockwise).
    /// </summary>
    public void UpdateForViewport(double mapRotationDegrees)
    {
        MapRotation = mapRotationDegrees;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Bounds.Height;
        var size = Math.Min(width, height);
        if (size <= 0)
            return;

        var cx = width / 2.0;
        var cy = height / 2.0;
        var outerRadius = size / 2.0 - 1.0;
        if (outerRadius <= 0)
            return;

        var bgBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
        var borderBrush = new SolidColorBrush(Color.FromArgb(64, 0, 0, 0));
        var borderPen = new Pen(borderBrush, 1);
        context.DrawEllipse(bgBrush, borderPen, new Point(cx, cy), outerRadius, outerRadius);

        var accent = TryFindAccentBrush() ?? Brushes.SteelBlue;
        var tickBrush = new SolidColorBrush(Color.FromArgb(150, 40, 40, 40));

        var rotation = MapRotation;
        var minorLength = Math.Max(2.0, size * 0.10);
        var cardinalLength = Math.Max(4.0, size * 0.22);
        var northLength = Math.Max(5.0, size * 0.28);

        // Minor ticks every 30 degrees, skipping the cardinals (which are
        // drawn as triangles below).
        for (var a = 30; a < 360; a += 30)
        {
            if (a % 90 == 0)
                continue;
            var rad = (rotation + a) * Math.PI / 180.0;
            var sin = Math.Sin(rad);
            var cos = Math.Cos(rad);
            var p1 = new Point(cx + sin * outerRadius, cy - cos * outerRadius);
            var p2 = new Point(cx + sin * (outerRadius - minorLength), cy - cos * (outerRadius - minorLength));
            var pen = new Pen(tickBrush, 1.0) { LineCap = PenLineCap.Round };
            context.DrawLine(pen, p1, p2);
        }

        // Cardinal ticks rendered as thin inward-pointing triangles. North is
        // larger and rendered in the accent color.
        for (var a = 0; a < 360; a += 90)
        {
            var isNorth = a == 0;
            var tickLen = isNorth ? northLength : cardinalLength;
            var halfBase = isNorth ? Math.Max(2.0, size * 0.07) : Math.Max(1.5, size * 0.05);
            IBrush fill = isNorth ? accent : tickBrush;

            var rad = (rotation + a) * Math.PI / 180.0;
            var sin = Math.Sin(rad);
            var cos = Math.Cos(rad);
            // Tangent direction (perpendicular to the radial), used to offset
            // the base vertices to either side of the tick line.
            var tx = cos;
            var ty = sin;

            var tip = new Point(cx + sin * outerRadius, cy - cos * outerRadius);
            var basePx = cx + sin * (outerRadius - tickLen);
            var basePy = cy - cos * (outerRadius - tickLen);
            var b1 = new Point(basePx + tx * halfBase, basePy + ty * halfBase);
            var b2 = new Point(basePx - tx * halfBase, basePy - ty * halfBase);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(tip, isFilled: true);
                ctx.LineTo(b1);
                ctx.LineTo(b2);
                ctx.EndFigure(isClosed: true);
            }
            context.DrawGeometry(fill, null, geometry);
        }

        // Center letter shows the cardinal direction nearest to screen-up.
        // A clockwise map rotation of R puts the bearing (360 - R) at screen-up.
        var upBearing = (((360.0 - rotation) % 360.0) + 360.0) % 360.0;
        var letter = NearestCardinal(upBearing);
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        var foreground = new SolidColorBrush(Color.FromRgb(26, 26, 26));
        var fontSize = Math.Max(8.0, size * 0.40);
        var formatted = new FormattedText(
            letter,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            foreground);
        var origin = new Point(cx - formatted.Width / 2.0, cy - formatted.Height / 2.0);
        context.DrawText(formatted, origin);
    }

    private IBrush? TryFindAccentBrush()
    {
        if (this.TryFindResource("AccentBrush", out var value) && value is IBrush brush)
            return brush;
        return null;
    }

    private static string NearestCardinal(double bearingDegrees)
    {
        // 0=N, 90=E, 180=S, 270=W. Snap to whichever is within 45 degrees.
        var b = ((bearingDegrees % 360.0) + 360.0) % 360.0;
        if (b < 45.0 || b >= 315.0) return "N";
        if (b < 135.0) return "E";
        if (b < 225.0) return "S";
        return "W";
    }
}
