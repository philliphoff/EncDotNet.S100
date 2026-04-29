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

        var bgBrush = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
        var borderBrush = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0));
        var borderPen = new Pen(borderBrush, 1);
        context.DrawEllipse(bgBrush, borderPen, new Point(cx, cy), outerRadius, outerRadius);

        var accent = TryFindAccentBrush() ?? Brushes.SteelBlue;
        var tickBrush = new SolidColorBrush(Color.FromArgb(190, 30, 30, 30));
        var cardinalBrush = new SolidColorBrush(Color.FromArgb(230, 20, 20, 20));

        var rotation = MapRotation;
        var cardinalLength = Math.Max(3.0, size * 0.18);
        var minorLength = Math.Max(2.0, size * 0.10);

        for (var a = 0; a < 360; a += 10)
        {
            var isCardinal = a % 90 == 0;
            var isNorth = a == 0;
            var tickLen = isCardinal ? cardinalLength : minorLength;
            var thickness = isCardinal ? 1.6 : 1.0;
            IBrush brush = isNorth ? accent : (isCardinal ? cardinalBrush : tickBrush);

            var rad = (rotation + a) * Math.PI / 180.0;
            var sin = Math.Sin(rad);
            var cos = Math.Cos(rad);
            var p1 = new Point(cx + sin * outerRadius, cy - cos * outerRadius);
            var p2 = new Point(cx + sin * (outerRadius - tickLen), cy - cos * (outerRadius - tickLen));
            var pen = new Pen(brush, thickness)
            {
                LineCap = PenLineCap.Round,
            };
            context.DrawLine(pen, p1, p2);
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
