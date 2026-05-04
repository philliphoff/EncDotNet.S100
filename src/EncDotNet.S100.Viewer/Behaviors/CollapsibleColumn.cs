using System;
using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Behaviors;

/// <summary>
/// Attached behaviors for collapsing/expanding a <see cref="ColumnDefinition"/>
/// based on a bound boolean. When <see cref="IsVisibleProperty"/> goes from
/// true to false the column's current absolute width is captured, then the
/// column is squeezed to zero (Width / MinWidth / MaxWidth all 0). When it
/// returns to true the captured width — or <see cref="ExpandedWidthProperty"/>
/// on first reveal — is restored along with the configured min/max bounds.
///
/// Used by <c>MainWindow.axaml</c> for the activity-pane and pick-panel
/// columns so the splitter-driven user width is preserved across hide/show.
/// </summary>
internal static class CollapsibleColumn
{
    /// <summary>
    /// Bind to a view-model boolean. <c>true</c> shows the column at its
    /// last user-chosen width (or <see cref="ExpandedWidthProperty"/> if
    /// never shown); <c>false</c> collapses it to zero.
    /// </summary>
    public static readonly AttachedProperty<bool> IsVisibleProperty =
        AvaloniaProperty.RegisterAttached<ColumnDefinition, bool>(
            "IsVisible",
            typeof(CollapsibleColumn),
            defaultValue: true);

    /// <summary>
    /// Width to use the first time the column is shown (when no user-chosen
    /// width has been captured yet). Defaults to <c>320</c> pixels.
    /// </summary>
    public static readonly AttachedProperty<double> ExpandedWidthProperty =
        AvaloniaProperty.RegisterAttached<ColumnDefinition, double>(
            "ExpandedWidth",
            typeof(CollapsibleColumn),
            defaultValue: 320);

    /// <summary>
    /// Minimum width applied while the column is visible.
    /// </summary>
    public static readonly AttachedProperty<double> MinWidthWhenVisibleProperty =
        AvaloniaProperty.RegisterAttached<ColumnDefinition, double>(
            "MinWidthWhenVisible",
            typeof(CollapsibleColumn));

    /// <summary>
    /// Maximum width applied while the column is visible.
    /// </summary>
    public static readonly AttachedProperty<double> MaxWidthWhenVisibleProperty =
        AvaloniaProperty.RegisterAttached<ColumnDefinition, double>(
            "MaxWidthWhenVisible",
            typeof(CollapsibleColumn),
            defaultValue: double.PositiveInfinity);

    private static readonly AttachedProperty<double> RememberedWidthProperty =
        AvaloniaProperty.RegisterAttached<ColumnDefinition, double>(
            "RememberedWidth",
            typeof(CollapsibleColumn),
            defaultValue: double.NaN);

    public static bool GetIsVisible(ColumnDefinition column) =>
        column.GetValue(IsVisibleProperty);

    public static void SetIsVisible(ColumnDefinition column, bool value) =>
        column.SetValue(IsVisibleProperty, value);

    public static double GetExpandedWidth(ColumnDefinition column) =>
        column.GetValue(ExpandedWidthProperty);

    public static void SetExpandedWidth(ColumnDefinition column, double value) =>
        column.SetValue(ExpandedWidthProperty, value);

    public static double GetMinWidthWhenVisible(ColumnDefinition column) =>
        column.GetValue(MinWidthWhenVisibleProperty);

    public static void SetMinWidthWhenVisible(ColumnDefinition column, double value) =>
        column.SetValue(MinWidthWhenVisibleProperty, value);

    public static double GetMaxWidthWhenVisible(ColumnDefinition column) =>
        column.GetValue(MaxWidthWhenVisibleProperty);

    public static void SetMaxWidthWhenVisible(ColumnDefinition column, double value) =>
        column.SetValue(MaxWidthWhenVisibleProperty, value);

    static CollapsibleColumn()
    {
        IsVisibleProperty.Changed.AddClassHandler<ColumnDefinition>(OnIsVisibleChanged);
    }

    private static void OnIsVisibleChanged(ColumnDefinition column, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not bool visible)
            return;

        if (visible)
        {
            var remembered = column.GetValue(RememberedWidthProperty);
            var width = double.IsNaN(remembered) || remembered <= 0
                ? GetExpandedWidth(column)
                : remembered;

            column.MinWidth = GetMinWidthWhenVisible(column);
            column.MaxWidth = GetMaxWidthWhenVisible(column);
            column.Width = new GridLength(width, GridUnitType.Pixel);
        }
        else
        {
            // Capture the current user-resized width (only meaningful for
            // absolute widths) so we can restore it next time.
            if (column.Width.IsAbsolute && column.Width.Value > 0)
                column.SetValue(RememberedWidthProperty, column.Width.Value);

            column.Width = new GridLength(0);
            column.MinWidth = 0;
            column.MaxWidth = 0;
        }
    }
}
