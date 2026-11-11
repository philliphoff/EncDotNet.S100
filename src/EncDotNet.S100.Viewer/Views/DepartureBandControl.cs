using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Paints the Clearance tab's departure band (#903, handoff D5): runs of
/// swept departures across the S-104 range, success where they meet your
/// settings, destructive at 35 % where they don't, hatched where the arrival
/// is after the data ends and muted where data is missing, with a knob at
/// the current departure. Pressing or dragging reports the fraction under
/// the pointer, and Left / Right (Shift for twelve steps, an hour at 5 minutes) nudge the departure;
/// the view turns those into departures with <see cref="DepartureBand"/>.
/// </summary>
internal sealed class DepartureBandControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<DepartureBandSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<DepartureBandControl, IReadOnlyList<DepartureBandSegment>?>(nameof(Segments));

    /// <summary>The knob's position, 0–1, or <c>null</c> for no knob.</summary>
    public static readonly StyledProperty<double?> KnobFractionProperty =
        AvaloniaProperty.Register<DepartureBandControl, double?>(nameof(KnobFraction));

    public static readonly StyledProperty<Color> SuccessColorProperty =
        AvaloniaProperty.Register<DepartureBandControl, Color>(nameof(SuccessColor), Color.FromRgb(0x13, 0x89, 0x5B));

    public static readonly StyledProperty<Color> DestructiveColorProperty =
        AvaloniaProperty.Register<DepartureBandControl, Color>(nameof(DestructiveColor), Color.FromRgb(0xD4, 0x2A, 0x3A));

    public static readonly StyledProperty<Color> MutedColorProperty =
        AvaloniaProperty.Register<DepartureBandControl, Color>(nameof(MutedColor), Color.FromRgb(0x8C, 0x95, 0xA3));

    public static readonly StyledProperty<Color> KnobColorProperty =
        AvaloniaProperty.Register<DepartureBandControl, Color>(nameof(KnobColor), Colors.Black);

    public static readonly StyledProperty<Color> KnobHaloColorProperty =
        AvaloniaProperty.Register<DepartureBandControl, Color>(nameof(KnobHaloColor), Colors.White);

    /// <summary>The height of the coloured runs; the knob is taller.</summary>
    public const double BarHeight = 8;

    private const double KnobWidth = 4;
    private const double KnobHeight = 18;

    static DepartureBandControl()
    {
        AffectsRender<DepartureBandControl>(
            SegmentsProperty, KnobFractionProperty, SuccessColorProperty, DestructiveColorProperty,
            MutedColorProperty, KnobColorProperty, KnobHaloColorProperty, IsFocusedProperty);
        FocusableProperty.OverrideDefaultValue<DepartureBandControl>(true);
        CursorProperty.OverrideDefaultValue<DepartureBandControl>(new Cursor(StandardCursorType.SizeWestEast));
    }

    public IReadOnlyList<DepartureBandSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double? KnobFraction
    {
        get => GetValue(KnobFractionProperty);
        set => SetValue(KnobFractionProperty, value);
    }

    public Color SuccessColor
    {
        get => GetValue(SuccessColorProperty);
        set => SetValue(SuccessColorProperty, value);
    }

    public Color DestructiveColor
    {
        get => GetValue(DestructiveColorProperty);
        set => SetValue(DestructiveColorProperty, value);
    }

    public Color MutedColor
    {
        get => GetValue(MutedColorProperty);
        set => SetValue(MutedColorProperty, value);
    }

    public Color KnobColor
    {
        get => GetValue(KnobColorProperty);
        set => SetValue(KnobColorProperty, value);
    }

    public Color KnobHaloColor
    {
        get => GetValue(KnobHaloColorProperty);
        set => SetValue(KnobHaloColorProperty, value);
    }

    /// <summary>A press on the band: a drag starts at this fraction.</summary>
    public event EventHandler<double>? DragStarted;

    /// <summary>The pointer moved during a drag, to this fraction.</summary>
    public event EventHandler<double>? Dragged;

    /// <summary>The drag ended (released or capture lost).</summary>
    public event EventHandler? DragCompleted;

    /// <summary>A key asked to move the departure by this many swept departures.</summary>
    public event EventHandler<int>? Nudged;

    private bool _dragging;

    private double FractionAt(PointerEventArgs e) =>
        Bounds.Width > 0 ? Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1) : 0;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Segments is not { Count: > 0 })
            return;
        _dragging = true;
        e.Pointer.Capture(this);
        Focus();
        DragStarted?.Invoke(this, FractionAt(e));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
            Dragged?.Invoke(this, FractionAt(e));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        Dragged?.Invoke(this, FractionAt(e));
        EndDrag();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (!_dragging)
            return;
        _dragging = false;
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var hour = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 12 : 1;
        var steps = e.Key switch
        {
            Key.Left => -hour,
            Key.Right => hour,
            _ => 0,
        };
        if (steps == 0)
            return;
        Nudged?.Invoke(this, steps);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
            return;

        var knobTop = Math.Max(0, (height - KnobHeight) / 2);
        var barTop = knobTop + ((KnobHeight - BarHeight) / 2);
        var success = new SolidColorBrush(SuccessColor);
        var below = new SolidColorBrush(DestructiveColor, 0.35);
        var muted = new SolidColorBrush(MutedColor, 0.35);
        var hatch = new Pen(new SolidColorBrush(MutedColor, 0.8), 1);

        foreach (var segment in Segments ?? [])
        {
            var rect = new Rect(segment.Start * width, barTop, Math.Max(1, (segment.End - segment.Start) * width), BarHeight);
            switch (segment.Kind)
            {
                case DepartureBandKind.Meets:
                    context.DrawRectangle(success, null, rect);
                    break;
                case DepartureBandKind.Below:
                    context.DrawRectangle(below, null, rect);
                    break;
                case DepartureBandKind.NoData:
                    context.DrawRectangle(muted, null, rect);
                    break;
                default:
                    // Arrival after the data ends: a 45° hatch.
                    using (context.PushClip(rect))
                    {
                        for (var x = rect.Left - BarHeight; x < rect.Right; x += 4)
                            context.DrawLine(hatch, new Point(x, rect.Bottom), new Point(x + BarHeight, rect.Top));
                    }

                    break;
            }
        }

        if (KnobFraction is { } f && double.IsFinite(f))
        {
            var x = Math.Clamp(f, 0, 1) * width;
            var knob = new Rect(x - (KnobWidth / 2), knobTop, KnobWidth, KnobHeight);
            context.DrawRectangle(new SolidColorBrush(KnobColor), new Pen(new SolidColorBrush(KnobHaloColor), 2), knob, 2, 2);
        }

        if (IsFocused)
            context.DrawRectangle(null, new Pen(new SolidColorBrush(KnobColor, 0.4), 1) { DashStyle = DashStyle.Dash }, new Rect(0.5, 0.5, width - 1, height - 1));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsFinite(availableSize.Width) ? availableSize.Width : 200, KnobHeight + 2);
}
