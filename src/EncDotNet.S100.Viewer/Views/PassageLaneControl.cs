using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Paints the Timeline's passage lane (#904, handoff E1): a bar from
/// departure to arrival in the accent colour at 35 % with a border, and a
/// tick at the min-UKC time in the verdict colour. Positions are fractions
/// of the control's width.
/// </summary>
internal sealed class PassageLaneControl : Control
{
    public static readonly StyledProperty<double> StartProperty =
        AvaloniaProperty.Register<PassageLaneControl, double>(nameof(Start), double.NaN);

    public static readonly StyledProperty<double> EndProperty =
        AvaloniaProperty.Register<PassageLaneControl, double>(nameof(End), double.NaN);

    /// <summary>The min-UKC tick, 0–1; NaN for none.</summary>
    public static readonly StyledProperty<double> TickProperty =
        AvaloniaProperty.Register<PassageLaneControl, double>(nameof(Tick), double.NaN);

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<PassageLaneControl, IBrush?>(nameof(Accent));

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<PassageLaneControl, IBrush?>(nameof(TickBrush));

    private const double BarHeight = 12;
    private const double TickHeight = 20;

    static PassageLaneControl()
    {
        AffectsRender<PassageLaneControl>(StartProperty, EndProperty, TickProperty, AccentProperty, TickBrushProperty);
    }

    public double Start
    {
        get => GetValue(StartProperty);
        set => SetValue(StartProperty, value);
    }

    public double End
    {
        get => GetValue(EndProperty);
        set => SetValue(EndProperty, value);
    }

    public double Tick
    {
        get => GetValue(TickProperty);
        set => SetValue(TickProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0 || !double.IsFinite(Start) || !double.IsFinite(End))
            return;

        var accent = Accent ?? Brushes.SteelBlue;
        var color = accent is ISolidColorBrush solid ? solid.Color : Colors.SteelBlue;
        var left = Math.Clamp(Math.Min(Start, End), 0, 1) * width;
        var right = Math.Clamp(Math.Max(Start, End), 0, 1) * width;
        var bar = new Rect(left, (height - BarHeight) / 2, Math.Max(2, right - left), BarHeight);
        context.DrawRectangle(new SolidColorBrush(color, 0.35), new Pen(accent, 1), bar, 3, 3);

        if (double.IsFinite(Tick) && TickBrush is { } tick)
        {
            var x = Math.Clamp(Tick, 0, 1) * width;
            var top = Math.Max(0, (height - TickHeight) / 2);
            context.DrawRectangle(tick, null, new Rect(x - 1.5, top, 3, Math.Min(height, TickHeight)), 1, 1);
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsFinite(availableSize.Width) ? availableSize.Width : 200, TickHeight + 2);
}
