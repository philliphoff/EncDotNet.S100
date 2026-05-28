using System;
using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Behaviors;

/// <summary>
/// PR-M3: persists the star-ratio of a <see cref="GridSplitter"/> separating
/// two star-sized rows or columns. Attached to the splitter itself; the
/// behaviour locates the two adjacent definitions by reading
/// <c>Grid.RowProperty</c> (for <c>ResizeDirection=Rows</c>) or
/// <c>Grid.ColumnProperty</c> in the parent <see cref="Grid"/>.
///
/// <para>
/// Storage shape: a single <c>double</c> in <c>[0, 1]</c> giving the share
/// of the previous (top/left) definition relative to the previous+next pair.
/// On apply the two adjacent definitions are rewritten to
/// <c>fraction*</c> / <c>(1-fraction)*</c>; their original sum of star
/// factors is preserved so neighbouring star definitions retain their
/// relative weights.
/// </para>
///
/// <para>
/// Used by <c>DatasetsView</c> and <c>CatalogPanelView</c> to persist the
/// master/detail split inside their respective activity tabs.
/// </para>
/// </summary>
internal static class SplitterPersistence
{
    /// <summary>
    /// Bound to the view-model's persisted fraction (0..1) for this splitter.
    /// 2-way: the behaviour writes back whenever either neighbouring
    /// definition's size changes (i.e. the user dragged the splitter).
    /// </summary>
    public static readonly AttachedProperty<double?> SavedFractionProperty =
        AvaloniaProperty.RegisterAttached<GridSplitter, double?>(
            "SavedFraction",
            typeof(SplitterPersistence),
            defaultValue: null,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private static readonly AttachedProperty<bool> IsAttachedProperty =
        AvaloniaProperty.RegisterAttached<GridSplitter, bool>(
            "IsAttached", typeof(SplitterPersistence));

    public static double? GetSavedFraction(GridSplitter splitter) =>
        splitter.GetValue(SavedFractionProperty);

    public static void SetSavedFraction(GridSplitter splitter, double? value) =>
        splitter.SetValue(SavedFractionProperty, value);

    static SplitterPersistence()
    {
        SavedFractionProperty.Changed.AddClassHandler<GridSplitter>(OnSavedFractionChanged);
    }

    private static void OnSavedFractionChanged(GridSplitter splitter, AvaloniaPropertyChangedEventArgs e)
    {
        if (!splitter.GetValue(IsAttachedProperty))
        {
            splitter.SetValue(IsAttachedProperty, true);
            splitter.AttachedToVisualTree += (_, _) => TryApplyAndSubscribe(splitter);
            // If already in the tree (binding fired after attach) apply now.
            if (splitter.Parent is Grid)
                TryApplyAndSubscribe(splitter);
        }
        else
        {
            // Reapply if the bound value changes after initial subscription.
            ApplyFraction(splitter, splitter.GetValue(SavedFractionProperty));
        }
    }

    private static void TryApplyAndSubscribe(GridSplitter splitter)
    {
        if (splitter.Parent is not Grid grid)
            return;

        // Apply persisted fraction first (so initial layout matches the
        // saved state) before wiring the write-back observers — otherwise
        // the apply itself would round-trip a slightly different value.
        ApplyFraction(splitter, splitter.GetValue(SavedFractionProperty));

        var rows = splitter.ResizeDirection == GridResizeDirection.Rows
            || (splitter.ResizeDirection == GridResizeDirection.Auto && splitter.Height < splitter.Width);

        if (rows)
        {
            var (prev, next) = FindRows(splitter, grid);
            if (prev is null || next is null) return;
            EventHandler<AvaloniaPropertyChangedEventArgs> rowHandler = (s, a) =>
            {
                if (a.Property == RowDefinition.HeightProperty)
                    PushBack(splitter, prev.Height, next.Height);
            };
            prev.PropertyChanged += rowHandler;
            next.PropertyChanged += rowHandler;
        }
        else
        {
            var (prev, next) = FindColumns(splitter, grid);
            if (prev is null || next is null) return;
            EventHandler<AvaloniaPropertyChangedEventArgs> colHandler = (s, a) =>
            {
                if (a.Property == ColumnDefinition.WidthProperty)
                    PushBack(splitter, prev.Width, next.Width);
            };
            prev.PropertyChanged += colHandler;
            next.PropertyChanged += colHandler;
        }
    }

    private static void PushBack(GridSplitter splitter, GridLength prev, GridLength next)
    {
        if (!prev.IsStar || !next.IsStar) return;
        var total = prev.Value + next.Value;
        if (total <= 0) return;
        var fraction = prev.Value / total;
        var current = splitter.GetValue(SavedFractionProperty);
        if (current is null || Math.Abs(current.Value - fraction) > 0.001)
            splitter.SetValue(SavedFractionProperty, fraction);
    }

    private static void ApplyFraction(GridSplitter splitter, double? fraction)
    {
        if (fraction is not double f || f <= 0 || f >= 1) return;
        if (splitter.Parent is not Grid grid) return;

        var rows = splitter.ResizeDirection == GridResizeDirection.Rows
            || (splitter.ResizeDirection == GridResizeDirection.Auto && splitter.Height < splitter.Width);

        if (rows)
        {
            var (prev, next) = FindRows(splitter, grid);
            if (prev is null || next is null) return;
            if (!prev.Height.IsStar || !next.Height.IsStar) return;
            var total = prev.Height.Value + next.Height.Value;
            if (total <= 0) total = 1.0;
            prev.Height = new GridLength(f * total, GridUnitType.Star);
            next.Height = new GridLength((1 - f) * total, GridUnitType.Star);
        }
        else
        {
            var (prev, next) = FindColumns(splitter, grid);
            if (prev is null || next is null) return;
            if (!prev.Width.IsStar || !next.Width.IsStar) return;
            var total = prev.Width.Value + next.Width.Value;
            if (total <= 0) total = 1.0;
            prev.Width = new GridLength(f * total, GridUnitType.Star);
            next.Width = new GridLength((1 - f) * total, GridUnitType.Star);
        }
    }

    private static (RowDefinition? prev, RowDefinition? next) FindRows(GridSplitter splitter, Grid grid)
    {
        var splitterRow = Grid.GetRow(splitter);
        RowDefinition? prev = null;
        RowDefinition? next = null;
        for (int i = splitterRow - 1; i >= 0; i--)
        {
            if (i < grid.RowDefinitions.Count) { prev = grid.RowDefinitions[i]; break; }
        }
        for (int i = splitterRow + 1; i < grid.RowDefinitions.Count; i++)
        {
            next = grid.RowDefinitions[i]; break;
        }
        return (prev, next);
    }

    private static (ColumnDefinition? prev, ColumnDefinition? next) FindColumns(GridSplitter splitter, Grid grid)
    {
        var splitterCol = Grid.GetColumn(splitter);
        ColumnDefinition? prev = null;
        ColumnDefinition? next = null;
        for (int i = splitterCol - 1; i >= 0; i--)
        {
            if (i < grid.ColumnDefinitions.Count) { prev = grid.ColumnDefinitions[i]; break; }
        }
        for (int i = splitterCol + 1; i < grid.ColumnDefinitions.Count; i++)
        {
            next = grid.ColumnDefinitions[i]; break;
        }
        return (prev, next);
    }
}
