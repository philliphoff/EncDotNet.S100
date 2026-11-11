using Avalonia;
using Avalonia.Controls;

namespace EncDotNet.S100.Viewer.Views;

/// <summary>
/// Lays out the Logs tab's toolbar (#1080): its two children are the filters
/// (level, search and counts, left) and the actions (Copy all … Send
/// feedback, right).
/// The two never overlap. When they fit side by side, the filters get every
/// pixel the actions leave, so the search box can grow; when they don't, the
/// actions move to a second line, still right-aligned, rather than being drawn
/// over the search box. On that line they get the full width, so an actions
/// panel that wraps (a <see cref="WrapPanel"/>) can wrap again in the
/// narrowest docks.
/// </summary>
internal sealed class LogsToolbarPanel : Panel
{
    /// <summary>The least space between the filters and the actions on one line.</summary>
    public const double Gap = 8;

    /// <summary>The space between the two lines when the actions wrap.</summary>
    public const double LineGap = 4;

    /// <summary>Whether the actions are on their own line at the last arrange.</summary>
    public bool IsActionsWrapped { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
            child.Measure(unbounded);

        if (Children.Count != 2)
            return default;

        var (filters, actions) = (Children[0], Children[1]);
        var oneLine = filters.DesiredSize.Width + Gap + actions.DesiredSize.Width;
        var width = double.IsInfinity(availableSize.Width) ? oneLine : availableSize.Width;

        IsActionsWrapped = oneLine > width;
        if (!IsActionsWrapped)
            return new Size(oneLine, Math.Max(filters.DesiredSize.Height, actions.DesiredSize.Height));

        // On their own line the filters get the full width, measured against it
        // so a stretching search box gives back what it can't have.
        filters.Measure(new Size(width, availableSize.Height));
        actions.Measure(new Size(width, double.PositiveInfinity));
        return new Size(
            Math.Min(width, Math.Max(filters.DesiredSize.Width, actions.DesiredSize.Width)),
            filters.DesiredSize.Height + LineGap + actions.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 2)
            return finalSize;

        var (filters, actions) = (Children[0], Children[1]);
        var width = finalSize.Width;
        var (f, a) = (filters.DesiredSize, actions.DesiredSize);

        if (IsActionsWrapped)
        {
            var actionsWidth = Math.Min(a.Width, width);
            filters.Arrange(new Rect(0, 0, width, f.Height));
            actions.Arrange(new Rect(width - actionsWidth, f.Height + LineGap, actionsWidth, a.Height));
        }
        else
        {
            var height = Math.Max(f.Height, a.Height);
            filters.Arrange(new Rect(0, (height - f.Height) / 2, Math.Max(0, width - Gap - a.Width), f.Height));
            actions.Arrange(new Rect(Math.Max(0, width - a.Width), (height - a.Height) / 2, a.Width, a.Height));
        }
        return finalSize;
    }
}
