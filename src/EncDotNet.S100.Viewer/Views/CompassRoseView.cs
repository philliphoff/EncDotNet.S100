using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Map compass-rose overlay. Draws a circular ring of tick marks with the four
/// cardinal ticks emphasized and the north tick rendered in the accent color.
/// The whole tick ring rotates with the map so the north tick always points to
/// true north; the center letter shows whichever cardinal direction is most
/// closely aligned with screen-up. Background, tick, and text colors follow
/// the active light/dark theme variant.
///
/// The compass also acts as a rotation control: pressing inside the compass
/// "grabs" an arm; subsequent pointer motion rotates the map so the grabbed
/// angle stays under the pointer. Releasing the pointer ends the gesture.
/// </summary>
internal sealed class CompassRoseView : Control
{
    /// <summary>
    /// Map rotation in degrees clockwise (matches <c>Mapsui.Viewport.Rotation</c>).
    /// </summary>
    public static readonly StyledProperty<double> MapRotationProperty =
        AvaloniaProperty.Register<CompassRoseView, double>(nameof(MapRotation));

    private bool _isDragging;
    private double _grabPointerAngle;
    private double _grabMapRotation;

    static CompassRoseView()
    {
        AffectsRender<CompassRoseView>(MapRotationProperty);
    }

    public CompassRoseView()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double MapRotation
    {
        get => GetValue(MapRotationProperty);
        set => SetValue(MapRotationProperty, value);
    }

    /// <summary>
    /// Raised continuously while the user is rotating via the compass. The
    /// argument is the requested map rotation in degrees clockwise, normalized
    /// to <c>[0, 360)</c>.
    /// </summary>
    public event Action<double>? RotationRequested;

    /// <summary>
    /// Raised when the user double-clicks the compass to request resetting
    /// the map to a north-up orientation.
    /// </summary>
    public event Action? RotationResetRequested;

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

        var palette = ResolvePalette();

        context.DrawEllipse(palette.Background, null, new Point(cx, cy), outerRadius, outerRadius);

        var rotation = MapRotation;
        // Inset the tick ring so ticks don't touch the compass edge.
        var tickPadding = Math.Max(3.0, size * 0.10);
        var tickOuterRadius = outerRadius - tickPadding;
        var minorLength = Math.Max(1.5, size * 0.05);
        var cardinalLength = Math.Max(3.0, size * 0.14);

        // Minor ticks every 30 degrees, skipping the cardinals (which are
        // drawn as triangles below).
        for (var a = 30; a < 360; a += 30)
        {
            if (a % 90 == 0)
                continue;
            var rad = (rotation + a) * Math.PI / 180.0;
            var sin = Math.Sin(rad);
            var cos = Math.Cos(rad);
            var p1 = new Point(cx + sin * tickOuterRadius, cy - cos * tickOuterRadius);
            var p2 = new Point(cx + sin * (tickOuterRadius - minorLength), cy - cos * (tickOuterRadius - minorLength));
            var pen = new Pen(palette.Tick, 1.0) { LineCap = PenLineCap.Round };
            context.DrawLine(pen, p1, p2);
        }

        // Cardinal ticks rendered as thin inward-pointing triangles. The
        // north tick uses the accent color but is otherwise the same size as
        // the other cardinals.
        for (var a = 0; a < 360; a += 90)
        {
            var isNorth = a == 0;
            var halfBase = isNorth ? Math.Max(2.0, size * 0.065) : Math.Max(1.5, size * 0.05);
            var tickLen = isNorth ? cardinalLength * 1.10 : cardinalLength;
            IBrush fill = isNorth ? palette.North : palette.Tick;

            var rad = (rotation + a) * Math.PI / 180.0;
            var sin = Math.Sin(rad);
            var cos = Math.Cos(rad);
            // Tangent direction (perpendicular to the radial), used to offset
            // the base vertices to either side of the tick line.
            var tx = cos;
            var ty = sin;

            var tip = new Point(cx + sin * tickOuterRadius, cy - cos * tickOuterRadius);
            var basePx = cx + sin * (tickOuterRadius - tickLen);
            var basePy = cy - cos * (tickOuterRadius - tickLen);
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
        var fontSize = Math.Max(8.0, size * 0.40);
        var formatted = new FormattedText(
            letter,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            palette.Foreground);
        // Center the visible glyph (not the full text box) on the compass.
        // FormattedText positions by the box top-left and the cap-height sits
        // above the baseline, so use Baseline + an approximate cap-height to
        // center vertically without drift toward the top.
        var capHeight = fontSize * 0.72;
        var baselineY = cy + capHeight / 2.0;
        var origin = new Point(cx - formatted.Width / 2.0, baselineY - formatted.Baseline);
        context.DrawText(formatted, origin);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var pos = e.GetPosition(this);
        if (!IsInsideCompass(pos))
            return;

        if (e.ClickCount >= 2)
        {
            // Double-click resets the map to north-up. Don't enter drag mode.
            _isDragging = false;
            RotationResetRequested?.Invoke();
            e.Handled = true;
            return;
        }

        _isDragging = true;
        _grabPointerAngle = AngleFromCenter(pos);
        _grabMapRotation = MapRotation;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging)
            return;

        var pos = e.GetPosition(this);
        var angle = AngleFromCenter(pos);
        var rotation = _grabMapRotation + (angle - _grabPointerAngle);
        rotation = ((rotation % 360.0) + 360.0) % 360.0;
        RotationRequested?.Invoke(rotation);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging)
            return;

        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    private bool IsInsideCompass(Point pos)
    {
        var cx = Bounds.Width / 2.0;
        var cy = Bounds.Height / 2.0;
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2.0 - 1.0;
        if (radius <= 0)
            return false;
        var dx = pos.X - cx;
        var dy = pos.Y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }

    private double AngleFromCenter(Point pos)
    {
        var cx = Bounds.Width / 2.0;
        var cy = Bounds.Height / 2.0;
        // Screen-up is angle 0; clockwise positive (matches Mapsui rotation).
        var dx = pos.X - cx;
        var dy = pos.Y - cy;
        var rad = Math.Atan2(dx, -dy);
        return rad * 180.0 / Math.PI;
    }

    private Palette ResolvePalette()
    {
        var isDark = ActualThemeVariant == ThemeVariant.Dark;
        var north = TryFindAccentBrush() ?? Brushes.SteelBlue;
        if (isDark)
        {
            return new Palette(
                Background: new SolidColorBrush(Color.FromArgb(140, 30, 30, 30)),
                Tick: new SolidColorBrush(Color.FromArgb(190, 230, 230, 230)),
                Foreground: new SolidColorBrush(Color.FromArgb(240, 240, 240, 240)),
                North: north);
        }

        return new Palette(
            Background: new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            Tick: new SolidColorBrush(Color.FromArgb(170, 40, 40, 40)),
            Foreground: new SolidColorBrush(Color.FromArgb(230, 26, 26, 26)),
            North: north);
    }

    private IBrush? TryFindAccentBrush()
    {
        if (this.TryFindResource("AccentBrush", out var value))
        {
            if (value is IBrush brush)
                return brush;
            if (value is Color color)
                return new SolidColorBrush(color);
        }
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

    private readonly record struct Palette(IBrush Background, IBrush Tick, IBrush Foreground, IBrush North);
}
