using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The Timeline's Now marker (#685, handoff D1): a "NOW" label at the top and a
/// 1 px dashed line down through the slider and the coverage band, at
/// <see cref="Position"/> along the axis (inset like the band, so the two line
/// up). Nothing is drawn while <see cref="Position"/> is NaN.
/// </summary>
internal sealed class NowMarkerControl : Control
{
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<NowMarkerControl, double>(nameof(Position), double.NaN);

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<NowMarkerControl, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<NowMarkerControl, string?>(nameof(Label));

    /// <summary>The height reserved at the top for the label.</summary>
    public static readonly StyledProperty<double> LabelHeightProperty =
        AvaloniaProperty.Register<NowMarkerControl, double>(nameof(LabelHeight), 14d);

    static NowMarkerControl()
    {
        AffectsRender<NowMarkerControl>(PositionProperty, StrokeProperty, LabelProperty, LabelHeightProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<NowMarkerControl>(false);
    }

    /// <summary>Where now lies on the axis, 0–1; NaN hides the marker.</summary>
    public double Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>The line and label colour.</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>The label above the line ("NOW").</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The height reserved at the top for <see cref="Label"/>.</summary>
    public double LabelHeight
    {
        get => GetValue(LabelHeightProperty);
        set => SetValue(LabelHeightProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var position = Position;
        if (double.IsNaN(position) || Stroke is not { } stroke || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var x = Math.Round(Math.Clamp(position, 0, 1) * Bounds.Width) + 0.5;
        var top = Math.Min(LabelHeight, Bounds.Height);
        var pen = new Pen(stroke, 1, new DashStyle([3, 2], 0));
        context.DrawLine(pen, new Point(x, top), new Point(x, Bounds.Height));

        if (string.IsNullOrEmpty(Label))
            return;
        var text = new FormattedText(Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 10.5, stroke);
        var left = Math.Clamp(x - text.Width / 2, 0, Math.Max(0, Bounds.Width - text.Width));
        context.DrawText(text, new Point(left, Math.Max(0, top - text.Height)));
    }
}
