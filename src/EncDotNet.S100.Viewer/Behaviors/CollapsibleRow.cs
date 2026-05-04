using System;
using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Behaviors;

/// <summary>
/// Row analogue of <see cref="CollapsibleColumn"/>: collapses a
/// <see cref="RowDefinition"/> to zero when bound boolean goes
/// false, restoring the captured (or expanded-default) height when
/// it returns to true. Used by the bottom timeline panel so the
/// row vanishes entirely when no time-varying dataset is loaded.
/// </summary>
internal static class CollapsibleRow
{
    public static readonly AttachedProperty<bool> IsVisibleProperty =
        AvaloniaProperty.RegisterAttached<RowDefinition, bool>(
            "IsVisible", typeof(CollapsibleRow), defaultValue: true);

    public static readonly AttachedProperty<double> ExpandedHeightProperty =
        AvaloniaProperty.RegisterAttached<RowDefinition, double>(
            "ExpandedHeight", typeof(CollapsibleRow), defaultValue: 120);

    public static readonly AttachedProperty<double> MinHeightWhenVisibleProperty =
        AvaloniaProperty.RegisterAttached<RowDefinition, double>(
            "MinHeightWhenVisible", typeof(CollapsibleRow));

    public static readonly AttachedProperty<double> MaxHeightWhenVisibleProperty =
        AvaloniaProperty.RegisterAttached<RowDefinition, double>(
            "MaxHeightWhenVisible", typeof(CollapsibleRow),
            defaultValue: double.PositiveInfinity);

    private static readonly AttachedProperty<double> RememberedHeightProperty =
        AvaloniaProperty.RegisterAttached<RowDefinition, double>(
            "RememberedHeight", typeof(CollapsibleRow), defaultValue: double.NaN);

    public static bool GetIsVisible(RowDefinition row) => row.GetValue(IsVisibleProperty);
    public static void SetIsVisible(RowDefinition row, bool v) => row.SetValue(IsVisibleProperty, v);
    public static double GetExpandedHeight(RowDefinition row) => row.GetValue(ExpandedHeightProperty);
    public static void SetExpandedHeight(RowDefinition row, double v) => row.SetValue(ExpandedHeightProperty, v);
    public static double GetMinHeightWhenVisible(RowDefinition row) => row.GetValue(MinHeightWhenVisibleProperty);
    public static void SetMinHeightWhenVisible(RowDefinition row, double v) => row.SetValue(MinHeightWhenVisibleProperty, v);
    public static double GetMaxHeightWhenVisible(RowDefinition row) => row.GetValue(MaxHeightWhenVisibleProperty);
    public static void SetMaxHeightWhenVisible(RowDefinition row, double v) => row.SetValue(MaxHeightWhenVisibleProperty, v);

    static CollapsibleRow()
    {
        IsVisibleProperty.Changed.AddClassHandler<RowDefinition>(OnIsVisibleChanged);
    }

    private static void OnIsVisibleChanged(RowDefinition row, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not bool visible) return;

        if (visible)
        {
            var remembered = row.GetValue(RememberedHeightProperty);
            var h = double.IsNaN(remembered) || remembered <= 0
                ? GetExpandedHeight(row)
                : remembered;
            row.MinHeight = GetMinHeightWhenVisible(row);
            row.MaxHeight = GetMaxHeightWhenVisible(row);
            row.Height = new GridLength(h, GridUnitType.Pixel);
        }
        else
        {
            if (row.Height.IsAbsolute && row.Height.Value > 0)
                row.SetValue(RememberedHeightProperty, row.Height.Value);
            row.Height = new GridLength(0);
            row.MinHeight = 0;
            row.MaxHeight = 0;
        }
    }
}
