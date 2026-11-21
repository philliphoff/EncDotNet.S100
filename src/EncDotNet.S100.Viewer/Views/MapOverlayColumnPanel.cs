using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// The map's overlay column (legends design note D15, D20): the mode buttons
/// (first child) above the zoom buttons and compass (second child). When both
/// don't fit in the height the panel is given, it wraps: the mode buttons keep
/// their place on the right edge and the second child moves to a column to
/// their left, starting <see cref="WrappedSecondTop"/> down so it clears the
/// View pill.
/// </summary>
/// <remarks>
/// The map gives the panel its height less the panel's margins, so with a
/// 12 px margin top and bottom the column wraps when it plus 24 px is taller
/// than the map. Any further children are ignored.
/// </remarks>
public class MapOverlayColumnPanel : Panel
{
    /// <summary>Defines the <see cref="ColumnGap"/> property.</summary>
    public static readonly StyledProperty<double> ColumnGapProperty =
        AvaloniaProperty.Register<MapOverlayColumnPanel, double>(nameof(ColumnGap), 8);

    /// <summary>Defines the <see cref="WrappedSecondTop"/> property.</summary>
    public static readonly StyledProperty<double> WrappedSecondTopProperty =
        AvaloniaProperty.Register<MapOverlayColumnPanel, double>(nameof(WrappedSecondTop));

    /// <summary>Defines the <see cref="IsWrapped"/> property.</summary>
    public static readonly DirectProperty<MapOverlayColumnPanel, bool> IsWrappedProperty =
        AvaloniaProperty.RegisterDirect<MapOverlayColumnPanel, bool>(nameof(IsWrapped), p => p.IsWrapped);

    private bool _isWrapped;

    static MapOverlayColumnPanel()
    {
        AffectsMeasure<MapOverlayColumnPanel>(ColumnGapProperty, WrappedSecondTopProperty);
    }

    /// <summary>The horizontal gap between the two columns when wrapped.</summary>
    public double ColumnGap
    {
        get => GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    /// <summary>How far down the second child starts when wrapped, from the panel's top.</summary>
    public double WrappedSecondTop
    {
        get => GetValue(WrappedSecondTopProperty);
        set => SetValue(WrappedSecondTopProperty, value);
    }

    /// <summary>Whether the column is wrapped into two (the <c>:wrapped</c> pseudo-class).</summary>
    public bool IsWrapped
    {
        get => _isWrapped;
        private set
        {
            if (SetAndRaise(IsWrappedProperty, ref _isWrapped, value))
                PseudoClasses.Set(":wrapped", value);
        }
    }

    /// <summary>Whether a column of <paramref name="columnHeight"/> wraps in <paramref name="availableHeight"/>.</summary>
    public static bool Wraps(double columnHeight, double availableHeight) => columnHeight > availableHeight;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        Control? first = Children.Count > 0 ? Children[0] : null;
        Control? second = Children.Count > 1 ? Children[1] : null;
        first?.Measure(infinite);
        second?.Measure(infinite);
        var a = first?.DesiredSize ?? default;
        var b = second?.DesiredSize ?? default;

        IsWrapped = second is not null && Wraps(a.Height + b.Height, availableSize.Height);
        return IsWrapped
            ? new Size(a.Width + ColumnGap + b.Width, Math.Max(a.Height, WrappedSecondTop + b.Height))
            : new Size(Math.Max(a.Width, b.Width), a.Height + b.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Control? first = Children.Count > 0 ? Children[0] : null;
        Control? second = Children.Count > 1 ? Children[1] : null;
        var a = first?.DesiredSize ?? default;
        var b = second?.DesiredSize ?? default;

        if (IsWrapped)
        {
            first?.Arrange(new Rect(finalSize.Width - a.Width, 0, a.Width, a.Height));
            second?.Arrange(new Rect(Math.Max(0, finalSize.Width - a.Width - ColumnGap - b.Width), WrappedSecondTop, b.Width, b.Height));
        }
        else
        {
            first?.Arrange(new Rect(0, 0, finalSize.Width, a.Height));
            second?.Arrange(new Rect(0, a.Height, finalSize.Width, b.Height));
        }
        return finalSize;
    }
}
