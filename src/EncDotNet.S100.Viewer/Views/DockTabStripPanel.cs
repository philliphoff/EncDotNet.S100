using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Lays out a dock's tabs in a row, and switches them to icons only when
/// their labels don't fit the width the dock gives them (legends design note
/// D13, D21): labels are never truncated and the strip never scrolls.
/// </summary>
/// <remarks>
/// The labelled width is measured while the labels show and kept until the
/// tabs change, so the switch back is decided without showing the labels
/// first. While compact the panel carries the <c>compact</c> class, which the
/// <see cref="DockTabStrip"/> styles use to swap each label for its icon, and
/// gives each tab its accessible name as tooltip.
/// </remarks>
public sealed class DockTabStripPanel : Panel
{
    /// <summary>The gap between tabs.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<DockTabStripPanel, double>(nameof(Spacing), 14);

    /// <summary>The gap between tabs while they show icons only.</summary>
    public static readonly StyledProperty<double> CompactSpacingProperty =
        AvaloniaProperty.Register<DockTabStripPanel, double>(nameof(CompactSpacing), 10);

    /// <summary>Whether the tabs show icons only.</summary>
    public static readonly DirectProperty<DockTabStripPanel, bool> IsCompactProperty =
        AvaloniaProperty.RegisterDirect<DockTabStripPanel, bool>(nameof(IsCompact), o => o.IsCompact);

    private bool _isCompact;
    private double _labelledWidth = double.NaN;

    static DockTabStripPanel()
    {
        AffectsMeasure<DockTabStripPanel>(SpacingProperty, CompactSpacingProperty);
    }

    /// <inheritdoc cref="SpacingProperty"/>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <inheritdoc cref="CompactSpacingProperty"/>
    public double CompactSpacing
    {
        get => GetValue(CompactSpacingProperty);
        set => SetValue(CompactSpacingProperty, value);
    }

    /// <inheritdoc cref="IsCompactProperty"/>
    public bool IsCompact
    {
        get => _isCompact;
        private set
        {
            if (_isCompact == value)
                return;
            SetAndRaise(IsCompactProperty, ref _isCompact, value);
            Classes.Set("compact", value);
            UpdateTooltips();
        }
    }

    /// <summary>Icons only: each tab's accessible name becomes its tooltip; with labels, no tooltip.</summary>
    private void UpdateTooltips()
    {
        foreach (var button in this.GetVisualDescendants().OfType<ToggleButton>())
            ToolTip.SetTip(button, _isCompact ? AutomationProperties.GetName(button) : null);
    }

    /// <inheritdoc />
    protected override void ChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.ChildrenChanged(sender, e);
        _labelledWidth = double.NaN;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsNaN(_labelledWidth))
        {
            // Show the labels to learn how wide they are.
            IsCompact = false;
            _labelledWidth = MeasureRow(Spacing).Width;
        }

        var compact = _labelledWidth > availableSize.Width;
        IsCompact = compact;
        if (compact)
            UpdateTooltips(); // tabs added while compact
        var size = MeasureRow(compact ? CompactSpacing : Spacing);
        if (!compact)
            _labelledWidth = size.Width;
        return size;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var spacing = IsCompact ? CompactSpacing : Spacing;
        var x = 0d;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
                continue;
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + spacing;
        }
        return finalSize;
    }

    private Size MeasureRow(double spacing)
    {
        var width = 0d;
        var height = 0d;
        var count = 0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            if (!child.IsVisible)
                continue;
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
            count++;
        }
        return new Size(width + (spacing * Math.Max(0, count - 1)), height);
    }
}
